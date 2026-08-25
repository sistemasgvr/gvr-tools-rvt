using System.Globalization;
using System.Text;
using System.Text.Json;
using GvrLicense.Contracts;
using GvrLicense.Domain.Entities;
using GvrLicense.Domain.LicenseKeys;
using GvrLicense.Domain.Versioning;
using GvrLicense.Infrastructure;
using GvrLicense.Infrastructure.Signing;
using GvrLicense.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace GvrLicense.Api.Services;

/// <summary>
/// El motor de negocio detrás de /v1/* (docs/LICENSING_PLAN.md, Pieza 1 "API mínima" + Pieza 3
/// "Servidor manda"). Un caso de uso por método público; cada uno hace su propia validación y
/// lanza <see cref="LicenseApiException"/> con el status HTTP correcto en vez de devolver
/// resultados ambiguos.
/// </summary>
public sealed class LicenseEngine(
    LicenseDbContext db,
    IEntitlementSigner signer,
    JwtSessionTokenService jwt,
    IReleaseArtifactStore artifacts)
{
    // Opciones por defecto (PascalCase, sin naming policy) a propósito: deben coincidir byte a byte
    // con lo que el cliente net48 espera de DataContractJsonSerializer (ver
    // src/GvrTools.Licensing/Crypto/EcdsaEntitlementSignatureVerifier.cs y
    // GvrLicense.Contracts/EntitlementBlob.cs).
    private static readonly JsonSerializerOptions WireJsonOptions = new();

    public async Task<ActivateResponse> ActivateAsync(ActivateRequest request, CancellationToken ct)
    {
        if (!LicenseKeyGenerator.TryValidateFormat(request.LicenseKey))
        {
            throw new LicenseApiException(400, "Formato de license key inválido.");
        }

        if (string.IsNullOrWhiteSpace(request.UserFullName) || string.IsNullOrWhiteSpace(request.UserEmail))
        {
            throw new LicenseApiException(400, "Nombre y correo son obligatorios para activar.");
        }

        var normalizedKey = request.LicenseKey.Trim().ToUpperInvariant();
        var license = await db.Licenses
            .Include(l => l.Plan)
            .Include(l => l.Devices)
            .FirstOrDefaultAsync(l => l.Key == normalizedKey, ct);

        if (license is null)
        {
            throw new LicenseApiException(404, "Licencia no encontrada.");
        }

        EnsureLicenseUsable(license);

        var companyUser = await FindOrCreateCompanyUserAsync(license.CustomerId, request.UserFullName, request.UserEmail, ct);

        var device = license.Devices.FirstOrDefault(d => d.Fingerprint == request.DeviceFingerprint);
        if (device is null)
        {
            var devicesOfThisUser = license.Devices.Where(d => d.CompanyUserId == companyUser.Id).ToList();

            // El seat se cuenta por persona, no por dispositivo (docs/LICENSING_PLAN.md, "Métodos
            // de suscripción"): si esta persona ya tiene otro dispositivo en la misma licencia, un
            // dispositivo más no gasta un seat nuevo -- pero sí está topado por cuántos dispositivos
            // puede tener UNA persona ("un usuario puede usar la licencia en uno o más dispositivos
            // según lo configuremos"), vía el feature seat.max_devices_per_user (mismo patrón que el
            // resto del catálogo: por defecto 1 si el plan no lo define, -1 = ilimitado).
            var maxDevicesPerUser = ParseFeatureInt(GetEffectiveFeatures(license), "seat.max_devices_per_user", defaultValue: 1);
            if (maxDevicesPerUser != -1 && devicesOfThisUser.Count >= maxDevicesPerUser)
            {
                throw new LicenseApiException(403,
                    $"{request.UserEmail} ya activó en {maxDevicesPerUser} dispositivo(s), el máximo que permite el plan. Desactiva uno antes de activar en este PC.");
            }

            var distinctUsersExcludingThis = license.Devices
                .Select(d => d.CompanyUserId)
                .Where(id => id != companyUser.Id)
                .Distinct()
                .Count();

            if (devicesOfThisUser.Count == 0 && distinctUsersExcludingThis >= license.MaxUsers)
            {
                throw new LicenseApiException(403,
                    $"Esta licencia ya tiene {license.MaxUsers} usuario(s) activo(s). Libera un seat antes de activar para {request.UserEmail}.");
            }

            device = new Device
            {
                Id = Guid.NewGuid(),
                LicenseId = license.Id,
                CompanyUserId = companyUser.Id,
                Fingerprint = request.DeviceFingerprint,
                DisplayName = request.DeviceName,
                ActivatedAtUtc = DateTimeOffset.UtcNow,
                LastSeenUtc = DateTimeOffset.UtcNow
            };
            db.Devices.Add(device);
        }
        else
        {
            // Mismo PC, persona distinta activando encima: reasigna el dispositivo a quien acaba de
            // activar -- el dueño de un fingerprint es quien lo usó por última vez.
            device.CompanyUserId = companyUser.Id;
            device.LastSeenUtc = DateTimeOffset.UtcNow;
            if (!string.IsNullOrWhiteSpace(request.DeviceName))
            {
                device.DisplayName = request.DeviceName;
            }
        }

        await EnsureCurrentPeriodCountersAsync(license, ct);
        await db.SaveChangesAsync(ct);

        var (json, signature) = await BuildSignedBlobAsync(license, device, ct);
        var accessToken = jwt.Issue(license.Id, device.Id);

        return new ActivateResponse
        {
            AccessToken = accessToken,
            EntitlementJson = json,
            EntitlementSignatureBase64 = Convert.ToBase64String(signature)
        };
    }

    /// <summary>
    /// licenseId/deviceId ya vienen autenticados: los extrajo el middleware AddJwtBearer de
    /// Program.cs de los claims del JWT (Authorization: Bearer), no se validan aquí -- ver
    /// Endpoints/V1Endpoints.cs.
    /// </summary>
    public async Task<HeartbeatResponse> HeartbeatAsync(Guid licenseId, Guid deviceId, HeartbeatRequest request, CancellationToken ct)
    {
        var license = await db.Licenses
            .Include(l => l.Plan)
            .Include(l => l.Devices)
            .FirstOrDefaultAsync(l => l.Id == licenseId, ct);

        if (license is null)
        {
            throw new LicenseApiException(404, "Licencia no encontrada.");
        }

        var device = license.Devices.FirstOrDefault(d => d.Id == deviceId && d.Fingerprint == request.DeviceFingerprint);
        if (device is null)
        {
            throw new LicenseApiException(401, "Dispositivo no reconocido.");
        }

        // Suspendida/vencida se corta aquí, no esperando a que se agote la gracia offline
        // (docs/LICENSING_PLAN.md, "Tokens y gracia offline": "bloqueo en el próximo heartbeat").
        EnsureLicenseUsable(license);

        device.LastSeenUtc = DateTimeOffset.UtcNow;
        await EnsureCurrentPeriodCountersAsync(license, ct);
        await db.SaveChangesAsync(ct);

        var (json, signature) = await BuildSignedBlobAsync(license, device, ct);
        var accessToken = jwt.Issue(license.Id, device.Id);

        return new HeartbeatResponse
        {
            AccessToken = accessToken,
            EntitlementJson = json,
            EntitlementSignatureBase64 = Convert.ToBase64String(signature)
        };
    }

    public async Task<UsageEventResponse> ReportUsageAsync(Guid licenseId, Guid deviceId, UsageEventRequest request, CancellationToken ct)
    {
        var deviceExists = await db.Devices.AnyAsync(
            d => d.Id == deviceId && d.LicenseId == licenseId && d.Fingerprint == request.DeviceFingerprint, ct);
        if (!deviceExists)
        {
            throw new LicenseApiException(401, "Dispositivo no reconocido.");
        }

        var receivedAtUtc = DateTimeOffset.UtcNow;

        // INSERT ... ON CONFLICT (id) DO NOTHING: idempotencia por EventId, sin buscar antes de
        // insertar (docs/LICENSING_PLAN.md, "Dónde vive la lógica: app vs Postgres").
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            insert into usage_event (id, license_id, device_id, feature_code, quantity, occurred_at_utc, received_at_utc)
            values ({request.EventId}, {licenseId}, {deviceId}, {request.FeatureCode}, {request.Quantity}, {request.OccurredAtUtc}, {receivedAtUtc})
            on conflict (id) do nothing
            """, ct);

        var period = CurrentPeriod();

        if (inserted == 0)
        {
            // Reintento de un evento ya procesado: no se vuelve a consumir cuota.
            var existing = await db.UsageCounters.FirstOrDefaultAsync(
                c => c.LicenseId == licenseId && c.FeatureCode == request.FeatureCode && c.Period == period, ct);
            return new UsageEventResponse { Remaining = existing is null ? null : RemainingOf(existing) };
        }

        var remaining = await db.Database
            .SqlQuery<int?>($"select consume_quota({licenseId}, {request.FeatureCode}, {request.Quantity}) as \"Value\"")
            .SingleAsync(ct);

        return new UsageEventResponse { Remaining = remaining };
    }

    /// <summary>
    /// Libera el dispositivo del JWT (mismo efecto que "Liberar" en admin): el add-in borra su
    /// cache local después. No exige licencia active -- un usuario debe poder desactivar aunque
    /// la licencia esté suspendida.
    /// </summary>
    public async Task<DeactivateResponse> DeactivateAsync(Guid licenseId, Guid deviceId, DeactivateRequest request, CancellationToken ct)
    {
        var device = await db.Devices.FirstOrDefaultAsync(
            d => d.Id == deviceId && d.LicenseId == licenseId && d.Fingerprint == request.DeviceFingerprint, ct);

        if (device is null)
        {
            throw new LicenseApiException(404, "Dispositivo no encontrado o ya liberado.");
        }

        db.Devices.Remove(device);
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            LicenseId = licenseId,
            Actor = "device",
            Action = "device.deactivate",
            DetailsJson = $"{{\"deviceId\":\"{deviceId}\",\"fingerprint\":\"{device.Fingerprint}\"}}",
            OccurredAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(ct);

        return new DeactivateResponse { Deactivated = true };
    }

    public async Task<UpdateCheckResponse> CheckUpdateAsync(string? currentVersion, string? revitVersion, CancellationToken ct)
    {
        var stableReleases = await db.Releases
            .Where(r => r.Channel == "stable"
                && (r.Kind == ReleaseKinds.Update || r.Kind == ReleaseKinds.Installer))
            .ToListAsync(ct);

        static (Release Release, SemVersion Version)? LatestValid(
            IEnumerable<Release> releases,
            string kind)
        {
            return releases
                .Where(r => r.Kind == kind)
                .Select(r => SemVersion.TryParse(r.Version, out var version)
                    ? (Release: r, Version: version!)
                    : ((Release Release, SemVersion Version)?)null)
                .Where(candidate => candidate.HasValue)
                .Select(candidate => candidate!.Value)
                .OrderByDescending(candidate => candidate.Version)
                .ThenByDescending(candidate => candidate.Release.PublishedAtUtc)
                .Cast<(Release Release, SemVersion Version)?>()
                .FirstOrDefault();
        }

        // Se prefieren paquetes update; el instalador solo es fallback si no hay un update válido.
        var latest = LatestValid(stableReleases, ReleaseKinds.Update)
            ?? LatestValid(stableReleases, ReleaseKinds.Installer);

        if (latest is null)
        {
            return new UpdateCheckResponse { UpdateAvailable = false };
        }

        var updateAvailable = !SemVersion.TryParse(currentVersion, out var current)
            || latest.Value.Version.IsGreaterThan(current!);
        if (!updateAvailable)
        {
            return new UpdateCheckResponse { UpdateAvailable = false };
        }

        return new UpdateCheckResponse
        {
            UpdateAvailable = true,
            LatestVersion = latest.Value.Version.ToString(),
            DownloadUrl = $"/v1/updates/download/{latest.Value.Release.Id}",
            ReleaseNotes = latest.Value.Release.Notes
        };
    }

    /// <summary>
    /// Devuelve URL firmada temporal (MinIO) para descargar el artefacto. Si MinIO no está
    /// configurado, devuelve la object key cruda (solo útil en desarrollo).
    /// </summary>
    public async Task<string> GetDownloadLocationAsync(Guid releaseId, CancellationToken ct)
    {
        var release = await db.Releases.FindAsync([releaseId], ct)
            ?? throw new LicenseApiException(404, "Release no encontrado.");

        if (artifacts.IsConfigured && !string.IsNullOrWhiteSpace(release.ArtifactLocation))
        {
            return await artifacts.CreatePresignedGetUrlAsync(release.ArtifactLocation, ct);
        }

        return release.ArtifactLocation;
    }

    /// <summary>Último instalador publicado (kind=installer) para el enlace público /download.</summary>
    public async Task<Release> GetLatestInstallerAsync(CancellationToken ct)
    {
        var installers = await db.Releases
            .Where(r => r.Channel == "stable" && r.Kind == ReleaseKinds.Installer)
            .ToListAsync(ct);

        var release = installers
            .Select(r => SemVersion.TryParse(r.Version, out var version)
                ? (Release: r, Version: version!)
                : ((Release Release, SemVersion Version)?)null)
            .Where(candidate => candidate.HasValue)
            .Select(candidate => candidate!.Value)
            .OrderByDescending(candidate => candidate.Version)
            .ThenByDescending(candidate => candidate.Release.PublishedAtUtc)
            .Select(candidate => candidate.Release)
            .FirstOrDefault();

        return release ?? throw new LicenseApiException(404, "Aún no hay instalador publicado.");
    }

    /// <summary>URL firmada del último instalador.</summary>
    public async Task<string> GetLatestInstallerDownloadUrlAsync(CancellationToken ct)
    {
        var release = await GetLatestInstallerAsync(ct);
        return await GetDownloadLocationAsync(release.Id, ct);
    }

    /// <summary>
    /// Empareja por (CustomerId, Email) sin distinguir mayúsculas -- si la persona ya activó antes
    /// (con otro dispositivo u otra licencia del mismo cliente) reusa el mismo CompanyUser en vez
    /// de duplicar.
    /// </summary>
    private async Task<CompanyUser> FindOrCreateCompanyUserAsync(Guid customerId, string fullName, string email, CancellationToken ct)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();

        var existing = await db.CompanyUsers.FirstOrDefaultAsync(
            u => u.CustomerId == customerId && u.Email == normalizedEmail, ct);

        if (existing != null)
        {
            if (!existing.IsActive)
            {
                throw new LicenseApiException(403, $"El usuario {normalizedEmail} está desactivado. Contacta a soporte.");
            }

            return existing;
        }

        var created = new CompanyUser
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            FullName = fullName.Trim(),
            Email = normalizedEmail,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        db.CompanyUsers.Add(created);
        return created;
    }

    private static void EnsureLicenseUsable(License license)
    {
        if (license.Status != LicenseStatus.Active)
        {
            throw new LicenseApiException(403, $"Licencia {license.Status.ToString().ToLowerInvariant()}. Contacta a soporte.");
        }

        if (license.ValidUntil < DateTimeOffset.UtcNow)
        {
            throw new LicenseApiException(403, "Licencia vencida. Contacta a soporte para renovarla.");
        }
    }

    private static DateOnly CurrentPeriod()
    {
        var nowUtc = DateTime.UtcNow;
        return new DateOnly(nowUtc.Year, nowUtc.Month, 1);
    }

    private static int ParseFeatureInt(Dictionary<string, string> effectiveFeatures, string code, int defaultValue) =>
        effectiveFeatures.TryGetValue(code, out var raw) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : defaultValue;

    private static int RemainingOf(UsageCounter counter) =>
        counter.QuotaLimit == -1 ? -1 : counter.QuotaLimit - counter.Consumed;

    /// <summary>Plan.Features con License.FeatureOverrides encima -- ver comentario en BuildSignedBlobAsync.</summary>
    private static Dictionary<string, string> GetEffectiveFeatures(License license)
    {
        var effective = new Dictionary<string, string>(license.Plan!.Features);
        foreach (var (code, value) in license.FeatureOverrides)
        {
            effective[code] = value;
        }
        return effective;
    }

    private async Task EnsureCurrentPeriodCountersAsync(License license, CancellationToken ct)
    {
        var period = CurrentPeriod();
        var quotaFeatures = GetEffectiveFeatures(license).Where(f => f.Key.StartsWith("quota.", StringComparison.Ordinal));

        foreach (var (code, rawValue) in quotaFeatures)
        {
            var exists = await db.UsageCounters.AnyAsync(
                c => c.LicenseId == license.Id && c.FeatureCode == code && c.Period == period, ct);
            if (exists)
            {
                continue;
            }

            var limit = int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
            db.UsageCounters.Add(new UsageCounter
            {
                Id = Guid.NewGuid(),
                LicenseId = license.Id,
                FeatureCode = code,
                Period = period,
                QuotaLimit = limit,
                Consumed = 0
            });
        }
    }

    private async Task<(string Json, byte[] Signature)> BuildSignedBlobAsync(License license, Device device, CancellationToken ct)
    {
        var period = CurrentPeriod();
        var counters = await db.UsageCounters
            .Where(c => c.LicenseId == license.Id && c.Period == period)
            .ToDictionaryAsync(c => c.FeatureCode, ct);

        var features = new List<FeatureEntry>();
        foreach (var (code, value) in GetEffectiveFeatures(license))
        {
            // Para quota.*, el blob lleva el REMANENTE vivo (limit - consumed), no el tope estático
            // del plan -- así el cliente cachea un número que ya refleja lo gastado este mes.
            var effectiveValue = code.StartsWith("quota.", StringComparison.Ordinal) && counters.TryGetValue(code, out var counter)
                ? RemainingOf(counter).ToString(CultureInfo.InvariantCulture)
                : value;

            features.Add(new FeatureEntry { Code = code, Value = effectiveValue });
        }

        var blob = new EntitlementBlob
        {
            LicenseId = license.Id.ToString(),
            PlanCode = license.Plan!.Code,
            Features = features,
            IssuedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            OfflineUntilUtc = DateTimeOffset.UtcNow.AddDays(7).ToString("O"),
            DeviceId = device.Id.ToString()
        };

        var json = JsonSerializer.Serialize(blob, WireJsonOptions);
        var signature = signer.Sign(Encoding.UTF8.GetBytes(json));
        return (json, signature);
    }
}

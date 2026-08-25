using System.Globalization;
using System.Text;
using System.Text.Json;
using GvrLicense.Contracts;
using GvrLicense.Domain.Entities;
using GvrLicense.Domain.LicenseKeys;
using GvrLicense.Infrastructure;
using GvrLicense.Infrastructure.Signing;
using Microsoft.EntityFrameworkCore;

namespace GvrLicense.Api.Services;

/// <summary>
/// El motor de negocio detrás de /v1/* (docs/LICENSING_PLAN.md, Pieza 1 "API mínima" + Pieza 3
/// "Servidor manda"). Un caso de uso por método público; cada uno hace su propia validación y
/// lanza <see cref="LicenseApiException"/> con el status HTTP correcto en vez de devolver
/// resultados ambiguos.
/// </summary>
public sealed class LicenseEngine(LicenseDbContext db, IEntitlementSigner signer, JwtSessionTokenService jwt)
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

        var device = license.Devices.FirstOrDefault(d => d.Fingerprint == request.DeviceFingerprint);
        if (device is null)
        {
            if (license.Devices.Count >= license.MaxDevices)
            {
                throw new LicenseApiException(403,
                    $"Esta licencia ya tiene {license.MaxDevices} dispositivo(s) activo(s). Desactiva uno antes de activar este PC.");
            }

            device = new Device
            {
                Id = Guid.NewGuid(),
                LicenseId = license.Id,
                Fingerprint = request.DeviceFingerprint,
                DisplayName = request.DeviceName,
                ActivatedAtUtc = DateTimeOffset.UtcNow,
                LastSeenUtc = DateTimeOffset.UtcNow
            };
            db.Devices.Add(device);
        }
        else
        {
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

        return new HeartbeatResponse
        {
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

    public async Task<UpdateCheckResponse> CheckUpdateAsync(string? currentVersion, string? revitVersion, CancellationToken ct)
    {
        var latest = await db.Releases
            .Where(r => r.Channel == "stable")
            .OrderByDescending(r => r.PublishedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (latest is null || latest.Version == currentVersion)
        {
            return new UpdateCheckResponse { UpdateAvailable = false };
        }

        return new UpdateCheckResponse
        {
            UpdateAvailable = true,
            LatestVersion = latest.Version,
            DownloadUrl = $"/v1/updates/download/{latest.Id}",
            ReleaseNotes = latest.Notes
        };
    }

    /// <summary>
    /// Simplificado a propósito: devuelve la ubicación cruda del artefacto (ArtifactLocation), no
    /// una URL firmada temporal -- eso depende de la elección de storage (volumen vs S3/MinIO) de
    /// docs/LICENSING_PLAN.md, Pieza 6, que todavía no se despliega. Se refina en Fase 3.
    /// </summary>
    public async Task<string> GetDownloadLocationAsync(Guid releaseId, CancellationToken ct)
    {
        var release = await db.Releases.FindAsync([releaseId], ct);
        return release?.ArtifactLocation ?? throw new LicenseApiException(404, "Release no encontrado.");
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

    private static int RemainingOf(UsageCounter counter) =>
        counter.QuotaLimit == -1 ? -1 : counter.QuotaLimit - counter.Consumed;

    private async Task EnsureCurrentPeriodCountersAsync(License license, CancellationToken ct)
    {
        var period = CurrentPeriod();
        var quotaFeatures = license.Plan!.Features.Where(f => f.Key.StartsWith("quota.", StringComparison.Ordinal));

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
        foreach (var (code, value) in license.Plan!.Features)
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
            PlanCode = license.Plan.Code,
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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GvrTools.Licensing.Crypto;
using GvrTools.Licensing.Device;
using GvrTools.Licensing.Entitlements;
using GvrTools.Licensing.Http;
using GvrTools.Licensing.Http.Dto;
using GvrTools.Licensing.Storage;

namespace GvrTools.Licensing
{
    /// <summary>
    /// Orquesta activate / heartbeat / usage / deactivate + cache local.
    /// </summary>
    public sealed class LicenseClient : IDisposable
    {
        private readonly ILicenseApiClient _api;
        private readonly IMachineFingerprint _fingerprint;
        private readonly FileLicenseCacheStore _cache;
        private readonly FileUsageQueueStore _usageQueue;
        private readonly EntitlementService _entitlements;
        private readonly IEntitlementSignatureVerifier _verifier;
        private readonly bool _ownsApi;
        private readonly SemaphoreSlim _usageFlushGate = new SemaphoreSlim(1, 1);

        private string _accessToken;
        private readonly object _gate = new object();

        public LicenseClient(
            ILicenseApiClient api = null,
            IMachineFingerprint fingerprint = null,
            FileLicenseCacheStore cache = null,
            FileUsageQueueStore usageQueue = null,
            IEntitlementSignatureVerifier verifier = null,
            bool ownsApi = false)
        {
            _verifier = verifier ?? new EcdsaEntitlementSignatureVerifier();
            _usageQueue = usageQueue ?? new FileUsageQueueStore();
            _cache = cache ?? new FileLicenseCacheStore();
            _fingerprint = fingerprint ?? new WindowsMachineFingerprint();
            _entitlements = new EntitlementService(_verifier, _usageQueue);
            _api = api;
            _ownsApi = ownsApi || api == null;

            if (_api == null)
            {
                _api = new HttpLicenseApiClient(Config.LicenseApiSettings.ResolveBaseUrl());
                _ownsApi = true;
            }

            LoadFromDisk();
        }

        public IEntitlementService Entitlements => _entitlements;

        public EntitlementService EntitlementService => _entitlements;

        public bool IsLicensed => _entitlements.IsLicensed;

        public string PlanCode => _entitlements.PlanCode;

        public DateTimeOffset? OfflineUntilUtc => _entitlements.OfflineUntilUtc;

        public string DeviceFingerprint => _fingerprint.GetFingerprint();

        public string SupportEmailHint { get; set; } = "soporte@gvr.tools";

        /// <summary>
        /// True tras 401/403 (sesión expirada, device kick, licencia suspendida).
        /// El host debe pedir reactivación con la clave GVR-….
        /// </summary>
        public bool NeedsReactivation { get; private set; }

        public string ReactivationReason { get; private set; }

        /// <summary>Se dispara cuando el servidor invalida la sesión (kick, suspensión, etc.).</summary>
        public event Action SessionInvalidated;

        /// <summary>Se dispara al activar de nuevo o limpiar el flag de reactivación.</summary>
        public event Action SessionRestored;

        public void ClearReactivationFlag()
        {
            NeedsReactivation = false;
            ReactivationReason = null;
            try
            {
                SessionRestored?.Invoke();
            }
            catch
            {
            }
        }

        public void LoadFromDisk()
        {
            if (!_cache.TryLoadEnvelope(out var envelope))
            {
                _entitlements.Clear();
                lock (_gate) _accessToken = null;
                return;
            }

            byte[] signature;
            try
            {
                signature = Convert.FromBase64String(envelope.EntitlementSignatureBase64 ?? string.Empty);
            }
            catch
            {
                _entitlements.Clear();
                lock (_gate) _accessToken = null;
                return;
            }

            lock (_gate) _accessToken = envelope.AccessToken;
            _entitlements.SetDeviceFingerprint(_fingerprint.GetFingerprint());

            // Firma inválida => no confiar. Gracia vencida => conservar token para heartbeat.
            if (!_entitlements.TryApplySignedBlob(envelope.EntitlementJson, signature, _fingerprint.GetFingerprint()))
            {
                if (!_verifier.TryVerify(envelope.EntitlementJson, signature, out _))
                {
                    ClearLocal();
                }
            }
        }

        public async Task ActivateAsync(string licenseKey, string userFullName, string userEmail, CancellationToken ct)
        {
            var normalizedLicenseKey = (licenseKey ?? string.Empty)
                .Trim()
                .ToUpperInvariant()
                .Replace(' ', '-');
            while (normalizedLicenseKey.Contains("--"))
                normalizedLicenseKey = normalizedLicenseKey.Replace("--", "-");

            var response = await _api.ActivateAsync(new ActivateRequest
            {
                LicenseKey = normalizedLicenseKey,
                DeviceFingerprint = _fingerprint.GetFingerprint(),
                DeviceName = Environment.MachineName,
                UserFullName = (userFullName ?? string.Empty).Trim(),
                UserEmail = (userEmail ?? string.Empty).Trim()
            }, ct).ConfigureAwait(false);

            ApplyServerEntitlements(response.AccessToken, response.EntitlementJson, response.EntitlementSignatureBase64);
            ClearReactivationFlag();
            await FlushUsageQueueAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Renueva JWT + gracia. Devuelve true si se actualizó el cache. No lanza ante fallo de red
        /// (el llamador usa cache); sí lanza LicenseApiClientException 401/403 (sesión/licencia).
        /// </summary>
        public async Task<bool> TryHeartbeatAsync(CancellationToken ct)
        {
            string token;
            lock (_gate) token = _accessToken;
            if (string.IsNullOrEmpty(token)) return false;

            try
            {
                var response = await _api.HeartbeatAsync(token, new HeartbeatRequest
                {
                    DeviceFingerprint = _fingerprint.GetFingerprint()
                }, ct).ConfigureAwait(false);

                // Servidor renueva el AccessToken en cada heartbeat (sliding expiry 14 días).
                var nextToken = string.IsNullOrEmpty(response.AccessToken) ? token : response.AccessToken;
                ApplyServerEntitlements(nextToken, response.EntitlementJson, response.EntitlementSignatureBase64);
                ClearReactivationFlag();
                await FlushUsageQueueAsync(ct).ConfigureAwait(false);
                return true;
            }
            catch (LicenseApiClientException ex) when (ex.StatusCode == 401 || ex.StatusCode == 403)
            {
                MarkNeedsReactivation(ex.Message);
                ClearLocal();
                throw;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // Red / timeout: seguir con cache si aún está en gracia.
                return false;
            }
        }

        public async Task DeactivateAsync(CancellationToken ct)
        {
            string token;
            lock (_gate) token = _accessToken;

            if (!string.IsNullOrEmpty(token))
            {
                try
                {
                    await _api.DeactivateAsync(token, new DeactivateRequest
                    {
                        DeviceFingerprint = _fingerprint.GetFingerprint()
                    }, ct).ConfigureAwait(false);
                }
                catch (LicenseApiClientException ex) when (ex.StatusCode == 404)
                {
                    // Ya liberado en servidor.
                }
            }

            ClearLocal();
        }

        public async Task FlushUsageQueueAsync(CancellationToken ct)
        {
            await _usageFlushGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                string token;
                lock (_gate) token = _accessToken;
                if (string.IsNullOrEmpty(token)) return;

                // Drena atómicamente: evita que un ReplaceAll concurrente borre eventos nuevos.
                var pending = _usageQueue.TakeAll();
                if (pending.Count == 0) return;

                var leftover = new List<UsageEventDto>();
                for (var i = 0; i < pending.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var item = pending[i];
                    try
                    {
                        if (string.IsNullOrEmpty(item.DeviceFingerprint))
                            item.DeviceFingerprint = _fingerprint.GetFingerprint();

                        var response = await _api.ReportUsageAsync(token, item, ct).ConfigureAwait(false);
                        if (response?.Remaining != null)
                            _entitlements.SetRemaining(item.FeatureCode, response.Remaining);
                    }
                    catch (LicenseApiClientException ex) when (ex.StatusCode == 401 || ex.StatusCode == 403)
                    {
                        MarkNeedsReactivation(ex.Message);
                        ClearLocal();
                        leftover.Clear();
                        break;
                    }
                    catch (Exception)
                    {
                        for (var j = i; j < pending.Count; j++)
                            leftover.Add(pending[j]);
                        break;
                    }
                }

                if (leftover.Count > 0)
                    _usageQueue.PrependAll(leftover);
            }
            finally
            {
                _usageFlushGate.Release();
            }
        }

        /// <summary>
        /// Consulta /v1/updates/check. Devuelve null si no hay red o no hay update.
        /// </summary>
        public async Task<UpdateCheckResponse> TryCheckForUpdateAsync(string currentVersion, string revitVersion, CancellationToken ct)
        {
            try
            {
                var response = await _api.CheckForUpdateAsync(currentVersion, revitVersion, ct).ConfigureAwait(false);
                if (response == null || !response.UpdateAvailable)
                    return null;
                return response;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Resuelve una URL absoluta descargable (MinIO firmada) a partir del DownloadUrl del check.
        /// </summary>
        public async Task<string> ResolveUpdateDownloadUrlAsync(UpdateCheckResponse update, CancellationToken ct)
        {
            if (update == null) return null;

            var raw = update.DownloadUrl;
            if (string.IsNullOrWhiteSpace(raw))
                return _api.BaseUrl.TrimEnd('/') + "/download";

            // Absolute already (MinIO or CDN)
            if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return raw;

            // /v1/updates/download/{guid}
            var marker = "/v1/updates/download/";
            var idx = raw.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var idPart = raw.Substring(idx + marker.Length).Trim().Trim('/');
                var slash = idPart.IndexOf('/');
                if (slash >= 0) idPart = idPart.Substring(0, slash);
                if (Guid.TryParse(idPart, out var releaseId))
                {
                    var download = await _api.GetUpdateDownloadAsync(releaseId, ct).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(download?.Location))
                        return download.Location;
                }
            }

            if (raw.StartsWith("/"))
                return _api.BaseUrl.TrimEnd('/') + raw;

            return _api.BaseUrl.TrimEnd('/') + "/" + raw.TrimStart('/');
        }

        public void ClearLocal()
        {
            _entitlements.Clear();
            lock (_gate) _accessToken = null;
            _cache.Clear();
            _usageQueue.Clear();
        }

        public void Dispose()
        {
            _usageFlushGate.Dispose();
            if (_ownsApi && _api is IDisposable disposable)
                disposable.Dispose();
        }

        private void MarkNeedsReactivation(string serverMessage)
        {
            NeedsReactivation = true;
            ReactivationReason = string.IsNullOrWhiteSpace(serverMessage)
                ? "Sesión de licencia expirada. Vuelve a activar con tu clave GVR-…."
                : serverMessage.Trim();
            try
            {
                SessionInvalidated?.Invoke();
            }
            catch
            {
                // El host no debe tumbar el cliente de licencia.
            }
        }

        private void ApplyServerEntitlements(string accessToken, string entitlementJson, string signatureBase64)
        {
            var signature = Convert.FromBase64String(signatureBase64 ?? string.Empty);
            _entitlements.SetDeviceFingerprint(_fingerprint.GetFingerprint());
            if (!_entitlements.TryApplySignedBlob(entitlementJson, signature, _fingerprint.GetFingerprint()))
                throw new InvalidOperationException("El servidor devolvió un blob de licencia inválido.");

            lock (_gate) _accessToken = accessToken;

            _cache.SaveEnvelope(new LicenseCacheEnvelope
            {
                AccessToken = accessToken,
                EntitlementJson = entitlementJson,
                EntitlementSignatureBase64 = signatureBase64
            });
        }

    }
}

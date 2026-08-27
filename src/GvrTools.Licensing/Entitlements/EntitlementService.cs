using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using GvrTools.Licensing.Crypto;
using GvrTools.Licensing.Http.Dto;
using GvrTools.Licensing.Storage;

namespace GvrTools.Licensing.Entitlements
{
    /// <summary>
    /// Lee el blob cacheado verificado. TryConsume decrementa remaining local y encola usage
    /// para reconciliar online (docs/LICENSING_PLAN.md, reglas de consumo offline).
    /// </summary>
    public sealed class EntitlementService : IEntitlementService
    {
        private readonly IEntitlementSignatureVerifier _verifier;
        private readonly FileUsageQueueStore _usageQueue;
        private readonly object _gate = new object();

        private EntitlementBlob _blob;
        private Dictionary<string, string> _features = new Dictionary<string, string>(StringComparer.Ordinal);
        private string _deviceFingerprint;

        public EntitlementService(IEntitlementSignatureVerifier verifier, FileUsageQueueStore usageQueue = null)
        {
            _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
            _usageQueue = usageQueue ?? new FileUsageQueueStore();
        }

        public EntitlementBlob CurrentBlob
        {
            get { lock (_gate) return _blob; }
        }

        public bool IsLicensed
        {
            get
            {
                lock (_gate)
                {
                    return _blob != null && IsWithinGrace(_blob);
                }
            }
        }

        public string PlanCode
        {
            get
            {
                lock (_gate)
                {
                    return _blob?.PlanCode;
                }
            }
        }

        public DateTimeOffset? OfflineUntilUtc
        {
            get
            {
                lock (_gate)
                {
                    return TryParseUtc(_blob?.OfflineUntilUtc, out var dt) ? dt : (DateTimeOffset?)null;
                }
            }
        }

        /// <summary>Carga blob firmado; false si firma inválida o gracia vencida.</summary>
        public bool TryApplySignedBlob(string rawJson, byte[] signature, string deviceFingerprint = null)
        {
            if (!_verifier.TryVerify(rawJson, signature, out var blob) || blob == null)
                return false;

            if (!IsWithinGrace(blob))
                return false;

            lock (_gate)
            {
                _blob = blob;
                _features = ToDictionary(blob);
                if (!string.IsNullOrEmpty(deviceFingerprint))
                    _deviceFingerprint = deviceFingerprint;
            }

            return true;
        }

        public void Clear()
        {
            lock (_gate)
            {
                _blob = null;
                _features = new Dictionary<string, string>(StringComparer.Ordinal);
            }
        }

        public bool CanUse(string featureCode)
        {
            if (string.IsNullOrEmpty(featureCode)) return true;

            lock (_gate)
            {
                if (_blob == null || !IsWithinGrace(_blob)) return false;
                if (!_features.TryGetValue(featureCode, out var value)) return false;
                return IsTruthy(value);
            }
        }

        public int Remaining(string featureCode)
        {
            lock (_gate)
            {
                if (_blob == null || !IsWithinGrace(_blob)) return 0;
                if (!_features.TryGetValue(featureCode, out var value)) return 0;
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return 0;
                return n;
            }
        }

        public int QuotaLimit(string featureCode)
        {
            if (string.IsNullOrEmpty(featureCode)) return 0;

            lock (_gate)
            {
                if (_blob == null || !IsWithinGrace(_blob)) return 0;
                var limitCode = featureCode + ".limit";
                if (!_features.TryGetValue(limitCode, out var value)) return 0;
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return 0;
                return n;
            }
        }

        public bool TryConsume(string featureCode, int quantity)
        {
            if (quantity <= 0) return true;

            lock (_gate)
            {
                if (_blob == null || !IsWithinGrace(_blob)) return false;
                if (!_features.TryGetValue(featureCode, out var value)) return false;
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var remaining))
                    return false;

                if (remaining == -1)
                {
                    EnqueueUsageLocked(featureCode, quantity);
                    return true;
                }

                if (remaining < quantity) return false;

                remaining -= quantity;
                _features[featureCode] = remaining.ToString(CultureInfo.InvariantCulture);
                UpdateFeatureInBlob(featureCode, _features[featureCode]);
                EnqueueUsageLocked(featureCode, quantity);
                return true;
            }
        }

        /// <summary>Tras un report exitoso online, alinea remaining con el servidor.</summary>
        public void SetRemaining(string featureCode, int? remaining)
        {
            if (remaining == null || string.IsNullOrEmpty(featureCode)) return;

            lock (_gate)
            {
                if (_blob == null) return;
                _features[featureCode] = remaining.Value.ToString(CultureInfo.InvariantCulture);
                UpdateFeatureInBlob(featureCode, _features[featureCode]);
            }
        }

        public void SetDeviceFingerprint(string fingerprint)
        {
            lock (_gate)
            {
                _deviceFingerprint = fingerprint;
            }
        }

        private void EnqueueUsageLocked(string featureCode, int quantity)
        {
            var fp = _deviceFingerprint ?? string.Empty;
            _usageQueue.Enqueue(new UsageEventDto
            {
                DeviceFingerprint = fp,
                EventId = Guid.NewGuid(),
                FeatureCode = featureCode,
                Quantity = quantity,
                OccurredAtUtc = DateTime.UtcNow
            });
        }

        private void UpdateFeatureInBlob(string code, string value)
        {
            if (_blob?.Features == null) return;
            foreach (var entry in _blob.Features)
            {
                if (string.Equals(entry.Code, code, StringComparison.OrdinalIgnoreCase))
                {
                    entry.Value = value;
                    return;
                }
            }
        }

        private static Dictionary<string, string> ToDictionary(EntitlementBlob blob)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (blob.Features == null) return map;
            foreach (var entry in blob.Features)
            {
                if (entry == null || string.IsNullOrEmpty(entry.Code)) continue;
                map[entry.Code] = entry.Value ?? string.Empty;
            }
            return map;
        }

        private static bool IsWithinGrace(EntitlementBlob blob)
        {
            if (!TryParseUtc(blob.OfflineUntilUtc, out var until)) return false;
            return DateTimeOffset.UtcNow <= until;
        }

        private static bool TryParseUtc(string value, out DateTimeOffset dt)
        {
            return DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out dt);
        }

        private static bool IsTruthy(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(value, "0", StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(value, "no", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using GvrTools.Licensing.Entitlements;

namespace GvrTools.Licensing
{
    /// <summary>
    /// Singleton de proceso del add-in: un LicenseClient compartido por App, ribbon y tools.
    /// </summary>
    public static class LicenseRuntime
    {
        private static readonly object Gate = new object();
        private static LicenseClient _client;
        private static int _initialized;

        public static LicenseClient Client
        {
            get
            {
                EnsureInitialized();
                return _client;
            }
        }

        public static IEntitlementService Entitlements => Client.Entitlements;

        public static bool IsLicensed => Client.IsLicensed;

        public static void EnsureInitialized()
        {
            if (Interlocked.CompareExchange(ref _initialized, 1, 0) != 0)
                return;

            lock (Gate)
            {
                if (_client != null) return;
                _client = new LicenseClient();
            }
        }

        /// <summary>
        /// Heartbeat con timeout corto (~2.5s). Nunca tumba el add-in: excepciones de red se
        /// tragan; 401/403 limpian la licencia local.
        /// </summary>
        public static async Task WarmupAsync(CancellationToken externalCt = default)
        {
            EnsureInitialized();

            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt))
            {
                cts.CancelAfter(TimeSpan.FromMilliseconds(2500));
                try
                {
                    await _client.TryHeartbeatAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // timeout o cancelación externa: seguir con cache
                }
                catch (Http.LicenseApiClientException)
                {
                    // ya limpió cache en TryHeartbeatAsync
                }
                catch (Exception)
                {
                    // no tumbar OnStartup
                }
            }
        }

        /// <summary>Solo tests / reinicio controlado.</summary>
        public static void ResetForTests()
        {
            lock (Gate)
            {
                _client?.Dispose();
                _client = null;
                Interlocked.Exchange(ref _initialized, 0);
            }
        }
    }
}

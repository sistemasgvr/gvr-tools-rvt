using System.Threading;
using System.Threading.Tasks;
using GvrTools.Licensing.Http.Dto;

namespace GvrTools.Licensing.Http
{
    /// <summary>
    /// Envoltorio sobre HttpClient para los cuatro endpoints de docs/LICENSING_PLAN.md ("API mínima
    /// v1"). Deliberadamente sin dependencias NuGet: HttpClient + System.Text.Json ya están en
    /// net48 y net8.0-windows (ver GvrTools.Licensing.csproj).
    /// </summary>
    public interface ILicenseApiClient
    {
        Task<ActivateResponse> ActivateAsync(ActivateRequest request, CancellationToken ct);

        Task<HeartbeatResponse> HeartbeatAsync(HeartbeatRequest request, CancellationToken ct);

        /// <summary>Idempotente por UsageEventDto.EventId -- reintentar tras un fallo de red es seguro.</summary>
        Task ReportUsageAsync(UsageEventDto usageEvent, CancellationToken ct);

        Task<UpdateCheckResponse> CheckForUpdateAsync(string currentVersion, string revitVersion, CancellationToken ct);
    }
}

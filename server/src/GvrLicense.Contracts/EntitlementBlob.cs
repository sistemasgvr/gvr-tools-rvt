namespace GvrLicense.Contracts;

/// <summary>
/// Forma exacta que se serializa a JSON y se firma con ECDsa P-256 (docs/LICENSING_PLAN.md, "Tokens
/// y gracia offline"). Espejo: src/GvrTools.Licensing/Entitlements/EntitlementBlob.cs.
/// </summary>
public sealed class EntitlementBlob
{
    public required string LicenseId { get; init; }
    public required string PlanCode { get; init; }
    public required Dictionary<string, string> Features { get; init; }
    public required DateTimeOffset IssuedAt { get; init; }
    public required DateTimeOffset OfflineUntil { get; init; }
    public required string DeviceId { get; init; }
}

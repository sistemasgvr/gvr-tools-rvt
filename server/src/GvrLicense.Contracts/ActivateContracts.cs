namespace GvrLicense.Contracts;

public sealed class ActivateRequest
{
    public required string LicenseKey { get; init; }
    public required string DeviceFingerprint { get; init; }
    public string? DeviceName { get; init; }
}

public sealed class ActivateResponse
{
    /// <summary>JWT (ES256) -- mandar como "Authorization: Bearer {AccessToken}" en /v1/heartbeat y /v1/usage.</summary>
    public required string AccessToken { get; init; }
    public required string EntitlementJson { get; init; }
    public required string EntitlementSignatureBase64 { get; init; }
}

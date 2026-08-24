namespace GvrLicense.Contracts;

public sealed class ActivateRequest
{
    public required string LicenseKey { get; init; }
    public required string DeviceFingerprint { get; init; }
    public string? DeviceName { get; init; }
}

public sealed class ActivateResponse
{
    public required string SessionToken { get; init; }
    public required string EntitlementJson { get; init; }
    public required string EntitlementSignatureBase64 { get; init; }
}

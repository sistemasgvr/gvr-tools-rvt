namespace GvrLicense.Contracts;

public sealed class HeartbeatRequest
{
    public required string SessionToken { get; init; }
    public required string DeviceFingerprint { get; init; }
}

public sealed class HeartbeatResponse
{
    public required string EntitlementJson { get; init; }
    public required string EntitlementSignatureBase64 { get; init; }
}

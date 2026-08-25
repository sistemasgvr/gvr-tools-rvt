namespace GvrLicense.Contracts;

/// <summary>El JWT ya no va en el body: viaja como "Authorization: Bearer" (ver ActivateResponse.AccessToken).</summary>
public sealed class HeartbeatRequest
{
    public required string DeviceFingerprint { get; init; }
}

public sealed class HeartbeatResponse
{
    public required string EntitlementJson { get; init; }
    public required string EntitlementSignatureBase64 { get; init; }
}

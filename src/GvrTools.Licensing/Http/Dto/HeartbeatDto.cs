namespace GvrTools.Licensing.Http.Dto
{
    /// <summary>
    /// POST /v1/heartbeat -- renueva offline_until y refresca entitlements/cuotas. El JWT no va
    /// aquí: ILicenseApiClient lo manda como header "Authorization: Bearer".
    /// </summary>
    public sealed class HeartbeatRequest
    {
        public string DeviceFingerprint { get; set; }
    }

    public sealed class HeartbeatResponse
    {
        public string EntitlementJson { get; set; }
        public string EntitlementSignatureBase64 { get; set; }
    }
}

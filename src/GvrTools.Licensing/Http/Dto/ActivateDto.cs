namespace GvrTools.Licensing.Http.Dto
{
    /// <summary>POST /v1/activate.</summary>
    public sealed class ActivateRequest
    {
        public string LicenseKey { get; set; }
        public string DeviceFingerprint { get; set; }
        public string DeviceName { get; set; }
    }

    public sealed class ActivateResponse
    {
        public string SessionToken { get; set; }
        public string EntitlementJson { get; set; }
        public string EntitlementSignatureBase64 { get; set; }
    }
}

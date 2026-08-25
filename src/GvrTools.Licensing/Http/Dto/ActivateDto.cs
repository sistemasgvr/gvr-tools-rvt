namespace GvrTools.Licensing.Http.Dto
{
    /// <summary>POST /v1/activate.</summary>
    public sealed class ActivateRequest
    {
        public string LicenseKey { get; set; }
        public string DeviceFingerprint { get; set; }
        public string DeviceName { get; set; }

        /// <summary>El seat se cuenta por persona, no por dispositivo -- ver docs/LICENSING_PLAN.md, "Métodos de suscripción".</summary>
        public string UserFullName { get; set; }
        public string UserEmail { get; set; }
    }

    public sealed class ActivateResponse
    {
        /// <summary>JWT (ES256) -- mandar como "Authorization: Bearer {AccessToken}" en /v1/heartbeat y /v1/usage.</summary>
        public string AccessToken { get; set; }
        public string EntitlementJson { get; set; }
        public string EntitlementSignatureBase64 { get; set; }
    }
}

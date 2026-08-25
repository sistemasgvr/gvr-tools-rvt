using System;

namespace GvrTools.Licensing.Http.Dto
{
    /// <summary>
    /// POST /v1/usage. EventId es un GUID generado por el cliente al momento del consumo (no al
    /// reportarlo), así un reintento de red por caída offline reenvía el mismo EventId y el servidor
    /// lo descarta por la constraint única (ver LICENSING_PLAN.md, "Dónde vive la lógica"). El JWT
    /// no va aquí: ILicenseApiClient lo manda como header "Authorization: Bearer".
    /// </summary>
    public sealed class UsageEventDto
    {
        public string DeviceFingerprint { get; set; }
        public Guid EventId { get; set; }
        public string FeatureCode { get; set; }
        public int Quantity { get; set; }
        public DateTimeOffset OccurredAtUtc { get; set; }
    }
}

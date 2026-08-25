using System.Collections.Generic;

namespace GvrTools.Licensing.Entitlements
{
    /// <summary>
    /// Forma parseada del blob firmado que devuelve el License API en /v1/activate y /v1/heartbeat.
    /// Espejo de EntitlementBlob en GvrLicense.Contracts (server/); no se comparte código porque el
    /// cliente no puede referenciar un proyecto net10.0 desde net48 -- debe mantenerse sincronizado
    /// campo a campo a mano (ver docs/LICENSING_PLAN.md, "Tokens y gracia offline").
    ///
    /// Solo tipos primitivos a propósito (string, List, nada de Dictionary/DateTimeOffset nativo):
    /// este tipo se deserializa con DataContractJsonSerializer (net48 no tiene System.Text.Json en
    /// el framework), que no es compatible byte a byte con la salida de System.Text.Json del
    /// servidor para Dictionary ni DateTimeOffset. Ver Crypto/EntitlementBlobParser.
    /// </summary>
    public sealed class EntitlementBlob
    {
        public string LicenseId { get; set; }
        public string PlanCode { get; set; }
        public List<FeatureEntry> Features { get; set; }
        public string IssuedAtUtc { get; set; }
        public string OfflineUntilUtc { get; set; }
        public string DeviceId { get; set; }
    }

    public sealed class FeatureEntry
    {
        public string Code { get; set; }
        public string Value { get; set; }
    }
}

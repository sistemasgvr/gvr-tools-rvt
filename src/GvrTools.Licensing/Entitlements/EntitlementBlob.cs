using System;
using System.Collections.Generic;

namespace GvrTools.Licensing.Entitlements
{
    /// <summary>
    /// Forma parseada del blob firmado que devuelve el License API en /v1/activate y /v1/heartbeat.
    /// Espejo de EntitlementBlob en GvrLicense.Contracts (server/); no se comparte código porque el
    /// cliente no puede referenciar un proyecto net10.0 desde net48 -- debe mantenerse sincronizado
    /// campo a campo a mano (ver docs/LICENSING_PLAN.md, "Tokens y gracia offline").
    /// </summary>
    public sealed class EntitlementBlob
    {
        public string LicenseId { get; set; }
        public string PlanCode { get; set; }
        public Dictionary<string, string> Features { get; set; }
        public DateTimeOffset IssuedAt { get; set; }
        public DateTimeOffset OfflineUntil { get; set; }
        public string DeviceId { get; set; }
    }
}

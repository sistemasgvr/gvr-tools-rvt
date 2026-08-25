namespace GvrLicense.Domain.Entities;

/// <summary>
/// Un dispositivo activado. El seat lo topa <c>License.MaxUsers</c> contando personas
/// (<see cref="CompanyUser"/>) distintas, no filas de Device: la misma persona puede activar en
/// más de un dispositivo (oficina + laptop) sin gastar un seat extra.
/// </summary>
public sealed class Device
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public License? License { get; set; }

    public Guid CompanyUserId { get; set; }
    public CompanyUser? CompanyUser { get; set; }

    /// <summary>Hash de MachineGuid + volumen de sistema + SID -- nunca el dato crudo (ver GvrTools.Licensing/Device).</summary>
    public string Fingerprint { get; set; } = string.Empty;

    public string? DisplayName { get; set; }
    public DateTimeOffset ActivatedAtUtc { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; }
}

namespace GvrLicense.Domain.Entities;

/// <summary>Un seat activado. Node-locked: License.MaxDevices topa cuántos puede tener una licencia.</summary>
public sealed class Device
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public License? License { get; set; }

    /// <summary>Hash de MachineGuid + volumen de sistema + SID -- nunca el dato crudo (ver GvrTools.Licensing/Device).</summary>
    public string Fingerprint { get; set; } = string.Empty;

    public string? DisplayName { get; set; }
    public DateTimeOffset ActivatedAtUtc { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; }
}

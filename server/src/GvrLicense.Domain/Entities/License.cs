namespace GvrLicense.Domain.Entities;

public enum LicenseStatus
{
    Active,
    Suspended,
    Expired
}

public sealed class License
{
    public Guid Id { get; set; }

    /// <summary>Formato GVR-XXXX-XXXX-XXXX (ver LICENSING_PLAN.md, "Decisiones fijadas" -- generación por Base32 Crockford + checksum).</summary>
    public string Key { get; set; } = string.Empty;

    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public Guid PlanId { get; set; }
    public Plan? Plan { get; set; }

    public LicenseStatus Status { get; set; }
    public DateTimeOffset ValidUntil { get; set; }

    /// <summary>Node-locked: un seat = un dispositivo. Ver Entities/Device.cs.</summary>
    public int MaxDevices { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public List<Device> Devices { get; set; } = [];
    public List<UsageCounter> UsageCounters { get; set; } = [];
}

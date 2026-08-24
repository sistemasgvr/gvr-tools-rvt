namespace GvrLicense.Domain.Entities;

public sealed class Customer
{
    public Guid Id { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string ContactName { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;

    /// <summary>Notas de pago (precio acordado, forma de pago). Nunca vive en el add-in.</summary>
    public string? PaymentNotes { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public List<License> Licenses { get; set; } = [];
}

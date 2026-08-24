namespace GvrLicense.Contracts;

/// <summary>Id = EventId generado por el cliente; la idempotencia se resuelve por constraint única en Postgres.</summary>
public sealed class UsageEventRequest
{
    public required Guid EventId { get; init; }
    public required string LicenseId { get; init; }
    public required string FeatureCode { get; init; }
    public required int Quantity { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
}

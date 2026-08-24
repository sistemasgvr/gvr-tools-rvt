namespace GvrLicense.Domain.Entities;

/// <summary>Un release publicado (docs/LICENSING_PLAN.md, Pieza 4). Canal único "stable" en v1.</summary>
public sealed class Release
{
    public Guid Id { get; set; }
    public string Version { get; set; } = string.Empty;
    public string Channel { get; set; } = "stable";
    public string Checksum { get; set; } = string.Empty;

    /// <summary>Ruta/clave del artefacto en el volumen o bucket S3 -- no la URL pública final (esa se firma al descargar).</summary>
    public string ArtifactLocation { get; set; } = string.Empty;

    public string? Notes { get; set; }
    public string SignatureBase64 { get; set; } = string.Empty;
    public DateTimeOffset PublishedAtUtc { get; set; }
}

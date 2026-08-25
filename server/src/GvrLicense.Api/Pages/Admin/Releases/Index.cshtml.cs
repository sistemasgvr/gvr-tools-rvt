using System.Security.Cryptography;
using GvrLicense.Domain.Entities;
using GvrLicense.Infrastructure;
using GvrLicense.Infrastructure.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GvrLicense.Api.Pages.Admin.Releases;

[RequestFormLimits(MultipartBodyLengthLimit = 524_288_000)]
[RequestSizeLimit(524_288_000)]
public class IndexModel(LicenseDbContext db, IReleaseArtifactStore artifacts) : PageModel
{
    public List<ReleaseRow> Rows { get; private set; } = [];

    /// <summary>Enlace estable para enviar al cliente (redirige al último instalador en MinIO).</summary>
    public string PublicInstallerUrl { get; private set; } = string.Empty;

    public bool MinioConfigured { get; private set; }

    [BindProperty]
    public ReleaseInput Input { get; set; } = new();

    [BindProperty]
    public IFormFile? ArtifactFile { get; set; }

    public async Task OnGetAsync()
    {
        MinioConfigured = artifacts.IsConfigured;
        PublicInstallerUrl = $"{Request.Scheme}://{Request.Host}/download";

        Rows = await db.Releases
            .OrderByDescending(r => r.PublishedAtUtc)
            .Select(r => new ReleaseRow(
                r.Id,
                r.Version,
                r.Channel,
                r.Kind,
                r.FileName,
                r.ArtifactLocation,
                r.Notes,
                r.PublishedAtUtc))
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        MinioConfigured = artifacts.IsConfigured;
        if (!artifacts.IsConfigured)
        {
            ModelState.AddModelError(string.Empty, "MinIO no está configurado en el servidor.");
            await OnGetAsync();
            return Page();
        }

        if (ArtifactFile is null || ArtifactFile.Length == 0)
        {
            ModelState.AddModelError(nameof(ArtifactFile), "Sube el archivo del instalador o del paquete de update.");
            await OnGetAsync();
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Input.Version))
        {
            ModelState.AddModelError("Input.Version", "La versión es obligatoria.");
            await OnGetAsync();
            return Page();
        }

        var kind = string.Equals(Input.Kind, ReleaseKinds.Update, StringComparison.OrdinalIgnoreCase)
            ? ReleaseKinds.Update
            : ReleaseKinds.Installer;

        await using var buffer = new MemoryStream();
        await ArtifactFile.CopyToAsync(buffer);
        buffer.Position = 0;
        var checksum = Convert.ToHexString(await SHA256.HashDataAsync(buffer)).ToLowerInvariant();
        buffer.Position = 0;

        var objectKey = await artifacts.UploadAsync(
            buffer,
            Input.Version.Trim(),
            ArtifactFile.FileName,
            ArtifactFile.ContentType ?? "application/octet-stream",
            HttpContext.RequestAborted);

        db.Releases.Add(new Release
        {
            Id = Guid.NewGuid(),
            Version = Input.Version.Trim(),
            Channel = "stable",
            Kind = kind,
            FileName = Path.GetFileName(ArtifactFile.FileName),
            ArtifactLocation = objectKey,
            Checksum = checksum,
            Notes = string.IsNullOrWhiteSpace(Input.Notes) ? null : Input.Notes.Trim(),
            SignatureBase64 = string.Empty,
            PublishedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        TempData["Saved"] = true;
        TempData["PublicUrl"] = $"{Request.Scheme}://{Request.Host}/download";
        return RedirectToPage();
    }

    public sealed record ReleaseRow(
        Guid Id,
        string Version,
        string Channel,
        string Kind,
        string? FileName,
        string ArtifactLocation,
        string? Notes,
        DateTimeOffset PublishedAtUtc);

    public sealed class ReleaseInput
    {
        public string Version { get; set; } = string.Empty;
        public string Kind { get; set; } = ReleaseKinds.Installer;
        public string? Notes { get; set; }
    }
}

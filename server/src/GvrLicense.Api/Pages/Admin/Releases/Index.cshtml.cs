using System.Security.Cryptography;
using GvrLicense.Domain.Entities;
using GvrLicense.Domain.Versioning;
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
            return await InvalidPageAsync();
        }

        if (ArtifactFile is null || ArtifactFile.Length == 0)
        {
            ModelState.AddModelError(nameof(ArtifactFile), "Sube el archivo del instalador o del paquete de update.");
            return await InvalidPageAsync();
        }

        if (!SemVersion.TryParse(Input.Version, out _))
        {
            ModelState.AddModelError("Input.Version", "Usa una versión válida con formato MAJOR.MINOR.PATCH, por ejemplo 1.0.0.");
            return await InvalidPageAsync();
        }

        var version = SemVersion.Normalize(Input.Version);
        var kind = string.Equals(Input.Kind, ReleaseKinds.Update, StringComparison.OrdinalIgnoreCase)
            ? ReleaseKinds.Update
            : ReleaseKinds.Installer;

        var existingVersions = await db.Releases
            .Where(r => r.Channel == "stable" && r.Kind == kind)
            .Select(r => r.Version)
            .ToListAsync();
        if (existingVersions.Any(existing =>
                SemVersion.TryParse(existing, out var parsed) && parsed!.ToString() == version))
        {
            ModelState.AddModelError("Input.Version", "Ya existe un release estable con esa versión y tipo.");
            return await InvalidPageAsync();
        }

        await using var buffer = new MemoryStream();
        await ArtifactFile.CopyToAsync(buffer);
        buffer.Position = 0;
        var checksum = Convert.ToHexString(await SHA256.HashDataAsync(buffer)).ToLowerInvariant();
        buffer.Position = 0;

        var objectKey = await artifacts.UploadAsync(
            buffer,
            version,
            ArtifactFile.FileName,
            ArtifactFile.ContentType ?? "application/octet-stream",
            HttpContext.RequestAborted);

        db.Releases.Add(new Release
        {
            Id = Guid.NewGuid(),
            Version = version,
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
        if (IsAjaxRequest())
        {
            return new JsonResult(new { redirect = Url.Page("/Admin/Releases/Index") });
        }

        return RedirectToPage();
    }

    private async Task<IActionResult> InvalidPageAsync()
    {
        await OnGetAsync();
        if (!IsAjaxRequest())
        {
            return Page();
        }

        var message = ModelState.Values
            .SelectMany(value => value.Errors)
            .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                ? "No se pudo publicar el release."
                : error.ErrorMessage)
            .FirstOrDefault() ?? "No se pudo publicar el release.";
        return BadRequest(new { message });
    }

    private bool IsAjaxRequest() =>
        string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

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

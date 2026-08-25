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
public class EditModel(LicenseDbContext db, IReleaseArtifactStore artifacts) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    [BindProperty]
    public ReleaseInput Input { get; set; } = new();

    [BindProperty]
    public IFormFile? ArtifactFile { get; set; }

    public string? ExistingFileName { get; private set; }
    public bool MinioConfigured => artifacts.IsConfigured;

    public async Task<IActionResult> OnGetAsync()
    {
        var release = await db.Releases.FindAsync(Id);
        if (release is null)
        {
            return NotFound();
        }

        ExistingFileName = release.FileName;
        Input = new ReleaseInput
        {
            Version = release.Version,
            Kind = release.Kind,
            Notes = release.Notes
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var release = await db.Releases.FindAsync(Id);
        if (release is null)
        {
            return NotFound();
        }

        ExistingFileName = release.FileName;
        if (!SemVersion.TryParse(Input.Version, out _))
        {
            ModelState.AddModelError("Input.Version", "Usa una versión válida con formato MAJOR.MINOR.PATCH, por ejemplo 1.0.0.");
            return InvalidEditPage("Usa una versión válida con formato MAJOR.MINOR.PATCH, por ejemplo 1.0.0.");
        }

        var version = SemVersion.Normalize(Input.Version);
        var kind = string.Equals(Input.Kind, ReleaseKinds.Update, StringComparison.OrdinalIgnoreCase)
            ? ReleaseKinds.Update
            : ReleaseKinds.Installer;

        var existingVersions = await db.Releases
            .Where(r => r.Id != Id && r.Channel == "stable" && r.Kind == kind)
            .Select(r => r.Version)
            .ToListAsync();
        if (existingVersions.Any(existing =>
                SemVersion.TryParse(existing, out var parsed) && parsed!.ToString() == version))
        {
            ModelState.AddModelError("Input.Version", "Ya existe un release estable con esa versión y tipo.");
            return InvalidEditPage("Ya existe un release estable con esa versión y tipo.");
        }

        if (ArtifactFile is { Length: > 0 })
        {
            if (!artifacts.IsConfigured)
            {
                ModelState.AddModelError(nameof(ArtifactFile), "MinIO no está configurado en el servidor.");
                return InvalidEditPage("MinIO no está configurado en el servidor.");
            }

            await using var buffer = new MemoryStream();
            await ArtifactFile.CopyToAsync(buffer);
            buffer.Position = 0;
            release.Checksum = Convert.ToHexString(await SHA256.HashDataAsync(buffer)).ToLowerInvariant();
            buffer.Position = 0;
            release.ArtifactLocation = await artifacts.UploadAsync(
                buffer,
                version,
                ArtifactFile.FileName,
                ArtifactFile.ContentType ?? "application/octet-stream",
                HttpContext.RequestAborted);
            release.FileName = Path.GetFileName(ArtifactFile.FileName);
        }

        release.Version = version;
        release.Kind = kind;
        release.Notes = string.IsNullOrWhiteSpace(Input.Notes) ? null : Input.Notes.Trim();
        await db.SaveChangesAsync();

        TempData["Saved"] = true;
        if (IsAjaxRequest())
        {
            return new JsonResult(new { redirect = Url.Page("/Admin/Releases/Edit", new { id = Id }) });
        }

        return RedirectToPage(new { id = Id });
    }

    private IActionResult InvalidEditPage(string message)
    {
        if (!IsAjaxRequest())
        {
            return Page();
        }

        return BadRequest(new { message });
    }

    private bool IsAjaxRequest() =>
        string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

    public sealed class ReleaseInput
    {
        public string Version { get; set; } = string.Empty;
        public string Kind { get; set; } = ReleaseKinds.Installer;
        public string? Notes { get; set; }
    }
}

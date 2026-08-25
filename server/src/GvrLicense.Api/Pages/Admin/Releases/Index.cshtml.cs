using GvrLicense.Domain.Entities;
using GvrLicense.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GvrLicense.Api.Pages.Admin.Releases;

public class IndexModel(LicenseDbContext db) : PageModel
{
    public List<ReleaseRow> Rows { get; private set; } = [];

    [BindProperty]
    public ReleaseInput Input { get; set; } = new();

    public async Task OnGetAsync()
    {
        Rows = await db.Releases
            .OrderByDescending(r => r.PublishedAtUtc)
            .Select(r => new ReleaseRow(r.Id, r.Version, r.Channel, r.ArtifactLocation, r.Notes, r.PublishedAtUtc))
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        db.Releases.Add(new Release
        {
            Id = Guid.NewGuid(),
            Version = Input.Version.Trim(),
            Channel = "stable",
            ArtifactLocation = Input.ArtifactLocation.Trim(),
            Checksum = Input.Checksum?.Trim() ?? string.Empty,
            Notes = string.IsNullOrWhiteSpace(Input.Notes) ? null : Input.Notes.Trim(),
            SignatureBase64 = string.Empty, // firmado en pipeline de release (Fase 3)
            PublishedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        TempData["Saved"] = true;
        return RedirectToPage();
    }

    public sealed record ReleaseRow(Guid Id, string Version, string Channel, string ArtifactLocation, string? Notes, DateTimeOffset PublishedAtUtc);

    public sealed class ReleaseInput
    {
        public string Version { get; set; } = string.Empty;
        public string ArtifactLocation { get; set; } = string.Empty;
        public string? Checksum { get; set; }
        public string? Notes { get; set; }
    }
}

using GvrLicense.Domain.Entities;
using GvrLicense.Domain.Versioning;
using GvrLicense.Infrastructure;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GvrLicense.Api.Pages;

/// <summary>
/// Landing pública en "/download" (template <c>_PublicLayout</c>).
/// La descarga real del .exe sigue en GET /download/file (redirect MinIO firmado).
/// </summary>
public class DownloadModel(LicenseDbContext db) : PageModel
{
    public bool HasInstaller { get; private set; }
    public string? LatestVersion { get; private set; }
    public string? LatestNotes { get; private set; }
    public DateTimeOffset? LatestPublishedAtUtc { get; private set; }
    public string? SupportEmail { get; private set; }
    public string? TosUrl { get; private set; }
    public string? PrivacyUrl { get; private set; }

    public async Task OnGetAsync()
    {
        var settings = await db.AppSettings.OrderBy(s => s.Id).FirstOrDefaultAsync();
        SupportEmail = settings?.SupportEmail;
        TosUrl = settings?.TermsOfServiceUrl;
        PrivacyUrl = settings?.PrivacyPolicyUrl;

        var installers = await db.Releases
            .Where(r => r.Channel == "stable" && r.Kind == ReleaseKinds.Installer)
            .ToListAsync();

        var latest = installers
            .Select(r => SemVersion.TryParse(r.Version, out var version)
                ? (Release: r, Version: version!)
                : ((Release Release, SemVersion Version)?)null)
            .Where(candidate => candidate.HasValue)
            .Select(candidate => candidate!.Value)
            .OrderByDescending(candidate => candidate.Version)
            .ThenByDescending(candidate => candidate.Release.PublishedAtUtc)
            .Select(candidate => candidate.Release)
            .FirstOrDefault();

        if (latest is null)
            return;

        HasInstaller = true;
        LatestVersion = latest.Version;
        LatestNotes = latest.Notes;
        LatestPublishedAtUtc = latest.PublishedAtUtc;
    }
}

using GvrLicense.Domain.Entities;
using GvrLicense.Domain.Versioning;
using GvrLicense.Infrastructure;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GvrLicense.Api.Pages;

/// <summary>
/// Landing pública en "/": CTA de descarga + pasos. Las versiones las publica el admin
/// en /Admin/Releases; el cliente solo ve la última (sin catálogo).
/// </summary>
public class IndexModel(LicenseDbContext db) : PageModel
{
    public ReleaseCard? LatestInstaller { get; private set; }
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

        LatestInstaller = installers
            .Select(r => SemVersion.TryParse(r.Version, out var version)
                ? (Release: r, Version: version!)
                : ((Release Release, SemVersion Version)?)null)
            .Where(candidate => candidate.HasValue)
            .Select(candidate => candidate!.Value)
            .OrderByDescending(candidate => candidate.Version)
            .ThenByDescending(candidate => candidate.Release.PublishedAtUtc)
            .Select(candidate => new ReleaseCard(
                candidate.Release.Id,
                candidate.Release.Version,
                candidate.Release.Notes,
                candidate.Release.PublishedAtUtc))
            .FirstOrDefault();
    }

    public sealed record ReleaseCard(Guid Id, string Version, string? Notes, DateTimeOffset PublishedAtUtc);
}

using GvrLicense.Domain.Entities;
using GvrLicense.Infrastructure;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GvrLicense.Api.Pages.Admin;

public class IndexModel(LicenseDbContext db) : PageModel
{
    private const int RecentCount = 5;

    public List<LicenseRow> RecentRows { get; private set; } = [];
    public int ActiveCount { get; private set; }
    public int SuspendedCount { get; private set; }
    public int ExpiringSoonCount { get; private set; }
    public int CustomerCount { get; private set; }

    public async Task OnGetAsync()
    {
        var allLicenses = await db.Licenses
            .Include(l => l.Customer)
            .Include(l => l.Plan)
            .Select(l => new { l.Id, l.Key, CustomerName = l.Customer!.CompanyName, PlanCode = l.Plan!.Code, l.Status, l.ValidUntil, l.CreatedAtUtc })
            .ToListAsync();

        RecentRows = allLicenses
            .OrderByDescending(l => l.CreatedAtUtc)
            .Take(RecentCount)
            .Select(l => new LicenseRow(l.Id, l.Key, l.CustomerName, l.PlanCode, l.Status, l.ValidUntil))
            .ToList();

        ActiveCount = allLicenses.Count(l => l.Status == LicenseStatus.Active);
        SuspendedCount = allLicenses.Count(l => l.Status == LicenseStatus.Suspended);
        var soonCutoff = DateTimeOffset.UtcNow.AddDays(30);
        ExpiringSoonCount = allLicenses.Count(l => l.Status == LicenseStatus.Active && l.ValidUntil <= soonCutoff);
        CustomerCount = await db.Customers.CountAsync();
    }

    public sealed record LicenseRow(Guid Id, string Key, string CustomerName, string PlanCode, LicenseStatus Status, DateTimeOffset ValidUntil);
}

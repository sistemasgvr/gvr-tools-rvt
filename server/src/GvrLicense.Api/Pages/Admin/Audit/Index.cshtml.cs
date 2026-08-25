using GvrLicense.Infrastructure;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GvrLicense.Api.Pages.Admin.Audit;

public class IndexModel(LicenseDbContext db) : PageModel
{
    public List<AuditRow> Rows { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Rows = await db.AuditLogs
            .OrderByDescending(a => a.OccurredAtUtc)
            .Take(500)
            .Select(a => new AuditRow(a.Id, a.Actor, a.Action, a.DetailsJson, a.OccurredAtUtc, a.LicenseId))
            .ToListAsync();
    }

    public sealed record AuditRow(Guid Id, string Actor, string Action, string? DetailsJson, DateTimeOffset OccurredAtUtc, Guid? LicenseId);
}

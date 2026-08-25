using GvrLicense.Domain.Entities;
using GvrLicense.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GvrLicense.Api.Pages.Admin.Licenses;

public class IndexModel(LicenseDbContext db) : PageModel
{
    public List<LicenseRow> Rows { get; private set; } = [];

    /// <summary>Para el <select> del modal "Nueva licencia" -- el POST real lo procesa Licenses/Create.</summary>
    public List<SelectListItem> CustomerOptions { get; private set; } = [];
    public List<SelectListItem> PlanOptions { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Rows = await db.Licenses
            .Include(l => l.Customer)
            .Include(l => l.Plan)
            .Include(l => l.Devices)
            .OrderByDescending(l => l.CreatedAtUtc)
            .Select(l => new LicenseRow(
                l.Id, l.Key, l.Customer!.CompanyName, l.Plan!.Code, l.Status, l.ValidUntil, l.Devices.Count, l.MaxDevices))
            .ToListAsync();

        CustomerOptions = await db.Customers
            .OrderBy(c => c.CompanyName)
            .Select(c => new SelectListItem(c.CompanyName, c.Id.ToString()))
            .ToListAsync();

        PlanOptions = await db.Plans
            .OrderBy(p => p.Code)
            .Select(p => new SelectListItem(p.DisplayName, p.Id.ToString()))
            .ToListAsync();
    }

    /// <summary>
    /// El trigger de auditoría (Sql/AuditLogTrigger.sql) ya registra el cambio de status solo con
    /// hacer el UPDATE, así que este handler no necesita escribir auditoría a mano.
    /// </summary>
    public async Task<IActionResult> OnPostToggleAsync(Guid licenseId)
    {
        var license = await db.Licenses.FindAsync(licenseId);
        if (license != null)
        {
            license.Status = license.Status == LicenseStatus.Active ? LicenseStatus.Suspended : LicenseStatus.Active;
            await db.SaveChangesAsync();
        }

        return RedirectToPage();
    }

    public sealed record LicenseRow(
        Guid Id, string Key, string CustomerName, string PlanCode, LicenseStatus Status,
        DateTimeOffset ValidUntil, int DeviceCount, int MaxDevices);
}

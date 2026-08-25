using GvrLicense.Api.Pages.Admin.Plans;
using GvrLicense.Domain.Entities;
using GvrLicense.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GvrLicense.Api.Pages.Admin.Licenses;

public class EditModel(LicenseDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    [BindProperty]
    public LicenseInput Input { get; set; } = new();

    public string LicenseKey { get; private set; } = string.Empty;
    public string CustomerName { get; private set; } = string.Empty;
    public string PlanSummary { get; private set; } = string.Empty;
    public List<DeviceRow> Devices { get; private set; } = [];
    public List<UsageRow> UsageRows { get; private set; } = [];

    /// <summary>Incluye planes descontinuados si es el que ya tiene la licencia -- si no, desaparecería del <select>.</summary>
    public List<SelectListItem> PlanOptions { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        var license = await LoadLicenseAsync();
        if (license is null)
        {
            return NotFound();
        }

        await BindPageAsync(license);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var license = await LoadLicenseAsync();
        if (license is null)
        {
            return NotFound();
        }

        var plan = await db.Plans.FindAsync(Input.PlanId)
            ?? throw new InvalidOperationException("Plan no encontrado.");

        license.PlanId = Input.PlanId;
        license.ValidUntil = new DateTimeOffset(Input.ValidUntil, TimeOnly.MaxValue, TimeSpan.Zero);
        license.MaxUsers = Input.MaxUsers;
        license.FeatureOverrides = PlanFeatureForm.DiffAgainstPlan(plan.Features, Input.Features.ToDictionary());
        await db.SaveChangesAsync();

        TempData["Saved"] = true;
        return RedirectToPage(new { id = Id });
    }

    /// <summary>
    /// Libera un PC (kick seat): borra el device para que otra máquina pueda activar.
    /// El add-in en ese PC dejará de renovar heartbeat con ese device id.
    /// </summary>
    public async Task<IActionResult> OnPostKickDeviceAsync(Guid deviceId)
    {
        var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId && d.LicenseId == Id);
        if (device is null)
        {
            return NotFound();
        }

        db.Devices.Remove(device);
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            LicenseId = Id,
            Actor = User.Identity?.Name ?? "admin",
            Action = "device.kick",
            DetailsJson = $"{{\"deviceId\":\"{deviceId}\",\"fingerprint\":\"{device.Fingerprint}\"}}",
            OccurredAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        TempData["Kicked"] = true;
        return RedirectToPage(new { id = Id });
    }

    private async Task<License?> LoadLicenseAsync() =>
        await db.Licenses
            .Include(l => l.Customer)
            .Include(l => l.Plan)
            .FirstOrDefaultAsync(l => l.Id == Id);

    private async Task BindPageAsync(License license)
    {
        LicenseKey = license.Key;
        CustomerName = license.Customer!.CompanyName;
        PlanSummary = PlanFeatureForm.Summarize(license.Plan!.Features);
        await LoadPlanOptionsAsync(license.PlanId);

        var effective = PlanFeatureForm.Merge(license.Plan.Features, license.FeatureOverrides);
        Input = new LicenseInput
        {
            PlanId = license.PlanId,
            ValidUntil = DateOnly.FromDateTime(license.ValidUntil.UtcDateTime),
            MaxUsers = license.MaxUsers,
            Features = PlanFeatureForm.FromDictionary(effective)
        };

        Devices = await db.Devices
            .Where(d => d.LicenseId == Id)
            .OrderByDescending(d => d.LastSeenUtc)
            .Select(d => new DeviceRow(
                d.Id,
                d.DisplayName ?? d.Fingerprint,
                d.CompanyUser != null ? d.CompanyUser.FullName : "—",
                d.LastSeenUtc,
                d.ActivatedAtUtc))
            .ToListAsync();

        var periodStart = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        UsageRows = await db.UsageCounters
            .Where(u => u.LicenseId == Id && u.Period == periodStart)
            .OrderBy(u => u.FeatureCode)
            .Select(u => new UsageRow(u.FeatureCode, u.Consumed, u.QuotaLimit, u.Period))
            .ToListAsync();
    }

    private async Task LoadPlanOptionsAsync(Guid currentPlanId)
    {
        var plans = await db.Plans
            .Where(p => p.IsActive || p.Id == currentPlanId)
            .OrderBy(p => p.Code)
            .ToListAsync();

        PlanOptions = plans
            .Select(p => new SelectListItem(p.IsActive ? p.DisplayName : $"{p.DisplayName} (descontinuado)", p.Id.ToString()))
            .ToList();
    }

    public sealed class LicenseInput
    {
        public Guid PlanId { get; set; }
        public DateOnly ValidUntil { get; set; }
        public int MaxUsers { get; set; } = 1;
        public PlanFeatureForm Features { get; set; } = new();
    }

    public sealed record DeviceRow(Guid Id, string Label, string UserName, DateTimeOffset LastSeenUtc, DateTimeOffset ActivatedAtUtc);
    public sealed record UsageRow(string FeatureCode, int Consumed, int QuotaLimit, DateOnly Period);
}

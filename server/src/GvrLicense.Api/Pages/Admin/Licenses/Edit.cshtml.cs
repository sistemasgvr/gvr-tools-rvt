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

    /// <summary>Incluye planes descontinuados si es el que ya tiene la licencia -- si no, desaparecería del <select>.</summary>
    public List<SelectListItem> PlanOptions { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        var license = await db.Licenses.Include(l => l.Customer).FirstOrDefaultAsync(l => l.Id == Id);
        if (license is null)
        {
            return NotFound();
        }

        LicenseKey = license.Key;
        CustomerName = license.Customer!.CompanyName;
        await LoadPlanOptionsAsync(license.PlanId);

        Input = new LicenseInput
        {
            PlanId = license.PlanId,
            ValidUntil = DateOnly.FromDateTime(license.ValidUntil.UtcDateTime),
            MaxUsers = license.MaxUsers,
            FeatureOverridesText = string.Join('\n', license.FeatureOverrides.Select(f => $"{f.Key}={f.Value}"))
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var license = await db.Licenses.Include(l => l.Customer).FirstOrDefaultAsync(l => l.Id == Id);
        if (license is null)
        {
            return NotFound();
        }

        license.PlanId = Input.PlanId;
        license.ValidUntil = new DateTimeOffset(Input.ValidUntil, TimeOnly.MaxValue, TimeSpan.Zero);
        license.MaxUsers = Input.MaxUsers;
        license.FeatureOverrides = Plans.IndexModel.ParseFeatures(Input.FeatureOverridesText);
        await db.SaveChangesAsync();

        LicenseKey = license.Key;
        CustomerName = license.Customer!.CompanyName;
        await LoadPlanOptionsAsync(license.PlanId);
        TempData["Saved"] = true;
        return RedirectToPage(new { id = Id });
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
        public string? FeatureOverridesText { get; set; }
    }
}

using GvrLicense.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GvrLicense.Api.Pages.Admin.Plans;

public class EditModel(LicenseDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    [BindProperty]
    public PlanInput Input { get; set; } = new();

    public string Code { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync()
    {
        var plan = await db.Plans.FindAsync(Id);
        if (plan is null)
        {
            return NotFound();
        }

        Code = plan.Code;
        Input = new PlanInput
        {
            DisplayName = plan.DisplayName,
            Features = PlanFeatureForm.FromDictionary(plan.Features)
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var plan = await db.Plans.FindAsync(Id);
        if (plan is null)
        {
            return NotFound();
        }

        plan.DisplayName = Input.DisplayName.Trim();
        plan.Features = Input.Features.ToDictionary();
        await db.SaveChangesAsync();

        Code = plan.Code;
        TempData["Saved"] = true;
        return RedirectToPage(new { id = Id });
    }

    public sealed class PlanInput
    {
        public string DisplayName { get; set; } = string.Empty;
        public PlanFeatureForm Features { get; set; } = new();
    }
}

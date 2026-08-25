using GvrLicense.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

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
            FeaturesText = string.Join('\n', plan.Features.Select(f => $"{f.Key}={f.Value}"))
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
        plan.Features = IndexModel.ParseFeatures(Input.FeaturesText);
        await db.SaveChangesAsync();

        Code = plan.Code;
        TempData["Saved"] = true;
        return RedirectToPage(new { id = Id });
    }

    public sealed class PlanInput
    {
        public string DisplayName { get; set; } = string.Empty;
        public string? FeaturesText { get; set; }
    }
}

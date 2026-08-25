using GvrLicense.Domain.Entities;
using GvrLicense.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GvrLicense.Api.Pages.Admin.Plans;

public class IndexModel(LicenseDbContext db, IAntiforgery antiforgery) : PageModel
{
    public List<PlanRow> Rows { get; private set; } = [];

    /// <summary>Tabulator genera los botones Editar/Activar/Desactivar por fila como HTML crudo, necesita el token a mano.</summary>
    public string AntiForgeryToken { get; private set; } = string.Empty;

    [BindProperty]
    public PlanInput Input { get; set; } = new();

    public async Task OnGetAsync()
    {
        AntiForgeryToken = antiforgery.GetAndStoreTokens(HttpContext).RequestToken!;

        var licenseCounts = await db.Licenses
            .GroupBy(l => l.PlanId)
            .Select(g => new { PlanId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PlanId, x => x.Count);

        var plans = await db.Plans.OrderBy(p => p.Code).ToListAsync();
        Rows = plans
            .Select(p => new PlanRow(
                p.Id, p.Code, p.DisplayName,
                string.Join(", ", p.Features.Select(f => $"{f.Key}={f.Value}")),
                p.IsActive, licenseCounts.GetValueOrDefault(p.Id)))
            .ToList();
    }

    /// <summary>
    /// Desactivar en vez de borrar: un plan descontinuado desaparece del selector de licencias
    /// nuevas (Licenses/Create) pero las licencias que ya lo usan lo siguen usando igual.
    /// </summary>
    public async Task<IActionResult> OnPostToggleAsync(Guid planId)
    {
        var plan = await db.Plans.FindAsync(planId);
        if (plan != null)
        {
            plan.IsActive = !plan.IsActive;
            await db.SaveChangesAsync();
        }

        return RedirectToPage();
    }

    /// <summary>
    /// Sin UI dinámica de agregar/quitar filas a propósito: el catálogo de features es texto libre
    /// (docs/LICENSING_PLAN.md, "El license server no lista tools hardcodeadas"), así que una tool
    /// nueva no necesita que yo le agregue un campo al formulario -- solo escribes su feature code.
    /// </summary>
    public async Task<IActionResult> OnPostAsync()
    {
        var exists = await db.Plans.AnyAsync(p => p.Code == Input.Code);
        if (exists)
        {
            ModelState.AddModelError(string.Empty, $"Ya existe un plan con código '{Input.Code}'.");
            await OnGetAsync();
            return Page();
        }

        db.Plans.Add(new Plan
        {
            Id = Guid.NewGuid(),
            Code = Input.Code.Trim(),
            DisplayName = Input.DisplayName.Trim(),
            Features = ParseFeatures(Input.FeaturesText)
        });
        await db.SaveChangesAsync();

        return RedirectToPage();
    }

    internal static Dictionary<string, string> ParseFeatures(string? text)
    {
        var features = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return features;
        }

        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('=', 2);
            if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]))
            {
                features[parts[0].Trim()] = parts[1].Trim();
            }
        }
        return features;
    }

    public sealed record PlanRow(Guid Id, string Code, string DisplayName, string FeaturesSummary, bool IsActive, int LicenseCount);

    public sealed class PlanInput
    {
        public string Code { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? FeaturesText { get; set; }
    }
}

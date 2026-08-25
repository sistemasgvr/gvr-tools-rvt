using GvrLicense.Infrastructure;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GvrLicense.Api.Pages.Admin.Customers;

public class IndexModel(LicenseDbContext db) : PageModel
{
    public List<CustomerRow> Rows { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Rows = await db.Customers
            .OrderByDescending(c => c.CreatedAtUtc)
            .Select(c => new CustomerRow(
                c.Id, c.CompanyName, c.ContactName, c.ContactEmail, c.PaymentNotes,
                c.Licenses.Count, c.CreatedAtUtc))
            .ToListAsync();
    }

    public sealed record CustomerRow(
        Guid Id, string CompanyName, string ContactName, string ContactEmail, string? PaymentNotes,
        int LicenseCount, DateTimeOffset CreatedAtUtc);
}

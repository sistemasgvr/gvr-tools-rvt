using GvrLicense.Api.Endpoints;
using GvrLicense.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// La connection string real vive SOLO en la variable de entorno ConnectionStrings__Postgres de
// EasyPanel (docs/LICENSING_PLAN.md, Pieza 6 "Secrets") -- nunca en un archivo del repo.
builder.Services.AddDbContext<LicenseDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

// Pieza 3 "Seguridad anti-abuso", punto 7: rate limit de activate/heartbeat. Nativo desde .NET 7,
// sin NuGet extra. Política real (ventanas, límites por IP/license) se afina en Fase 3.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

var app = builder.Build();

app.UseRateLimiter();

// Pieza 6 "Monitoreo": valida conexión real a Postgres, no solo "el proceso responde" -- el monitor
// externo (UptimeRobot) pega aquí.
app.MapGet("/health", async (LicenseDbContext db) =>
    await db.Database.CanConnectAsync() ? Results.Ok() : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));

app.MapV1Endpoints();

// Razor Pages de /admin/* se agregan aquí en Fase 1 (ver Admin/README.md).

app.Run();

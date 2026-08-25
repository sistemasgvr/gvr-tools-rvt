using System.Security.Cryptography;
using GvrLicense.Api.Services;
using GvrLicense.Api.Endpoints;
using GvrLicense.Infrastructure;
using GvrLicense.Infrastructure.Signing;
using GvrLicense.Infrastructure.Storage;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// La connection string real vive SOLO en la variable de entorno ConnectionStrings__Postgres de
// EasyPanel (docs/LICENSING_PLAN.md, Pieza 6 "Secrets") -- nunca en un archivo del repo.
builder.Services.AddDbContext<LicenseDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.Configure<MinioOptions>(builder.Configuration.GetSection(MinioOptions.SectionName));
builder.Services.AddSingleton<IReleaseArtifactStore, MinioReleaseArtifactStore>();

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 524_288_000; // 500 MB
});
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 524_288_000;
});

builder.Services.AddSingleton<IEntitlementSigner, EcdsaEntitlementSigner>();
builder.Services.AddSingleton<JwtSessionTokenService>();
builder.Services.AddScoped<LicenseEngine>();

// Misma clave ECDsa P-256 que firma el blob de entitlements (Signing:PrivateKeyPem), reusada para
// firmar/validar el JWT de sesión del add-in -- un solo par de claves, dos usos relacionados.
var signingPem = builder.Configuration["Signing:PrivateKeyPem"]
    ?? throw new InvalidOperationException("Signing:PrivateKeyPem no está configurado (ver server/README.md).");
var jwtValidationKey = ECDsa.Create();
jwtValidationKey.ImportFromPem(signingPem);

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    // Panel /admin/* (docs/LICENSING_PLAN.md, Pieza 5): usuario/contraseña + cookie tokenizada, sin
    // 2FA -- ver Pages/Admin/Login.cshtml.cs. Toda la carpeta Admin exige sesión salvo el login.
    .AddCookie(options =>
    {
        options.LoginPath = "/Admin/Login";
        options.AccessDeniedPath = "/Admin/Login";
    })
    // /v1/heartbeat y /v1/usage (docs/LICENSING_PLAN.md, "API mínima"): JWT ES256 emitido por
    // /v1/activate, mandado como "Authorization: Bearer {AccessToken}".
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = JwtSessionTokenService.Issuer,
            ValidateAudience = true,
            ValidAudience = JwtSessionTokenService.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new ECDsaSecurityKey(jwtValidationKey)
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(V1Endpoints.BearerPolicy, policy => policy
        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser());
});

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/Admin");
    options.Conventions.AllowAnonymousToPage("/Admin/Login");
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "GVR License API",
        Version = "v1",
        Description = "API que consume el add-in Revit (/v1/*). El panel admin es Razor Pages (cookie), no aparece aquí."
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Pegar solo el AccessToken de /v1/activate (sin 'Bearer '). Solo hace falta en heartbeat y usage."
    });
    // No AddSecurityRequirement global: AuthorizeCheckOperationFilter lo aplica solo a endpoints autenticados.
    options.OperationFilter<GvrLicense.Api.OpenApi.AuthorizeCheckOperationFilter>();
});

// Pieza 3 "Seguridad anti-abuso", punto 7: rate limit de activate/heartbeat. Nativo desde .NET 7,
// sin NuGet extra. Política real (ventanas, límites por IP/license) se afina en Fase 3.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

var app = builder.Build();

app.UseRateLimiter();

// Swagger siempre disponible en este producto self-hosted (enlace "Avanzado · API" del admin).
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "GVR License API v1");
    options.DocumentTitle = "GVR License API";
});

app.UseStaticFiles(); // wwwroot/lib -- AdminLTE/Bootstrap vendorizados, ver Pages/Shared/_Layout.cshtml
app.UseAuthentication();
app.UseAuthorization();

// Pieza 6 "Monitoreo": valida conexión real a Postgres.
app.MapGet("/health", async (LicenseDbContext db) =>
        await db.Database.CanConnectAsync()
            ? Results.Ok(new { status = "ok" })
            : Results.Json(new { status = "unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable))
    .WithTags("ops")
    .WithSummary("Health check (Postgres)")
    .WithDescription("UptimeRobot / EasyPanel. 200 si la base responde; 503 si no.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status503ServiceUnavailable)
    .AllowAnonymous()
    .ExcludeFromDescription();

// Enlace estable para el cliente: redirige a URL firmada MinIO del último instalador.
app.MapGet("/download", async (LicenseEngine engine, CancellationToken ct) =>
    {
        try
        {
            var url = await engine.GetLatestInstallerDownloadUrlAsync(ct);
            return Results.Redirect(url);
        }
        catch (LicenseApiException ex)
        {
            return Results.Problem(ex.Message, statusCode: ex.StatusCode);
        }
    })
    .WithTags("downloads")
    .WithSummary("Descarga el instalador más reciente")
    .WithDescription("Público. Redirige a una URL firmada temporal del bucket MinIO gvr-tools-releases (último release kind=installer).")
    .AllowAnonymous();

app.MapV1Endpoints();
app.MapRazorPages();

app.Run();

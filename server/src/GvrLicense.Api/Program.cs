using System.Security.Cryptography;
using GvrLicense.Api.Services;
using GvrLicense.Api.Endpoints;
using GvrLicense.Infrastructure;
using GvrLicense.Infrastructure.Signing;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// La connection string real vive SOLO en la variable de entorno ConnectionStrings__Postgres de
// EasyPanel (docs/LICENSING_PLAN.md, Pieza 6 "Secrets") -- nunca en un archivo del repo.
builder.Services.AddDbContext<LicenseDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

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
        Description = "docs/LICENSING_PLAN.md -- API que consume el add-in (/v1/*) y, más adelante, el panel admin (/admin/*)."
    });

    // Botón "Authorize" en Swagger UI: pegar el AccessToken de /v1/activate para probar
    // /v1/heartbeat y /v1/usage sin salir del navegador.
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Pegar solo el AccessToken devuelto por /v1/activate (sin el prefijo 'Bearer ')."
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
            []
        }
    });
});

// Pieza 3 "Seguridad anti-abuso", punto 7: rate limit de activate/heartbeat. Nativo desde .NET 7,
// sin NuGet extra. Política real (ventanas, límites por IP/license) se afina en Fase 3.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

var app = builder.Build();

app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseStaticFiles(); // wwwroot/lib -- AdminLTE/Bootstrap/jQuery/FontAwesome vendorizados, ver Pages/Shared/_Layout.cshtml
app.UseAuthentication();
app.UseAuthorization();

// Pieza 6 "Monitoreo": valida conexión real a Postgres, no solo "el proceso responde" -- el monitor
// externo (UptimeRobot) pega aquí.
app.MapGet("/health", async (LicenseDbContext db) =>
    await db.Database.CanConnectAsync() ? Results.Ok() : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));

app.MapV1Endpoints();
app.MapRazorPages();

app.Run();

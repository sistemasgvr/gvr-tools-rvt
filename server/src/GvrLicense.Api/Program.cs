using System.Security.Cryptography;
using GvrLicense.Api.Services;
using GvrLicense.Api.Endpoints;
using GvrLicense.Infrastructure;
using GvrLicense.Infrastructure.Signing;
using GvrLicense.Infrastructure.Storage;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
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
builder.Services.AddSingleton<ReleaseUploadProgressStore>();

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
var signingPem = PemNormalizer.Normalize(builder.Configuration["Signing:PrivateKeyPem"]);
if (string.IsNullOrWhiteSpace(signingPem))
{
    throw new InvalidOperationException("Signing:PrivateKeyPem no está configurado (ver server/README.md).");
}

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
            IssuerSigningKey = new ECDsaSecurityKey(jwtValidationKey),
            ClockSkew = TimeSpan.FromMinutes(2)
        };
        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                // Evita el challenge vacío por defecto; el add-in lee el cuerpo ProblemDetails.
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/problem+json";

                var detail = context.AuthenticateFailure is SecurityTokenExpiredException
                    ? "Sesión de licencia expirada. Vuelve a activar con tu clave de licencia."
                    : "Token inválido o ausente. Vuelve a activar la licencia.";

                await context.Response.WriteAsJsonAsync(new
                {
                    title = "Unauthorized",
                    status = 401,
                    detail
                });
            }
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

// Development only: apply pending EF migrations so schema drifts (e.g. missing columns)
// fail fast at startup instead of as Postgres 42703 at runtime. Production/EasyPanel
// must use `dotnet ef database update` explicitly -- never auto-migrate there.
if (app.Environment.IsDevelopment())
{
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LicenseDbContext>();
        db.Database.Migrate();
    }
    catch (Exception ex)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
        logger.LogError(ex, "Failed to apply EF migrations in Development. Stop the running API if files are locked, then fix the DB or run: dotnet ef database update --project ../GvrLicense.Infrastructure");
        throw;
    }
}

// Red de seguridad: sin esto, cualquier excepción que un endpoint no atrape (una NpgsqlException
// transitoria, un timeout de MinIO, etc.) llega sin manejar hasta el cliente. Antes de esto solo
// /v1/updates/download/{id} tenía este problema documentado, pero cualquier endpoint nuevo que se
// agregue sin pasar por el helper RunAsync de V1Endpoints caería en el mismo hueco -- este handler
// es el respaldo para todos, no solo para los que ya se acordaron de envolver su propio try/catch.
// Nunca expone el mensaje de la excepción real al cliente (podría filtrar detalles de infraestructura);
// eso solo se registra en el log del servidor.
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var feature = context.Features.Get<IExceptionHandlerPathFeature>();
        var exception = feature?.Error;

        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("UnhandledException");
        logger.LogError(exception, "Excepción sin manejar en {Path}", feature?.Path ?? context.Request.Path.Value);

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;

        if (context.Request.Path.StartsWithSegments("/v1"))
        {
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new
            {
                title = "Internal Server Error",
                status = 500,
                detail = "No se pudo completar la operación. Intenta de nuevo en unos minutos."
            });
            return;
        }

        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(
            "<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\">" +
            "<title>Error -- GVR Tools</title></head>" +
            "<body style=\"font-family:system-ui,sans-serif;max-width:32rem;margin:4rem auto;text-align:center;color:#212121\">" +
            "<h1>Algo salió mal</h1>" +
            "<p>Ocurrió un error inesperado. Intenta de nuevo en unos minutos.</p>" +
            "<p><a href=\"/\">Volver al inicio</a></p>" +
            "</body></html>");
    });
});

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

// Descarga real del .exe (redirect MinIO). La landing HTML vive en Pages/Download.cshtml (/download).
app.MapGet("/download/file", async (LicenseEngine engine, CancellationToken ct) =>
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
    .WithSummary("Descarga el instalador más reciente (archivo)")
    .WithDescription("Público. Redirige a una URL firmada temporal del bucket MinIO gvr-tools-releases (último release kind=installer). La landing está en GET /download.")
    .AllowAnonymous();

app.MapV1Endpoints();
app.MapRazorPages();

app.Run();

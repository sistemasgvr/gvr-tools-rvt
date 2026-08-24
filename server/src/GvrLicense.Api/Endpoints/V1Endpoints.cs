namespace GvrLicense.Api.Endpoints;

/// <summary>
/// Los cinco endpoints de docs/LICENSING_PLAN.md, "API mínima v1". Los handlers son placeholders
/// (501) a propósito -- la lógica real (verificar key, firmar el blob, llamar consume_quota, etc.)
/// se implementa en Fase 0/1; esto es solo el mapeo de rutas que el resto de la arquitectura asume.
/// </summary>
public static class V1Endpoints
{
    public static IEndpointRouteBuilder MapV1Endpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1");

        v1.MapPost("/activate", () => NotImplemented());
        v1.MapPost("/heartbeat", () => NotImplemented());
        v1.MapPost("/usage", () => NotImplemented());
        v1.MapGet("/updates/check", () => NotImplemented());
        v1.MapGet("/updates/download/{id}", (string id) => NotImplemented());

        return app;
    }

    private static IResult NotImplemented() =>
        Results.Problem("Endpoint definido en la arquitectura, sin implementar todavía.", statusCode: StatusCodes.Status501NotImplemented);
}

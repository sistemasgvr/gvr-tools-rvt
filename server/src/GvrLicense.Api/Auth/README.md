# Auth

Dos esquemas de autenticación en el mismo proyecto (docs/LICENSING_PLAN.md, Pieza 1):

- **`/v1/*`** (add-in cliente): API key o JWT de sesión emitido en `/v1/activate`. Sin sesión de
  navegador, sin cookies.
- **`/admin/*`** (panel dueño): cookie de sesión + **TOTP obligatorio** (`Otp.NET`, ya referenciado
  en `GvrLicense.Api.csproj`). Solo el dueño en v1 -- sin roles ni multi-usuario.

Los `AuthenticationHandler` concretos se implementan en Fase 0/1 junto con los endpoints reales;
carpeta reservada.

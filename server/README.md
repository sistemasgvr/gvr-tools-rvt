# server/ -- License API

Backend de licencias descrito en [`docs/LICENSING_PLAN.md`](../docs/LICENSING_PLAN.md). Vive en
este mismo repo (monorepo) en vez de en uno aparte: comparte versión/documentación con el add-in y
es un solo desarrollador manteniendo ambos lados, así que un solo `git log` es más simple que
sincronizar dos repos. Su árbol de build es independiente del de `src/` -- `Directory.Build.props`
aquí fija `net10.0`, sin relación con la matriz `net48`/`net8.0-windows` de Revit.

## Proyectos

| Proyecto | Qué es | Pieza del plan |
| --- | --- | --- |
| `GvrLicense.Domain` | Entidades puras (`Customer`, `Plan`, `License`, `Device`, `UsageCounter`, `UsageEvent`, `Release`, `AuditLog`, `AppSettings`) | Pieza 1, "Modelo de datos" |
| `GvrLicense.Contracts` | DTOs de `/v1/*` (espejo mantenido a mano de `src/GvrTools.Licensing/Http/Dto`, porque el cliente corre en net48 y no puede referenciar este proyecto) | Pieza 1, "API mínima" |
| `GvrLicense.Infrastructure` | `LicenseDbContext` (EF Core + Npgsql) y las funciones/triggers SQL en `Sql/` | "Dónde vive la lógica: app vs Postgres" |
| `GvrLicense.Api` | ASP.NET Core: sirve `/v1/*` y `/admin/*` desde un solo contenedor | Pieza 1, 5 y 6 |
| `tests/GvrLicense.Api.Tests` | xUnit, mismo patrón que `tests/GvrTools.Core.Tests` | -- |

## Desarrollo local

```bash
docker compose up -d          # Postgres desechable en localhost:5432
dotnet build GvrLicense.slnx
```

La connection string local va en `appsettings.Development.json` (gitignored, `dotnet user-secrets`
también sirve) -- nunca en `appsettings.json`, que sí se versiona y solo trae un placeholder vacío.

## Deploy

Pieza 6 del plan: un servicio Docker en EasyPanel construido desde `server/Dockerfile`, más
`postgres` y un volumen para artefactos de update. Dominio, backups y monitoreo documentados ahí.

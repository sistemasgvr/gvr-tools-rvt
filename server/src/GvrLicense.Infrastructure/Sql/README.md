# Sql/

Funciones y triggers de Postgres que EF Core no puede expresar en Fluent API (ver
docs/LICENSING_PLAN.md, "Dónde vive la lógica: app (EF Core) vs Postgres (función/trigger)").

Se aplican como SQL crudo dentro de una migración EF Core normal, por ejemplo:

```csharp
migrationBuilder.Sql(File.ReadAllText("Sql/ConsumeQuota.sql"));
migrationBuilder.Sql(File.ReadAllText("Sql/AuditLogTrigger.sql"));
```

para que sigan versionadas junto con el resto del schema y viajen con `dotnet ef database update`
en vez de aplicarse a mano.

- `ConsumeQuota.sql` -- consumo atómico de cuota mensual, llamado desde `POST /v1/usage`.
- `AuditLogTrigger.sql` -- inserta en `audit_log` automáticamente cuando cambia `License.Status`.

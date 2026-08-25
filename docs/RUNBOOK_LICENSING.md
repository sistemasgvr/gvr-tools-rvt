# Runbook: licencias (cobro y entrega manual)

Sin Stripe ni SMTP en v1. El dinero y el envío de la clave van por transferencia / factura / WhatsApp / correo tuyo.

## Tras confirmar el pago

1. En el **panel admin**: crea o abre el **Cliente** (empresa o persona). Anota en notas el pago (monto, fecha, referencia).
2. **Licencias → Nueva**: elige cliente, plan, `MaxUsers`, `ValidUntil` → **copia la clave** `GVR-…`.
3. Envía **tú** al cliente (correo/WhatsApp):
   - el enlace de descarga del instalador: `https://<dominio-license>/download` (o el que muestra **Actualizaciones** en el admin tras publicar el Setup)
   - la clave `GVR-…`
   - que en Revit abran **Cuenta / Licencia**, peguen la clave y usen **nombre + correo de cada colaborador**

## Escenarios

| Caso | Cliente | Plan | MaxUsers | ValidUntil |
| --- | --- | --- | --- | --- |
| Una sola persona | Él/ella | Starter/Pro/Trial | **1** | +1 mes / +1 año |
| Empresa, N colaboradores | La empresa | El acordado | **N** | Según contrato |
| De por vida | Persona o empresa | El acordado | 1 o N | Fecha lejana (p. ej. 2099-01-01) |

Extras puntuales (más cuota, DWG en Starter, etc.): edita la licencia → **overrides** (no hace falta un plan nuevo).

## Operación diaria

- **Más asientos** tras pago extra: edita la licencia y sube `MaxUsers`.
- **Renovar** suscripción: edita `ValidUntil`.
- **Moroso**: **Suspender**. El add-in corta en el próximo heartbeat (gracia offline máx. ~7 días).
- **PC atascado / cambio de máquina**: en la licencia, **Liberar** el device; o el usuario usa **Desactivar este PC**.
- **Colaborador que se va**: **Miembros** del cliente → desactivar esa persona.

## Esquema (Postgres)

En **Development**, la API aplica migraciones EF al arrancar (`Database.Migrate()`). En **Production**, no: usa `dotnet ef database update` con `ConnectionStrings__Postgres` (ver `server/README.md`).

## Qué no hace el sistema (a propósito)

- No cobra solo.
- No envía la key por correo automático.
- No hay portal self-service del cliente en v1.

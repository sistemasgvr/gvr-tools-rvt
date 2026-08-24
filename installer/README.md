# installer/ -- Empaque comercial (`.exe`)

Wizard multi-versión estilo ProSheets descrito en
[`docs/LICENSING_PLAN.md`](../docs/LICENSING_PLAN.md), Pieza 7. Se implementa en Fase 2, sobre
[`Inno Setup`](https://jrsoftware.org/isinfo.php) (script `.iss`).

Carpeta reservada -- todavía sin script, porque el wizard necesita el catálogo real de años Revit
soportados y la allow-list de impresoras PDF (ya existente en `GvrTools.Revit/Export`, ver
[`docs/ARCHITECTURE.md`](../docs/ARCHITECTURE.md)) antes de tener sentido escribirlo.

Layout previsto cuando se implemente:

```
installer/
  GvrToolsSetup.iss       # script Inno Setup: idioma -> prerequisitos -> TOS -> años Revit -> install
  Prereqs/                # setup embebido/descarga de PDF24 + detección
  Assets/                 # branding del wizard
  build-installer.ps1     # empaqueta build/<año>/ + firma Authenticode
```

Sigue produciendo la misma salida `build/<año>/` que hoy genera
[`scripts/install-addin.ps1`](../scripts/install-addin.ps1); ese script se mantiene para desarrollo
interno, el `.exe` es el camino de cliente de pago (ver plan, "Relación con el script actual").

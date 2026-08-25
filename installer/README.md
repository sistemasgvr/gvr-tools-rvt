# Instalador GVR Tools (Inno Setup)

Wizard comercial alineado con `docs/LICENSING_PLAN.md` (Pieza 7). El script de desarrollo [`scripts/install-addin.ps1`](../scripts/install-addin.ps1) **sigue** siendo el camino interno.

## Requisitos

1. [Inno Setup 6](https://jrsoftware.org/isinfo.php) (incluye `ISCC.exe`).
2. Builds Release por año en `build/2021` … `build/2025`:

```powershell
foreach ($v in 2021,2022,2023,2024,2025) {
  dotnet build src/GvrTools.App/GvrTools.App.csproj -c Release -p:RevitVersion=$v
}
```

O usa el helper:

```powershell
.\installer\build-installer.ps1
```

## Salida

`dist/GvrTools-Setup-1.0.0.exe` (sin firmar).

## Qué hace el wizard

1. Idioma ES/EN  
2. Aceptación de TOS (`installer/assets/TOS.txt`)  
3. Checkboxes Revit 2021–2025 (premarca los detectados en `Program Files\Autodesk\Revit <año>`)  
4. Si eliges 2021: exige PDF24 detectado  
5. Copia DLLs a `%ProgramData%\GVR\GvrTools\<año>\` y escribe el `.addin`  
6. Tras instalar: abrir Revit → **Cuenta / Licencia** → pegar key  

## Authenticode (cuando tengáis certificado)

No está integrado en CI todavía. Ejemplo manual:

```powershell
signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /a dist\GvrTools-Setup-1.0.0.exe
```

Sin firma, SmartScreen avisará a clientes externos — obligatorio antes de la primera venta formal.

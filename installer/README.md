# Instalador GVR Tools (Inno Setup)

Wizard comercial alineado con `docs/LICENSING_PLAN.md` (Pieza 7). El script de desarrollo [`scripts/install-addin.ps1`](../scripts/install-addin.ps1) **sigue** siendo el camino interno.

## Requisitos

1. [Inno Setup 6](https://jrsoftware.org/isinfo.php) (incluye `ISCC.exe`).
2. Builds Release por año en `build/2021` … `build/2027`.
3. El instalador de PDF24 Creator en
   `installer/prereqs/pdf24-creator-installer.exe` (incluido en el bundle).

```powershell
foreach ($v in 2021,2022,2023,2024,2025,2026,2027) {
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
3. Checkboxes Revit 2021–2027 (premarca los detectados en `Program Files\Autodesk\Revit <año>`)
4. Página de prerrequisitos con columnas **Name / Required / Found / Action**:
   - Si PDF24 ya está instalado, muestra **Already installed** y no vuelve a ejecutarlo.
   - Para Revit 2021, PDF24 es obligatorio y se instala automáticamente.
   - Para Revit 2022+, si falta se ofrece como instalación opcional porque Revit ya incluye PDF nativo.
5. Copia DLLs a `%ProgramData%\GVR\GvrTools\<año>\` y escribe el `.addin` en `%ProgramData%`
   para Revit 2021–2026 o en `%ProgramFiles%\Autodesk\Revit\Addins\2027` para Revit 2027  
6. Tras instalar: abrir Revit → **Cuenta / Licencia** → pegar key  

## Prerrequisito PDF24

El ejecutable incluido se lanza con:

```text
/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-
```

PDF24 documenta `/SILENT` y `/VERYSILENT` para su instalador EXE basado en Inno
Setup. Se usa `/VERYSILENT` para una instalación desatendida, `/NORESTART` para
evitar reinicios iniciados por el prerrequisito y `/SP-` para omitir el aviso
inicial. Al reemplazar el binario por una versión nueva, conserva exactamente el
nombre `pdf24-creator-installer.exe`, verifica la firma del proveedor y prueba el
setup en una máquina limpia.

## Authenticode (cuando tengáis certificado)

No está integrado en CI todavía. Ejemplo manual:

```powershell
signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /a dist\GvrTools-Setup-1.0.0.exe
```

Sin firma, SmartScreen avisará a clientes externos — obligatorio antes de la primera venta formal.

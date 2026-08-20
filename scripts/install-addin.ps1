#requires -version 5.1
<#
.SYNOPSIS
    Builds the Mass PDF Export add-in and registers it with a local Revit install for testing.

.DESCRIPTION
    Compiles src/MassPdfExport, then writes a .addin manifest pointing at the freshly built DLL
    into Revit's per-user (or all-users) add-ins folder for the given Revit version. Restart Revit
    afterwards to pick it up.

.PARAMETER RevitVersion
    Revit release year folder under ...\Addins\, e.g. 2021, 2022, 2023, 2024. Default: 2021.

.PARAMETER Configuration
    Build configuration to compile. Default: Release.

.PARAMETER AllUsers
    Install into %PROGRAMDATA%\Autodesk\Revit\Addins\<version> instead of the current user's
    %APPDATA%. Requires an elevated (Run as administrator) PowerShell session.

.EXAMPLE
    .\scripts\install-addin.ps1
    Builds Release and installs the add-in for the current user on Revit 2021.

.EXAMPLE
    .\scripts\install-addin.ps1 -Configuration Debug -AllUsers
    Builds Debug and installs for all users (needs an elevated shell).
#>
param(
    [string]$RevitVersion = "2021",
    [string]$Configuration = "Release",
    [switch]$AllUsers
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$csproj = Join-Path $repoRoot "src\MassPdfExport\MassPdfExport.csproj"
$assemblyName = "GvrTools.MassPdfExport"
$addinName = "$assemblyName.addin"

if (-not (Test-Path $csproj)) {
    throw "No se encontró el proyecto en '$csproj'."
}

Write-Host "Compilando $assemblyName ($Configuration)..." -ForegroundColor Cyan
dotnet build $csproj -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "La compilación falló (código $LASTEXITCODE)."
}

$dllPath = Join-Path $repoRoot "src\MassPdfExport\bin\$Configuration\$assemblyName.dll"
if (-not (Test-Path $dllPath)) {
    throw "No se encontró el ensamblado compilado en '$dllPath'."
}
$dllPath = (Resolve-Path $dllPath).Path

$addinsRoot = if ($AllUsers) { $env:ProgramData } else { $env:APPDATA }
$targetDir = Join-Path $addinsRoot "Autodesk\Revit\Addins\$RevitVersion"

if (-not (Test-Path $targetDir)) {
    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
}

$manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>GVR Tools - Exportador PDF Masivo</Name>
    <Assembly>$dllPath</Assembly>
    <AddInId>87c18a7a-dc0c-47af-a20d-86d2bcd59a91</AddInId>
    <FullClassName>GvrTools.MassPdfExport.App</FullClassName>
    <VendorId>GVR</VendorId>
    <VendorDescription>GVR, www.github.com/sistemasgvr</VendorDescription>
  </AddIn>
</RevitAddIns>
"@

$targetAddinPath = Join-Path $targetDir $addinName
Set-Content -Path $targetAddinPath -Value $manifest -Encoding utf8

Write-Host ""
Write-Host "Add-in instalado correctamente." -ForegroundColor Green
Write-Host "  Ensamblado: $dllPath"
Write-Host "  Manifiesto: $targetAddinPath"
Write-Host ""
Write-Host "Reinicia Revit $RevitVersion. Busca la pestaña 'GVR Tools' en la cinta de opciones." -ForegroundColor Yellow

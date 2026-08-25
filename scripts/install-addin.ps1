#requires -version 5.1
<#
.SYNOPSIS
    Compila GVR Tools y lo registra en las versiones de Revit instaladas en este equipo.

.DESCRIPTION
    Para cada versión de Revit solicitada:
      1. compila src/GvrTools.App con -p:RevitVersion=<año> (salida en build/<año>/), y
      2. escribe un manifiesto .addin apuntando a esa carpeta.

    Sin parámetros, detecta las versiones de Revit instaladas y solo trabaja con esas. Reinicia
    Revit al terminar.

.PARAMETER RevitVersion
    Versiones a compilar/instalar, por ejemplo -RevitVersion 2021,2025. Por defecto: las instaladas.

.PARAMETER Configuration
    Configuración de compilación. Por defecto: Release.

.PARAMETER AllUsers
    Instala en %PROGRAMDATA% en vez de %APPDATA% (requiere PowerShell como administrador).

.PARAMETER SkipBuild
    Solo reescribe los manifiestos, usando lo que ya haya en build/<año>/.

.PARAMETER Uninstall
    Quita los manifiestos de GVR Tools en vez de instalarlos.

.EXAMPLE
    .\scripts\install-addin.ps1
    Compila e instala para todas las versiones de Revit detectadas.

.EXAMPLE
    .\scripts\install-addin.ps1 -RevitVersion 2025
    Compila e instala solo para Revit 2025.
#>
param(
    [int[]]$RevitVersion,
    [string]$Configuration = "Release",
    [switch]$AllUsers,
    [switch]$SkipBuild,
    [switch]$Uninstall
)

$ErrorActionPreference = "Stop"

# Versiones que el código soporta. Al agregar una nueva, añádela también a <Configurations> y a los
# símbolos REVITxxxx_OR_GREATER en src\GvrTools.Revit.props.
$SupportedVersions = @(2021, 2022, 2023, 2024, 2025, 2026, 2027)

$AddinFileName = "GvrTools.addin"
$AssemblyFileName = "GvrTools.App.dll"
# Manifiesto de la versión anterior del complemento (un solo proyecto), que hay que retirar para no
# terminar con dos entradas en la cinta.
$LegacyAddinFileName = "GvrTools.MassPdfExport.addin"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src\GvrTools.App\GvrTools.App.csproj"

function Get-InstalledRevitVersions {
    $found = @()
    foreach ($version in $SupportedVersions) {
        $exe = Join-Path ${env:ProgramFiles} "Autodesk\Revit $version\Revit.exe"
        if (Test-Path $exe) { $found += $version }
    }
    return $found
}

function Get-AddinDirectory([int]$version) {
    if (-not $AllUsers) {
        return Join-Path $env:APPDATA "Autodesk\Revit\Addins\$version"
    }

    # Revit 2027 stopped loading machine-wide manifests from ProgramData. Autodesk's new shared
    # third-party location is Program Files\Autodesk\Revit\Addins\<año>.
    if ($version -ge 2027) {
        return Join-Path $env:ProgramFiles "Autodesk\Revit\Addins\$version"
    }

    return Join-Path $env:ProgramData "Autodesk\Revit\Addins\$version"
}

function New-Manifest([string]$assemblyPath) {
    return @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>GVR Tools</Name>
    <Assembly>$assemblyPath</Assembly>
    <AddInId>87c18a7a-dc0c-47af-a20d-86d2bcd59a91</AddInId>
    <FullClassName>GvrTools.App.GvrApplication</FullClassName>
    <VendorId>GVR</VendorId>
    <VendorDescription>GVR, www.github.com/sistemasgvr</VendorDescription>
  </AddIn>
</RevitAddIns>
"@
}

if (-not (Test-Path $project)) {
    throw "No se encontró el proyecto en '$project'."
}

if (-not $RevitVersion -or $RevitVersion.Count -eq 0) {
    $RevitVersion = Get-InstalledRevitVersions
    if ($RevitVersion.Count -eq 0) {
        throw "No se detectó ninguna versión de Revit instalada. Indica una con -RevitVersion 2025."
    }
    Write-Host "Versiones de Revit detectadas: $($RevitVersion -join ', ')" -ForegroundColor Cyan
}

$unsupported = $RevitVersion | Where-Object { $SupportedVersions -notcontains $_ }
if ($unsupported) {
    throw "Versiones no soportadas: $($unsupported -join ', '). Soportadas: $($SupportedVersions -join ', ')."
}

if ($Uninstall) {
    foreach ($version in $RevitVersion) {
        $dir = Get-AddinDirectory $version
        foreach ($name in @($AddinFileName, $LegacyAddinFileName)) {
            $path = Join-Path $dir $name
            if (Test-Path $path) {
                Remove-Item $path -Force
                Write-Host "Quitado: $path" -ForegroundColor Yellow
            }
        }
    }
    Write-Host "Desinstalación completa. Reinicia Revit." -ForegroundColor Green
    return
}

foreach ($version in $RevitVersion) {
    Write-Host ""
    Write-Host "=== Revit $version ===" -ForegroundColor Cyan

    if (-not $SkipBuild) {
        Write-Host "Compilando ($Configuration, RevitVersion=$version)..."
        dotnet build $project -c $Configuration -p:RevitVersion=$version
        if ($LASTEXITCODE -ne 0) {
            throw "La compilación para Revit $version falló (código $LASTEXITCODE)."
        }
    }

    $assemblyPath = Join-Path $repoRoot "build\$version\$AssemblyFileName"
    if (-not (Test-Path $assemblyPath)) {
        throw "No se encontró el ensamblado compilado en '$assemblyPath'."
    }
    $assemblyPath = (Resolve-Path $assemblyPath).Path

    $targetDir = Get-AddinDirectory $version
    if (-not (Test-Path $targetDir)) {
        New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    }

    # El complemento anterior registraba su propio manifiesto; si sigue ahí, Revit cargaría las dos
    # versiones y aparecerían botones duplicados.
    $legacyPath = Join-Path $targetDir $LegacyAddinFileName
    if (Test-Path $legacyPath) {
        Remove-Item $legacyPath -Force
        Write-Host "Se retiró el manifiesto anterior: $legacyPath" -ForegroundColor Yellow
    }

    $manifestPath = Join-Path $targetDir $AddinFileName
    Set-Content -Path $manifestPath -Value (New-Manifest $assemblyPath) -Encoding utf8

    Write-Host "Instalado." -ForegroundColor Green
    Write-Host "  Ensamblado: $assemblyPath"
    Write-Host "  Manifiesto: $manifestPath"
}

Write-Host ""
Write-Host "Listo. Reinicia Revit y busca la pestaña 'GVR Tools' en la cinta." -ForegroundColor Yellow

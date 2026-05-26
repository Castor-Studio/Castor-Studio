# build-native.ps1

param(
    [string]$Preset = "x64-debug",
    [switch]$Reconfigure   # Supprime le build dir existant pour forcer une reconfiguration cmake
)

$ErrorActionPreference = "Stop"

Write-Host "[build-native] Preset: $Preset"

# Find Visual Studio installation
$vswhere = "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe"

if (!(Test-Path $vswhere)) {
    Write-Error "[build-native] ERREUR : vswhere introuvable."
    exit 1
}

$VS_PATH = & $vswhere -latest -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath

if (-not $VS_PATH) {
    Write-Error "[build-native] ERREUR : Visual Studio introuvable."
    exit 1
}

# Init VS environment
$vcvars = Join-Path $VS_PATH "VC\Auxiliary\Build\vcvarsall.bat"

if (!(Test-Path $vcvars)) {
    Write-Error "[build-native] ERREUR : vcvarsall.bat introuvable."
    exit 1
}

# ⚠️ catch .bat env
$envVars = cmd /c "`"$vcvars`" x64 && set"

foreach ($line in $envVars) {
    if ($line -match "^(.*?)=(.*)$") {
        [System.Environment]::SetEnvironmentVariable($matches[1], $matches[2])
    }
}

# configuration
$buildDir = "out/build/$Preset"
$ninjaFile = Join-Path $buildDir "build.ninja"

if ($Reconfigure -and (Test-Path $buildDir)) {
    Write-Host "[build-native] -Reconfigure : suppression de $buildDir ..."
    Remove-Item -Recurse -Force $buildDir
}

if (!(Test-Path $ninjaFile)) {
    Write-Host "[build-native] Configuration cmake --preset $Preset ..."
    & cmake --preset $Preset
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

# Build
Write-Host "[build-native] cmake --build $buildDir ..."
& cmake --build $buildDir
exit $LASTEXITCODE

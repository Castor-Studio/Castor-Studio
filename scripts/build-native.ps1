param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [ValidateSet("x64")]
    [string]$Arch = "x64",

    [switch]$Reconfigure,
    [switch]$CleanVcpkg
)

$ErrorActionPreference = "Stop"

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$ConfigurationLower = $Configuration.ToLowerInvariant()
$Preset = "$Arch-$ConfigurationLower"
$BuildDir = Join-Path $RepoRoot "out/build/$Preset"
$NinjaFile = Join-Path $BuildDir "build.ninja"
$VcpkgInstalled = Join-Path $RepoRoot "libcastor/vcpkg_installed"

Write-Host "=== Native: Configuration $Configuration / $Arch ==="

$vswhere = "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe"
if (!(Test-Path $vswhere)) {
    throw "vswhere not found. Install Visual Studio Build Tools with C++ workload."
}

$VsPath = & $vswhere -latest -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $VsPath) {
    throw "Visual Studio C++ tools not found. Install Desktop development with C++."
}

$vcvars = Join-Path $VsPath "VC\Auxiliary\Build\vcvarsall.bat"
if (!(Test-Path $vcvars)) {
    throw "vcvarsall.bat not found at '$vcvars'."
}

Write-Host "=== Native: Setup MSVC environment ==="
$envVars = cmd /c "`"$vcvars`" $Arch && set"
foreach ($line in $envVars) {
    if ($line -match "^(.*?)=(.*)$") {
        [System.Environment]::SetEnvironmentVariable($matches[1], $matches[2])
    }
}

if ($Reconfigure -and (Test-Path $BuildDir)) {
    Write-Host "=== Native: Remove build directory ==="
    Remove-Item -Recurse -Force $BuildDir
}

if ($CleanVcpkg -and (Test-Path $VcpkgInstalled)) {
    Write-Host "=== Native: Remove vcpkg installed directory ==="
    Remove-Item -Recurse -Force $VcpkgInstalled

    if (Test-Path $BuildDir) {
        Write-Host "=== Native: Remove build directory after vcpkg clean ==="
        Remove-Item -Recurse -Force $BuildDir
    }
}

if (!(Test-Path $NinjaFile)) {
    Write-Host "=== Native: Configure CMake ($Preset) ==="
    Push-Location $RepoRoot
    try {
        & cmake --preset $Preset
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    finally {
        Pop-Location
    }
}
else {
    Write-Host "=== Native: Configure CMake skipped ($Preset already configured) ==="
}

Write-Host "=== Native: Build DLL ==="
Push-Location $RepoRoot
try {
    & cmake --build $BuildDir
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
    Pop-Location
}

Write-Host "=== Native: Build completed ==="

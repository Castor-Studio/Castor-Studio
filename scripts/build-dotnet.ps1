param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [ValidateSet("x64")]
    [string]$Arch = "x64"
)

$ErrorActionPreference = "Stop"

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$ConfigurationLower = $Configuration.ToLowerInvariant()
$NativeOutputDir = Join-Path $RepoRoot "out/build/x64-$ConfigurationLower/libcastor"
$NativeDll = Join-Path $NativeOutputDir "castor_core.dll"
$Solution = Join-Path $RepoRoot "Castor.sln"
$SolutionPlatform = "Any CPU"

if (!(Test-Path $NativeDll)) {
    throw "Native DLL not found at '$NativeDll'. Run scripts/build-native.ps1 first, or scripts/build-all.ps1 -Configuration $Configuration from the repository root."
}

Write-Host "=== .NET: Restore solution ==="
Push-Location $RepoRoot
try {
    & dotnet restore $Solution "-p:Platform=$SolutionPlatform"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Write-Host "=== .NET: Build solution ==="
    & dotnet build $Solution -c $Configuration "-p:Platform=$SolutionPlatform" --no-restore
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
    Pop-Location
}

Write-Host "=== .NET: Build completed ==="

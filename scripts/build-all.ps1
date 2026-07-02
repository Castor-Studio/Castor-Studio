param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [ValidateSet("x64")]
    [string]$Arch = "x64",

    [switch]$Reconfigure,
    [switch]$CleanVcpkg
)

$ErrorActionPreference = "Stop"

Write-Host "=== Build All: Native ==="
& "$PSScriptRoot/build-native.ps1" -Configuration $Configuration -Arch $Arch -Reconfigure:$Reconfigure -CleanVcpkg:$CleanVcpkg
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "=== Build All: .NET ==="
& "$PSScriptRoot/build-dotnet.ps1" -Configuration $Configuration -Arch $Arch
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "=== Build All: Success ==="

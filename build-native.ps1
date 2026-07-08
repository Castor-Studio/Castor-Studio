param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [ValidateSet("x64")]
    [string]$Arch = "x64",

    [switch]$Reconfigure,
    [switch]$CleanVcpkg
)

$ErrorActionPreference = "Stop"

& "$PSScriptRoot/scripts/build-native.ps1" -Configuration $Configuration -Arch $Arch -Reconfigure:$Reconfigure -CleanVcpkg:$CleanVcpkg
exit $LASTEXITCODE

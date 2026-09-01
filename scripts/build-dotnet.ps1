param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$Solution = Join-Path $RepoRoot "Castor-Studio.sln"

Push-Location $RepoRoot
try {
    dotnet restore $Solution
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    dotnet build $Solution -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
    Pop-Location
}

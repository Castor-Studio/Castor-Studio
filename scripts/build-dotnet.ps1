param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$Solution = Join-Path $RepoRoot "Castor-Studio.sln"
$Project = Join-Path $RepoRoot "Castor.Studio\Castor-Studio.csproj"

Push-Location $RepoRoot
try {
    dotnet restore $Solution
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    dotnet build $Solution -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    # The local AI worker goes to <app output>\ai, where the app looks for it first.
    $appOutput = dotnet msbuild $Project -getProperty:TargetDir -p:Configuration=$Configuration
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & (Join-Path $PSScriptRoot "fetch-ai-worker.ps1") -Destination $appOutput.Trim()
}
finally {
    Pop-Location
}

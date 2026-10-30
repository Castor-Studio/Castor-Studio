<#
.SYNOPSIS
Installs the Castor.AI worker into <Destination>\ai, next to Castor Studio.

.DESCRIPTION
The worker comes from the GitHub release of Castor-Studio/Castor-AI-Worker whose version is the
Castor.AI package referenced by Castor-Studio.csproj: the SDK and the worker share the gRPC
contract and the shared memory layout, they must always match. The zip is checked against its
published SHA-256 and cached in artifacts\ai-worker. Models are not shipped: the worker
downloads them on first use.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Destination
)

$ErrorActionPreference = "Stop"
# Invoke-WebRequest is very slow with its progress bar on Windows PowerShell.
$ProgressPreference = "SilentlyContinue"
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$Project = Join-Path $RepoRoot "Castor.Studio\Castor-Studio.csproj"

[xml]$projectXml = Get-Content $Project -Raw
$version = $projectXml.Project.ItemGroup.PackageReference |
    Where-Object { $_.Include -eq "Castor.AI" } |
    Select-Object -ExpandProperty Version -First 1
if (-not $version) { throw "No Castor.AI PackageReference in $Project." }

$target = Join-Path $Destination "ai"
$marker = Join-Path $target "castor-ai-worker.version"
if ((Test-Path (Join-Path $target "Castor.AI.Worker.exe")) -and
    (Test-Path $marker) -and ((Get-Content $marker -Raw).Trim() -eq $version)) {
    Write-Host "Castor.AI worker $version already in $target"
    exit 0
}

$asset = "Castor.AI.Worker-$version-win-x64.zip"
$baseUrl = "https://github.com/Castor-Studio/Castor-AI-Worker/releases/download/v$version"
$cache = Join-Path $RepoRoot "artifacts\ai-worker\$version"
$zip = Join-Path $cache $asset
New-Item -ItemType Directory -Force $cache | Out-Null

# The checksum is always fetched again: a cached zip is only reused when it still matches.
Invoke-WebRequest "$baseUrl/$asset.sha256" -OutFile "$zip.sha256"
$expected = (Get-Content "$zip.sha256" -Raw).Trim().Split(" ")[0].ToLowerInvariant()

if (-not (Test-Path $zip) -or (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) {
    Write-Host "Downloading $asset"
    Invoke-WebRequest "$baseUrl/$asset" -OutFile $zip
}

$actual = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $expected) {
    Remove-Item $zip -Force
    throw "$asset : SHA-256 $actual, expected $expected"
}

# A previous worker version must not leave files behind.
if (Test-Path $target) { Remove-Item $target -Recurse -Force }
Expand-Archive $zip -DestinationPath $target -Force
Set-Content $marker $version -Encoding ascii
Write-Host "Castor.AI worker $version installed in $target"

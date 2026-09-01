$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")

Get-ChildItem $RepoRoot -Recurse -Directory -Include bin,obj |
    Remove-Item -Recurse -Force

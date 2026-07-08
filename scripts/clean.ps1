$ErrorActionPreference = "Stop"

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")

Write-Host "=== Clean: native build directories ==="
Remove-Item -Recurse -Force (Join-Path $RepoRoot "out/build") -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force (Join-Path $RepoRoot "out/install") -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force (Join-Path $RepoRoot "build") -ErrorAction SilentlyContinue

Write-Host "=== Clean: bin/obj directories ==="
Get-ChildItem $RepoRoot -Recurse -Directory -Include bin,obj |
    Where-Object { $_.FullName -notlike "*\libcastor\vcpkg_installed\*" } |
    Remove-Item -Recurse -Force

Write-Host "=== Clean completed ==="

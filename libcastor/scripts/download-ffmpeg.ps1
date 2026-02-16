#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Downloads and extracts FFmpeg shared libraries for libcastor compilation.

.DESCRIPTION
    This script automatically downloads FFmpeg 8.0.1 full-build-shared from gyan.dev,
    verifies its SHA-256 checksum, and extracts the necessary files (bin, lib, include)
    to the extern/ffmpeg directory.

.PARAMETER Force
    Force re-download even if FFmpeg is already present.

.PARAMETER Version
    FFmpeg version to download (default: 8.0.1).

.EXAMPLE
    .\download-ffmpeg.ps1
    Downloads FFmpeg if not already present.

.EXAMPLE
    .\download-ffmpeg.ps1 -Force
    Forces re-download of FFmpeg.
#>

param(
    [switch]$Force = $false,
    [string]$Version = "8.0.1"
)

# Configuration
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"  # Faster downloads

$FFMPEG_VERSION = $Version
$FFMPEG_PACKAGE = "ffmpeg-$FFMPEG_VERSION-full_build-shared.7z"
$FFMPEG_URL = "https://www.gyan.dev/ffmpeg/builds/packages/$FFMPEG_PACKAGE"
$FFMPEG_SHA256_URL = "$FFMPEG_URL.sha256"

# Paths
$SCRIPT_DIR = $PSScriptRoot
$LIBCASTOR_DIR = Split-Path $SCRIPT_DIR -Parent
$EXTERN_DIR = Join-Path $LIBCASTOR_DIR "extern"
$FFMPEG_DIR = Join-Path $EXTERN_DIR "ffmpeg"
$TEMP_DIR = Join-Path $LIBCASTOR_DIR "temp_ffmpeg_download"
$VERSION_FILE = Join-Path $FFMPEG_DIR ".version"

# 7-Zip paths (common locations)
$SEVEN_ZIP_PATHS = @(
    "C:\Program Files\7-Zip\7z.exe",
    "C:\Program Files (x86)\7-Zip\7z.exe",
    "$env:ProgramFiles\7-Zip\7z.exe",
    "${env:ProgramFiles(x86)}\7-Zip\7z.exe"
)

function Write-Info
{
    param([string]$Message)
    Write-Host "[FFmpeg] " -ForegroundColor Cyan -NoNewline
    Write-Host $Message
}

function Write-Success
{
    param([string]$Message)
    Write-Host "[FFmpeg] " -ForegroundColor Green -NoNewline
    Write-Host $Message
}

function Write-Error-Custom
{
    param([string]$Message)
    Write-Host "[FFmpeg] ERROR: " -ForegroundColor Red -NoNewline
    Write-Host $Message
}

function Find-7Zip
{
    foreach ($path in $SEVEN_ZIP_PATHS)
    {
        if (Test-Path $path)
        {
            return $path
        }
    }

    # Try to find in PATH
    $sevenZipCmd = Get-Command "7z" -ErrorAction SilentlyContinue
    if ($sevenZipCmd)
    {
        return $sevenZipCmd.Source
    }

    return $null
}

function Test-FFmpegInstalled
{
    if (-not (Test-Path $VERSION_FILE))
    {
        return $false
    }

    $installedVersion = Get-Content $VERSION_FILE -Raw -ErrorAction SilentlyContinue
    if ($installedVersion -ne $FFMPEG_VERSION)
    {
        return $false
    }

    # Check if required directories exist
    $requiredDirs = @(
        (Join-Path $FFMPEG_DIR "bin"),
        (Join-Path $FFMPEG_DIR "lib"),
        (Join-Path $FFMPEG_DIR "include")
    )

    foreach ($dir in $requiredDirs)
    {
        if (-not (Test-Path $dir))
        {
            return $false
        }
    }

    return $true
}

function Download-File
{
    param(
        [string]$Url,
        [string]$OutputPath
    )

    Write-Info "Downloading from: $Url"
    Write-Info "Destination: $OutputPath"

    try
    {
        $webClient = New-Object System.Net.WebClient
        $webClient.DownloadFile($Url, $OutputPath)
        Write-Success "Download completed"
    } catch
    {
        Write-Error-Custom "Failed to download: $_"
        throw
    }
}

function Get-RemoteSHA256
{
    param([string]$Url)

    try
    {
        Write-Info "Fetching SHA-256 checksum..."
        $sha256Content = (New-Object System.Net.WebClient).DownloadString($Url)
        # SHA256 file format: "HASH *filename" or just "HASH"
        $hash = ($sha256Content -split '\s+')[0]
        return $hash.Trim().ToUpper()
    } catch
    {
        Write-Error-Custom "Failed to fetch SHA-256: $_"
        return $null
    }
}

function Get-FileSHA256
{
    param([string]$FilePath)

    Write-Info "Computing SHA-256 checksum..."
    
    try {
        # Use .NET cryptography for compatibility with older PowerShell versions
        $sha256 = [System.Security.Cryptography.SHA256]::Create()
        $fileStream = [System.IO.File]::OpenRead($FilePath)
        $hashBytes = $sha256.ComputeHash($fileStream)
        $fileStream.Close()
        $fileStream.Dispose()
        $sha256.Dispose()
        
        # Convert bytes to hex string
        $hash = [System.BitConverter]::ToString($hashBytes) -replace '-', ''
        return $hash.ToUpper()
    }
    catch {
        Write-Error-Custom "Failed to compute SHA-256: $_"
        return $null
    }
}

function Verify-Checksum
{
    param(
        [string]$FilePath,
        [string]$ExpectedHash
    )

    if (-not $ExpectedHash)
    {
        Write-Info "Warning: No expected hash provided, skipping verification"
        return $true
    }

    $actualHash = Get-FileSHA256 -FilePath $FilePath

    if ($actualHash -eq $ExpectedHash)
    {
        Write-Success "Checksum verified: $actualHash"
        return $true
    } else
    {
        Write-Error-Custom "Checksum mismatch!"
        Write-Error-Custom "Expected: $ExpectedHash"
        Write-Error-Custom "Actual:   $actualHash"
        return $false
    }
}

function Extract-7z
{
    param(
        [string]$ArchivePath,
        [string]$DestinationPath,
        [string]$SevenZipPath
    )

    Write-Info "Extracting archive to: $DestinationPath"

    # Extract to temp location first
    $extractTemp = Join-Path $TEMP_DIR "extracted"
    New-Item -ItemType Directory -Path $extractTemp -Force | Out-Null

    $arguments = "x", "`"$ArchivePath`"", "-o`"$extractTemp`"", "-y"

    try
    {
        $process = Start-Process -FilePath $SevenZipPath -ArgumentList $arguments -Wait -PassThru -NoNewWindow

        if ($process.ExitCode -ne 0)
        {
            throw "7-Zip extraction failed with exit code: $($process.ExitCode)"
        }

        Write-Success "Extraction completed"

        # Find the ffmpeg folder (usually ffmpeg-8.0.1-full_build-shared/)
        $extractedFolders = Get-ChildItem -Path $extractTemp -Directory

        if ($extractedFolders.Count -eq 0)
        {
            throw "No folders found in extracted archive"
        }

        # Take the first (and usually only) folder
        $ffmpegExtracted = $extractedFolders[0].FullName
        Write-Info "Found FFmpeg folder: $($extractedFolders[0].Name)"

        # Copy required directories
        $dirsToCopy = @("bin", "lib", "include")

        foreach ($dir in $dirsToCopy)
        {
            $sourcePath = Join-Path $ffmpegExtracted $dir
            $destPath = Join-Path $DestinationPath $dir

            if (Test-Path $sourcePath)
            {
                Write-Info "Copying $dir directory..."
                Copy-Item -Path $sourcePath -Destination $destPath -Recurse -Force
            } else
            {
                Write-Error-Custom "Warning: Directory not found: $sourcePath"
            }
        }

        Write-Success "FFmpeg files copied to: $DestinationPath"
    } catch
    {
        Write-Error-Custom "Extraction failed: $_"
        throw
    }
}

# Main script
try
{
    Write-Info "FFmpeg Auto-Download Script"
    Write-Info "Version: $FFMPEG_VERSION"
    Write-Info "=========================="

    # Check if already installed
    if ((Test-FFmpegInstalled) -and -not $Force)
    {
        Write-Success "FFmpeg $FFMPEG_VERSION is already installed at: $FFMPEG_DIR"
        Write-Info "Use -Force to re-download"
        exit 0
    }

    if ($Force)
    {
        Write-Info "Force flag detected, re-downloading FFmpeg..."
    }

    # Find 7-Zip
    $sevenZip = Find-7Zip
    if (-not $sevenZip)
    {
        Write-Error-Custom "7-Zip not found!"
        Write-Error-Custom "Please install 7-Zip from: https://www.7-zip.org/"
        Write-Error-Custom "Or install via: winget install 7zip.7zip"
        exit 1
    }

    Write-Success "Found 7-Zip at: $sevenZip"

    # Create temp directory
    if (Test-Path $TEMP_DIR)
    {
        Write-Info "Cleaning temporary directory..."
        Remove-Item -Path $TEMP_DIR -Recurse -Force
    }
    New-Item -ItemType Directory -Path $TEMP_DIR -Force | Out-Null

    # Download FFmpeg archive
    $archivePath = Join-Path $TEMP_DIR $FFMPEG_PACKAGE
    Write-Info "Downloading FFmpeg $FFMPEG_VERSION (~180 MB, this may take a while)..."
    Download-File -Url $FFMPEG_URL -OutputPath $archivePath

    # Get expected SHA-256
    $expectedHash = Get-RemoteSHA256 -Url $FFMPEG_SHA256_URL

    # Verify checksum
    if ($expectedHash)
    {
        if (-not (Verify-Checksum -FilePath $archivePath -ExpectedHash $expectedHash))
        {
            Write-Error-Custom "Checksum verification failed!"
            exit 1
        }
    }

    # Remove old FFmpeg directory if exists
    if (Test-Path $FFMPEG_DIR)
    {
        Write-Info "Removing old FFmpeg installation..."
        Remove-Item -Path $FFMPEG_DIR -Recurse -Force
    }

    # Create FFmpeg directory
    New-Item -ItemType Directory -Path $FFMPEG_DIR -Force | Out-Null

    # Extract archive
    Extract-7z -ArchivePath $archivePath -DestinationPath $FFMPEG_DIR -SevenZipPath $sevenZip

    # Write version file
    Set-Content -Path $VERSION_FILE -Value $FFMPEG_VERSION -NoNewline
    Write-Success "Version file created: $VERSION_FILE"

    # Cleanup
    Write-Info "Cleaning up temporary files..."
    Remove-Item -Path $TEMP_DIR -Recurse -Force

    Write-Success "=========================="
    Write-Success "FFmpeg $FFMPEG_VERSION installed successfully!"
    Write-Success "Location: $FFMPEG_DIR"
    Write-Info ""
    Write-Info "DLLs location: $(Join-Path $FFMPEG_DIR 'bin')"
    Write-Info "Libs location: $(Join-Path $FFMPEG_DIR 'lib')"
    Write-Info "Headers location: $(Join-Path $FFMPEG_DIR 'include')"

    exit 0
} catch
{
    Write-Error-Custom "Script failed: $_"
    Write-Error-Custom $_.ScriptStackTrace

    # Cleanup on error
    if (Test-Path $TEMP_DIR)
    {
        Remove-Item -Path $TEMP_DIR -Recurse -Force -ErrorAction SilentlyContinue
    }

    exit 1
}

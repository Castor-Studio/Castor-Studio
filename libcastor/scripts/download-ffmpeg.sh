#!/usr/bin/env bash
#
# Downloads and extracts FFmpeg shared libraries for libcastor compilation.
#
# This script automatically downloads FFmpeg 8.0.1 full-build-shared from gyan.dev,
# verifies its SHA-256 checksum, and extracts the necessary files (bin, lib, include)
# to the extern/ffmpeg directory.
#
# Usage:
#   ./download-ffmpeg.sh         # Download if not present
#   ./download-ffmpeg.sh --force # Force re-download
#   ./download-ffmpeg.sh --help  # Show help
#

set -euo pipefail

# Configuration
FFMPEG_VERSION="${FFMPEG_VERSION:-8.0.1}"
FFMPEG_PACKAGE="ffmpeg-${FFMPEG_VERSION}-full_build-shared.7z"
FFMPEG_URL="https://www.gyan.dev/ffmpeg/builds/packages/${FFMPEG_PACKAGE}"
FFMPEG_SHA256_URL="${FFMPEG_URL}.sha256"

# Paths
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LIBCASTOR_DIR="$(dirname "$SCRIPT_DIR")"
EXTERN_DIR="${LIBCASTOR_DIR}/extern"
FFMPEG_DIR="${EXTERN_DIR}/ffmpeg"
TEMP_DIR="${LIBCASTOR_DIR}/temp_ffmpeg_download"
VERSION_FILE="${FFMPEG_DIR}/.version"

# Flags
FORCE_DOWNLOAD=false

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
CYAN='\033[0;36m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Functions
print_info() {
    echo -e "${CYAN}[FFmpeg]${NC} $*"
}

print_success() {
    echo -e "${GREEN}[FFmpeg]${NC} $*"
}

print_error() {
    echo -e "${RED}[FFmpeg] ERROR:${NC} $*" >&2
}

print_warning() {
    echo -e "${YELLOW}[FFmpeg] WARNING:${NC} $*"
}

show_help() {
    cat << EOF
FFmpeg Auto-Download Script for libcastor

Usage: $(basename "$0") [OPTIONS]

Downloads FFmpeg 8.0.1 full-build-shared from gyan.dev and extracts it
to extern/ffmpeg directory.

Options:
    --force         Force re-download even if FFmpeg is already installed
    --version VER   Download specific FFmpeg version (default: 8.0.1)
    --help          Show this help message

Examples:
    $(basename "$0")                    # Download if not present
    $(basename "$0") --force            # Force re-download
    $(basename "$0") --version 7.1.1    # Download specific version

Requirements:
    - curl or wget
    - p7zip-full (7z command)
    - sha256sum

EOF
}

check_command() {
    local cmd="$1"
    if ! command -v "$cmd" &> /dev/null; then
        return 1
    fi
    return 0
}

find_download_tool() {
    if check_command curl; then
        echo "curl"
    elif check_command wget; then
        echo "wget"
    else
        return 1
    fi
}

find_7zip() {
    if check_command 7z; then
        echo "7z"
    elif check_command 7za; then
        echo "7za"
    else
        return 1
    fi
}

test_ffmpeg_installed() {
    if [[ ! -f "$VERSION_FILE" ]]; then
        return 1
    fi

    local installed_version
    installed_version=$(cat "$VERSION_FILE" 2>/dev/null || echo "")

    if [[ "$installed_version" != "$FFMPEG_VERSION" ]]; then
        return 1
    fi

    # Check if required directories exist
    local required_dirs=("${FFMPEG_DIR}/bin" "${FFMPEG_DIR}/lib" "${FFMPEG_DIR}/include")

    for dir in "${required_dirs[@]}"; do
        if [[ ! -d "$dir" ]]; then
            return 1
        fi
    done

    return 0
}

download_file() {
    local url="$1"
    local output="$2"
    local download_tool

    download_tool=$(find_download_tool)

    print_info "Downloading from: $url"
    print_info "Destination: $output"

    case "$download_tool" in
        curl)
            if ! curl -fSL --progress-bar -o "$output" "$url"; then
                print_error "Failed to download with curl"
                return 1
            fi
            ;;
        wget)
            if ! wget --show-progress -O "$output" "$url"; then
                print_error "Failed to download with wget"
                return 1
            fi
            ;;
        *)
            print_error "No download tool found (curl or wget required)"
            return 1
            ;;
    esac

    print_success "Download completed"
    return 0
}

get_remote_sha256() {
    local url="$1"
    local download_tool

    download_tool=$(find_download_tool)

    print_info "Fetching SHA-256 checksum..."

    local sha256_content
    case "$download_tool" in
        curl)
            sha256_content=$(curl -fsSL "$url" 2>/dev/null || echo "")
            ;;
        wget)
            sha256_content=$(wget -qO- "$url" 2>/dev/null || echo "")
            ;;
        *)
            return 1
            ;;
    esac

    if [[ -z "$sha256_content" ]]; then
        print_warning "Failed to fetch SHA-256 checksum"
        return 1
    fi

    # SHA256 file format: "HASH *filename" or just "HASH"
    local hash
    hash=$(echo "$sha256_content" | awk '{print $1}' | tr -d '\n')
    echo "$hash" | tr '[:lower:]' '[:upper:]'
}

get_file_sha256() {
    local filepath="$1"

    print_info "Computing SHA-256 checksum..."

    if ! check_command sha256sum; then
        print_warning "sha256sum not found, skipping checksum verification"
        return 1
    fi

    local hash
    hash=$(sha256sum "$filepath" | awk '{print $1}')
    echo "$hash" | tr '[:lower:]' '[:upper:]'
}

verify_checksum() {
    local filepath="$1"
    local expected_hash="$2"

    if [[ -z "$expected_hash" ]]; then
        print_warning "No expected hash provided, skipping verification"
        return 0
    fi

    local actual_hash
    actual_hash=$(get_file_sha256 "$filepath")

    if [[ -z "$actual_hash" ]]; then
        print_warning "Could not compute hash, skipping verification"
        return 0
    fi

    if [[ "$actual_hash" == "$expected_hash" ]]; then
        print_success "Checksum verified: $actual_hash"
        return 0
    else
        print_error "Checksum mismatch!"
        print_error "Expected: $expected_hash"
        print_error "Actual:   $actual_hash"
        return 1
    fi
}

extract_7z() {
    local archive_path="$1"
    local destination_path="$2"
    local seven_zip_cmd

    seven_zip_cmd=$(find_7zip)

    print_info "Extracting archive to: $destination_path"

    # Extract to temp location first
    local extract_temp="${TEMP_DIR}/extracted"
    mkdir -p "$extract_temp"

    if ! "$seven_zip_cmd" x "$archive_path" -o"$extract_temp" -y > /dev/null; then
        print_error "7-Zip extraction failed"
        return 1
    fi

    print_success "Extraction completed"

    # Find the ffmpeg folder (usually ffmpeg-8.0.1-full_build-shared/)
    local ffmpeg_extracted
    ffmpeg_extracted=$(find "$extract_temp" -maxdepth 1 -type d ! -path "$extract_temp" | head -n1)

    if [[ -z "$ffmpeg_extracted" ]]; then
        print_error "No folders found in extracted archive"
        return 1
    fi

    print_info "Found FFmpeg folder: $(basename "$ffmpeg_extracted")"

    # Copy required directories
    local dirs_to_copy=("bin" "lib" "include")

    for dir in "${dirs_to_copy[@]}"; do
        local source_path="${ffmpeg_extracted}/${dir}"
        local dest_path="${destination_path}/${dir}"

        if [[ -d "$source_path" ]]; then
            print_info "Copying $dir directory..."
            cp -r "$source_path" "$dest_path"
        else
            print_warning "Directory not found: $source_path"
        fi
    done

    print_success "FFmpeg files copied to: $destination_path"
    return 0
}

cleanup() {
    if [[ -d "$TEMP_DIR" ]]; then
        print_info "Cleaning up temporary files..."
        rm -rf "$TEMP_DIR"
    fi
}

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --force|-f)
            FORCE_DOWNLOAD=true
            shift
            ;;
        --version|-v)
            FFMPEG_VERSION="$2"
            FFMPEG_PACKAGE="ffmpeg-${FFMPEG_VERSION}-full_build-shared.7z"
            FFMPEG_URL="https://www.gyan.dev/ffmpeg/builds/packages/${FFMPEG_PACKAGE}"
            FFMPEG_SHA256_URL="${FFMPEG_URL}.sha256"
            shift 2
            ;;
        --help|-h)
            show_help
            exit 0
            ;;
        *)
            print_error "Unknown option: $1"
            echo ""
            show_help
            exit 1
            ;;
    esac
done

# Main script
main() {
    print_info "FFmpeg Auto-Download Script"
    print_info "Version: $FFMPEG_VERSION"
    print_info "=========================="

    # Check if already installed
    if test_ffmpeg_installed && [[ "$FORCE_DOWNLOAD" == "false" ]]; then
        print_success "FFmpeg $FFMPEG_VERSION is already installed at: $FFMPEG_DIR"
        print_info "Use --force to re-download"
        exit 0
    fi

    if [[ "$FORCE_DOWNLOAD" == "true" ]]; then
        print_info "Force flag detected, re-downloading FFmpeg..."
    fi

    # Check for required tools
    if ! find_download_tool > /dev/null; then
        print_error "No download tool found!"
        print_error "Please install curl or wget"
        exit 1
    fi

    local seven_zip_cmd
    if ! seven_zip_cmd=$(find_7zip); then
        print_error "7-Zip not found!"
        print_error "Please install p7zip-full package:"
        print_error "  Ubuntu/Debian: sudo apt-get install p7zip-full"
        print_error "  Fedora/RHEL:   sudo dnf install p7zip p7zip-plugins"
        print_error "  macOS:         brew install p7zip"
        exit 1
    fi

    print_success "Found 7-Zip command: $seven_zip_cmd"

    # Set up cleanup trap
    trap cleanup EXIT INT TERM

    # Create temp directory
    if [[ -d "$TEMP_DIR" ]]; then
        print_info "Cleaning temporary directory..."
        rm -rf "$TEMP_DIR"
    fi
    mkdir -p "$TEMP_DIR"

    # Download FFmpeg archive
    local archive_path="${TEMP_DIR}/${FFMPEG_PACKAGE}"
    print_info "Downloading FFmpeg $FFMPEG_VERSION (~180 MB, this may take a while)..."

    if ! download_file "$FFMPEG_URL" "$archive_path"; then
        print_error "Download failed"
        exit 1
    fi

    # Get expected SHA-256
    local expected_hash
    expected_hash=$(get_remote_sha256 "$FFMPEG_SHA256_URL" || echo "")

    # Verify checksum
    if [[ -n "$expected_hash" ]]; then
        if ! verify_checksum "$archive_path" "$expected_hash"; then
            print_error "Checksum verification failed!"
            exit 1
        fi
    fi

    # Remove old FFmpeg directory if exists
    if [[ -d "$FFMPEG_DIR" ]]; then
        print_info "Removing old FFmpeg installation..."
        rm -rf "$FFMPEG_DIR"
    fi

    # Create FFmpeg directory
    mkdir -p "$FFMPEG_DIR"

    # Extract archive
    if ! extract_7z "$archive_path" "$FFMPEG_DIR"; then
        print_error "Extraction failed"
        exit 1
    fi

    # Write version file
    echo -n "$FFMPEG_VERSION" > "$VERSION_FILE"
    print_success "Version file created: $VERSION_FILE"

    print_success "=========================="
    print_success "FFmpeg $FFMPEG_VERSION installed successfully!"
    print_success "Location: $FFMPEG_DIR"
    print_info ""
    print_info "DLLs location: ${FFMPEG_DIR}/bin"
    print_info "Libs location: ${FFMPEG_DIR}/lib"
    print_info "Headers location: ${FFMPEG_DIR}/include"

    exit 0
}

# Run main function
main "$@"

# ============================================================================
# FFmpeg Auto-Download Module
# ============================================================================
#
# This module automatically downloads FFmpeg if not present or if the version
# doesn't match the required version.
#
# Variables set by this module:
#   FFMPEG_DIR          - Path to FFmpeg installation directory
#   FFMPEG_INSTALLED    - TRUE if FFmpeg is installed and up-to-date
#
# Usage:
#   include(cmake/DownloadFFmpeg.cmake)
#

set(FFMPEG_DIR "${CMAKE_CURRENT_SOURCE_DIR}/extern/ffmpeg")
set(FFMPEG_VERSION_FILE "${FFMPEG_DIR}/.version")
set(FFMPEG_REQUIRED_VERSION "8.0.1")

# Check if FFmpeg is installed
set(FFMPEG_INSTALLED FALSE)
if(EXISTS "${FFMPEG_VERSION_FILE}")
    file(READ "${FFMPEG_VERSION_FILE}" FFMPEG_CURRENT_VERSION)
    if(FFMPEG_CURRENT_VERSION STREQUAL FFMPEG_REQUIRED_VERSION)
        # Check if required directories exist
        if(EXISTS "${FFMPEG_DIR}/bin" AND 
           EXISTS "${FFMPEG_DIR}/lib" AND 
           EXISTS "${FFMPEG_DIR}/include")
            set(FFMPEG_INSTALLED TRUE)
        endif()
    endif()
endif()

# Download FFmpeg if not installed
if(NOT FFMPEG_INSTALLED)
    message(STATUS "FFmpeg ${FFMPEG_REQUIRED_VERSION} not found, downloading...")
    
    # Determine which script to use based on platform
    if(WIN32)
        set(DOWNLOAD_SCRIPT "${CMAKE_CURRENT_SOURCE_DIR}/scripts/download-ffmpeg.ps1")
        set(SCRIPT_COMMAND "powershell" "-ExecutionPolicy" "Bypass" "-File" "${DOWNLOAD_SCRIPT}")
    else()
        set(DOWNLOAD_SCRIPT "${CMAKE_CURRENT_SOURCE_DIR}/scripts/download-ffmpeg.sh")
        set(SCRIPT_COMMAND "${DOWNLOAD_SCRIPT}")
    endif()
    
    # Execute download script
    execute_process(
        COMMAND ${SCRIPT_COMMAND}
        WORKING_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}"
        RESULT_VARIABLE DOWNLOAD_RESULT
        OUTPUT_VARIABLE DOWNLOAD_OUTPUT
        ERROR_VARIABLE DOWNLOAD_ERROR
    )
    
    # Check if download was successful
    if(NOT DOWNLOAD_RESULT EQUAL 0)
        message(FATAL_ERROR 
            "Failed to download FFmpeg!\n"
            "Output: ${DOWNLOAD_OUTPUT}\n"
            "Error: ${DOWNLOAD_ERROR}\n"
            "Please run the download script manually:\n"
            "  Windows: powershell -File ${DOWNLOAD_SCRIPT}\n"
            "  Linux/Mac: ${DOWNLOAD_SCRIPT}"
        )
    endif()
    
    message(STATUS "FFmpeg ${FFMPEG_REQUIRED_VERSION} downloaded successfully")
else()
    message(STATUS "FFmpeg ${FFMPEG_REQUIRED_VERSION} found at: ${FFMPEG_DIR}")
endif()

# Verify FFmpeg directories exist after download
if(NOT EXISTS "${FFMPEG_DIR}/include")
    message(FATAL_ERROR "FFmpeg include directory not found: ${FFMPEG_DIR}/include")
endif()

if(WIN32 AND NOT EXISTS "${FFMPEG_DIR}/lib")
    message(FATAL_ERROR "FFmpeg lib directory not found: ${FFMPEG_DIR}/lib")
endif()

message(STATUS "FFmpeg directories verified successfully")

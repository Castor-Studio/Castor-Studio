cmake_minimum_required(VERSION 4.1)
project(libcastor C)

set(CMAKE_C_STANDARD 17)
set(CMAKE_C_STANDARD_REQUIRED ON)

# ============================================================================
# Dependencies
# ============================================================================

# Download FFmpeg if needed
include(cmake/DownloadFFmpeg.cmake)

# ============================================================================
# Build Configuration
# ============================================================================

file(GLOB SRC "src/*.c")
add_library(castor_core SHARED ${SRC})

target_include_directories(castor_core
        PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
        ${CMAKE_CURRENT_SOURCE_DIR}/extern/ffmpeg/include
)

if (WIN32)
    target_link_directories(castor_core PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/extern/ffmpeg/lib)
    target_compile_definitions(castor_core PRIVATE CASTOR_CORE_BUILD_DLL)
endif ()

target_link_libraries(castor_core
        PRIVATE
        avcodec
        avformat
        avutil
        swscale
)

# ============================================================================
# Post-Build: Copy FFmpeg DLLs
# ============================================================================

if (WIN32)
    # Get list of FFmpeg DLLs (exclude .exe files)
    file(GLOB FFMPEG_DLLS "${CMAKE_CURRENT_SOURCE_DIR}/extern/ffmpeg/bin/*.dll")
    
    # Copy only DLLs to the output directory after build
    # This ensures the DLLs are in the same directory as castor_core.dll
    # Excludes: ffmpeg.exe, ffplay.exe, ffprobe.exe
    foreach(DLL_FILE ${FFMPEG_DLLS})
        get_filename_component(DLL_NAME "${DLL_FILE}" NAME)
        add_custom_command(TARGET castor_core POST_BUILD
            COMMAND ${CMAKE_COMMAND} -E copy_if_different
                "${DLL_FILE}"
                "$<TARGET_FILE_DIR:castor_core>"
            COMMENT "Copying ${DLL_NAME} to output directory"
        )
    endforeach()
endif()

# ============================================================================
# Installation
# ============================================================================

include(GNUInstallDirs)

install(TARGETS castor_core
        RUNTIME DESTINATION ${CMAKE_INSTALL_BINDIR}
        LIBRARY DESTINATION ${CMAKE_INSTALL_LIBDIR}
        ARCHIVE DESTINATION ${CMAKE_INSTALL_LIBDIR}
)

install(DIRECTORY include/
        DESTINATION ${CMAKE_INSTALL_INCLUDEDIR}
)

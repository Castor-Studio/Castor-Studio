cmake_minimum_required(VERSION 3.10)
project(libcastor C CXX)

set(CMAKE_C_STANDARD 17)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

file(GLOB_RECURSE SRC "src/*.c" "src/*.cpp" "src/**/*.c" "src/**/*.cpp" "src/output/*.c")

add_library(castor_core SHARED ${SRC})

target_include_directories(castor_core PRIVATE "include")
target_include_directories(castor_core PRIVATE "include/output")
target_include_directories(castor_core PRIVATE "include/services")
target_include_directories(castor_core PRIVATE "include/core")
target_include_directories(castor_core PRIVATE "include/scene")
target_include_directories(castor_core PRIVATE "include/source")

if (WIN32)
    target_compile_definitions(castor_core PRIVATE CASTOR_CORE_BUILD_DLL)
    target_link_libraries(castor_core PRIVATE winmm dwmapi mmdevapi windowsapp d3d11 winhttp)
endif()

find_package(FFMPEG REQUIRED)
target_include_directories(castor_core PRIVATE ${FFMPEG_INCLUDE_DIRS})
target_link_directories(castor_core PRIVATE ${FFMPEG_LIBRARY_DIRS})
target_link_libraries(castor_core PRIVATE ${FFMPEG_LIBRARIES})

find_package(PThreads4W REQUIRED)
target_link_libraries(castor_core PRIVATE PThreads4W::PThreads4W)

# ── Copie automatique de la DLL et des dépendances après build ────────────────

# 1. Répertoire de staging legacy (cmake-build-release)
set(CASTOR_OUTPUT_DIR "${CMAKE_CURRENT_SOURCE_DIR}/cmake-build-release")

# 2. Répertoire de sortie .NET (cible principale)
set(CASTOR_NET_OUT
    "${CMAKE_SOURCE_DIR}/CastorApplication/bin/$<IF:$<CONFIG:Debug>,Debug,Release>/net8.0")

# Copie dans cmake-build-release/
add_custom_command(TARGET castor_core POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E make_directory "${CASTOR_OUTPUT_DIR}"
    COMMAND ${CMAKE_COMMAND} -E copy_if_different
        "$<TARGET_FILE:castor_core>"
        "${CASTOR_OUTPUT_DIR}"
    COMMENT "[castor_core] → cmake-build-release/"
)

# Copie directe dans la sortie .NET (contourne les soucis de phony target ninja)
add_custom_command(TARGET castor_core POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E make_directory "${CASTOR_NET_OUT}"
    COMMAND ${CMAKE_COMMAND} -E copy_if_different
        "$<TARGET_FILE:castor_core>"
        "${CASTOR_NET_OUT}"
    COMMENT "[castor_core] → sortie .NET"
)

if (WIN32)
    # Copie les DLLs vcpkg selon la config (Debug → debug/bin, Release → bin)
    set(VCPKG_DLL_SRC
        "$<IF:$<CONFIG:Debug>,${VCPKG_INSTALLED_DIR}/${VCPKG_TARGET_TRIPLET}/debug/bin,${VCPKG_INSTALLED_DIR}/${VCPKG_TARGET_TRIPLET}/bin>")

    add_custom_command(TARGET castor_core POST_BUILD
        COMMAND ${CMAKE_COMMAND} -E copy_directory
            "${VCPKG_DLL_SRC}"
            "${CASTOR_OUTPUT_DIR}"
        COMMENT "[vcpkg DLLs] → cmake-build-release/"
    )

    add_custom_command(TARGET castor_core POST_BUILD
        COMMAND ${CMAKE_COMMAND} -E copy_directory
            "${VCPKG_DLL_SRC}"
            "${CASTOR_NET_OUT}"
        COMMENT "[vcpkg DLLs] → sortie .NET"
    )
endif()
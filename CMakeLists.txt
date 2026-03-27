cmake_minimum_required(VERSION 3.21)

if (POLICY CMP0141)
    cmake_policy(SET CMP0141 NEW)
    set(CMAKE_MSVC_DEBUG_INFORMATION_FORMAT
        "$<IF:$<AND:$<C_COMPILER_ID:MSVC>,$<CXX_COMPILER_ID:MSVC>>,$<$<CONFIG:Debug,RelWithDebInfo>:EditAndContinue>,$<$<CONFIG:Debug,RelWithDebInfo>:ProgramDatabase>>")
endif()

project("Castor")

# ── Sous-projets natifs ───────────────────────────────────────────────────────
add_subdirectory("libcastor")
add_subdirectory("libcastor/_main")

# ── Chemins ───────────────────────────────────────────────────────────────────
set(SLN_DIR "${CMAKE_CURRENT_SOURCE_DIR}")
set(APP_BIN "${SLN_DIR}/CastorApplication/bin")

# Répertoire de sortie .NET selon la config courante
set(NET_OUT "${APP_BIN}/$<IF:$<CONFIG:Debug>,Debug,Release>/net8.0")

# DLLs vcpkg selon la config (Debug → debug/bin, Release → bin)
set(VCPKG_DLL_DIR
    "$<IF:$<CONFIG:Debug>,${VCPKG_INSTALLED_DIR}/${VCPKG_TARGET_TRIPLET}/debug/bin,${VCPKG_INSTALLED_DIR}/${VCPKG_TARGET_TRIPLET}/bin>")

# ── Étape 1 : copier castor_core.dll + DLLs vcpkg dans la sortie .NET ────────
add_custom_target(CastorCopyDlls ALL
    COMMAND ${CMAKE_COMMAND} -E make_directory "${NET_OUT}"
    COMMAND ${CMAKE_COMMAND} -E copy_if_different
        "$<TARGET_FILE:castor_core>"
        "${NET_OUT}"
    COMMAND ${CMAKE_COMMAND}
        -DSRC="${VCPKG_DLL_DIR}"
        -DDST="${NET_OUT}"
        -P "${SLN_DIR}/cmake/copy_dir_if_exists.cmake"
    DEPENDS castor_core
    COMMENT "[Castor] Copie des DLLs natives → ${NET_OUT}"
)

# Note : le build .NET est déclenché par le pre-build event de CastorApplication.csproj
# (évite la boucle circulaire cmake → dotnet build → cmake)

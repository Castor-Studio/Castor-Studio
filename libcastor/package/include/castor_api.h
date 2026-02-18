#pragma once

/**
 * @file castor_api.h
 * @brief DLL export/import macro for Castor Core library
 *
 * This file defines the CASTOR_CORE_API macro used to mark functions that should be
 * exported from the castor_core shared library (DLL on Windows).
 *
 * ## Usage
 *
 * When **building** the castor_core library:
 *   - CASTOR_CORE_BUILD_DLL must be defined (done automatically by CMake)
 *   - CASTOR_CORE_API expands to __declspec(dllexport) on Windows
 *   - Functions marked with CASTOR_CORE_API are exported from the DLL
 *
 * When **using** the castor_core library (in applications):
 *   - CASTOR_CORE_BUILD_DLL must NOT be defined
 *   - CASTOR_CORE_API expands to __declspec(dllimport) on Windows
 *   - Functions marked with CASTOR_CORE_API are imported from the DLL
 *
 * On non-Windows platforms (Linux/macOS):
 *   - CASTOR_CORE_API expands to visibility attribute for GCC/Clang
 *   - Or expands to nothing if visibility is not supported
 *
 * ## Example
 *
 * ```c
 * // In a public header file:
 * #include "castor_api.h"
 *
 * CASTOR_CORE_API void my_public_function(int arg);
 * ```
 */

#ifdef _WIN32
    #ifdef CASTOR_CORE_BUILD_DLL
        #define CASTOR_CORE_API __declspec(dllexport)
    #else
        #define CASTOR_CORE_API __declspec(dllimport)
    #endif
#else
    #if defined(__GNUC__) || defined(__clang__)
        #define CASTOR_CORE_API __attribute__((visibility("default")))
    #else
        #define CASTOR_CORE_API
    #endif
#endif
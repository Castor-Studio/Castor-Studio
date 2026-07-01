cmake_minimum_required(VERSION 3.10)
project(libcastor C CXX)

set(CMAKE_C_STANDARD 17)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

file(GLOB_RECURSE SRC CONFIGURE_DEPENDS
    "src/*.c"
    "src/*.cpp"
    "src/**/*.c"
    "src/**/*.cpp")

add_library(castor_core SHARED ${SRC})

target_include_directories(castor_core PRIVATE "include")
target_include_directories(castor_core PRIVATE "include/output")
target_include_directories(castor_core PRIVATE "include/services")
target_include_directories(castor_core PRIVATE "include/core")
target_include_directories(castor_core PRIVATE "include/scene")
target_include_directories(castor_core PRIVATE "include/source")

if (WIN32)
    target_compile_definitions(castor_core PRIVATE CASTOR_CORE_BUILD_DLL)
    target_compile_definitions(castor_core PRIVATE _SILENCE_EXPERIMENTAL_COROUTINE_DEPRECATION_WARNINGS)
    target_link_libraries(castor_core PRIVATE winmm dwmapi mmdevapi windowsapp d3d11 dxgi d3dcompiler winhttp)
endif()

find_package(FFMPEG REQUIRED)
target_include_directories(castor_core PRIVATE ${FFMPEG_INCLUDE_DIRS})
target_link_directories(castor_core PRIVATE ${FFMPEG_LIBRARY_DIRS})
target_link_libraries(castor_core PRIVATE ${FFMPEG_LIBRARIES})

find_package(PThreads4W REQUIRED)
target_link_libraries(castor_core PRIVATE PThreads4W::PThreads4W)

set(SLN_DIR "${CMAKE_CURRENT_SOURCE_DIR}/..")
set(CASTOR_APP_BIN "${SLN_DIR}/CastorApplication/bin/$<CONFIG>/net8.0")

add_custom_command(TARGET castor_core POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E make_directory "${CASTOR_APP_BIN}"
    COMMAND ${CMAKE_COMMAND} -E copy_directory
        "$<TARGET_FILE_DIR:castor_core>"
        "${CASTOR_APP_BIN}"
)

cmake_minimum_required(VERSION 4.1)
project(libcastor C)

set(CMAKE_C_STANDARD 17)
set(CMAKE_C_STANDARD_REQUIRED ON)

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

include(GNUInstallDirs)

install(TARGETS castor_core
        RUNTIME DESTINATION ${CMAKE_INSTALL_BINDIR}
        LIBRARY DESTINATION ${CMAKE_INSTALL_LIBDIR}
        ARCHIVE DESTINATION ${CMAKE_INSTALL_LIBDIR}
)

install(DIRECTORY include/
        DESTINATION ${CMAKE_INSTALL_INCLUDEDIR}
)

cmake_minimum_required(VERSION 3.21)
project(libcastor C)

add_library(libcastor SHARED
	"src/castor_api.h"
	"src/version.c"
	"src/version.h"
)

find_package(FFMPEG MODULE REQUIRED)
target_include_directories(libcastor PRIVATE ${FFMPEG_INCLUDE_DIRS})
target_link_directories(libcastor PRIVATE ${FFMPEG_LIBRARY_DIRS})
target_link_libraries(libcastor PRIVATE ${FFMPEG_LIBRARIES})
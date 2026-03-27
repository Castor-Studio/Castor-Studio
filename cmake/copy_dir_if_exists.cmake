# copy_dir_if_exists.cmake
# Copie SRC → DST seulement si SRC existe. Ne plante pas si absent.
if(EXISTS "${SRC}" AND IS_DIRECTORY "${SRC}")
    file(GLOB DLL_FILES "${SRC}/*.dll")
    foreach(f ${DLL_FILES})
        file(COPY "${f}" DESTINATION "${DST}")
    endforeach()
endif()

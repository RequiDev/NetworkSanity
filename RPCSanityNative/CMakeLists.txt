cmake_minimum_required (VERSION 3.5)

set (project RPCSanityNative)
project (${project} LANGUAGES CXX)

set (CMAKE_CXX_STANDARD 20)
set (CMAKE_POSITION_INDEPENDENT_CODE ON)

if (${CMAKE_BUILD_TYPE} MATCHES "Release")
    set (CMAKE_CXX_FLAGS " /MD /DEBUG:NONE /O2 /Ob2")
else ()
    set (CMAKE_CXX_FLAGS " /MDd /DEBUG:FULL /Od /Ob0 /Wall")
endif ()

set (THREADS_PREFER_PTHREAD_FLAG ON)
find_package (Threads REQUIRED)

add_library(${project} SHARED
    rpcsanitynativewrapper.cpp
    ratelimiter.cpp
    ratelimiter.h
)

target_link_libraries(${project} PRIVATE
    ${CMAKE_DL_LIBS}
    Threads::Threads
)

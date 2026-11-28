cmake_minimum_required(VERSION 3.21)
project(cinnabarr-play VERSION 0.1.0 LANGUAGES C CXX)

# Cinnabarr's game streamer: runs a libretro core on the server and streams it to a browser over WebRTC.
# See README.md and docs/cinnabarr/JELLYFIN.md (step G7).

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

if(NOT CMAKE_BUILD_TYPE)
    set(CMAKE_BUILD_TYPE Release)
endif()

option(CINNABARR_PLAY_TESTS "Build the test core and the tests" ON)

find_package(PkgConfig REQUIRED)
pkg_check_modules(FFMPEG REQUIRED IMPORTED_TARGET libavcodec libavutil libswscale libswresample)
find_package(Threads REQUIRED)
find_package(nlohmann_json 3 REQUIRED)
# cpp-httplib: Debian/Ubuntu ship it as a compiled library (pkg-config cpp-httplib); elsewhere it is header-only.
pkg_check_modules(HTTPLIB IMPORTED_TARGET cpp-httplib)
find_path(HTTPLIB_INCLUDE_DIR httplib.h REQUIRED)

# libdatachannel (WebRTC) is not packaged by most distributions, so it is built from a pinned release.
# Offline: -DFETCHCONTENT_SOURCE_DIR_LIBDATACHANNEL=/path/to/a/checkout (with its submodules).
include(FetchContent)
set(NO_WEBSOCKET ON CACHE BOOL "" FORCE)
set(NO_EXAMPLES ON CACHE BOOL "" FORCE)
set(NO_TESTS ON CACHE BOOL "" FORCE)
FetchContent_Declare(libdatachannel
    GIT_REPOSITORY https://github.com/paullouisageneau/libdatachannel.git
    GIT_TAG v0.24.8
    GIT_SHALLOW TRUE
    GIT_SUBMODULES_RECURSE TRUE)
FetchContent_MakeAvailable(libdatachannel)

add_executable(cinnabarr-play
    src/main.cpp
    src/api.cpp
    src/core.cpp
    src/encoder.cpp
    src/session.cpp)

target_include_directories(cinnabarr-play PRIVATE third_party/libretro ${HTTPLIB_INCLUDE_DIR})
target_compile_definitions(cinnabarr-play PRIVATE CINNABARR_PLAY_VERSION="${PROJECT_VERSION}")
target_compile_options(cinnabarr-play PRIVATE -Wall -Wextra)
target_link_libraries(cinnabarr-play PRIVATE
    LibDataChannel::LibDataChannelStatic
    PkgConfig::FFMPEG
    nlohmann_json::nlohmann_json
    Threads::Threads
    ${CMAKE_DL_LIBS})
if(HTTPLIB_FOUND)
    target_link_libraries(cinnabarr-play PRIVATE PkgConfig::HTTPLIB)
endif()

if(CINNABARR_PLAY_TESTS)
    # A tiny libretro core for tests: a moving bar on a colour that changes while a button is held, and a tone.
    add_library(cinnabarr_test_libretro SHARED test/testcore.c)
    target_include_directories(cinnabarr_test_libretro PRIVATE third_party/libretro)
    set_target_properties(cinnabarr_test_libretro PROPERTIES PREFIX "")
    target_link_libraries(cinnabarr_test_libretro PRIVATE m)
endif()

install(TARGETS cinnabarr-play RUNTIME DESTINATION bin)

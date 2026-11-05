cmake_minimum_required(VERSION 3.21)
project(gba_xr_native LANGUAGES C)

include(CTest)

add_library(gba_xr_reference STATIC
  src/reference_compositor.c
)
target_include_directories(gba_xr_reference PUBLIC include)
target_compile_features(gba_xr_reference PUBLIC c_std_11)

add_library(gba_xr_bridge_objects OBJECT
  src/bridge.c
)
target_include_directories(gba_xr_bridge_objects PUBLIC include)
target_compile_features(gba_xr_bridge_objects PUBLIC c_std_11)
set_target_properties(gba_xr_bridge_objects PROPERTIES POSITION_INDEPENDENT_CODE ON)

add_library(gba_xr_bridge STATIC $<TARGET_OBJECTS:gba_xr_bridge_objects>)
target_include_directories(gba_xr_bridge PUBLIC include)
target_compile_features(gba_xr_bridge PUBLIC c_std_11)

add_library(gba_xr_visible_partition STATIC
  src/visible_partition.c
)
target_include_directories(gba_xr_visible_partition PUBLIC include)
target_compile_features(gba_xr_visible_partition PUBLIC c_std_11)
set_target_properties(gba_xr_visible_partition PROPERTIES POSITION_INDEPENDENT_CODE ON)

add_library(gba_xr_independent_candidate STATIC
  src/independent_candidate.c
)
target_include_directories(gba_xr_independent_candidate PUBLIC include)
target_compile_features(gba_xr_independent_candidate PUBLIC c_std_11)
set_target_properties(gba_xr_independent_candidate PROPERTIES POSITION_INDEPENDENT_CODE ON)

if(MSVC)
  target_compile_options(gba_xr_reference PRIVATE /W4 /WX)
  target_compile_options(gba_xr_bridge_objects PRIVATE /W4 /WX)
  target_compile_options(gba_xr_visible_partition PRIVATE /W4 /WX)
  target_compile_options(gba_xr_independent_candidate PRIVATE /W4 /WX)
else()
  target_compile_options(gba_xr_reference PRIVATE -Wall -Wextra -Wpedantic -Werror)
  target_compile_options(gba_xr_bridge_objects PRIVATE -Wall -Wextra -Wpedantic -Werror)
  target_compile_options(gba_xr_visible_partition PRIVATE -Wall -Wextra -Wpedantic -Werror)
  target_compile_options(gba_xr_independent_candidate PRIVATE -Wall -Wextra -Wpedantic -Werror)
endif()

option(GBAXR_BUILD_MGBA_ADAPTER "Build the mGBA resolved-frame adapter" OFF)
set(MGBA_ROOT "${CMAKE_CURRENT_LIST_DIR}/../third_party/mgba" CACHE PATH "Pinned local mGBA checkout")

if(GBAXR_BUILD_MGBA_ADAPTER)
  if(NOT EXISTS "${MGBA_ROOT}/CMakeLists.txt")
    message(FATAL_ERROR "mGBA checkout not found at MGBA_ROOT: ${MGBA_ROOT}")
  endif()

  set(MGBA_PROVENANCE_PATCH "${CMAKE_CURRENT_LIST_DIR}/../third_party/mgba-patches/0001-gbaxr-final-pixel-provenance.patch")
  if(NOT EXISTS "${MGBA_PROVENANCE_PATCH}")
    message(FATAL_ERROR "Required mGBA provenance patch is missing: ${MGBA_PROVENANCE_PATCH}")
  endif()
  execute_process(
    COMMAND git -C "${MGBA_ROOT}" apply --reverse --check "${MGBA_PROVENANCE_PATCH}"
    RESULT_VARIABLE MGBA_PROVENANCE_ALREADY_APPLIED
    OUTPUT_QUIET
    ERROR_QUIET
  )
  if(NOT MGBA_PROVENANCE_ALREADY_APPLIED EQUAL 0)
    execute_process(
      COMMAND git -C "${MGBA_ROOT}" apply "${MGBA_PROVENANCE_PATCH}"
      RESULT_VARIABLE MGBA_PROVENANCE_APPLY_RESULT
    )
    if(NOT MGBA_PROVENANCE_APPLY_RESULT EQUAL 0)
      message(FATAL_ERROR "Could not apply the pinned mGBA provenance patch")
    endif()
  endif()

  # mGBA is added as a static, core-only dependency. Its frontend, Libretro,
  # and source tree remain outside this repository's tracked project files.
  set(LIBMGBA_ONLY ON CACHE BOOL "Build mGBA as a static core" FORCE)
  set(M_CORE_GBA ON CACHE BOOL "Build the GBA core" FORCE)
  set(M_CORE_GB OFF CACHE BOOL "Exclude the Game Boy core" FORCE)
  set(BUILD_STATIC ON CACHE BOOL "Build static mGBA" FORCE)
  set(BUILD_SHARED OFF CACHE BOOL "Do not build shared mGBA" FORCE)
  add_subdirectory("${MGBA_ROOT}" "${CMAKE_BINARY_DIR}/mgba")

  add_library(gba_xr_mgba_adapter SHARED
    src/mgba_adapter.c
    $<TARGET_OBJECTS:gba_xr_bridge_objects>
  )
  target_include_directories(gba_xr_mgba_adapter
    PUBLIC include
    PRIVATE "${MGBA_ROOT}/include" "${CMAKE_BINARY_DIR}/mgba/include"
  )
  target_compile_features(gba_xr_mgba_adapter PUBLIC c_std_11)
  # mCore is a public C struct whose layout depends on these mGBA feature
  # macros. They must match the static mgba target's compile definitions.
  # In particular, ENABLE_DIRECTORIES inserts mCore::dirs before its function
  # pointers; omitting it makes core->init call an invalid address on device.
  target_compile_definitions(gba_xr_mgba_adapter PRIVATE
    ENABLE_VFS
    ENABLE_DIRECTORIES
  )
  target_link_libraries(gba_xr_mgba_adapter PRIVATE mgba gba_xr_visible_partition gba_xr_independent_candidate)
  set_target_properties(gba_xr_mgba_adapter PROPERTIES OUTPUT_NAME "gba_xr")
  if(ANDROID)
    # Unity 6000.3.12f1 ships NDK r27. Android 15+ 16 KB page-size devices
    # require every arm64 shared-library LOAD segment to use 16 KB alignment.
    # NDK r28 enables this by default; r27 and lower require these linker flags.
    target_link_options(gba_xr_mgba_adapter PRIVATE
      "-Wl,-z,max-page-size=16384"
      "-Wl,-z,common-page-size=16384"
    )
    target_link_libraries(gba_xr_mgba_adapter PRIVATE log)
  endif()
endif()

if(BUILD_TESTING)
  add_executable(gba_xr_reference_tests tests/reference_compositor_test.c)
  target_link_libraries(gba_xr_reference_tests PRIVATE gba_xr_reference)
  add_test(NAME gba_xr_reference_tests COMMAND gba_xr_reference_tests)

  find_package(Threads REQUIRED)
  add_executable(gba_xr_bridge_tests tests/bridge_test.c)
  target_link_libraries(gba_xr_bridge_tests PRIVATE gba_xr_bridge Threads::Threads)
  add_test(NAME gba_xr_bridge_tests COMMAND gba_xr_bridge_tests)

  add_executable(gba_xr_visible_partition_tests tests/visible_partition_test.c)
  target_link_libraries(gba_xr_visible_partition_tests PRIVATE gba_xr_visible_partition)
  add_test(NAME gba_xr_visible_partition_tests COMMAND gba_xr_visible_partition_tests)

  add_executable(gba_xr_independent_candidate_tests tests/independent_candidate_test.c)
  target_link_libraries(gba_xr_independent_candidate_tests PRIVATE gba_xr_independent_candidate)
  add_test(NAME gba_xr_independent_candidate_tests COMMAND gba_xr_independent_candidate_tests)
endif()

# Build and setup

## Native test target

Prerequisites: CMake 3.21+, a C11 compiler, and a threading library supplied by the host toolchain. On Windows, install CMake and the Desktop C++ workload (or an equivalent C11-capable compiler) before running:

```powershell
cmake -S native -B build/native -DBUILD_TESTING=ON
cmake --build build/native --config Debug
ctest --test-dir build/native -C Debug --output-on-failure
```

The expected tests are `gba_xr_reference_tests` and `gba_xr_bridge_tests`. They remain a host-toolchain validation step; the Android plug-in build below does not run them.

## mGBA adapter target

Prerequisites: a separately acquired pinned mGBA checkout, Android NDK matching the Unity toolchain, and a confirmed mGBA license/patch policy. Follow `MGBA_INTEGRATION.md`; do not connect the adapter to a commercial ROM for automated tests.

The Android output is an `arm64-v8a` Unity native plugin. A Release build has been verified using Unity 6000.3.12f1's bundled CMake, Ninja, and Android NDK; the result is `build/android-arm64/libgba_xr.so` and is copied to `unity/Assets/Plugins/Android/arm64-v8a/libgba_xr.so`. Retain the mGBA commit, NDK version, ABI version, and fixture hashes with each artifact.

The pinned local checkout is recorded in `third_party/mgba.commit` and is intentionally ignored by Git. Once CMake and the Android NDK are installed, configure the adapter with the Unity editor's NDK toolchain:

```powershell
& "<Unity-Editor>/Data/PlaybackEngines/AndroidPlayer/SDK/cmake/3.22.1/bin/cmake.exe" -S native -B build/android-arm64 -G Ninja `
  -DCMAKE_MAKE_PROGRAM="<Unity-Editor>/Data/PlaybackEngines/AndroidPlayer/SDK/cmake/3.22.1/bin/ninja.exe" `
  -DGBAXR_BUILD_MGBA_ADAPTER=ON `
  -DMGBA_ROOT="$PWD/third_party/mgba" `
  -DCMAKE_TOOLCHAIN_FILE="<Unity-NDK>/build/cmake/android.toolchain.cmake" `
  -DANDROID_ABI=arm64-v8a `
  -DANDROID_PLATFORM=android-24
& "<Unity-Editor>/Data/PlaybackEngines/AndroidPlayer/SDK/cmake/3.22.1/bin/cmake.exe" --build build/android-arm64 --target gba_xr_mgba_adapter --parallel 4
```

Copy the produced `libgba_xr.so` to `unity/Assets/Plugins/Android/arm64-v8a/` and let Unity import it. The P/Invoke binding is already present; select the imported plug-in in Unity and confirm Android is enabled and only `ARM64` is selected before building.

Unity 6000.3.12f1 currently bundles Android NDK r27. The adapter target explicitly passes `max-page-size=16384` and `common-page-size=16384` to its linker so its ARM64 `LOAD` segments are compatible with Android 15+ 16 KB page-size devices. Rebuild the plug-in after changes and verify `llvm-readelf -Wl libgba_xr.so` reports `Align 0x4000` for every `LOAD` segment.

## Unity Android XR target

Prerequisites: Unity 6 with Android Build Support (SDK, NDK, OpenJDK), a physical Android XR headset, and access to the official Unity package registry.

1. Open `unity/` in Unity 6.
2. Select the Android XR build profile.
3. Install and enable Unity OpenXR: Android XR (`com.unity.xr.androidxr-openxr`) in XR Plug-in Management.
4. Use URP and Vulkan; set Android API level 24 or higher, GameActivity, and Resizable Activity.
5. Run XR Project Validation and resolve every issue.
6. Commit the resolved package manifest/lock and project settings; run edit-mode tests before producing an APK.

The Android XR emulator cannot replace physical headset validation for Unity/OpenXR behavior. Use `DEVICE_VALIDATION.md` for the required on-device sequence.

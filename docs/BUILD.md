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

### XR audio output suspension

`ProjectSettings/AudioManager.asset` deliberately sets **Enable Output
Suspension** off (`m_EnableOutputSuspension: 0`). Galaxy XR previously showed a
live mGBA ring but zero Unity audio callbacks despite a listener and 2D
source. Keeping Unity's output awake avoids that dormant-output path, at the
cost of some additional idle battery use while the app is foregrounded. Do not
claim the tradeoff is acceptable until headset battery/thermal validation is
recorded.

The runtime reasserts the direct-filter `AudioSource` after worker attach,
focus, pause-resume, audio configuration changes, and while an audio worker is
attached. A long silent 48 kHz source clip only keeps the voice alive;
`OnAudioFilterRead` directly fills Unity's actual output-channel buffer from
the native stereo PCM ring. It deliberately does not call `AudioSettings.Reset`
on XR: a callback-local linear resampler consumes the fixed native 48 kHz PCM
at the current cached Unity output rate (for example, two native frames per
24 kHz output frame). The source bypasses listener/source/reverb effects and
XR spatialization, and ignores listener pause/volume, for deterministic 2D
emulator routing. Device logs emit a main-thread, two-second audio diagnostic
with callback/read/non-silent deltas, ring availability/drops, source state,
and output rate. A zero non-silent delta alone is informational because a game
can be quiet. Device logs report source enabled/active/playing/clip state, output
sample rate/DSP buffer, audio-filter callbacks, native PCM scratch-read capacity,
returned PCM frames, empty reads, and non-silent PCM frames. They label faults
as `UnityOutputDormant`, `NativePcmStarvation`, or `NativePcmReadStalled`.
The last case means callbacks did request PCM and the native ring was non-empty
but the bridge returned zero frames; capture that log before changing rates or
the resampler.

### Quick save states

The admin console provides **Save state · quick slot 0** and **Load state ·
quick slot 0**. The state path is app-private and ROM-specific:
`states/<content-or-staged-rom-id>/slot-0.ss0`. Loading requires that exact
slot to exist; states are never shared between imported ROM content identities.
Saving serializes through the mGBA worker after a completed emulated frame,
writes a temporary sibling file, then atomically replaces the slot only on
success. Unity never accesses mGBA state directly.
The temporary file is opened read/write because mGBA's serializer uses a
read/write memory map; opening it write-only makes Android reject that map and
must not be reintroduced.

Build the ARM64 native plug-in after changing state code, then copy
`build/android-arm64/libgba_xr.so` to
`Assets/Plugins/Android/arm64-v8a/libgba_xr.so` before the Unity APK build.
On device, save during gameplay, advance the game, load slot 0, and verify the
expected frame/input state resumes. Also test a missing slot, a corrupted file,
ROM switching, and headset sleep/wake; failed loads must retain the active
session and failed saves must retain the previous slot.

### App-private ROM import plug-in

The project includes `Assets/Plugins/Android/GbaXrRomImport.androidlib`, a
Unity Android Library source plug-in. Keep it enabled for Android builds; Unity
compiles and merges its private `RomImportActivity` alongside the GameActivity
entry point. No custom activity subclass, AndroidX dependency, or
`UnityPlayerActivity` assumption is used. The library reflects Unity's activity
and `UnitySendMessage` entry points rather than adding a Gradle dependency on
`unityLibrary`, avoiding a module cycle.

The helper activity launches an `ACTION_OPEN_DOCUMENT` picker with
`CATEGORY_OPENABLE`, copies the selected `.gba` stream to Unity's app-specific
`persistentDataPath/roms` directory, and returns JSON metadata through `UnitySendMessage`.
It intentionally does **not** use persisted document access: the product owns
the copied file after selection. Do not add `READ_MEDIA_*`, `READ_EXTERNAL_STORAGE`,
or broad filesystem permissions for this flow. Confirm the generated Android
manifest retains `com.tiger.gbaixr.RomImportActivity` as `exported="false"`.

Choose **Load ROM** from the admin console to open the picker each time. A valid
selection is copied into app-private storage and launched; cancellation or an
invalid selection leaves the current worker running. The app no longer reads or
writes the old ROM catalog. Existing copies and save files are retained.
On a Development APK, test selection, cancellation, non-`.gba` files, provider
read failure, selecting the same ROM twice, switching ROMs, and headset
sleep/wake. Confirm audio re-primes and frame/provenance state resets.

The Android XR emulator cannot replace physical headset validation for Unity/OpenXR behavior. Use `DEVICE_VALIDATION.md` for the required on-device sequence.

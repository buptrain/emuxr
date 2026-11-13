# Build and setup

## Native test target

Prerequisites: CMake 3.21+, a C11 compiler, and a threading library supplied by the host toolchain. On Windows, install CMake and the Desktop C++ workload (or an equivalent C11-capable compiler) before running:

The CMake project enables MSVC's documented experimental C11-atomics switch
for these native tests. Use Visual Studio 2022 17.5 or newer, or use Clang/GCC
on another host platform.

```powershell
cmake -S native -B build/native -DBUILD_TESTING=ON
cmake --build build/native --config Debug
ctest --test-dir build/native -C Debug --output-on-failure
```

The expected suite includes the reference compositor and bridge tests plus
`emuxr_libretro_host_tests`, `emuxr_gba_bridge_sink_tests`, and
`emuxr_pcm_resampler_tests`, and `emuxr_gba_libretro_worker_tests`. The worker
contract uses a fake core to verify worker-thread ownership, 240 × 160 frame
publication, controller input, 48 kHz audio normalization, and save/load
serialization. These remain host-toolchain validation steps; the Android
plug-in build below compiles, but does not execute, Android test binaries.

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

## Opt-in mGBA libretro core

The libretro route is a native refactor target, not the Unity runtime default.
It builds the pinned mGBA libretro core and the EmuXR provider without changing
the currently packaged `libgba_xr.so` or Unity P/Invoke binding:

```powershell
& "<Unity-Editor>/Data/PlaybackEngines/AndroidPlayer/SDK/cmake/3.22.1/bin/cmake.exe" -S native -B build/mgba-libretro-arm64 -G Ninja `
  -DCMAKE_MAKE_PROGRAM="<Unity-Editor>/Data/PlaybackEngines/AndroidPlayer/SDK/cmake/3.22.1/bin/ninja.exe" `
  -DGBAXR_BUILD_MGBA_LIBRETRO_CORE=ON `
  -DMGBA_ROOT="$PWD/third_party/mgba" `
  -DCMAKE_TOOLCHAIN_FILE="<Unity-NDK>/build/cmake/android.toolchain.cmake" `
  -DANDROID_ABI=arm64-v8a `
  -DANDROID_PLATFORM=android-24 `
  -DBUILD_TESTING=ON
& "<Unity-Editor>/Data/PlaybackEngines/AndroidPlayer/SDK/cmake/3.22.1/bin/cmake.exe" --build build/mgba-libretro-arm64 --target emuxr_mgba_libretro emuxr_mgba_libretro_provider_tests emuxr_mgba_libretro_adapter_tests emuxr_gba_libretro_worker_tests
```

The experiment produces `build/mgba-libretro-arm64/libemuxr_mgba_libretro.so`
and its opt-in mGBA core dependency
`build/mgba-libretro-arm64/mgba/mgba_libretro.so`. The controlled experimental
package copies this exact pair to
`Assets/Plugins/Android/GbaXrLibretroExperimental/arm64-v8a/` with Android-only
ARM64 importer metadata; see
[UNITY_LIBRETRO_PACKAGING.md](UNITY_LIBRETRO_PACKAGING.md). This is packaging,
not runtime enablement: run resolved-frame/audio/input/state equivalence
against the direct adapter using authorized fixtures before selecting it. The
adapter-specific ABI is
`emuxr_mgba_libretro_adapter_*`; its future Unity binding must use the
adapter-owned frame acquire/release facade rather than mixing its private
bridge context with `gbaxr_*` exports from direct `libgba_xr.so`. It does not
expose the generic worker as a runtime contract; its static bridge references
are bound locally and not exported. Both Android ARM64 shared targets apply 16 KB load-segment
alignment; verify all `LOAD` segments report `0x4000` before any device
deployment.

The managed selection seam currently fails closed to `mGBA (Direct)` and is
not shown in the admin console. Do not mark its libretro route launch-eligible
until the packaged binding and all four parity results are recorded.

The declaration-only Unity binding and its controlled experimental package are
documented in [UNITY_LIBRETRO_PACKAGING.md](UNITY_LIBRETRO_PACKAGING.md). The
pair is present only in the dedicated Android ARM64 experimental folder; it
does not enable runtime selection or replace the direct plug-in.

For an explicitly authorized, ROM-free Galaxy XR package-load check, add the
default-off `GbaLibretroSmokeProbe` component to a separate empty scene object
and enable its **Run On Android Awake** toggle for one Development build. It
only creates/destroys the isolated adapter and emits the
`GBA XR libretro probe` result marker. Follow the exact procedure in
[`UNITY_LIBRETRO_PACKAGING.md`](UNITY_LIBRETRO_PACKAGING.md#opt-in-packaged-adapter-smoke-probe);
do not attach it to `GbaNativeFrameDriver`, call a ROM-start API, or treat it
as runtime selection/certification.

The next default-off `GbaLibretroFixtureFrameProbe` uses only the generated,
hash-pinned `frame_sequence.gba` fixture to start the adapter and acquire/release
one 240 × 160 RGBA8 lease. Generate the ignored StreamingAssets test asset with
`fixtures/gba/build.ps1 -Fixture frame_sequence -InstallIntoUnityStreamingAssets`
before the authorized Development build. Its procedure and required SHA-256/log
markers are in
[`UNITY_LIBRETRO_PACKAGING.md`](UNITY_LIBRETRO_PACKAGING.md#opt-in-synthetic-fixtureframe-lease-probe).
It remains a separate GameObject/component and cannot upload a texture, expose
the console, select a core, or read a user/commercial ROM.

For the later, explicitly authorized Development-build batch, package both
`frame_sequence` and `input_state_replay` fixtures and use the separate,
default-off `GbaLibretroCertificationBatchProbe`. It validates resolved-frame
baseline, input observability, 48 kHz adapter PCM, and adapter-owned state
replay without touching `GbaNativeFrameDriver` or a user ROM. Its one-line log
contract and the final manual Galaxy XR approval requirement are documented in
[`UNITY_LIBRETRO_PACKAGING.md`](UNITY_LIBRETRO_PACKAGING.md#fixture-only-certification-batch-default-off).

### Native direct/libretro parity runner

The experimental runner is built only when both native switches are enabled:

```powershell
& "<Unity-Editor>/Data/PlaybackEngines/AndroidPlayer/SDK/cmake/3.22.1/bin/cmake.exe" --build build/mgba-parity-arm64 --target emuxr_mgba_parity_runner
```

It is not a Unity plug-in and must not be copied into `Assets/Plugins`. The
runner accepts a source-built synthetic/homebrew fixture and disposable state
directory only:

```text
emuxr_mgba_parity_runner --fixture <input_state_replay.gba> --state-dir <writable-temp-dir> --max-frames 3 --input-script 0,1,0 --deadline-ms 20000
```

Do not execute it on a headset without explicit authorization. For an approved
ADB run, stage the runner plus `libgba_xr.so`, `libemuxr_mgba_libretro.so`, its
`mgba_libretro.so` dependency, and one generated fixture into an isolated
device-temp directory; set `LD_LIBRARY_PATH` to that directory. The sole
machine-readable outcome is one `EMUXR_PARITY_V1` JSON line. A pass requires:
matching copied-RGBA hash arrays and first-pixel RGBA diagnostics after each
script mask, monotonically advancing lease sequences, both output rates at
48000, and state results `[-1,0,0]`
(invalid-path rejection, save, load) for both paths. The fixture's `0,1,0`
script is a release/A-press/release sequence. The runner discards two released
startup frames before this trace and again after state load, so it never
compares an emulator transport startup or pre-load framebuffer. The fixture
clears full VRAM before display, samples input on VBlank, and stores its state
in emulated RAM. Before the initial trace only, the runner applies an unobserved
stable A press/release to canonicalize the fixture stage; it must not repeat
that transition after a load. Each logical mask is held for two completed
emulation frames; the transition frame is discarded and the stable second frame
is hashed. After the initial warmup trace,
the runner checkpoints each backend, advances the same script, loads that
backend's own state, and requires its replayed resolved-frame hash to equal its
pre-load result; it then compares those final replayed hashes across backends.
PCM hashes remain diagnostic only and neither PCM nor state blobs are compared
as byte-equivalence claims. `--deadline-ms` is one watchdog across both fresh,
sequential sessions. Treat an init failure/timeout, frame timeout, state-replay
mismatch, unsupported geometry, missing dependency, nonzero exit code, or any
other reason as a parity failure. Record the fixture source and hash, mGBA
commit, NDK, and all JSON fields; never upload a commercial ROM.

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
source. This avoids that dormant-output path while a session is playing. Do
not claim the tradeoff is acceptable until headset battery/thermal validation
is recorded.

The runtime reasserts the direct-filter `AudioSource` after worker attach,
focus, pause-resume, audio configuration changes, and while an audio worker is
attached. The silent 48 kHz filter host is stopped and its `AudioSource` is
disabled whenever no native audio worker is attached, including before launch,
during session priming, and after session disposal, so it does not retain a
Unity voice or invoke audio-filter callbacks while no emulation audio worker
is active. It is re-enabled only after the native PCM ring is primed. This
removes the app's persistent silent-source work, but does not by itself
guarantee that the platform audio device suspends because output suspension
remains disabled.
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
manifest retains `io.github.buptrain.emuxr.romimport.RomImportActivity` as
`exported="false"`.

Choose **Load ROM** from the admin console to open the picker each time. A valid
selection is copied into app-private storage and launched; cancellation or an
invalid selection leaves the current worker running. The app no longer reads or
writes the old ROM catalog. Existing copies and save files are retained.
On a Development APK, test selection, cancellation, non-`.gba` files, provider
read failure, selecting the same ROM twice, switching ROMs, and headset
sleep/wake. Confirm audio re-primes and frame/provenance state resets.

The Android XR emulator cannot replace physical headset validation for Unity/OpenXR behavior. Use `DEVICE_VALIDATION.md` for the required on-device sequence.

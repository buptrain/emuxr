# GBA XR POC — implementation handoff

**Handoff date:** 2026-11-03

## What is working

The Unity Android XR baseline has been built, deployed, and visually validated on the physical Samsung Galaxy XR headset. The test scene displayed a cube at a stable head-tracked world position.

The headset is paired to the workstation through wireless ADB. The available `adb.exe` is at:

```text
C:\Workspace\android-tools\sdk\platform-tools\adb.exe
```

Wireless ADB can disconnect when the headset sleeps. Wake/unlock the headset, check the current address and connection port in **Developer options → Wireless debugging**, then reconnect using that current endpoint. Pair again only if ADB reports an authentication/pairing failure.

## Active project and workspace

- Repository root: `C:\Workspace\emulatorVR`
- **Active Unity project:** `C:\Workspace\emulatorVR\unity`
- Scene used for device baseline: `unity\Assets\GbaXr\Scenes\Prototype.unity`
- `C:\Workspace\emulatorVR\GBAVR` is a separate Unity project directory that appeared during setup. It was not edited by the agent. Do not treat it as the active project unless deliberately switching projects.
- Git was initialized at the workspace root, but no files have been committed; every project file remains untracked.

## Unity configuration in use

The active project uses Unity **6000.3.12f1** and the Android XR build profile.

The resolved package manifest contains:

- `com.unity.xr.androidxr-openxr` 1.1.0
- `com.google.xr.extensions` from `https://github.com/android/android-xr-unity-package.git`
- URP 17.3.0
- OpenXR Plugin 1.16.1
- XR Plug-in Management 4.5.4
- XR Interaction Toolkit 3.3.2
- XR Composition Layers 2.3.0
- Unity Test Framework 1.1.33

Project settings already configured manually:

- Android XR build profile selected
- Vulkan is the Android graphics API
- Android minimum API level is 24+
- Application entry point is GameActivity
- Resizable Activity is enabled
- Active Input Handling is **Input System Package (New)**, not Both
- Android OpenXR is enabled with Android XR Support; Hand Interaction and Oculus Touch Controller profiles were added
- URP HDR and post-processing were disabled for the POC

The Android XR Emulator is not useful for this Unity/OpenXR project. Continue testing on the physical Galaxy XR.

## Build failure fixed

The initial Android IL2CPP build failed with `IL1005` because `LayerOrderingTests.cs` referenced NUnit from the player `Assembly-CSharp` assembly.

The fix is present and should be retained:

- `unity/Assets/GbaXr/Runtime/GbaXr.Runtime.asmdef` — player/runtime assembly
- `unity/Assets/GbaXr/Tests/EditMode/GbaXr.EditModeTests.asmdef` — Editor-only test assembly, with `TestAssemblies`

This keeps NUnit out of the Android player. The successful cube deployment occurred after this correction.

## Current implementation assets

### Native

- `native/include/gba_xr_bridge.h` — v1 engine-independent C ABI
- `native/src/bridge.c` — triple-buffer resolved-frame mailbox, producer/consumer thread ownership, frame leases
- `native/src/reference_compositor.c` — deterministic fixture compositor
- `native/tests/bridge_test.c` and `native/tests/reference_compositor_test.c` — queue, thread, priority, blend, and miniature golden tests

The bridge does **not** yet contain mGBA. It has an explicit `gbaxr_publish_resolved_rgba` adapter hook for the future mGBA frame boundary.

Update: a pinned upstream checkout and first resolved-frame adapter now exist. See `third_party/mgba.commit`, `native/src/mgba_adapter.c`, and `native/include/gba_xr_mgba_adapter.h`. The adapter is not built yet because CMake/a C compiler are still unavailable on this workstation.

### Unity

- `unity/Assets/GbaXr/Runtime/LayerOrdering.cs` — managed layer-order policy
- `unity/Assets/GbaXr/Runtime/GbaTestFrameSurface.cs` — creates and uploads a point-filtered 240×160 RGBA32 diagnostic frame
- `unity/Assets/GbaXr/Tests/EditMode/LayerOrderingTests.cs` — edit-mode tests for ordering and diagnostic-frame dimensions

`GbaTestFrameSurface` is new and has **not** yet been deployed to the headset. It is the next device test.

The Android `arm64-v8a` native plug-in builds at `build/android-arm64/libgba_xr.so` and is copied to `unity/Assets/Plugins/Android/arm64-v8a/libgba_xr.so`. The resolved-frame path is validated on physical Galaxy XR with the synthetic Mode 3 fixture. Its mGBA worker owns the producer thread, while Unity owns consumer leases. The confirmed on-device resolved-frame hash is `0xF9838FC5` at settled lease 30 for the recorded mGBA revision.

## Parallax Emu startup menu

`GbaNativeFrameDriver` now creates the **Parallax Emu** world-space startup
menu at runtime; no scene Canvas or EventSystem is required. It uses only
controller input and does not rely on raycasts. For the blank-ROM startup flow,
set the driver component's **Rom Relative Path** to empty in
`Prototype.unity`; the menu opens instead of the advanced console. With a ROM
running, press **Xbox R3** to open the same root menu.

- **D-pad** moves the selection; **A** confirms; **B** backs out.
- Root options are **Core Selection**, **Load ROM**, and **Settings**.
- **Load ROM** invokes the existing Android document picker; a successful
  import follows the existing explicit safe launch path.
- **Settings** contains fixed controller-map information, **View Mode**
  (Virtual Field or Plane Anchoring), and the separate **Advanced Console**.
- Core selection is an explicit A-confirmed transaction. Direct mGBA is always
  available; the experimental libretro core is shown only when the existing
  readiness policy permits it. An active worker is restarted only on that
  confirmation path.

The menu is a child of the Quad/driver and appears automatically after script
reimport. Do not add a second menu component or a Graphic Raycaster. Keep
`GbaLibretroDiagnosticPreview` disabled unless deliberately conducting its
separate fixture-only experiment.

## Immediate next steps

1. Open `unity/` in Unity and allow source reimport.
2. Run **Window → General → Test Runner → Edit Mode**. The two `LayerOrderingTests` tests should pass.
3. In `Prototype.unity`, create a Quad at `(0, 1.6, 2)` with scale `(1.5, 1, 1)`.
4. Give the Quad a **Universal Render Pipeline/Unlit** material.
5. Attach `GbaTestFrameSurface`, then build and deploy it. Expect a 3:2 frame with a white border, cyan upper-left marker, magenta lower-right marker, and unblurred pixels.
6. Install a CMake/C11 compiler toolchain, then run the native test suite:

   ```powershell
   cmake -S native -B build/native -DBUILD_TESTING=ON
   cmake --build build/native --config Debug
   ctest --test-dir build/native -C Debug --output-on-failure
   ```

   At handoff, `adb` is installed but neither `cmake` nor a C compiler is visible on the command path, so native tests have not been executed.
7. Pin an upstream mGBA revision externally and implement the resolved-frame adapter first. Do not vendor mGBA or add commercial ROMs/BIOS files to this repository.

## Architecture and test rules

Read these before the mGBA adapter work:

- `ARCHITECTURE.md` — data path, memory/thread ownership, and Unity vs native boundaries
- `MGBA_INTEGRATION.md` — resolved frame → provenance → separable layer patch sequence
- `TEST_STRATEGY.md` — synthetic ROM fixtures and golden policy
- `DEVICE_VALIDATION.md` — physical Galaxy XR validation matrix
- `BUILD.md` — build prerequisites and commands

Key rule: first prove the resolved 240×160 mGBA framebuffer as an exact golden. Only then export PPU provenance and independent BG/OBJ planes. At zero parallax, recomposed layers must equal the resolved frame byte-for-byte; unsupported PPU cases must fail explicitly.

## Things not to do

- Do not add NUnit tests directly under runtime code without an Editor-only test assembly definition.
- Do not use the Android XR Emulator as a substitute for physical Unity/OpenXR validation.
- Do not expose mGBA types through `gba_xr_bridge.h`.
- Do not retain mGBA framebuffer pointers after publishing; the bridge owns copied frame data and leases it to Unity.
- Do not commit ROMs, BIOS files, Unity `Library/`, build artifacts, or mGBA source.

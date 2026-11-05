# Unity renderer bootstrap

Open this folder with a supported Unity 6 editor, then create/select the Android XR build profile. Use URP and Vulkan. In Package Manager/XR Plug-in Management, install and enable **Unity OpenXR: Android XR** (`com.unity.xr.androidxr-openxr`); run Project Validation and fix all findings before building.

Set Android minimum API level to 24 or higher, select GameActivity as the application entry point, and enable Resizable Activity. For a standard immersive POC, keep HDR and post-processing disabled in the URP asset until a measured device requirement justifies them.

The minimal manifest only includes the test runner. Provider configuration is editor/version-specific; keep the resolved `Packages/manifest.json`, `Packages/packages-lock.json`, and project settings under version control after configuring the official provider in Unity.

`Assets/GbaXr/Tests/EditMode` contains pure edit-mode tests for the zero-parallax layer ordering and native RGBA-frame contract. The phase-one native binding is in `Assets/GbaXr/Runtime/GbaNativeBindings.cs`; it is intentionally limited to resolved-frame output until the PPU instrumentation milestones are complete.

The edit-mode tests compile in `GbaXr.EditModeTests`, an Editor-only test assembly. Do not place NUnit test files directly in runtime folders or they will be included in Android player linking.

## First on-device compositor check

Create a Quad in the XR scene at `(0, 1.6, 2)` with scale `(1.5, 1, 1)`, then attach `GbaTestFrameSurface`. The component creates a point-filtered 240×160 texture and uploads a diagnostic frame. Its cyan top-left and magenta bottom-right markers make orientation and filtering mistakes visible before mGBA is connected.

## Native resolved-frame proof

`GbaNativeFrameDriver` is an Android-only resolved-frame driver. It looks for a **user-provided homebrew/test fixture** at `Application.persistentDataPath/gba-fixtures/frame_sequence.gba`, starts mGBA on a dedicated native producer thread, and replaces the diagnostic frame with its newest completed RGBA output. Unity only acquires, uploads, and releases frame leases on its render/main thread.

For the checked source-only synthetic fixture, the build script can stage its generated ROM under ignored `Assets/StreamingAssets/gba-fixtures/`. If the persistent file is absent, the driver imports that packaged fixture once using `UnityWebRequest`; it never attempts to load a ROM directly from the Android APK.

Attach this component only after the Android `libgba_xr.so` plug-in has been built and imported at `Assets/Plugins/Android/arm64-v8a/`. Do not place commercial ROMs or BIOS files in the Unity project or source repository.

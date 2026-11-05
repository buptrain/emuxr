# Samsung Galaxy XR device-validation protocol

## Required environment record

Before each run, record the Galaxy XR model and Android XR build, Unity editor and package versions, native ABI version, mGBA revision, app commit, fixture ROM hash, and chosen rendering mode. Do not log or redistribute copyrighted ROM contents.

## Bring-up sequence

1. Build the Unity 6 Android XR project with the Android XR build profile, URP, and Vulkan.
2. In XR Plug-in Management, enable Unity OpenXR: Android XR and resolve every Project Validation item.
3. Install on a physical headset. The Android XR emulator is not a substitute for Unity/OpenXR device testing.
4. Begin with the synthetic zero-parallax fixture. Capture the Unity output and compare to the native golden frame.
5. Enable one depth band at a time, then validate the full profile.

## Test matrix

| Area | Procedure | Pass condition |
| --- | --- | --- |
| Pixel fidelity | Run every synthetic fixture at zero parallax. | Frame hash/pixels match the approved golden. |
| Layering | Toggle each source and priority fixture. | No leakage, inverted priority, or blending discontinuity. |
| Bridge safety | Repeatedly pause/resume, background/foreground, and reload a ROM. | No native crash, hung render thread, or stale lease. |
| Interaction | Exercise controller and hand input while rotating/moving head. | Stable tracking and game input; no focus loss. |
| Comfort | Use default then maximum allowed profile depth for 20 minutes. | Default has no conspicuous double images or discomfort. |
| Performance | Capture Unity/XR runtime frame metrics after cold start and thermal soak. | Meets the project budget with no sustained frame pacing regression. |

Keep screenshots, logs, timing captures, and the fixture hash together in a dated local test report. Treat any runtime package or firmware upgrade as a new validation run.

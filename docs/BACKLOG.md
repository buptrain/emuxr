# Implementation-ready backlog

## Stage 0 — reproducible foundations (completed by this bootstrap)

- [x] Define an engine-independent C ABI with opaque context, bounded frame mailbox, and frame leases.
- [x] Add a deterministic RGBA reference compositor and native priority/blend/golden tests.
- [x] Add producer/consumer ownership and cross-thread-rejection tests for the native mailbox.
- [x] Add a Unity source/test skeleton without checking in generated Unity state.
- [x] Document legal fixture boundaries and device validation.

Acceptance: `ctest` passes on a desktop host. No ROM, BIOS, or third-party emulator source is present.

## Stage 1 — emulator oracle and fixtures

- [x] Pin an upstream mGBA revision and record its license/patch policy.
- [x] Add an adapter build target that produces an Android `arm64-v8a` shared library.
- [x] Export the exact 240x160 resolved RGBA frame at VBlank through the C ABI.
- [~] Create synthetic homebrew ROM fixtures for: BG priority, OBJ priority, alpha blend, brighten/darken, windows, affine BG, and sprite semi-transparency. (BG0 and BG0/BG1 tie contracts are complete.)
- [~] Generate versioned golden RGBA hashes/images from a known mGBA build; review every golden update. (Resolved-frame and BG0 contracts are approved; BG tie capture is pending.)

Acceptance: 60 Hz-frame stepping produces stable full-frame hashes and no C ABI ownership violations under thread sanitizers on supported desktop targets.

## Stage 2 — PPU provenance and separable layers

- [x] Instrument the final PPU composition boundary; lease final source, priority, blend effect, and diagnostic window state with each resolved frame.
- [x] Add a BG2 bitmap provenance probe and a full-frame BG0 text-mode provenance contract fixture.
- [ ] Export BG0–3, OBJ, and backdrop image/mask planes and per-pixel priority/blend metadata.
- [ ] Preserve window and OBJ mode behavior in the metadata contract.
- [ ] Add zero-parallax recomposition tests against the resolved oracle for every synthetic fixture.
- [ ] Define a game profile format mapping source classes or regions to authored depth bands.

Acceptance: all supported synthetic modes recompose byte-identically at zero separation; unsupported modes fail explicitly rather than silently rendering a wrong layer.

## Stage 3 — Unity renderer

- [ ] Create a Unity 6 URP Android XR project from the official Android XR build profile.
- [ ] Install/enable Unity OpenXR: Android XR through XR Plug-in Management; select Vulkan.
- [ ] Bind the native ABI with safe P/Invoke handles and render-thread texture upload.
- [ ] Implement a zero-parallax compositor view, then authored depth planes and a mono fallback.
- [ ] Add Unity edit-mode tests for draw ordering, depth mapping, lease disposal, and exact zero-parallax pixel output; add play-mode smoke tests.

Acceptance: an emulator-produced fixture presents at 1:1 pixel scale in Unity and the C# zero-parallax result matches the native golden.

## Stage 4 — device and performance validation

- [ ] Run the physical-device checklist in `DEVICE_VALIDATION.md`.
- [ ] Establish an objective frame-time budget on the target Galaxy XR firmware/build.
- [ ] Test head motion, stereoscopic comfort, controllers, hands, suspend/resume, and thermal behavior.
- [ ] Record device model, OS/build, Unity/editor/packages, mGBA commit, native ABI version, and fixture hashes in each report.

Acceptance: no emulator/render deadlocks, no corrupted frames, comfortable default separation in a 20-minute session, and reproducible logs/screenshots.

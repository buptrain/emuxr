# Architecture decision record: GBA 2.5D XR POC

## Scope and non-goals

The POC turns a GBA frame into an authored 2.5D scene: extracted PPU sources are placed at controlled depths and rendered by Unity on Android XR. It does not claim that all games can be converted into correct 3D worlds. Gameplay remains emulated; depth is visual presentation metadata.

ROMs, BIOS images, and game-specific assets are supplied only by the test/device operator and are never committed.

## Runtime data path

```text
mGBA emulation thread
  -> GBA PPU instrumentation (BG0..BG3, OBJ, backdrop, per-pixel provenance)
  -> engine-independent C ABI / double-buffered frame lease
  -> Unity Android native plug-in
  -> C# compositor model / URP planes or meshes
  -> Unity OpenXR: Android XR / Vulkan
  -> Samsung Galaxy XR
```

The first integration target is a full resolved 240x160 framebuffer plus diagnostic per-pixel provenance. The second target exports individual layers. This ordering makes every emulator change comparable against a known-good reference frame before introducing occlusion holes and depth placement.

## Ownership and threading

* The emulator thread is the only producer and publishes complete immutable frame snapshots.
* The rendering thread is the only consumer. It obtains a `gbaxr_frame_lease`, reads it, then releases it before the next render submission.
* A snapshot owns its pixel memory until its lease is released. Callers may not retain row pointers.
* A bounded triple-buffer queue is used; dropped presentation frames are acceptable, partially written frames are not. The bridge owns its copies of published frames; it never retains mGBA pixel pointers.
* Core callbacks never call Unity APIs. Unity owns all managed allocations and GPU uploads.

The C header is the compatibility boundary. mGBA-specific types must not escape it.

## Layer semantics

GBA priority is a per-pixel PPU outcome, not merely a fixed order of textured planes. Instrumentation must preserve at least: source kind (backdrop/BG0–3/OBJ), priority, OBJ mode, blend effect and coefficients, window visibility, and final source pixel. The reference compositor is intentionally small and deterministic; it specifies the initial composition policy for synthetic fixtures, not a replacement GBA PPU.

The adapter should expose both:

1. `resolved_rgba`: exact emulator display output, used as the visual oracle;
2. `layer_rgba` plus provenance/mask planes: inputs for 2.5D presentation.

For each test ROM and supported game profile, recomposing unwarped layers at zero separation must match `resolved_rgba` exactly, subject to explicitly documented unsupported PPU features.

## Renderer decision

Use Unity 6 with URP, Vulkan, and **Unity OpenXR: Android XR** as the base provider. Add Android XR Extensions only when a needed Android-specific capability is unavailable through the official provider. Keep the C ABI free of Unity types so a native OpenXR renderer can later replace Unity without changing emulator integration.

The renderer starts with a comfortable curved screen or layered quads, a depth scale capped per title, and a mono fallback. It must not infer depth automatically from arbitrary sprites during this POC.

## External dependencies

Obtain mGBA separately and pin its commit in the eventual superproject or dependency lockfile. Patch instrumentation as a small, rebasing-friendly adapter layer; do not fork unrelated emulator subsystems. Verify mGBA licensing and the licensing of any distributed test materials before redistribution.

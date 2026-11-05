# mGBA integration handoff

## Dependency boundary

Use an unmodified, pinned mGBA checkout as the dependency. Record the Git commit and upstream license notice before adding any adapter patch. Do not fork mGBA into this repository or mix emulator headers into `native/include/gba_xr_bridge.h`.

The adapter is the only component permitted to include both mGBA headers and the GBA XR bridge header. It owns an `mCore`/GBA instance and a `gbaxr_context`; Unity and the compositor see only the stable ABI. `mCore` is a public, feature-macro-dependent C struct: the adapter must compile with the same layout macros as the linked mGBA target (currently `ENABLE_VFS` and `ENABLE_DIRECTORIES`). Treat a mismatch as an ABI error; it can redirect function-pointer calls to invalid addresses.

## Milestone A — resolved-frame oracle (no PPU instrumentation)

1. Create an unbound `gbaxr_context` on the Unity thread, then create the mGBA core on the emulator worker thread. The worker's first publish atomically binds producer ownership; the Unity thread's first acquire binds consumer ownership.
2. Load a synthetic ROM and run exactly one mGBA frame per adapter `step` call.
3. At the frame boundary, obtain mGBA's resolved framebuffer through the pinned `mCore` video-pixel interface, convert it to tightly packed non-premultiplied RGBA8, and call `gbaxr_publish_resolved_rgba`.
4. The Unity thread calls `gbaxr_acquire_latest_frame`, uploads the leased pixels, then calls `gbaxr_release_frame` on that same render/consumer thread.

The first adapter implementation is in `native/src/mgba_adapter.c`. It uses the upstream `GBACoreCreate`, `runFrame`, and caller-supplied software video buffer APIs. It makes the mGBA-native color format explicit RGBA8 before publishing to the engine-independent bridge. This is intentionally resolved-frame-only: it does not yet claim that mGBA layers are separable.

Upstream mGBA currently exposes a core video-pixel interface and its GBA frame-end path obtains pixels from the active renderer. Validate those names and call ordering at the pinned revision rather than coding against `master`. This milestone deliberately avoids renderer patching: it establishes frame cadence, color conversion, queue ownership, and a full-frame golden oracle first.

## Milestone B — diagnostic provenance

Add a small renderer-side diagnostic surface at the final pixel-composition boundary. For each final pixel, record the winning source (backdrop/BG0–3/OBJ), priority, blend operand/effect, and window visibility. Publish this only with the resolved frame. The test fixture proves each field influences the expected pixel before it is used by Unity.

The bridge now reserves a copied `gbaxr_pixel_provenance` plane for this purpose. A null plane explicitly means unsupported; no caller may infer separable source data from the resolved color. Patch `third_party/mgba-patches/0001-gbaxr-final-pixel-provenance.patch` fills this plane from the software renderer's post-composition row flags. It records final source, priority, configured blend effect, and effective window blend/OBJ-window bits; it does not yet export independently recomposable layers.

Keep the hook inside a minimal adapter patch near the pinned GBA software renderer/frame-end boundary. It must have no Unity dependency and no heap allocation during scanline or frame composition.

## Milestone C — separable sources

Export color plus coverage/mask planes for BG0–3, OBJ, and backdrop, with enough provenance to diagnose non-separable pixels. The adapter must declare a feature unsupported for a fixture if zero-parallax recomposition cannot equal the resolved oracle; it must never silently approximate a layer.

The frame lease already reserves layer and provenance fields. Populate them only after their lifetime is tied to the lease's queue slot. Do not publish pointers into mGBA's working buffers.

## Required adapter tests

- 240×160 resolved output has a stable RGBA hash for each synthetic fixture.
- Frame sequences are strictly increasing and dropped queued frames present only the newest complete frame.
- A producer call from the Unity/render thread and a consumer call from the emulator thread return `GBAXR_WRONG_THREAD`.
- Releasing twice or using a stale lease returns `GBAXR_STATE_ERROR`.
- At zero parallax, recomposed extracted layers match resolved RGBA byte-for-byte.

## Patch hygiene

Keep each mGBA change as a focused patch series: resolved frame adapter, provenance, then layer export. CI must build the pinned revision, apply the series, and run the synthetic fixture suite. A rebase that changes any golden must be reviewed as an emulator-behavior change, not automatically accepted.

The current CMake configuration applies `0001-gbaxr-final-pixel-provenance.patch` before adding the pinned checkout. Configure against a clean checkout at the recorded commit; do not hand-edit the dependency instead of updating the patch.

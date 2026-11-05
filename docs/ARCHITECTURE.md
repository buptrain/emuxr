# GBA XR POC Architecture

## Purpose

GBA XR is a proof of concept for presenting an emulated Game Boy Advance image as a comfortable, authored 2.5D view in an Android XR headset. It targets Samsung Galaxy XR through Unity 6, URP, Vulkan, and the official Android XR OpenXR provider.

The emulator remains responsible for running the game and producing the canonical 240 × 160 GBA image. XR presentation is a separate concern. The first deliverable is a pixel-exact, head-tracked virtual screen; selective depth placement of backgrounds and sprites comes only after the project can prove that any extracted layers reconstruct the original image exactly at zero parallax.

This document describes the implemented architecture, the contracts between its parts, and the deliberate limits of the current milestone.

## Design principles

1. **Emulator correctness precedes XR treatment.** The resolved mGBA frame is the visual oracle. No visual reinterpretation is accepted until it can be compared with that oracle.
2. **The C ABI is the product boundary.** Unity does not know mGBA types; mGBA does not know Unity types. Either side can evolve without leaking internal structures across the boundary.
3. **A frame is all-or-nothing.** Unity sees a complete immutable snapshot or no frame. It never reads an mGBA working buffer while the PPU is writing it.
4. **The headset can drop presentation frames, but not corrupt one.** When Unity is slower than emulation, the bridge keeps the newest complete frame rather than delaying emulation or exposing torn pixels.
5. **Provenance is diagnostic before it is creative input.** Knowing that a final pixel came from BG2 does not mean BG2 can safely be moved in depth. Separate color and coverage planes need their own proof.
6. **Every new PPU behavior receives a synthetic fixture before it receives a game profile.** The project does not use commercial ROMs or BIOS images as automated test assets.

## System at a glance

```text
Synthetic fixture or user-supplied homebrew ROM
                |
                v
  native mGBA worker thread
  - owns mCore and GBA PPU state
  - executes runFrame()
  - receives final software-renderer provenance
  - captures one strictly-gated raw BG0 candidate or builds a conservative visible-source partition
                |
                | resolved RGBA8 + provenance + candidate or visible-source partition
                v
  engine-independent C ABI
  - three owned snapshot slots
  - producer/consumer thread affinity
  - sequence-numbered frame leases
                |
                | P/Invoke only; no mGBA pointers cross this boundary
                v
  Unity main thread
  - acquires newest lease
  - copies RGBA8 into managed upload memory
  - uploads a point-filtered 240 x 160 texture
  - releases lease
                |
                v
  URP unlit material on a virtual frame surface
                |
                v
  Unity OpenXR Android XR provider -> Vulkan -> Galaxy XR
```

The current renderer displays the resolved texture on a single world-space surface. A managed reference compositor reconstructs disjoint visible-source lease planes for zero-parallax validation. A first raw BG0 capture path now exists only for an opaque, full-frame, Mode 0 case that byte-recomposes the resolved oracle; the display path remains resolved-frame-first. This is intentionally not yet a true multilayer/depth compositor.

## Components and ownership

| Component | Owns | Must not own or expose | Responsibility |
| --- | --- | --- | --- |
| Fixture ROM | GBA program and test scene data | Product ROMs, BIOS files | Produces a deterministic PPU case. |
| mGBA checkout | Emulator source and internal state | Unity API types | Runs the GBA and supplies the software renderer. |
| mGBA adapter | One `mCore`, resolved native frame buffer, conversion buffer, provenance buffer | Unity objects or long-lived caller image pointers | Runs frames and publishes owned snapshots. |
| `gbaxr_context` bridge | Three resolved/provenance slots plus up to six copied source planes per slot, sequence numbers, thread affinity | mGBA or Unity implementation details | Moves complete frames across the native/managed boundary. |
| Unity binding and driver | Native context/worker handles, managed upload buffer, texture upload | mGBA frame pointers after lease release | Starts the worker, presents current frames, and reports fixture results. |
| Unity XR scene | Virtual surface, material, XR session | PPU state, mGBA headers | Places the resolved image in the Android XR world. |

The public native contract is [gba_xr_bridge.h](../native/include/gba_xr_bridge.h). It is deliberately small and C-compatible. `native/src/mgba_adapter.c` is the only code that is allowed to include both mGBA headers and this bridge header.

## Runtime lifecycle

### Startup

1. `GbaNativeFrameDriver` runs only on Android.
2. It looks for the selected fixture below `Application.persistentDataPath/gba-fixtures/`.
3. If a package-staged fixture is missing from persistent storage, Unity copies it once from `StreamingAssets` using `UnityWebRequest`. This avoids Android scoped-storage and APK-path limitations.
4. Unity calls `gbaxr_create_unbound(GBAXR_ABI_VERSION)`. The context is deliberately unbound because Unity is not the producer.
5. Unity starts `gbaxr_mgba_worker_start`, passing the local ROM path and requested frame rate.
6. The native worker creates the mGBA core, loads the ROM, resets it, then attaches the provenance output to the active software renderer.

Attaching provenance after load/reset is important. Before that point, mGBA can still expose a small dummy renderer object. Treating that object as a software renderer corrupts callback memory and crashes the process.

### Per-frame path

```text
Worker thread                              Unity main thread
-------------                              -----------------
runFrame()
  |
  +-- mGBA resolves the GBA image
  +-- software renderer writes final-pixel provenance
  +-- adapter converts mColor -> RGBA8
  +-- captures a narrow raw BG0 candidate before PPU composition
  +-- otherwise builds disjoint visible-source planes from final provenance
  +-- gbaxr_publish_frame_layers()
        copies resolved, provenance, and source-partition planes into a bridge slot
                                               acquire_latest_frame()
                                               copies RGBA bytes for upload
                                               uploads the Texture2D
                                               verifies the selected fixture
                                               release_frame()
```

The worker uses a paced loop, not Unity callbacks, to execute emulation. Unity never calls `runFrame()`, and the worker never calls Unity APIs.

### Shutdown

`OnDisable` stops and joins the worker before destroying its bridge context. This order ensures that a producer cannot publish to released queue storage. The worker then unloads and deinitializes its mGBA core. Any managed references to a released native lease are invalid by contract.

## Native frame bridge

### ABI shape

ABI version 2 fixes the GBA image size at 240 × 160 and uses tightly packed, non-premultiplied RGBA8 for resolved pixels. A `gbaxr_frame_lease` contains:

- a monotonically increasing `sequence` number;
- one resolved RGBA image view;
- optional layer views: up to six full-size RGBA8 planes, each with a coverage byte per pixel;
- explicit layer semantics: none, final-visible partition, or independent PPU candidates;
- an optional provenance pointer and stride.

The bridge's lifetime is opaque to Unity. Native structs are mirrored only where required for P/Invoke layout; Unity never allocates or frees bridge-owned image storage.

### Queue behavior

The bridge owns three snapshot slots. Each slot contains a full resolved frame, a same-sized provenance plane, and capacity for up to six copied color/coverage source planes. A slot is one of:

```text
FREE -> READY -> LEASED -> FREE
```

Publishing copies data into a free slot. If no free slot exists, the producer replaces the oldest `READY` slot. It never replaces `LEASED` storage. Acquiring selects the newest `READY` slot, so presentation favors freshness over showing an outdated backlog. The consumer can hold only one lease at a time; a second acquire before release is a state error.

This is a presentation mailbox, not a recording queue. Frames can be skipped, but the consumer cannot observe partly copied data.

### Thread-affinity rules

The first `publish` call binds the producer thread. The first `acquire` call binds the consumer thread. Later calls from another thread return `GBAXR_WRONG_THREAD`.

`gbaxr_create_unbound` is used because the native worker, not the Unity startup thread, must become the producer. The bridge tests cover ownership, double release, stale leases, and producer/consumer misuse.

## mGBA integration

### Dependency policy

mGBA is a separately acquired dependency pinned by [third_party/mgba.commit](../third_party/mgba.commit). Its source checkout is ignored; only a small, reviewable patch is tracked in [third_party/mgba-patches](../third_party/mgba-patches/).

The CMake target applies the patch before adding mGBA as a static core-only dependency. This is intentional: a clean checkout at the recorded revision must build the same adapter rather than relying on manual edits inside the ignored dependency directory.

### Adapter responsibilities

`native/src/mgba_adapter.c` performs four jobs:

1. creates one GBA `mCore` instance on the native worker;
2. asks mGBA to render into caller-owned native pixel storage;
3. converts mGBA's native `mColor` representation to explicit RGBA8;
4. captures a raw Mode 0 BG0 candidate and coverage mask when requested;
5. publishes either that candidate only after it proves byte-exact recomposition,
   or the resolved image plus final-provenance visible partition as one bridge snapshot.

The first independent candidate path is intentionally narrow: its BG0 coverage
must be complete, every resolved pixel must have BG0 provenance, blending and
window effects must be disabled, and its RGBA bytes must exactly equal the
resolved frame. Any failure uses the zero-parallax-only visible partition.
This is a safety gate, not a claim that every Mode 0 BG0 is independently
parallaxable.

The adapter must compile with the same mGBA feature macros as the static mGBA target. `mCore` is a public C struct whose layout changes when feature macros change. In particular, omitting `ENABLE_DIRECTORIES` moved function pointers relative to the adapter's expectation and caused a device crash. The adapter explicitly defines `ENABLE_VFS` and `ENABLE_DIRECTORIES` to match its mGBA build.

### Android binary requirements

The Unity plug-in is `libgba_xr.so` for `arm64-v8a`. It is linked with 16 KB page-size alignment so Android 15+ devices can load it. Every ARM64 `LOAD` segment must report alignment `0x4000`; see [BUILD.md](BUILD.md) for the verification command.

## PPU provenance

### What it means

The current mGBA patch tracks the winning source through the GBA software renderer's composition operations, then emits a four-byte `gbaxr_pixel_provenance` record at the final composition boundary. It uses a parallel per-scanline metadata row because mGBA intentionally clears ordering flags from the visible row after rejecting an equal-priority candidate; changing that sentinel would alter normal PPU composition.

For every final pixel it writes:

| Byte | Meaning | Values in ABI v1 |
| --- | --- | --- |
| `source` | Winning final source | backdrop `0`, BG0–BG3 `1`–`4`, OBJ `5` |
| `priority` | Winning pixel priority | GBA priority `0`–`3` |
| `blend_effect` | Current renderer blend mode | none `0`, alpha `1`, brighten `2`, darken `3` |
| `window` | Diagnostic window bits | bit 0 effective blend enabled; bit 1 OBJ-window pixel |

The record describes the pixel that won after normal GBA PPU composition. It is copied into the same queue slot as its resolved RGBA pixel, so the two planes have identical sequence and lifetime.

### What it does not mean

Provenance is not a BG/OBJ extraction API. In particular, it does not currently provide:

- the color that a hidden background or sprite would have contributed;
- coverage/transparent masks for every source;
- separate semi-transparent OBJ input and backdrop input;
- blend coefficients, target selection, or a complete reconstructable window mask;
- a depth model for uncovered pixels revealed by parallax.

Unity must never turn final `source` values into independently displaced layers. ABI v2 explicitly labels each lease as `none`, `visible partition`, or `independent candidates`. Most frames use a conservative visible-source partition: each resolved pixel is copied into exactly the bucket named by final provenance. Unity verifies this partition recomposes byte-identically at zero parallax. It contains no hidden PPU candidates, so it is not a depth-displaceable BG/OBJ extraction. The single exception is the strictly-gated raw BG0 capture path described above; BG1–3, OBJ, backdrop, windows, blending, and occluded pixels still require renderer-side capture work.

## Unity and Android XR presentation

### Why Unity is the current frontend

Unity owns application startup, the Android XR OpenXR session, Vulkan integration, virtual scene placement, and the initial user-facing controls. The native library is an emulator adapter, not a RetroArch replacement and not an OpenXR frontend. It supplies frames to Unity much as an emulator core supplies frames to another frontend, but through this project's intentionally narrow ABI.

This separation makes a future native OpenXR frontend possible without changing the emulator-facing interface. Such a frontend would replace the Unity consumer and renderer only; the mGBA adapter and frame bridge would remain native and engine-independent.

### Current Unity path

`GbaNativeFrameDriver` receives each lease on Unity's main thread, copies its resolved RGBA bytes into a managed array, and calls `GbaTestFrameSurface.UploadRgba32`. The surface owns a point-filtered `Texture2D` and assigns it to a URP/Unlit material. Point filtering is deliberate: it keeps GBA pixel boundaries deterministic for fixture validation.

The diagnostic frame surface remains useful before a worker produces a valid frame. Its checkerboard, colored corner markers, and border distinguish setup/rendering errors from emulator failures.

### XR configuration boundary

The active Unity project uses URP and Vulkan with the official Android XR OpenXR provider. HDR and post-processing are disabled in the POC because they would alter the visual oracle. Any later compositor effect that changes pixels must be outside the zero-parallax comparison path or have a separate documented oracle.

## Fixture and golden strategy

Fixtures are small source-only ARM7TDMI programs under [fixtures/gba](../fixtures/gba). `build.ps1` compiles a selected fixture with the Unity Android NDK toolchain and can stage the resulting ignored ROM into Unity `StreamingAssets`.

| Fixture | Behavior under test | Status |
| --- | --- | --- |
| `frame_sequence` | Mode 3 BG2 resolved-frame path, texture upload, and baseline provenance probe | Approved resolved hash `0x9B341219` on the pinned device/core configuration |
| `bg0_priority` | Opaque Mode 0 BG0, source `1`, priority `2`, no blend, cyan output | Approved full-frame hash `0x87A1ADC5` |
| `bg_tie` | Equal-priority BG0/BG1 tie rule; BG0 cyan left half wins while transparent BG0 exposes magenta BG1 on the right | Approved full-frame hash `0x0BB5FDC5` |
| `obj_priority` | Equal-priority OBJ/BG0 tie rule; a 64 × 64 magenta OBJ wins over cyan BG0 | Approved full-frame hash `0xEBBEADC5` |
| `alpha_blend` | Equal 8/16 alpha blend: red BG0 target 1 over blue BG1 target 2 | Approved full-frame hash `0xC4D18DC5` |
| `brighten` | `EY=8` brighten effect on a red BG0 target 1 | Approved full-frame hash `0x06A215C5` |
| `darken` | `EY=8` darken effect on a red BG0 target 1 | Approved full-frame hash `0x86158DC5` |
| `window_alpha` | WIN0 selectively enables normal alpha blending | Approved full-frame hash `0x4738CBC5` |
| `affine_bg2` | Mode 2 BG2 identity transform, opaque cyan | Approved full-frame hash `0x87A1ADC5` |
| `obj_alpha` | Semi-transparent OBJ alpha blends over opaque BG0 | Approved full-frame hash `0x28C36DC5` |

An approved golden is not merely a hash typed into code. It is a reviewed fixture source and ROM hash, a pinned mGBA revision, a native ABI version, a captured resolved frame hash, and a physical-device observation. Any change in one of those inputs requires deliberate review rather than bulk regeneration.

## Test layers

```text
Native reference compositor
  -> tiny deterministic priority/alpha cases
Native frame bridge
  -> ownership, thread-affinity, lease and copying cases
Synthetic mGBA fixtures
  -> resolved image and provenance contracts
Unity edit and play mode
  -> ordering, native layout, upload and lease disposal
Galaxy XR device validation
  -> OpenXR session, image fidelity, suspend/resume, comfort and performance
```

The reference compositor is deliberately a compact test oracle. It models the project’s explicit layer ordering and generic alpha behavior; it is not an attempt to reimplement the GBA PPU.

## Layer extraction roadmap

Layer extraction is intentionally deferred until the following proof chain exists:

1. add a fixture that establishes BG/OBJ priority behavior;
2. add alpha-target, brighten/darken, window, affine-BG, and sprite-mode fixtures;
3. capture approved resolved-frame goldens for each fixture;
4. export one color plane plus coverage mask per source into bridge-owned slots;
5. recompose those planes at zero separation and compare every byte with the resolved golden;
6. reject unsupported blend/window/sprite combinations explicitly;
7. only then map selected source classes or regions to authored XR depth bands.

The initial stereoscopic renderer should remain conservative: modest separation, a comfortable virtual-screen distance, optional curved display, and a mono fallback. It must not attempt generic automatic depth reconstruction from arbitrary GBA sprites.

## Operational guidance

- Build and native binary details: [BUILD.md](BUILD.md)
- mGBA patch and adapter specifics: [MGBA_INTEGRATION.md](MGBA_INTEGRATION.md)
- Fixture build and expected logs: [ROM_FIXTURE_TEST.md](ROM_FIXTURE_TEST.md)
- Full validation policy: [TEST_STRATEGY.md](TEST_STRATEGY.md)
- Physical Galaxy XR test protocol: [DEVICE_VALIDATION.md](DEVICE_VALIDATION.md)

Do not commit commercial ROMs, BIOS images, the mGBA source checkout, Unity `Library` state, packaged APKs, or generated fixture ROMs. The repository contains source, pinned dependency metadata, the focused mGBA patch, and the built native plug-in required by the active Unity project.

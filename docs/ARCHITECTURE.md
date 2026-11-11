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

## Multi-core refactor seam

The working direct-mGBA path remains the production path while the project
introduces a libretro-compatible core host. The first slice is
[`emuxr_libretro_host.h`](../native/include/emuxr_libretro_host.h): a small
frontend function table that owns standard libretro callbacks, converts the
three software video formats to transient RGBA8, offers a controller mailbox,
and forwards serialization and PCM batches to host-owned sinks.

It is intentionally separate from `gba_xr_bridge.h`. That bridge is ABI v3
and has fixed 240 × 160 lease slots; widening it for arbitrary core geometry
is a later ABI-v4 task. The libretro host therefore has no GBA dimensions and
does not know Unity. The temporary
[`emuxr_gba_bridge_sink`](../native/include/emuxr_gba_bridge_sink.h) copies
only matching 240 × 160 resolved RGBA8 frames into the existing bridge without
making SNES/DS semantics look like GBA.

```text
libretro core (one worker) -> emuxr_libretro_host -> resolved RGBA8 sink
                                                     -> current GBA bridge
                                                     -> Unity
```

The standard libretro route is resolved video, audio, input, content, and
state only. Layer provenance and 2.5D candidate planes remain optional
core-specific composition providers, negotiated above the standard host. A
core that has no provider is correct by presenting `ResolvedOnly`.

`GbaCoreSelectionPolicy` is a managed launch-policy seam, not an exposed
runtime switch. A request for the optional libretro route falls back to direct
mGBA unless all five gates are recorded: packaged Unity binding,
resolved-frame parity, input parity, audio parity, and save-state parity. The
current app always requests direct mGBA, so a native build artifact alone can
never change device behavior.

The first concrete provider is optional mGBA libretro support. Configure with
`-DGBAXR_BUILD_MGBA_LIBRETRO_CORE=ON` to build the pinned mGBA libretro core
and [`emuxr_mgba_libretro_provider`](../native/include/emuxr_mgba_libretro_provider.h).
It supplies the standard entry points to the generic host and intentionally
has no PPU metadata path yet. Its isolated Android ARM64 dependency pair is
packaged behind an experimental Unity gate, but it is not selected by the
current direct-mGBA worker until resolved-frame, audio, input, and state
equivalence plus device certification are demonstrated.

That option also builds `libemuxr_mgba_libretro.so`, an isolated experimental
adapter shared library. Its only adapter-specific public entry points are in
[`emuxr_mgba_libretro_adapter.h`](../native/include/emuxr_mgba_libretro_adapter.h):
opaque create/start/stop/destroy, joypad, PCM, state, and an adapter-owned
frame acquire/release facade. It links the generic GBA libretro worker to the
pinned mGBA libretro provider; the public wrapper exposes no libretro or mGBA
headers. The underlying `gbaxr_context` is intentionally private: a future
Unity P/Invoke binding must acquire and release through this same shared
object, never pass its context to `gbaxr_*` symbols from `libgba_xr.so`.
The libretro adapter binds its private bridge references locally and hides its
static bridge/worker/provider symbols, leaving only adapter-specific exports.
It is not copied to Unity, not selectable, and does not change `libgba_xr.so`.

### Native parity harness

When **both** native backends are configured, the build also produces the
fixture-only `emuxr_mgba_parity_runner`. It runs direct mGBA first, destroys
that session, then creates a fresh mGBA-libretro session; this ordering avoids
the libretro host's process-global callback state. The runner consumes leases
on one thread, copies only resolved RGBA pixels before hashing, and emits one
`EMUXR_PARITY_V1` JSON result. Its bounded, GBA-order mask-per-frame script is
translated at the libretro boundary and is executed against fresh direct then
libretro sessions. `input_state_replay.gba` samples one input at VBlank and
stores its stage/latch in emulated RAM; `0,1,0` visibly toggles a small
cyan/magenta patch. The runner discards two released-key startup frames, then
records initial script hashes; it repeats that released-key drain after load so
the replay cannot consume a queued pre-load lease. Each logical mask spans two
emulation frames and only the stable second frame is hashed. Before this first
trace only, an unobserved A press/release canonicalizes fixture state; replay
does not repeat it after load. It then records 48 kHz output-rate, a
first-pixel RGBA diagnostic, PCM counters/hash, and invalid/save/load statuses;
then it
checkpoints each backend, advances the script, loads its own state, and
requires its replayed resolved-frame hash to equal the pre-load frame before
comparing the final replayed frame across backends. It never compares PCM or
state blobs as byte-equivalence claims. One absolute deadline spans both
sessions, and the JSON reason distinguishes init, frame, and replay failures.

The executable accepts only a caller-supplied UTF-8 **synthetic/homebrew
fixture path** and a disposable writable state directory. It embeds no ROM,
never receives commercial content from the project, and requires separately
authorized ADB execution.

The mGBA libretro provider explicitly selects a conversion profile only for
its standard RGB565 output. mGBA has already converted GBA BGR555 to standard
RGB565, so red/blue retain normal RGB565 positions. Its sixth green bit is a
duplicate of the source GBA five-bit green MSB; the profile restores that
five-bit value before 8-bit expansion to match direct mGBA. Generic libretro
RGB565 retains its normal six-bit-green conversion.

`emuxr_gba_libretro_worker` is the next seam: it owns one libretro host on a
native producer thread, uses the current bridge only for exact 240 × 160
resolved GBA frames, queues 48 kHz PCM, and performs serialize operations at a
completed-frame boundary. It is likewise not connected to Unity yet. The
worker-owned `emuxr_pcm_resampler` converts each core's source-rate stereo
batches before they reach its bounded ring; the original source rate remains
available only as diagnostic telemetry. This gives the future generic Unity
binding the same fixed-rate contract as the direct-mGBA path without letting
Unity call a core or own resampler state.

## Runtime lifecycle

### Startup

1. `GbaNativeFrameDriver` runs only on Android.
2. It looks for the selected fixture below `Application.persistentDataPath/gba-fixtures/`.
3. If a package-staged fixture is missing from persistent storage, Unity copies it once from `StreamingAssets` using `UnityWebRequest`. This avoids Android scoped-storage and APK-path limitations.
4. Unity calls `gbaxr_create_unbound(GBAXR_ABI_VERSION)`. The context is deliberately unbound because Unity is not the producer.
5. Unity starts `gbaxr_mgba_worker_start`, passing the local ROM path and the
   fixed 60 FPS worker cadence used by the audio path.
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

## Frontend foundation

`GbaFrontendRuntimeModel` describes the one supported core as `mgba-direct`
(`mGBA (Direct)`), including the bridge ABI version and the current audio
snapshot. Its immutable capability manifest distinguishes the implemented
native worker, resolved renderer, final-pixel PPU provenance, visible-source
partition, and PCM-pull path from features that are not ready: general
independent PPU candidate planes, persistent save RAM, and save states. It is
not a libretro or generic-core abstraction.

`GbaAppPrivateStoragePaths` is a pure layout policy for a future app-private
ROM library. Given the application root it defines `roms`, `saves`, `states`,
`system`, and `temp`, while rejecting absolute and parent-traversal user-ROM
paths. It creates or moves no directories and does not alter the current
`gba-fixtures` or `gba-user` staging paths. Android document-picker import,
library metadata, save RAM, and state serialization remain separate follow-up
services.

`GbaRomImportService` is the first document-picker/library service. On Android
the private `GbaXrRomImport.androidlib` helper activity launches a one-time
`ACTION_GET_CONTENT` + `CATEGORY_OPENABLE` picker, copies the selected `.gba`
stream to Unity's app-specific `persistentDataPath/roms`, enforces the 32 MiB GBA limit, calculates
SHA-256, and returns metadata only to Unity. It deliberately retains no
external URI permission and requests no broad storage permission. Unity
validates the result, records a JSON catalog through a same-directory temporary
file and replacement policy, and does not start or reload mGBA. A later library
selection operation must explicitly choose a catalog entry before altering the
active worker.

The world-space admin console now exposes **Import ROM…** and **Open local ROM
library** as controller-selectable actions. The library lists metadata and
retains a selected entry. **Launch selected ROM** is enabled only after an
explicit Xbox A press on that row; importing never launches content. The driver
revalidates the selected catalog path under app-private `roms`, fences frame
acquisition (leases are local to `Update` and released before this input path),
detaches the Unity audio callback, destroys worker then bridge, starts a new
direct-mGBA worker, and resets fixture/provenance/presentation state. A start
failure leaves no native handles and keeps the library console interactive so
the user can choose another ROM. Xbox B returns from the library to the open
admin console; only closing that console returns gameplay input.

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
| `frame_sequence` | Mode 3 BG2 resolved-frame path, texture upload, and baseline provenance probe | Approved resolved hash `0xF9838FC5` at settled lease 30 on the pinned device/core configuration |
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

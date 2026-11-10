# Nintendo DS support backlog

## Purpose and decision

This backlog describes the additive Nintendo DS (DS) path for the Galaxy XR
emulator POC. It must not regress the existing direct-mGBA GBA path.

The target is a common, engine-independent frame transport with two levels of
contract:

1. A shared **composition-domain** frame schema for resolved output surfaces,
   coverage-backed planes, leases, and fallback behavior.
2. Exact, opt-in metadata schemas for each core. GBA PPU provenance remains
   GBA-specific; DS compositor data remains DS-specific.

Do **not** model GBA as `NdsEngineB`. That label carries DS-only hardware
meaning. A GBA frame has one `GbaPpu` composition domain; a DS frame has
`NdsEngineA` and `NdsEngineB` domains that may route to either LCD output.

## Guardrails

- [ ] Resolve the GPL-3.0 distribution implications of the selected melonDS
  source before linking it into the Unity Android APK. Libretro hosting does
  not remove a core's license obligations.
- [ ] Pin the selected melonDS source and all submodules by commit. Record the
  upstream URL, commit, local patches, toolchain, and Android build flags.
- [ ] Retain the current direct-mGBA adapter as the GBA XR reference path
  until a replacement reproduces its approved resolved-frame, provenance,
  audio, input, and state behavior.
- [ ] Keep user ROMs, BIOS/firmware, saves, and states outside source control,
  test fixtures, diagnostic uploads, and logs.
- [ ] Ship no DS depth mode until it has a byte-exact zero-parallax
  recomposition contract. Resolved display remains the safe fallback.

## Proposed shared model

```text
FrameSet
  OutputSurface[]
    - id, role, width, height, format, resolved RGBA8
    - maps to one or more composition domains

  CompositionDomain[]
    - id, kind, capabilities, output routing
    - Layer[]: role, index, color, coverage, priority, metadata reference

  MetadataSchema
    - schema id + schema version
    - opaque only to consumers that explicitly support that exact schema
```

Required generic output roles:

- `PrimaryDisplay` for a single-screen system.
- `TopDisplay` and `TouchDisplay` for DS.

Required generic layer roles:

- `Background` with a core-local index.
- `Object`.
- `Backdrop`.
- `External3D` for a separately produced 3D image when the core supports it.
- `ResolvedOnly` when source-level separation is unavailable or ineligible.

Required safety rules:

- A frame lease is acquired and released on the emulator worker; Unity only
  consumes bridge-owned copies.
- Each frame has a monotonic sequence and immutable dimensions/strides.
- A plane always includes an explicit coverage mask; transparent color values
  are never used as coverage inference.
- Unknown schemas, incomplete planes, overlap, mismatched sequence, or failed
  recomposition select the resolved surface atomically.
- Core-specific metadata is never interpreted merely because a layer name or
  index matches another core.

## Core mapping

| Core | Output surfaces | Composition domains | Depth status |
| --- | --- | --- | --- |
| Direct mGBA | `PrimaryDisplay` (240x160) | `GbaPpu`: BG0-3, OBJ, backdrop | Existing GBA-only provenance/candidate experiments |
| melonDS initial POC | `TopDisplay`, `TouchDisplay` (256x192 each) | Resolved-only | No depth separation |
| melonDS later | `TopDisplay`, `TouchDisplay` | `NdsEngineA`, `NdsEngineB`; optional Engine A external 3D producer | Research-only until contracts pass |

The DS 3D image must not be treated as a generic sprite/background plane. It
is composited through DS Engine A rules, and a native Z buffer is useful for
diagnostics or image reprojection but does not alone create correct second-eye
geometry.

## P0 — feasibility and build provenance

- [ ] Choose the melonDS integration target: a maintained libretro melonDS
  fork or a dedicated wrapper over the Android core library.
- [ ] Build a minimal `arm64-v8a` native DS library with the Unity Android NDK.
- [ ] Prove Android 15+ 16 KB ELF `LOAD` alignment.
- [ ] Define a `CoreDescriptor`: core ID, system ID, supported extensions,
  build ID, required system assets, input schema, display capabilities, audio
  format, and state-format version.
- [ ] Add an explicit system-assets policy for user-provided DS BIOS/firmware;
  direct boot and firmware boot must report their requirements before launch.
- [ ] Record the license decision and third-party notices.

**Acceptance:** The pinned core builds reproducibly for Galaxy XR and can be
loaded without changing the existing mGBA plug-in.

## P1 — generic host seam without behavior changes

- [ ] Add a core-neutral Unity `EmulatorSession` interface; wrap the existing
  `GbaNativeFrameDriver` rather than changing its GBA fixture behavior.
- [ ] Add native `FrameSet`/`OutputSurface` descriptors with dynamic
  width/height, sequence, row stride, and bounded lease ownership.
- [ ] Keep the existing GBA ABI as a legacy compatibility path or introduce a
  distinct versioned generic ABI. Never reinterpret a v3 GBA lease as a
  generic frame-set lease.
- [ ] Generalize audio pull, worker state, input dispatch, ROM identity, save
  state metadata, and diagnostics behind the core-neutral interface.
- [ ] Generalize app-private identities to
  `core-id/system-id/rom-content-sha256`; retain no ROM-selection list.
- [ ] Move GBA-only fixture checks and presentation profile checks behind a
  `GbaPpu` capability gate.

**Acceptance:** Existing GBA EditMode/device behavior is unchanged; an
unsupported core can be reported without attempting a GBA frame upload.

## P2 — libretro XR extension experiment

- [ ] Implement a host-side libretro worker for standard lifecycle, resolved
  video callback, PCM callback, input, serialization, system directory, and
  save directory behavior.
- [ ] Use libretro's negotiated procedure-address interface to expose an
  optional `retrox_get_render_extension_v1` symbol. Do not overload the
  standard video callback or add unregistered private environment commands.
- [ ] Define `retrox_render_extension_v1` capability negotiation, static ABI
  table lifetime, same-worker acquire/release rules, and resolved-frame
  sequence matching.
- [ ] Define generic `FrameSet` descriptors and the metadata-schema registry.
- [ ] Add `org.gbaxr.mgba.gba-ppu.v1` as a core-specific metadata schema and
  prove that a patched mGBA libretro build matches the direct-mGBA oracle.
- [ ] Make every extension optional: an ordinary libretro core must continue
  to render as resolved-only.

**Acceptance:** A core without the extension remains playable, while a patched
mGBA core can expose data equivalent to the direct GBA contract without Unity
depending on mGBA headers.

## P3 — melonDS resolved dual-display vertical slice

- [ ] Add `.nds` to the core-declared document-picker validation policy; keep
  the one-time picker/no-persistent-ROM-list interaction model.
- [ ] Launch a user-selected app-private `.nds` file through the DS session.
- [ ] Publish top and touch LCDs as two resolved 256x192 RGBA8 surfaces. Do
  not concatenate them in Unity merely because a libretro core may provide a
  256x384 composite fallback.
- [ ] Create world-space top/touch display panels under the existing screen
  placement root: stacked, side-by-side, top-focused, and touch-focused.
- [ ] Map Xbox input to the DS button schema. Preserve R3 for the frontend
  console and do not reuse the GBA X/Y-to-L/R compatibility mapping.
- [ ] Map a gameplay hand/controller ray hit on `TouchDisplay` UVs to integer
  DS touch coordinates; resolve gesture ownership before placement handling.
- [ ] Feed DS PCM into the generic 48 kHz audio endpoint; do not call melonDS
  from Unity's audio callback.
- [ ] Add DS save RAM and state paths under the DS core/system/ROM identity,
  including core build and state-format compatibility checks.

**Acceptance:** A user can select an authorized DS ROM, play with controller
and touch input, hear audio, save/load state, and place both screens in the
Galaxy XR environment using resolved-only presentation.

## P4 — DS compositor extraction research

- [ ] Define `org.gbaxr.melonds.nds-compositor.v1`; it must identify Engine A
  versus Engine B, output routing, BG index, OBJ, backdrop, priority, blend,
  window/effect state, and whether an image is Engine A external 3D output.
- [ ] Instrument candidate output from both DS 2D engines without exposing
  mutable renderer buffers to Unity.
- [ ] Export Engine A external-3D color/coverage and, if renderer-supported,
  native depth as explicitly typed data—not a guessed 2D depth band.
- [ ] Create legal synthetic DS fixtures for engine routing, BG/OBJ priority,
  blending, windows, touch screen selection, 3D-over-2D composition, and
  display capture.
- [ ] Add native and Unity zero-parallax recomposition goldens per output
  surface. Reject a frame on incomplete/overlapping/unsupported extraction.
- [ ] Investigate second-eye 3D only after composition extraction is correct;
  distinguish image reprojection from true geometry rerendering.

**Acceptance:** Each supported DS fixture recomposes byte-exactly to both
resolved LCD outputs at zero separation. All unsupported frames remain
resolved-only.

## P5 — performance and Galaxy XR validation

- [ ] Measure worker emulation time, callback-to-bridge copy time, texture
  upload time, displayed-frame age, dropped frames, audio underflows, memory,
  thermals, and battery consumption.
- [ ] Validate controller, touch, placement gesture ownership, layout changes,
  focus loss, sleep/wake, app restart, and state recovery on Galaxy XR.
- [ ] Verify no `SIGSEGV`, `SIGBUS`, `AndroidRuntime`, worker failure,
  OpenXR error, or 16 KB package-alignment warning occurs.
- [ ] Require a 20-minute resolved dual-display session before enabling any DS
  compositor or spatial-depth experiment.

**Acceptance:** DS resolved presentation is stable and comfortable on Galaxy
XR before DS 2.5D work is considered.

## Explicit non-goals for the first DS milestone

- DSi mode, DSi NAND/SD emulation, local multiplayer, netplay, camera,
  microphone, achievements, rewind, cheats, compressed-ROM loading, and
  GPU texture sharing are excluded.
- GLES texture IDs are not passed to the Unity Vulkan renderer. CPU-resolved
  output is the initial correctness path.
- Existing GBA ForcePartition and parallax modes never apply to DS content.

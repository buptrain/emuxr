# GBA XR implementation backlog

This is the working backlog for the Samsung Galaxy XR proof of concept. A
checked item has implementation and contract, fixture, or device evidence; it
does not by itself mean production readiness.

## Status snapshot

- [x] **Done:** Native mGBA adapter behind an engine-independent C ABI,
  worker-owned emulation thread, frame leases, and a resolved RGBA fallback.
- [x] **Done:** Native and Unity fixture contracts for BG priority/ties, OBJ
  priority, alpha, brighten, darken, windows, affine BG2, and OBJ alpha.
- [x] **Done:** Per-pixel resolved provenance and conservative visible-source
  plane transport. Unsupported frames retain the resolved presentation path.
- [x] **Done:** Unity 6 URP/Vulkan Android XR rendering, Galaxy XR deployment,
  controller input, and a local user-ROM resolved-frame smoke path.
- [x] **Done:** First safe fixture-only depth profile and opt-in
  force-partition experiments. Neither is general commercial-ROM 2.5D.
- [x] **Done:** Virtual-field and floor/table placement, pinch-drag, two-hand
  scale, bottom-center screen anchoring, and a placement indicator.
- [x] **Done:** Controller admin console for real-time measured emulation FPS,
  presentation, and screen placement. The worker cadence remains fixed at
  60 FPS for audio stability.
- [ ] **Partial:** Audio now has a worker-owned 48 kHz PCM path (mGBA stream
  callback → native resampler/ring → Unity callback). It still needs physical
  Galaxy XR latency, interruption, and long-run validation.
- [ ] **Partial:** ROMs can be staged or imported through a one-time Android
  system picker into an app-private, SHA-256 catalog; physical picker/provider
  validation and explicit library selection are still needed.
- [x] **Done:** A direct-mGBA frontend capability model and pure app-private
  storage layout now define the contract for later document-picker, library,
  save-RAM, and state services. They do not change current fixture/staged-ROM
  loading or claim unimplemented persistence.
- [ ] **Partial:** Quick slot-0 save/load now uses worker-owned mGBA state
  serialization under app-private ROM-specific storage. Named slots, metadata,
  checksums, save RAM validation, and physical-device recovery tests remain.

## P0 — playable, local-only emulator frontend

### 1. Audio completion

- [x] Export a bounded single-consumer PCM-read ABI from the worker-owned
  native ring. The worker resamples mGBA output to 48 kHz stereo before it
  enters the ring; Unity never calls mGBA.
- [x] Feed the Unity audio filter directly from pinned/preallocated 48 kHz
  stereo PCM storage; a long silent source clip only keeps the filter voice
  active while a callback-local linear resampler adapts it to the current XR
  output rate without resetting device audio. Underflow is silent,
  full-ring input drops new frames and reports a counter, Unity underflows are
  counted and polled from the frame driver, and callback/pulled-frame liveness
  is checked from the main thread when the native ring remains primed. Teardown
  gates the callback before worker destruction. Startup waits for a deliberate
  4,096-frame (~85 ms) prime, below the 8,192-frame native ring capacity, so
  mGBA's normal post-buffer cadence does not create misleading startup
  underruns.
- [x] Disable Unity Android output suspension for this foreground XR emulator,
  explicitly reassert the 2D streaming source after attach/focus/pause/config
  resume, and classify liveness faults as dormant Unity output versus native
  PCM starvation. This trades some idle power for reliable headset audio and
  still needs physical battery/thermal validation.
- [ ] Add native concurrent producer/consumer stress coverage and Unity
  callback lifecycle/underflow tests in addition to the current pure PCM and
  bounded-ring tests.
- [ ] Add pause, mute, and volume controls (not part of the first vertical
  slice).
- [ ] Validate sync, latency, headset sleep/wake, and thermal behavior on
  Galaxy XR.

**Acceptance:** An authorized ROM has stable stereo audio for a 20-minute
Galaxy XR session without glitches, runaway latency, or worker deadlock.

### 2. Android XR ROM import and library

- [x] Define the direct-mGBA frontend capability manifest and pure app-private
  roots for `roms`, `saves`, `states`, `system`, and `temp`; validate future
  user-ROM library paths beneath `roms` before any file operation.
- [x] Add a one-time Android `ACTION_GET_CONTENT` picker bridge that copies a
  selected `.gba` stream to app-private `roms`, validates size, computes
  SHA-256, and returns metadata only. It requires no broad-storage permission
  or retained provider URI permission, but still needs Galaxy XR validation.
- [x] Record display name, original filename, content-derived stable ID,
  ROM-directory-relative path, and content hash in an atomically replaced JSON
  catalog. A catalog entry is not auto-started.
- [x] Expose Import and controller-navigable local-library views in the
  world-space console, including import status, current-ROM details, and an
  explicit A-only Launch action.
- [x] Implement the direct-mGBA worker-owned library switch lifecycle: validate
  app-private path/file, detach audio, destroy worker/bridge, start selected
  ROM at the fixed cadence, reset presentation checks, and retain a stopped,
  retryable console state on failure. It still requires device validation.
- [ ] Add last-played entry and richer user-facing handling for cancellation,
  I/O failures, duplicates, and invalid ROMs.
- [ ] Keep ROMs, BIOS, save data, and states out of source control and
  diagnostic uploads.
- [ ] Test scoped-storage import on a physical Galaxy XR.

**Acceptance:** A user can import, select, reload, and remove their own ROM
from the headset without ADB or an APK rebuild.

### 3. Save RAM and save states

- [ ] Add mGBA save-RAM persistence under a per-ROM app-private directory.
- [x] Add worker-owned C ABI commands to serialize/deserialize quick slot 0
  only at a frame boundary; Unity never accesses mGBA state directly.
- [x] Wire controller console Save state and Load state operations to explicit
  quick-slot status/error feedback. Save writes a temporary app-private file
  and atomically replaces the slot only on successful mGBA serialization.
- [ ] Implement named slots with checksums, timestamps, and ROM/content-version
  compatibility validation.
- [ ] Test round trips, corrupt state rejection, interrupted writes, and app
  restart recovery.

**Acceptance:** A state loads back to the same visual and emulation state
after an app restart, while invalid states fail safely.

### 4. Frontend reliability and usability

- [ ] Finish the visible console: ROM selection, audio controls, state slots,
  busy/error feedback, and reliable controller/hand focus return.
- [ ] Add controller remapping and an input diagnostics view; retain the Xbox
  mapping as the default.
- [ ] Define pause, background, focus-loss, headset-sleep, resume, and clean
  worker/audio shutdown behavior.
- [ ] Run one authorized ROM through boot, input, audio, saves, state load,
  pause/resume, and exit as a resolved-frame regression test.

**Acceptance:** The app is a dependable resolved-frame emulator frontend
before any 2.5D feature is enabled.

## P1 — correct, opt-in 2.5D rendering

### 5. Independent PPU candidate capture

- [ ] Export independently captured BG0–BG3, OBJ, and backdrop color/mask
  planes, not only final visible-source partitions.
- [ ] Carry priority, window, OBJ mode, blend effect, EVA/EVB, and target flags
  through the versioned C ABI.
- [ ] Reject incomplete/overlapping/unsupported candidate leases and retain the
  resolved oracle fallback.
- [ ] Add native fixtures and Unity zero-parallax recomposition tests for every
  supported PPU mode.

**Acceptance:** Supported fixtures recompose byte-identically to mGBA's
resolved framebuffer at zero separation. Unsupported content falls back
atomically to the resolved surface.

### 6. Game profiles and safe presentation

- [ ] Define a versioned profile format for approved sources/regions, depth
  bands, exclusions, and review evidence.
- [ ] Build profile-authoring/review support from captured frames and fixture
  contracts.
- [ ] Keep commercial ROMs resolved-frame-only unless a reviewed profile
  accepts the current lease.
- [ ] Keep `ForceUnprofiledCandidates`, `ForcePartition`,
  `ForcePartitionParallax`, and `ForcePartitionExtendedParallax` diagnostic-only, with
  prominent fallback telemetry.

**Acceptance:** A reviewed profile adds comfortable stereo separation without
changing zero-parallax pixels; all other ROMs remain correct on the resolved
display.

## P2 — mixed-reality placement polish

- [ ] Validate bottom-center anchoring, clearance, and cyan/amber floor/table
  highlights on Galaxy XR.
- [ ] Handle lost/reclassified planes, anchor removal, permission denial, and
  suspend/resume without stranding the screen.
- [ ] Add placement reset/recenter, comfort-oriented scale limits, and clearer
  invalid-drop feedback.
- [ ] Decide whether to add optional persistent anchors between sessions.
- [ ] Validate controller/hand conflicts, head-motion comfort, accessibility,
  and room-lighting variability.

**Acceptance:** A user can reliably place, move, scale, reset, and recover the
screen on a floor or table, while VirtualField remains available.

## P3 — quality, device validation, and release readiness

- [ ] Run Unity EditMode tests in a licensed editor; add PlayMode smoke tests
  for frame upload, leases, console, input, storage, audio, and placement.
- [ ] Run native CI tests for ABI compatibility, ownership, audio-ring stress,
  and state-file failures.
- [ ] Establish Galaxy XR frame-time, memory, latency, battery, and thermal
  budgets; repeat 20-minute comfort sessions after major changes.
- [ ] Automate device log validation for startup, ROM load, frame production,
  audio, storage, state load, anchoring, and absence of fatal native errors.
- [ ] Record build provenance: headset/firmware, Unity/packages, mGBA revision,
  ABI/app/profile versions, and fixture hashes.
- [ ] Enforce Android packaging checks: ARM64 16 KB page alignment, ABI
  packaging, Vulkan/OpenXR setup, and debug/release symbols.
- [ ] Complete licensing, attribution, privacy messaging, and local-only
  ROM/BIOS policy before distribution.

**Acceptance:** Reproducible builds install on Galaxy XR, survive normal
lifecycle events, and produce actionable logs without collecting user ROM
content.

## Recommended next sequence

1. Complete audio end to end.
2. Implement system-picker ROM import and app-private ROM library storage.
3. Implement save RAM and worker-owned save/load states.
4. Run one authorized commercial ROM through the full resolved-frame
   regression checklist.
5. Resume independent PPU candidate capture and profile-gated 2.5D work.

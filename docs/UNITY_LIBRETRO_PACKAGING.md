# Isolated libretro Unity packaging gate

`GbaLibretroNativeBindings` declares the Android P/Invoke surface for the
isolated `libemuxr_mgba_libretro.so` experiment. The exact ARM64 pair is now
packaged under
`unity/Assets/Plugins/Android/GbaXrLibretroExperimental/arm64-v8a/`; the
running `GbaNativeFrameDriver` still selects direct `libgba_xr.so`.

## ABI boundary

Every imported entry point begins with
`emuxr_mgba_libretro_adapter_`. In particular, frame presentation uses:

```text
emuxr_mgba_libretro_adapter_acquire_latest_frame(adapter, out lease)
emuxr_mgba_libretro_adapter_release_frame(adapter, ref lease)
```

Never call `gbaxr_acquire_latest_frame`, `gbaxr_release_frame`,
`gbaxr_destroy`, or another raw `gbaxr_*` symbol for an adapter handle. The
adapter owns a private bridge implementation. It binds that implementation
locally and hides static bridge symbols so the direct `libgba_xr.so` cannot
interpose on it.

## Staged build and controlled copy

Build to a disposable directory outside Unity assets:

```powershell
& "<Unity-Editor>/Data/PlaybackEngines/AndroidPlayer/SDK/cmake/3.22.1/bin/cmake.exe" -S native -B build/unity-libretro-staging -G Ninja `
  -DCMAKE_MAKE_PROGRAM="<Unity-Editor>/Data/PlaybackEngines/AndroidPlayer/SDK/cmake/3.22.1/bin/ninja.exe" `
  -DGBAXR_BUILD_MGBA_ADAPTER=OFF `
  -DGBAXR_BUILD_MGBA_LIBRETRO_CORE=ON `
  -DMGBA_ROOT="$PWD/third_party/mgba" `
  -DCMAKE_TOOLCHAIN_FILE="<Unity-NDK>/build/cmake/android.toolchain.cmake" `
  -DANDROID_ABI=arm64-v8a `
  -DANDROID_PLATFORM=android-24 `
  -DBUILD_TESTING=ON
& "<Unity-Editor>/Data/PlaybackEngines/AndroidPlayer/SDK/cmake/3.22.1/bin/cmake.exe" --build build/unity-libretro-staging --target emuxr_mgba_libretro emuxr_mgba_libretro_adapter_tests
```

The staging outputs are `libemuxr_mgba_libretro.so` and its required
`mgba/mgba_libretro.so`. Keep the build directory disposable; the only
authorized Unity copy is the reviewed experimental package pair described
below. Confirm the adapter exports only
`emuxr_mgba_libretro_adapter_*` wrappers and that every `LOAD` segment has
`0x4000` alignment before any packaging review.

## Controlled experimental package

The package contains only these Android ARM64 files:

| File | Unity import behavior | Role |
| --- | --- | --- |
| `mgba_libretro.so` | Android ARM64, preloaded | mGBA libretro core dependency |
| `libemuxr_mgba_libretro.so` | Android ARM64, loaded by future P/Invoke | isolated adapter |

The adapter has `DT_NEEDED: mgba_libretro.so`, so Android's loader enforces the
dependency independently of Unity's plug-in enumeration order. Both importer
records declare `Is16KbAligned: true`; the source ELF artifacts are verified
to have `0x4000` `LOAD` alignment. The adjacent `package-manifest.json` pins
the two artifact hashes and the protected direct plug-in/hash metadata.

Run the verification after each rebuild:

```powershell
& native/tools/verify_unity_libretro_package.ps1
```

Do not replace or alter `libgba_xr.so`. `IsAdapterPackaged` now accurately
reports that this experimental pair is in Unity assets, while
`IsDeviceCertified` remains false. The active driver does not select this
binding unless the persisted certification record has all resolved-frame,
input, audio, state, and Galaxy XR evidence. Until then
`GbaCoreSelectionPolicy` fails closed to direct mGBA.

## Controlled runtime-selection gate

`GbaNativeFrameDriver` now has one internal native-session boundary. Its
direct implementation owns `libgba_xr.so`; its experimental implementation
owns only `emuxr_mgba_libretro_adapter_*` lifecycle, lease, input, audio, and
state calls. Unity never passes an adapter handle to a raw `gbaxr_*` import.

The default console setting is **DirectMgba**. A **Core** row appears only
when `GbaLibretroReadinessStore` reads all persisted evidence flags:

1. synthetic fixture-frame probe;
2. resolved-frame parity;
3. input parity;
4. audio parity;
5. behavioral save-state parity; and
6. approved Galaxy XR device certification.

The fixture probe records only its own success flag; it cannot unlock the row.
No console control writes the remaining certification flags. This is an
intentional test seam for the approved validation workflow, not a user-facing
override. Selecting a certified experimental core applies only when **Apply**
is pressed. The driver fences before its next lease, detaches Unity audio,
destroys the old backend, then starts the selected backend against the same
validated app-private ROM. If the experimental session cannot initialize, the
same ROM is restarted through direct mGBA and the fallback is logged.

The libretro route is currently **resolved-frame only**: the driver disables
2.5D candidate/provenance presentation for that session rather than reading
direct-mGBA layer pointers through the adapter boundary.

## Opt-in packaged-adapter smoke probe

`GbaLibretroSmokeProbe` is a deliberately minimal physical-packaging check. It
is **not** in the prototype scene, and its `Run On Android Awake` inspector
toggle defaults to off. When explicitly enabled in an Android player, it calls
only:

```text
emuxr_mgba_libretro_adapter_create(ABI v3)
emuxr_mgba_libretro_adapter_destroy(adapter)
```

It does not pass a ROM path, call `start`, acquire/release a frame, touch audio
or state, or alter `GbaNativeFrameDriver`/core selection. This proves only that
the Android linker can load the packaged adapter and its `mgba_libretro.so`
dependency and that the two ABI endpoints are present.

### Authorized Galaxy XR procedure

1. Run `native/tools/verify_unity_libretro_package.ps1` and ensure it passes.
2. In the Unity prototype scene, create a separate empty GameObject (do not
   add it to the `GbaNativeFrameDriver` object), add `GbaLibretroSmokeProbe`,
   and enable **Run On Android Awake**.
3. Build a Development Android APK, install it only when device deployment is
   authorized, and launch it on Galaxy XR. No ROM is required.
4. Capture logcat. The required success marker is:
   `GBA XR libretro probe: success; adapter create/destroy completed without starting a ROM. Direct mGBA remains selected.`
5. Disable the inspector toggle again before normal ROM or parity testing.

Failures use the distinct `GBA XR libretro probe: failure` marker. A missing
adapter/core dependency reports `NativeLibraryMissing`; a wrapper ABI mismatch
reports `AdapterEntryPointMissing`. Neither success nor failure is parity or
device certification; leave `IsDeviceCertified` false until the full approved
validation matrix is complete.

## Opt-in synthetic fixture/frame-lease probe

`GbaLibretroFixtureFrameProbe` is the next, still isolated smoke step. It is
also absent from the scene and defaults off. It accepts no inspector ROM path:
the only allowed content is the generated synthetic Mode 3 checkerboard fixture
`gba-fixtures/frame_sequence.gba`.

Generate and stage that fixture before building the APK:

```powershell
& .\fixtures\gba\build.ps1 -Fixture frame_sequence -InstallIntoUnityStreamingAssets
Get-FileHash .\unity\Assets\StreamingAssets\gba-fixtures\frame_sequence.gba -Algorithm SHA256
```

The required result is exactly 280 bytes with SHA-256
`760CEF4F09635F762EDDD04473DBF0E6D2B227F0BC4AAECAF1A55EE00C8DC450`.
`StreamingAssets/gba-fixtures` is an ignored generated test-asset directory;
do not add a user or commercial ROM to it. On Android the probe reads the
packaged asset with `UnityWebRequest`, verifies that contract, copies it to
`persistentDataPath/gba-libretro-fixture-probe/frame_sequence.gba`, and only
then calls the adapter start API.

The bounded probe sequence is:

1. Create the isolated adapter and start that fixed fixture at 60 FPS.
2. Wait up to the serialized 10-second default for worker state `running`.
3. Acquire one adapter-owned frame lease on the Unity main thread.
4. Validate nonzero sequence/pixel pointer, width `240`, height `160`, and
   RGBA row stride `960`; release that same lease.
5. Destroy the adapter. It never uploads a texture, forwards input, accesses
   audio/state, exposes a console control, or changes the direct driver.

### Authorized Galaxy XR procedure

1. Run the package verifier and regenerate/check the fixture hash above.
2. Add `GbaLibretroFixtureFrameProbe` to one separate empty GameObject; do not
   add it to the frame driver or enable the create/destroy-only smoke probe at
   the same time. Enable **Run On Android Awake** only for this Development
   build.
3. Build/install only with explicit device authorization, launch once, and
   capture logcat. No commercial ROM or user ROM is involved.
4. The required success marker is:
   `GBA XR libretro fixture probe: success; the isolated adapter started the synthetic fixture and released one valid frame lease. Direct mGBA remains selected.`
5. A failed running transition, no published frame, invalid layout, or release
   error emits `GBA XR libretro fixture probe: failure` with a distinct reason.
   Disable the toggle again after capture.

This is a package/load/worker/lease transport check only. A success persists
only the `FixtureFrameProbe` evidence flag; it does not establish frame-hash
parity, audio, input, state, visual presentation, or Galaxy XR device
certification and cannot enable runtime core selection.

## Fixture-only certification batch (default off)

`GbaLibretroCertificationBatchProbe` is the bounded, default-off follow-up for
an authorized Development build. Add it to one separate empty GameObject only;
never add it to `GbaNativeFrameDriver`, and disable the smoke/frame probe
toggles for the same launch. It starts fresh isolated adapter sessions in
sequence and accepts only these generated fixtures:

```powershell
& .\fixtures\gba\build.ps1 -Fixture frame_sequence -InstallIntoUnityStreamingAssets
& .\fixtures\gba\build.ps1 -Fixture input_state_replay -InstallIntoUnityStreamingAssets
Get-FileHash .\unity\Assets\StreamingAssets\gba-fixtures\frame_sequence.gba -Algorithm SHA256
Get-FileHash .\unity\Assets\StreamingAssets\gba-fixtures\input_state_replay.gba -Algorithm SHA256
```

The expected source-controlled contracts are:

| Fixture | Bytes | SHA-256 | Batch contract |
| --- | ---: | --- | --- |
| `frame_sequence.gba` | 280 | `760CEF4F09635F762EDDD04473DBF0E6D2B227F0BC4AAECAF1A55EE00C8DC450` | 240×160 RGBA lease, settled-lease-30 canonical RGBA hash `0xF9838FC5`, 48 kHz non-empty adapter PCM |
| `input_state_replay.gba` | 428 | `5100940CC68685A07E841C14217CA2AB515AB7DF188F76D966F2A6D3F4D9BE7A` | stable A-button visual transition and worker-owned private save/load replay |

The component copies both assets into its own app-private probe directory,
uses adapter-owned acquire/release/input/audio/state calls only, and never
uploads a texture or accesses a user ROM. Before comparing the `frame_sequence`
golden it acquires/releases 30 successive post-running leases and hashes the
30th; this intentionally matches `GbaNativeFrameDriver`'s resolved-fixture
settle contract. The fixture fills Mode 3 VRAM through CPU stores, so an
earlier valid-layout lease can still contain a partially initialized
checkerboard and must not be treated as a graphics mismatch. If the settled
hash still fails, the summary includes lease sequence, four RGBA samples, and
a full-frame comparison against the source checker formula (canonical source
FNV `0xF9838FC5`): mismatch count, first `(x,y)` expected/actual RGBA, and
candidate hashes for RB swap, transparent alpha, and vertical flip. The
confirmed device golden is the same canonical `0xF9838FC5`; diagnostics still
distinguish a real adapter pitch/color/geometry difference from a partial
frame. It emits exactly one summary marker:

The direct `GbaNativeFrameDriver` emits the same source-only full-frame
diagnostic whenever its own `frame_sequence` golden fails. For a visual-parity
investigation, run the direct fixture and the explicit diagnostic-preview batch
with the same packaged fixture revision, then compare the two `first=`,
`expected=`, `actual=`, and mismatch-count fields. Do not change either golden
from matching corner samples alone.

When the adapter's unchanged 30-lease parity gate fails, the batch stays in
diagnostic mode and continues to acquire/release valid resolved leases through
lease counts **30**, **60**, and **120**. Its one terminal summary appends
`temporalSweep=lease=.../hash=.../mismatches=.../first=...` for each checkpoint.
Those values are evidence only: they neither alter the approved `0xF9838FC5`
golden nor mark the resolved-frame stage as passed. A stable later hash supports
a persistent conversion/geometry investigation; changing hashes or first
coordinates indicates a timing/initialization issue.

To collect an equivalent direct trace, enable **Enable Synthetic Fixture
Temporal Sweep** on the Quad's `Gba Native Frame Driver` while its **Rom
Relative Path** is exactly `gba-fixtures/frame_sequence.gba`. The default is
off. Direct logs one source-only line at leases 30, 60, and 120; compare its
`hash`, `mismatches`, `first`, expected/actual RGBA, and `candidate` values to
the adapter's terminal `temporalSweep=` value. This setting is ignored outside
that named synthetic fixture and neither changes the 30th-lease direct golden
gate nor writes libretro certification evidence.

```text
GBA XR libretro certification batch: PASS|FAIL; resolved=..., input=..., audio=..., state=..., evidence=..., GalaxyXrDevice=not-recorded; ...
```

On each genuinely successful stage it persists only that stage’s evidence:
resolved frame also records `FixtureFrameProbe`, then input, 48 kHz PCM, and
state replay record their respective flags. A partial failure never records an
unpassed stage. The batch **never** writes `GalaxyXrDevice`; review the single
summary, fixture hashes, app stability, and headset logs before an authorized
manual final device-certification action. After that review, add
`GbaLibretroGalaxyXrDeviceApproval` to a separate empty GameObject, enable
**Approve Persisted Evidence On Android Awake**, and make one explicit
Development build. The component records `GalaxyXrDevice` only if all batch
evidence is already persisted, and logs `GBA XR libretro device certification:
approved`. Disable or remove it immediately afterward. Until that final flag
is separately recorded, the Core console row remains hidden and Direct mGBA
remains active.

Certification persistence is now **v2**. It intentionally ignores the old
five-lease/historical-hash PlayerPrefs record, so this 30-lease canonical-frame
batch must freshly record resolved, input, audio, state, and manually reviewed
device evidence before normal runtime selection can be eligible.

## Unresolved visual diagnostic preview (explicit override)

## Developer-only external release registry

`GbaDeveloperCoreReleaseProvider` is an optional component for development or
sideload builds only. With its explicit enable flag and an HTTPS GitHub Release
manifest URL, its context-menu/API call downloads a manifest and ZIP artifact,
validates SHA-256, stages `persistentDataPath/developer-cores/<id>/<version>/`,
and atomically writes `active.json` while retaining `previous.json` for rollback.

This is a **separate developer delivery manifest**, not the repository's
existing Unity release/package manifest. It must contain `coreId`, `version`,
an HTTPS `assetUrl` pointing to the release ZIP, and its SHA-256. The provider
is compiled into an eligible build only under `UNITY_EDITOR`,
`DEVELOPMENT_BUILD`, or explicit `GBA_XR_DEVELOPER_SIDELOAD`, and still requires
its Inspector enable checkbox. A developer can call the provider/driver's
explicit rollback API after review to atomically restore `previous.json`.

The published preview configuration is available at:

```text
https://raw.githubusercontent.com/buptrain/mgba-parallax/main/parallax/release/developer-delivery.json
```

It currently identifies the `v0.1.0-preview.1` release ZIP. The manifest is
updated with each core release; do not point the provider at the packaged
`core-manifest.json`, since that ABI artifact intentionally has no download
URL.

Its status deliberately says `DownloadedNotLoadable`. The existing direct host
and experimental adapter statically link their native cores; this registry does
not extract, load, or replace a Unity native bridge, and it never changes core
selection/readiness. Dynamic core loading is a separate native-host milestone
and is not suitable for Play distribution.

`GbaLibretroDiagnosticPreview` is an intentionally unsafe, default-off
component for visual investigation before parity approval. It does **not**
write readiness evidence, does not record `GalaxyXrDevice`, and does not make
the libretro route certified. It exposes the admin-console **Core** row only
while its explicit toggle is enabled and the experimental adapter package is
present. The libretro session remains resolved-frame-only; no provenance or
2.5D layer transport is used.

Use only in a controlled Development build:

1. On the same GameObject as `GbaNativeFrameDriver`, add
   **Gba Libretro Diagnostic Preview**.
2. Enable **Enable Uncertified Adapter Preview**. It defaults off and should
   be disabled again immediately after the observation.
3. Build/install the Development APK, open the admin console with Xbox R3,
   choose **Core: LibretroResolvedExperimental**, then press **Apply**.
4. Confirm the `UNRESOLVED DIAGNOSTIC` console warning and the distinct
   `GBA XR core selection: UNRESOLVED DIAGNOSTIC PREVIEW active` log. This is
   not a pass marker.
5. If adapter startup fails, the driver logs it and restarts the same private
   ROM through direct mGBA. Review visual output and logs; do not treat the
   preview as validation or turn on final certification.

### Preview input and audio checks

The adapter preview translates the project’s GBA input bits into mGBA
libretro’s RetroPad IDs; it does not forward the direct-worker mask unchanged:
GBA `A/B/Select/Start/Right/Left/Up/Down/R/L` map to RetroPad
`8/0/2/3/7/6/4/5/11/10`. In particular, **GBA Up/Down must move vertically**;
if they move left/right, capture the `GBA XR core selection` line and stop the
preview.

The libretro worker keeps its PCM ring at 48 kHz and corrects its source cadence
for the fixed frontend target FPS (mGBA nominally runs about 59.7275 FPS while
the Unity worker targets 60). This prevents the slowly growing ring backlog
that caused preview audio lag and eventually audible drops/distortion. During a
successful preview, expect `GBA XR audio: native worker source cadence is ...`
followed by two-second `GBA XR audio diagnostic` lines with callbacks and PCM
pulls advancing. `nativeDropped` should remain stable after initial priming;
recurring growth or underflow is a failure to investigate, not certification.

Without this component/toggle, the Core row remains governed exclusively by
the full persisted certification gate.

# Synthetic ROM fixture test

`fixtures/gba/frame_sequence.s` is a small, source-only GBA test program. It sets GBA Mode 3 and draws a deterministic red/white 16-pixel checkerboard. It is a visual resolved-frame smoke test, not a game ROM and not a PPU-layer test.

`fixtures/gba/bg0_priority.s` is the first provenance contract fixture. It sets Mode 0 BG0 to priority 2 and fills it with one opaque cyan text tile. Every output pixel must have final source BG0 (`1`), priority `2`, blend `0`, and RGBA `00FFFFFF`. It does not yet test priority ties, OBJ, or windows.

`fixtures/gba/bg_tie.s` is the second provenance fixture. BG0 and BG1 both have priority 2. BG0's opaque cyan left half must win the equal-priority overlap; its transparent right half exposes BG1's magenta pixels. This tests both equal-priority tie behavior and BG1 source attribution.

## Build

From the repository root:

```powershell
& .\fixtures\gba\build.ps1
```

Generated ROM artifacts are ignored under `fixtures/gba/out/`. The reviewed ROM SHA-256 for `bg0_priority.gba` is `B59786BCC8B7BD1CCB0FD5277728281E1E0F8EEAA3F43FCC0E9387A8CC33DA5B`.
The reviewed ROM SHA-256 for the unapproved `bg_tie.gba` candidate is `25A431C4EA802904C61AFDF84FBDFA66C5D3CCB6C71172CD5C51F6354099582B`.

Build the BG0 provenance fixture instead:

```powershell
& .\fixtures\gba\build.ps1 -Fixture bg0_priority
```

## Package and install on Galaxy XR

Build and copy the fixture into Unity's ignored `StreamingAssets` staging directory:

```powershell
& .\fixtures\gba\build.ps1 -InstallIntoUnityStreamingAssets
```

To package the BG0 provenance fixture:

```powershell
& .\fixtures\gba\build.ps1 -Fixture bg0_priority -InstallIntoUnityStreamingAssets
```

Build and install the APK from Unity. On first launch, `GbaNativeFrameDriver` reads the selected packaged asset using `UnityWebRequest`, copies it under `Application.persistentDataPath/gba-fixtures/`, and starts mGBA from that local file. This works with Android scoped storage; no ADB file copy is needed.

Restart the app. The driver should replace the dark diagnostic frame with the fixture's bright red/white checkerboard. The Unity log should contain `copied packaged fixture` once and should not contain `no fixture ROM` or `mGBA could not load`.

## Pass/fail

- Pass: a stable full-frame red/white checkerboard appears; orientation is consistent with the surface; the app stays running for at least 60 seconds.
- Pass: after the fifth presented frame, Unity logs `synthetic fixture golden hash verified: 0x9B341219`. This is the pinned mGBA resolved-frame golden captured on Galaxy XR; it is separate from the source-model fixture hash used by the EditMode test.
- Fail: the old dark diagnostic frame remains, the surface is blank, colors are incorrect, a native error is logged, or the app exits.

Do not use commercial ROMs or BIOS images for this test.

## BG0 provenance contract

After packaging `bg0_priority.gba`, change the `Gba Native Frame Driver` component's **Rom Relative Path** to `gba-fixtures/bg0_priority.gba`, build/install, and restart the app. The surface should be solid cyan. On its fifth presented frame, Unity must log:

```text
GBA XR: BG0 provenance contract verified: all 38400 pixels source=1, priority=2, blend=0, rgba=00FFFFFF, candidateHash=0x...
```

The reviewed mGBA golden is now `0x87A1ADC5`; Unity logs `BG0 provenance golden verified` when both the per-pixel contract and full-frame hash match. A failed contract is a PPU-provenance regression; do not update expected fields to make it pass.

## BG0/BG1 equal-priority contract

Package the fixture and change **Rom Relative Path** to `gba-fixtures/bg_tie.gba`:

```powershell
& .\fixtures\gba\build.ps1 -Fixture bg_tie -InstallIntoUnityStreamingAssets
```

The surface must be cyan on the left and magenta on the right. On the fifth presented frame Unity must log a `BG tie provenance contract verified` message and its candidate hash. That candidate remains unapproved until it is recorded with the pinned mGBA revision.

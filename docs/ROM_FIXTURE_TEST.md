# Synthetic ROM fixture test

`fixtures/gba/frame_sequence.s` is a small, source-only GBA test program. It sets GBA Mode 3 and draws a deterministic red/white 16-pixel checkerboard. It is a visual resolved-frame smoke test, not a game ROM and not a PPU-layer test.

`fixtures/gba/bg0_priority.s` is the first provenance and raw-candidate contract fixture. It sets Mode 0 BG0 to priority 2 and fills it with one opaque cyan text tile. Every output pixel must have final source BG0 (`1`), priority `2`, blend `0`, and RGBA `00FFFFFF`. It is the only fixture eligible for the first `INDEPENDENT_CANDIDATES` lease: its raw BG0 color/mask must fully and byte-exactly recompose the resolved frame. It does not yet test priority ties, OBJ, or windows.

`fixtures/gba/bg_tie.s` is the second provenance fixture. BG0 and BG1 both have priority 2. BG0's opaque cyan left half must win the equal-priority overlap; its transparent right half exposes BG1's magenta pixels. This tests both equal-priority tie behavior and BG1 source attribution.

`fixtures/gba/obj_priority.s` establishes the OBJ/BG tie rule. A normal 8bpp 64 × 64 magenta OBJ and an opaque cyan BG0 both use priority 2. The OBJ must win only within its upper-left square; BG0 remains the winning source everywhere else. The fixture uses 16-bit writes for OBJ VRAM because the GBA rejects byte writes in that region.

`fixtures/gba/alpha_blend.s` establishes normal GBA alpha blending. Red BG0 is target 1 at priority 1; blue BG1 is target 2 at priority 2; `EVA=8` and `EVB=8` produce opaque purple RGBA `7F007FFF`. Final provenance remains BG0/source `1`, priority `1`, and alpha blend `1`.

`fixtures/gba/brighten.s` establishes the GBA brighten effect. Red BG0 is target 1 at priority 1; `EY=8` brightens its zero-valued green and blue channels halfway to white, producing `FF7F7FFF`. Final provenance remains BG0/source `1`, priority `1`, and brighten effect `2`.

`fixtures/gba/darken.s` establishes the GBA darken effect. Red BG0 is target 1 at priority 1; `EY=8` halves its red channel. The pinned mGBA resolved output rounds that channel to `800000FF`. Final provenance remains BG0/source `1`, priority `1`, and darken effect `3`.

`fixtures/gba/window_alpha.s` establishes the WIN0 color-effect gate. Red BG0 is target 1 at priority 1 and blue BG1 is target 2 at priority 2. Alpha blending is disabled outside WIN0 (left half: opaque red) and enabled inside WIN0 (right half: purple). Final provenance remains BG0/source `1` with global alpha effect `1`; window byte bit 0 changes from `0` outside to `1` inside.

`fixtures/gba/affine_bg2.s` establishes a Mode 2 affine BG2 identity transform. It renders one opaque cyan tile at priority 2. Every output pixel must have final source BG2 (`3`), priority `2`, blend `0`, and RGBA `00FFFFFF`.

`fixtures/gba/obj_alpha.s` establishes a semi-transparent 8bpp OBJ. A 64 × 64 magenta OBJ at priority 2 blends over an opaque cyan BG0 at priority 2 using `EVA=8`, `EVB=8`. The upper-left square must have final source OBJ (`5`), priority `2`, alpha blend `1`, and RGBA `7F7FFFFF`; BG0 remains visible elsewhere.

## Build

From the repository root:

```powershell
& .\fixtures\gba\build.ps1
```

Generated ROM artifacts are ignored under `fixtures/gba/out/`. The reviewed ROM SHA-256 for `bg0_priority.gba` is `B59786BCC8B7BD1CCB0FD5277728281E1E0F8EEAA3F43FCC0E9387A8CC33DA5B`.
The reviewed ROM SHA-256 for `bg_tie.gba` is `25A431C4EA802904C61AFDF84FBDFA66C5D3CCB6C71172CD5C51F6354099582B`.
The reviewed ROM SHA-256 for `obj_priority.gba` is `EFBAB40A4E055A85A1DAF269FD7B028C37CD5F3831BCB22CC6132E07ADD8C697`.
The reviewed ROM SHA-256 for `alpha_blend.gba` is `B16E1F7ADD54AB76E3B1A2E5008D7A3AFACDB74A497738F129A0A5155B298841`.
The reviewed ROM SHA-256 for `brighten.gba` is `387546352C69225305E93767699DBCAE607F55F60CDC9C662B9AB2AC8C40C1B0`.
The reviewed ROM SHA-256 for `darken.gba` is `8582905FFF500E9FCFC4FEC345E318227AF50AE51A7710ED5BB05B80659896B7`.
The reviewed ROM SHA-256 for `window_alpha.gba` is `E3F237759FFF8309E638A543191C9FC4CF255D412F044031E12D1DEB33239EDD`.
The reviewed ROM SHA-256 for `affine_bg2.gba` is `461ED80DFD0D607221A7E87B012F5D69A4B294FCC935DBF898CEF8782A7EB317`.
The reviewed ROM SHA-256 for `obj_alpha.gba` is `EB881CB8036C6BE521EFC4D2408BD851482C40A93FB398C7C2D275DDBB7FAB3C`.

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

Build and install the APK from Unity. On every launch, `GbaNativeFrameDriver` refreshes a selected packaged `gba-fixtures/` asset using `UnityWebRequest`, copies it under `Application.persistentDataPath/gba-fixtures/`, and starts mGBA from that local file. This prevents an earlier fixture revision in app storage from masking a newly built APK. Other fixture paths retain persistent-file behavior. This works with Android scoped storage; no ADB file copy is needed.

Restart the app. The driver should replace the dark diagnostic frame with the fixture's bright red/white checkerboard. The Unity log should contain `copied packaged fixture` once and should not contain `no fixture ROM` or `mGBA could not load`.

## Pass/fail

- Pass: a stable full-frame red/white checkerboard appears; orientation is consistent with the surface; the app stays running for at least 60 seconds.
- Pass: after the fifth presented frame, Unity logs `synthetic fixture golden hash verified: 0x9B341219`. This is the pinned mGBA resolved-frame golden captured on Galaxy XR; it is separate from the source-model fixture hash used by the EditMode test.
- Fail: the old dark diagnostic frame remains, the surface is blank, colors are incorrect, a native error is logged, or the app exits.

Do not use commercial ROMs or BIOS images for this test.

## BG0 provenance contract

After packaging `bg0_priority.gba`, change the `Gba Native Frame Driver` component's **Rom Relative Path** to `gba-fixtures/bg0_priority.gba`, build/install, and restart the app. The surface should be solid cyan. On its fifth presented frame, Unity must log:

```text
GBA XR: BG0 provenance golden verified: all 38400 pixels source=1, priority=2, blend=0, rgba=00FFFFFF, hash=0x87A1ADC5.
GBA XR: raw BG0 candidate verified: complete coverage, priority=2, and byte-exact zero-parallax recomposition.
```

The reviewed mGBA golden is now `0x87A1ADC5`; Unity logs `BG0 provenance golden verified` when both the per-pixel contract and full-frame hash match. A failed contract is a PPU-provenance regression; do not update expected fields to make it pass.

## BG0/BG1 equal-priority contract

Package the fixture and change **Rom Relative Path** to `gba-fixtures/bg_tie.gba`:

```powershell
& .\fixtures\gba\build.ps1 -Fixture bg_tie -InstallIntoUnityStreamingAssets
```

The surface must be cyan on the left and magenta on the right. The reviewed mGBA golden is `0x0BB5FDC5`; on the fifth presented frame Unity must log `BG tie provenance golden verified`. A source, priority, or color failure is a PPU-provenance regression; a hash-only failure requires golden review.

## OBJ priority contract

Package the VBlank-safe, OBJ-VRAM-safe fixture revision and set **Rom Relative Path** to `gba-fixtures/obj_priority_halfword.gba`:

```powershell
& .\fixtures\gba\build.ps1 -Fixture obj_priority -StreamingAssetName obj_priority_halfword -InstallIntoUnityStreamingAssets
```

The surface must be cyan with a magenta 64 × 64 square in the upper-left. The reviewed mGBA golden is `0xEBBEADC5`; on the fifth presented frame Unity must log `OBJ priority provenance golden verified`.

## Alpha-blend contract

Package the fixture and set **Rom Relative Path** to `gba-fixtures/alpha_blend.gba`:

```powershell
& .\fixtures\gba\build.ps1 -Fixture alpha_blend -InstallIntoUnityStreamingAssets
```

The surface must be solid purple. The reviewed mGBA golden is `0xC4D18DC5`; on the fifth presented frame Unity must log `alpha-blend provenance golden verified`. A source, priority, blend, or color failure is a PPU-provenance regression; a hash-only failure requires golden review.

## Brighten contract

Package the fixture and set **Rom Relative Path** to `gba-fixtures/brighten.gba`:

```powershell
& .\fixtures\gba\build.ps1 -Fixture brighten -InstallIntoUnityStreamingAssets
```

The surface must be light red/pink (`FF7F7FFF`). The reviewed mGBA golden is `0x06A215C5`; on the fifth presented frame Unity must log `brighten provenance golden verified`. A source, priority, blend, or color failure is a PPU-provenance regression; a hash-only failure requires golden review.

## Darken contract

Package the fixture and set **Rom Relative Path** to `gba-fixtures/darken.gba`:

```powershell
& .\fixtures\gba\build.ps1 -Fixture darken -InstallIntoUnityStreamingAssets
```

The surface must be dark red (`800000FF`). The reviewed mGBA golden is `0x86158DC5`; on the fifth presented frame Unity must log `darken provenance golden verified`. A source, priority, blend, or color failure is a PPU-provenance regression; a hash-only failure requires golden review.

## Windowed alpha contract

Package the fixture and set **Rom Relative Path** to `gba-fixtures/window_alpha.gba`:

```powershell
& .\fixtures\gba\build.ps1 -Fixture window_alpha -InstallIntoUnityStreamingAssets
```

The left 120 pixels must be opaque red (`FF0000FF`) and the right 120 pixels purple (`7F007FFF`). The window provenance byte is `0x00` on the left and `0x01` on the right. The reviewed mGBA golden is `0x4738CBC5`; on the fifth presented frame Unity must log `window-alpha provenance golden verified`.

## Affine BG2 contract

Package the fixture and set **Rom Relative Path** to `gba-fixtures/affine_bg2.gba`:

```powershell
& .\fixtures\gba\build.ps1 -Fixture affine_bg2 -InstallIntoUnityStreamingAssets
```

The surface must be solid cyan (`00FFFFFF`). The reviewed mGBA golden is `0x87A1ADC5`; on the fifth presented frame Unity must log `affine BG2 provenance golden verified`.

## Semi-transparent OBJ contract

Package the fixture and set **Rom Relative Path** to `gba-fixtures/obj_alpha.gba`:

```powershell
& .\fixtures\gba\build.ps1 -Fixture obj_alpha -InstallIntoUnityStreamingAssets
```

The surface must be cyan with a 64 × 64 upper-left square in blended magenta/cyan (`7F7FFFFF`). The square's provenance source is OBJ (`5`) and its blend effect is alpha (`1`); BG0 remains source `1` elsewhere. The reviewed mGBA golden is `0x28C36DC5`; on the fifth presented frame Unity must log `OBJ alpha provenance golden verified`.

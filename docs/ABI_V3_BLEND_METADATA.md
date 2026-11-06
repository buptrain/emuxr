# ABI v3 blend-metadata contract

## Purpose

ABI v2 proves opaque BG and OBJ candidates at zero parallax. It deliberately
does not label a semi-transparent GBA OBJ as a normal RGBA-alpha layer: the
GBA PPU combines target-1 and target-2 colors with `BLDALPHA` coefficients,
subject to `BLDCNT` and window state.

ABI v3 adds an optional copied metadata plane. It is additive to the resolved
RGBA, provenance, and layer planes; the bridge retains ownership and Unity may
read it only for the lifetime of its frame lease.

## Per-pixel record

The v3 record is tightly packed and independent of C enum width:

```text
byte effect       // none, alpha, brighten, darken
byte eva          // GBA BLDALPHA EVA, 0..16
byte evb          // GBA BLDALPHA EVB, 0..16
byte flags        // bit 0 target-1, bit 1 target-2, bit 2 window enables blend
```

The record is meaningful only when `effect == alpha`; brightness paths remain
resolved-frame fallbacks until their own reference equations are implemented.
`eva` and `evb` are captured from the same mGBA software-renderer scanline
state that produces the resolved pixel.

## Acceptance sequence

1. Keep ABI v2 behavior and opaque candidate tests byte-identical.
2. Add bridge copy/lease tests for absent and present v3 metadata.
3. Instrument mGBA to emit the record at final composition.
4. Capture raw BG0 and semi-transparent OBJ candidates.
5. Add a GBA fixed-point reference compositor using `EVA=EVB=8`.
6. Publish `INDEPENDENT_CANDIDATES` for `obj_alpha.gba` only after the
   reference output equals the resolved `0x28C36DC5` golden on device.

An unavailable or malformed plane is a fallback condition, never permission
to infer blend behavior from a resolved purple pixel.

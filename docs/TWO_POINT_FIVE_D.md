# 2.5D conversion contract

GBA XR does not infer a 3D world from a ROM. It converts verified PPU source
planes into an authored stereo presentation only when their zero-parallax
composition is byte-identical to mGBA's resolved image.

## First profile

| Source | Depth band | Eligibility |
| --- | ---: | --- |
| BG0 | 0.00 m | opaque BG0 fixture / approved game region |
| BG1 | 0.01 m behind BG0 | verified BG0/BG1 tie candidate pair |
| OBJ | 0.02 m in front of BG0 | verified opaque OBJ candidate pair |
| alpha, brighten, darken, windows, affine, unsupported sources | 0.00 m | resolved-frame fallback |

Depth is relative to the virtual display surface, not real-world scene scale.
The initial maximum separation is deliberately small (2 cm) and must be
disabled whenever the lease semantics are not `INDEPENDENT_CANDIDATES`.

## Physical validation sequence

1. Use `bg_tie.gba`; toggle Xbox **Y** to confirm BG1 moves 1 cm behind BG0
   while cyan remains left and magenta remains right.
2. Use `obj_priority_halfword.gba`; toggle Xbox **Y** to confirm the magenta
   64 × 64 OBJ moves 2 cm in front of the cyan BG0 plane while its footprint
   and colors remain unchanged.
3. If a fixture is not published as independent candidates, the same control
   must retain the resolved frame and log the fallback selection.

## Profile authorization

Candidate capture is an evidence gate, not an authorization to add depth to
arbitrary content. The first runtime profile is explicitly allow-listed for
`bg_tie.gba` and `obj_priority_halfword.gba` only. Every user-supplied ROM,
including one that happens to expose an opaque raw candidate on a particular
frame, remains resolved-frame-only until it has a reviewed authored profile.

### Developer force mode

`ForceUnprofiledCandidates` bypasses only this ROM allow-list for exploratory
testing. It does **not** make a visible-source partition depth-safe and it
does not force depth on frames missing `INDEPENDENT_CANDIDATES`; those frames
still switch to the resolved surface. Expect profile activation to be
intermittent in an unprofiled game. It is a diagnostic tool, not a supported
commercial-ROM presentation mode.

### Unsafe visible-partition experiment

`UnsafeVisiblePartition` places every final visible-source lease plane at a
fixed experimental depth: OBJ −2 cm, BG0 0 cm, BG1 +1 cm, BG2 +2 cm, BG3
+3 cm, and backdrop +4 cm. It accepts only a well-formed `VISIBLE_PARTITION`
lease and still verifies dimensions, unique source IDs, and coverage pointers.

The partition reconstitutes the resolved frame only at zero parallax. It has
no pixels hidden behind a winning source, so head motion can reveal black holes
or stale-looking gaps; GBA alpha/window behavior is final-color data rather
than independently recomposable inputs. This is an opt-in visual experiment,
not an emulator-accurate renderer.

## Required renderer behavior

1. Upload each candidate color/mask plane into a point-filtered texture.
2. Render planes at the profile depth using their coverage masks.
3. At zero separation, render a reference composite and compare it with the
   resolved image before allowing any stereo separation.
4. Fall back atomically to the resolved texture if a layer is missing,
   overlaps unexpectedly, has unsupported blend metadata, or the profile does
   not cover its source set.

The first device proof uses synthetic opaque fixtures. A commercial ROM stays
on the resolved surface until it has an explicit profile and a reviewed
zero-parallax capture.

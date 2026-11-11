# Test strategy and fixtures

## Assumptions

- The first POC supports GBA screen mode/frame data only; it does not promise perspective-correct game geometry.
- Separating visual sources can reveal pixels that were hidden in the original 2D composition. Game profiles must choose conservative depths and may mask/reconstruct exposed regions.
- The native reference compositor is a deterministic fixture oracle. Its 8-bit alpha mode is deliberately a generic test primitive; the mGBA adapter must additionally test real GBA `BLDALPHA` coefficients, brightness effects, OBJ modes, and windows against the emulator's resolved framebuffer.
- A test fixture ROM is homebrew/synthetic and has a documented source and SHA-256. Commercial ROMs and BIOS images remain outside this repository.

## Native test layers

| Level | Input | Oracle | Required cases |
| --- | --- | --- | --- |
| Reference compositor | In-memory 2×1 and 240×160 planes | Versioned expected RGBA bytes/hash | transparent pixels, priority ties, alpha, invalid dimensions |
| mGBA resolved frame | Synthetic ROM | Golden 240×160 RGBA image and SHA-256 | mode selection, VBlank frame sequencing |
| Layer extraction | Synthetic ROM metadata planes | Recompose at zero parallax, compare to resolved frame | BG/OBJ priority, windows, blend targets, affine BG, OBJ semi-transparency |
| C bridge | Test-only producer/consumer | Address/thread/sequence assertions | acquire/release lifecycle, dropped frame, stale lease, wrong thread |
| Unity edit mode | Known layer descriptors/textures | Same native golden bytes | priority sort, depth profile mapping, native-handle disposal |
| Unity play mode | Native fixture library | Screenshot/hash plus runtime logs | texture upload, pause/resume, XR session focus change |

## Golden-frame policy

Store approved golden images and their SHA-256 values with fixture source metadata. Tests compare full RGBA bytes, not perceptual similarity. A golden update requires the mGBA revision, adapter ABI version, fixture source/hash, reviewer, and a human diff image. Never regenerate all goldens as part of a normal build.

## Minimum synthetic ROM fixture set

1. `priority_ties`: BG priorities plus OBJ/tie ordering at controlled pixels.
2. `alpha_targets`: `BLDALPHA`, brightening, and darkening over known colors.
3. `windows`: WIN0/WIN1/OBJ-window masking boundaries.
4. `affine`: rotated/scaled affine background with transparent texels.
5. `sprite_modes`: normal, semi-transparent, and OBJ-window sprites.
6. `frame_sequence`: changing colors/frame counter to detect stale or torn publication.
7. `input_state_replay`: VBlank-sampled A-button rising-edge state held in
   emulated RAM. It changes an observable resolved patch and must replay after
   an in-core save/load checkpoint without a DMA/IRQ/BIOS dependency.

Each fixture emits a visible fixture/version identifier in the frame and supports deterministic stepping without audio/input timing.

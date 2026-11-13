# EmuXR

**EmuXR** is an Android XR experiment that brings classic game rendering into
spatial space. The goal is simple:
use rendering-layer information to explore a little depth for fun.

> This is an independent, unofficial project. It is not affiliated with or
> endorsed by Microsoft, the mGBA project, console manufacturers, or game
> rights holders. No ROMs, BIOS files, or copyrighted game assets are included.

## The story

The project originated in 2016 inside Microsoft as a hackathon project for HoloLens. It was targeting to
provide more game options, leveraging the good retro gaming community. To show
the uniqueness, we tried to add depth data (Z-axis) from the classic game
rendering pipeline for fun. The project was built inside Unity but not publicly
released ever.

Fast forward. spatial hardware is now much more powerful than the original
HoloLens. I decided to revive the effort with the latest tech stack. No
proprietary code was used in this practice.

Today, EmuXR is a clean-room Android XR proof of concept tested on Samsung
Galaxy XR with an Xbox controller. It starts with Direct mGBA core, while
keeping an engine-independent path for future cores and platforms. More core download funcationlity is on the way.

## Visual timeline

The documentation images are published separately in
[`buptrain/emuxr-media`](https://github.com/buptrain/emuxr-media).

### Early HoloLens console concept in 2016

![Early HoloLens console experiment](https://raw.githubusercontent.com/buptrain/emuxr-media/ffeff7f/images/hololens-2016-prototype.jpg)

### Galaxy XR test runtime

![Galaxy XR character-select runtime](https://raw.githubusercontent.com/buptrain/emuxr-media/ffeff7f/images/galaxy-xr-character-select.jpg)

![Galaxy XR ForcePartition runtime](https://raw.githubusercontent.com/buptrain/emuxr-media/ffeff7f/images/galaxy-xr-force-partition.jpg)

## What it does

The baseline is still the original resolved GBA screen. When a compatible
rendering path can describe its candidates, EmuXR can separate selected visual
sources—such as background and object layers—into spatial planes. The user can
stay with the original view or try the 2.5D presentation experiments.

```text
Instrumented emulator core
        ↓ resolved pixels + optional pixel provenance
Engine-independent C ABI bridge
        ↓ frame leases and layer candidates
Unity 6 URP + OpenXR on Android XR
        ↓
Original screen or experimental 2.5D spatial presentation
```

The current implementation includes:

- Direct mGBA rendering through a native Android ARM64 bridge.
- Pixel/provenance fixtures for GBA priority, blending, windows, affine
  backgrounds, and object behavior.
- Unity 6 URP/OpenXR rendering for Samsung Galaxy XR.
- Original, Auto, ForcePartition, and parallax presentation experiments.
- ROM selection through Android's picker, controller mapping, save/load slots,
  audio, and optional spatial screen placement.
- A narrow libretro-compatible host boundary as an example for broader core
  compatibility.

## Installation

### Android XR

1. Download the current Android XR APK from the project's
   [GitHub Releases](https://github.com/buptrain/emuxr/releases).
2. Install it on a supported Android XR headset using the normal Android
   package-install flow or `adb install`.
3. Pair a controller, then use **Load ROM** from the startup menu to select a
   ROM you legally own. EmuXR does not include ROMs or BIOS files.

The current release is an Android XR build. A headset with the Android XR
OpenXR runtime is required for this path.

### Android mobile
Do not install the Android XR APK on a regular Android phone expecting a supported experience. The rendering pipeline won't be activated so a black screen is expected.
Frankly I dont see a need for now. Running mGBA inside Unity on regular mobile drains more power for no reason. So An Android mobile release is **not available yet**.
I am experimenting some alternative idea.

## Supported devices

- **Samsung Galaxy XR** — physically validated with an Xbox controller.
- **XREAL Aura** — next device planned for validation; not yet supported or
  certified by this project.

## Core support

- **mGBA / Game Boy Advance** — supported through the Direct mGBA native
  bridge.
- **Nintendo DS** — in development.

## Caveats

- The 2.5D experience would cost up to 4× streaming bandwidth compared with a
  single consolidated screen. So it can consume more power.
- Libretro API is added as an example for general compatibility only. To expose
  detailed pixel metadata, a separate core-specific pipeline is still needed.
- Performance optimization is still in the work.
- The current physical validation is on Android XR with an Xbox controller.
- Layer separation is an experiment, not a claim that every ROM can be safely
  converted to 2.5D. The Original view remains the compatibility fallback.

## Project layout

- `native/` — stable C ABI, mGBA adapter boundary, compositor, and native
  contract tests.
- `unity/` — Unity 6 Android XR project, UI, rendering, and EditMode tests.
- `fixtures/` — source-only synthetic fixture definitions and test inputs.
- `docs/` — architecture, build, validation, release, and future-core plans.

## Quick start: native checks

```powershell
cmake -S native -B build/native -DBUILD_TESTING=ON
cmake --build build/native --config Debug
ctest --test-dir build/native -C Debug --output-on-failure
```

For Unity/Android XR setup and device validation, start with
[`docs/BUILD.md`](docs/BUILD.md) and
[`docs/DEVICE_VALIDATION.md`](docs/DEVICE_VALIDATION.md).

## Core and license boundary

EmuXR frontend and bridge code are Apache-2.0. The instrumented mGBA source is
maintained separately in
[`buptrain/mgba-parallax`](https://github.com/buptrain/mgba-parallax) under
MPL-2.0. A distributed core binary must identify its exact matching source tag,
commit, notices, and checksum. See
[`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md).

## Releases

Android XR sideload builds, when available, are published through
[GitHub Releases](https://github.com/buptrain/emuxr/releases). Each release
identifies its supported-device scope, known issues, and matching core-source
provenance. There is no Android mobile release yet.

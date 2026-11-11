# Third-party notices and release boundaries

This repository contains EmuXR frontend, bridge, and test code. It must not
include commercial ROMs, BIOS files, Unity-generated caches, or a third-party
core checkout.

## mGBA Parallax

- Instrumented source fork: <https://github.com/buptrain/mgba-parallax>
- Pinned fork revision: `91c4761abb86f74dcc66bac7347f0c4faa6e1540`
- Upstream mGBA base: <https://github.com/mgba-emu/mgba>
- Upstream base revision: `a67176cd3e6bf60a5016d7adc94357142dd12617`
- License: Mozilla Public License 2.0 (MPL-2.0)
- Local provenance: [`third_party/mgba.commit`](third_party/mgba.commit)
- Compiled-core releases: <https://github.com/buptrain/mgba-parallax/releases>

The direct adapter and experimental libretro packaging can produce binaries
that include or depend on mGBA. A distributed binary release must identify the
matching `buptrain/mgba-parallax` source tag/commit, provide the MPL-2.0 text
and preserved notices, and give recipients a reasonable way to obtain the
corresponding source. The app/frontend license does not alter recipients' MPL
rights in the covered mGBA files.

## Libretro

EmuXR implements a narrow libretro-compatible host boundary. RetroArch source
is not included in this repository. Each core and any copied libretro source
must be recorded in the release SBOM with its own license and source offer.

## SeedlessCoreDS / future NDS support

- Upstream: <https://github.com/SapphireRhodonite/SeedlessDS-Core>
- Pinned source revision: `1cae6f9d8ad7dc651a48452a5ebe23fd8c347d12`
- License recorded by the project: GPL-3.0-or-later
- Local provenance: [`third_party/seedlessds.commit`](third_party/seedlessds.commit)

The NDS adapter foundation is source-only. Do not package, load, or distribute
a SeedlessCoreDS binary in a proprietary release until GPL distribution and
combined-work obligations have been approved for that release.

## Unity and Android XR packages

Unity editor caches, package caches, generated projects, and build artifacts
are not source-distribution material and are excluded from this repository.
The exact package versions are recorded in `unity/Packages/manifest.json` and
`unity/Packages/packages-lock.json`; their notices must be included in a
release-specific SBOM when required by their respective terms.

## Release artifacts

Do not commit native `.so` files to a public source repository unless the
release is accompanied by its source provenance, notices, checksums, build
recipe, and license review record. Prefer a source-only frontend repository
plus versioned core release repositories.

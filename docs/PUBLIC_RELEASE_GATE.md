# Public-release gate

This checklist applies before changing the repository visibility or publishing
an APK/core release. It is a release-engineering control, not legal advice.

## Blockers

- [ ] Remove the tracked `GBAVR/` Unity cache tree from Git history. Adding it
  to `.gitignore` prevents new additions but does not remove existing blobs.
- [ ] Verify the public history contains no ROMs, BIOS files, user save data,
  credentials, signing keys, Unity `Library/`, generated Android projects, or
  other build output.
- [ ] Remove distributable core binaries from the frontend source repository,
  or release them with matching source, notices, checksums, and provenance.
- [ ] Select and add a root license for EmuXR-authored code. Until then, public
  access is not an open-source grant.
- [ ] Generate a release-specific SBOM for Unity packages, Android libraries,
  native bridge code, and each enabled core.

## mGBA release requirements

- [ ] Publish the exact instrumented mGBA source as a public MPL-2.0 fork/tag.
- [ ] Preserve mGBA/MPL notices and include the MPL-2.0 text.
- [ ] Record upstream revision, instrumentation commit(s), patch status, NDK,
  CMake, ABI, SHA-256, and reproducible build command for every binary.
- [ ] Make the matching source link visible with every APK/core binary release.

## GPL-core gate

- [ ] Do not package a GPL core until the release model and corresponding-source
  obligations have been reviewed and approved.
- [ ] Treat runtime download, dynamic linking, and repository separation as
  implementation details, not automatic exemptions from GPL obligations.

## Recommended repository split

1. `emuxr` — source-only frontend and neutral bridge, with its own chosen
   license and no core checkout/binary.
2. `emuxr-mgba` — public instrumented mGBA fork, MPL-2.0, tagged source and
   matching binary release assets.
3. Future GPL core forks — separate GPL repositories with their own complete
   source and release obligations.

# Public-release gate

This checklist applies before changing the visibility of
[`buptrain/emuxr`](https://github.com/buptrain/emuxr), publishing a GitHub
Release, or distributing an APK outside the development team. It is a
release-engineering control, not legal advice.

## Established baseline

- [x] `master` has been history-sanitized for the legacy `GBAVR/` cache tree
  and locally built Android core plug-ins.
- [x] Locally built `.so` files, ROMs, BIOS files, save data, generated Unity
  state, third-party checkouts, and release staging content are ignored.
- [x] EmuXR-authored code is licensed under Apache-2.0.
- [x] The instrumented mGBA source authority is the MPL-2.0 fork
  [`buptrain/mgba-parallax`](https://github.com/buptrain/mgba-parallax).

The baseline does **not** approve a release. Re-run the following checks at
the exact commit/tag being released.

## Required release checks

- [ ] Audit a fresh clone of the candidate public commit for ROMs, BIOS files,
  saves, credentials, signing keys, Unity `Library/`, generated Android
  projects, build output, and proprietary assets.
- [ ] Verify the public source tree contains `LICENSE`,
  `THIRD_PARTY_NOTICES.md`, `SECURITY.md`, and the current release notes.
- [ ] Build the ARM64 APK from a clean checkout and record the complete
  toolchain, Unity, Android SDK/NDK, CMake, and source revisions.
- [ ] Run native, Unity EditMode/PlayMode, and physical Galaxy XR validation
  gates. Attach or link the evidence in the release notes.
- [ ] Verify every packaged ARM64 native library has 16 KB load-segment
  alignment and record the verification command/result.
- [ ] Release-sign the APK; record its application ID, version code, signing
  certificate SHA-256 fingerprint, and APK SHA-256.
- [ ] Confirm the Store flavor excludes developer-only core download/loading
  paths. A GitHub sideload release must disclose any materially different
  behavior from the Store flavor.

## Direct mGBA requirements

- [ ] Create/publish an immutable `mgba-parallax` source tag for the exact
  revision used by `libgba_xr.so`.
- [ ] Preserve mGBA/MPL notices and include the MPL-2.0 text in the release
  notices.
- [ ] Record upstream revision, instrumentation commit(s), bridge ABI,
  toolchain, SHA-256, and reproducible build command for the direct core.
- [ ] Link the matching source tag/commit in the APK release notes and in the
  app's About/Licenses view.

## User-facing release requirements

- [ ] State that no ROMs, BIOS files, or copyrighted game assets are included.
- [ ] State that users must provide games they are legally entitled to use.
- [ ] State that EmuXR is independent and is not affiliated with or endorsed
  by the mGBA project or game-rights holders.
- [ ] Review every README, release, and store screenshot that shows
  third-party game content or platform UI; publish it only with the necessary
  permission or remove it.
- [ ] Publish install, update, rollback, support, and known-issues guidance.

## Future GPL-core gate

- [ ] Do not package a GPL core until the release model and corresponding
  source obligations have been separately reviewed and approved.
- [ ] Treat runtime download, dynamic linking, and repository separation as
  implementation details, not automatic exemptions from GPL obligations.

Follow [GITHUB_UNOFFICIAL_RELEASE.md](GITHUB_UNOFFICIAL_RELEASE.md) for the
ordered GitHub release procedure.

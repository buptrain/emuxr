# Unofficial GitHub release runbook

This runbook produces an Android XR sideload release from the private
development upstream without making unreleased feature branches public.

## Repository roles

| Role | Remote/repository | Rule |
| --- | --- | --- |
| Private development upstream | `origin` / `buptrain/emuxr-private` | Normal development and all feature branches. |
| Public release mirror | `public` / `buptrain/emuxr` | Reviewed release-safe `master` commits and public release tags only. |
| Instrumented mGBA source | `buptrain/mgba-parallax` | MPL-2.0 source and the matching direct-core provenance release. |

Never use `git push --mirror`. Do not publish an unreviewed private branch.

## 1. Freeze a candidate

1. Select a semantic prerelease version, for example `v0.1.0-beta.1`.
2. Record the private `master` commit and run the checks in
   [PUBLIC_RELEASE_GATE.md](PUBLIC_RELEASE_GATE.md).
3. Build from a clean checkout with the production/sideload configuration:
   Direct mGBA is embedded, developer core-download controls are disabled, and
   test ROMs/probes are excluded.
4. Complete Galaxy XR smoke, controller, ROM-picker, audio, save/load,
   sleep/resume, and first-run validation.

## 2. Publish matching mGBA source first

Before publishing an APK that contains `libgba_xr.so`:

1. Tag the exact instrumented source commit in `buptrain/mgba-parallax`.
2. Create a GitHub Release containing its source/provenance details, MPL-2.0
   license and notices, build instructions, toolchain, bridge ABI, and
   `libgba_xr.so` SHA-256.
3. Fill `docs/RELEASE_MANIFEST.template.json` with the actual tag, commit,
   checksums, and validation evidence; save the completed manifest as a GitHub
   Release asset, not as an unreviewed source-tree placeholder.

## 3. Promote the audited frontend source

After the candidate is approved, push only the reviewed release commit:

```powershell
git checkout master
git push public master
git tag -a v0.1.0-beta.1 -m "EmuXR v0.1.0-beta.1"
git push public v0.1.0-beta.1
```

Tag only after confirming that `public/master` is the audited commit. If the
public repository is still private, this prepares it but does not make it
public; repository-visibility changes require an explicit owner decision.

## 4. Create a draft GitHub Release

Create a **draft prerelease** at the public tag. Attach:

- `EmuXR-v0.1.0-beta.1-arm64-v8a.apk`
- `SHA256SUMS.txt`
- completed release provenance manifest
- SPDX or CycloneDX SBOM
- `THIRD_PARTY_NOTICES.txt`
- release notes with the matching `mgba-parallax` source tag and commit

The release notes must include installation/update steps, tested headset and
controller scope, known issues, the no-ROM/no-BIOS statement, an independent
unofficial-project statement, and source/license links.

## 5. Verify the published artifact

From a clean machine/account where practical:

1. Download the APK and verify its SHA-256.
2. Verify its signing certificate fingerprint.
3. Install it on Galaxy XR and repeat the first-run and 30-minute soak tests.
4. Verify that the release links to the exact mGBA source tag and that the
   source tag remains reachable.

Publish the draft only after all checks pass. If a released APK is defective,
publish a new versioned release; do not silently replace the asset.

## Distribution boundary

This GitHub route is for sideload distribution. It does not authorize or
implement remote native-core download/activation. A future Google Play build
must package its executable native code through its reviewed delivery path.

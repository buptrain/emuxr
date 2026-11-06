# User-ROM resolved-frame smoke test

This is a local, resolved-frame-only smoke test for a GBA ROM you are legally
authorized to use. Do not commit, upload, hash, screenshot for redistribution,
or otherwise share a commercial ROM or BIOS image.

## Stage a local ROM

1. Create `unity/Assets/StreamingAssets/gba-user/` locally.
2. Copy your ROM into that directory as `first-test.gba`. The directory is
   ignored by Git, so verify `git status` does not show the file.
3. In Unity select the object with **Gba Native Frame Driver** and set **Rom
   Relative Path** to `gba-user/first-test.gba`.
4. Build and install the Android app. On launch, the driver copies this staged
   asset into `Application.persistentDataPath/gba-user/` and starts mGBA from
   app-private storage.

This path intentionally refreshes its packaged source each launch, so a new
APK cannot silently retain an earlier local test file.

## Acceptance

- The app reaches an Android XR focused session without a native crash.
- The diagnostic texture is replaced by the game’s resolved frame.
- `logcat` contains `GBA XR: copied packaged fixture` (the wording is shared
  with fixture staging) and no native worker failure.

There is no commercial-ROM golden hash, layer claim, input mapping, save-data,
or audio acceptance in this milestone. The frame is presented as the resolved
zero-parallax surface only.

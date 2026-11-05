# GBA XR Layer POC

This is a clean-room 2.5D VR proof of concept for Android XR devices, including Samsung Galaxy XR. It deliberately contains no ROMs, BIOS files, mGBA source, or Unity package cache.

The working path is native mGBA core → engine-independent C ABI bridge → Unity 6 URP/OpenXR Android XR surface. It has been validated on Galaxy XR with source-only synthetic fixtures covering resolved output and final-pixel PPU provenance. The bridge now supports copied, coverage-backed source-plane leases; renderer-side mGBA capture and Unity recomposition remain the next milestone.

## Quick start: native checks

```powershell
cmake -S native -B build/native -DBUILD_TESTING=ON
cmake --build build/native --config Debug
ctest --test-dir build/native -C Debug --output-on-failure
```

## Layout

- `native/` — stable C ABI and deterministic, engine-independent reference compositor.
- `unity/` — Unity 6 project skeleton and edit-mode compositor contract tests.
- `docs/` — architecture, backlog, build instructions, and device-validation protocol.

Read `docs/ARCHITECTURE.md` for the current system design and limits, then `docs/MGBA_INTEGRATION.md` before changing the core adapter or its patch. `docs/BUILD.md` covers local setup, `docs/TEST_STRATEGY.md` defines fixture/golden policy, and `docs/DEVICE_VALIDATION.md` is the physical-headset test protocol.

# GBA XR Layer POC

This is a clean-room bootstrap for a 2.5D VR proof of concept on Android XR devices, including Samsung Galaxy XR. It deliberately contains no ROMs, BIOS files, mGBA source, or Unity package cache.

The native reference compositor is immediately buildable and testable. The mGBA adapter and Unity scene remain integration milestones because they require separately obtained upstream mGBA source and a locally installed Unity 6 Android XR toolchain.

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

Read `docs/ARCHITECTURE.md` and `docs/MGBA_INTEGRATION.md` before connecting an emulator core. `docs/BUILD.md` covers local setup, `docs/TEST_STRATEGY.md` defines fixture/golden policy, and `docs/DEVICE_VALIDATION.md` is the physical-headset test protocol.

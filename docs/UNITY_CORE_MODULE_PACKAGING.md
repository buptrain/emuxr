# Fixed-host core-module Unity package

This separate, development-only Android ARM64 experiment does not replace
`libgba_xr.so`, the legacy adapter package, the active backend, or readiness.

```text
unity/Assets/Plugins/Android/GbaXrCoreModuleExperimental/arm64-v8a/
  libemuxr_core_host.so
  emuxr_mgba_libretro_core.so
```

Only `libemuxr_core_host.so` is P/Invoked. It resolves the installed bare
soname `emuxr_mgba_libretro_core.so` with `RTLD_NOW | RTLD_LOCAL`; paths and
downloaded/persistent-data code are rejected.

Build the fixed host and module with `GBAXR_BUILD_FIXED_CORE_HOST=ON`,
`GBAXR_BUILD_MGBA_LIBRETRO_CORE=ON`, and
`GBAXR_BUILD_LOADABLE_MGBA_LIBRETRO_MODULE=ON`, copy only reviewed ARM64
outputs to this new folder, update the manifest hashes, then run:

```powershell
& native/tools/verify_unity_core_module_package.ps1
```

## Default-off probe

Add `GbaCoreModuleProbe` only to a separate empty GameObject; it is not added
to the scene automatically. Enable **Run On Android Awake** only in an
authorized Development build. It accepts only the generated, hash-pinned
`input_state_replay.gba`, verifies observable release/press/release frame
hashes, requires non-empty host 48 kHz PCM, and verifies worker state replay
against an advanced-frame hash. It then destroys/recreates/reloads the
host/module for a second lease. Required
terminal log: `GBA XR core module probe: PASS; ... Direct mGBA remains selected.`

The probe writes no readiness or certification evidence and cannot alter core
selection. PCM-content comparison and direct/libretro visual parity remain
later certification work.

# Fixed host and loadable-core milestone

## Goal

Replace the current experimental pair, in which
`libemuxr_mgba_libretro.so` statically owns the mGBA provider, with a fixed
Unity-facing host and a separately versioned libretro module. Unity continues
to call only the fixed host ABI; it never imports a core's `retro_*` symbols.

```text
Unity C# -> libemuxr_core_host.so -> worker/bridge/audio/state
                                      -> validated libretro API table
                                      -> emuxr_mgba_libretro_core.so
```

The direct `libgba_xr.so` route remains the production fallback throughout
this work. The existing certified static libretro adapter is not modified by
this first slice.

## Completed fixed-host slices

`emuxr_core_module_loader` owns a platform-neutral, injected module resolver.
It resolves every required libretro entry point into the existing
`emuxr_libretro_api` table and fails closed when one is unavailable. A module
may additionally export `emuxr_core_module_get_descriptor_v1()` for its core ID
and RGB565 conversion policy. The descriptor is metadata only; direct mGBA
PPU provenance is deliberately not folded into libretro.

The loader refuses to unload while the fixed host declares a session active.
The eventual host must call `set_session_active(false)` only after its worker
has stopped and all frame leases, audio reads, and state operations are done.

The contract test covers open failure, required-symbol failure, descriptor
validation, expected-core-ID rejection, metadata propagation, and unload
rejection during an active session.

`-DGBAXR_BUILD_FIXED_CORE_HOST=ON` now builds `libemuxr_core_host.so`. It owns
the bridge, generic GBA libretro worker, resolved-frame leases, PCM and state
requests, but has no dependency on `mgba_libretro` or the mGBA provider. Its
only start seam accepts the already-validated `emuxr_libretro_api` table; that
is native host plumbing, not a Unity binding to `retro_*` symbols. The
`emuxr_core_host_contract_tests` target validates invalid inputs and safe
pre-start stop/destroy behavior without a core or ROM.

When both `-DGBAXR_BUILD_MGBA_LIBRETRO_CORE=ON` and
`-DGBAXR_BUILD_LOADABLE_MGBA_LIBRETRO_MODULE=ON` are selected, the mGBA core
is emitted as `emuxr_mgba_libretro_core.so` instead of the legacy
`mgba_libretro.so` and exports the optional
`emuxr_core_module_get_descriptor_v1()` metadata function. Its descriptor
identifies `mgba-parallax` and the mGBA GBA555 conversion profile. This build
flavor is intentionally separate from the existing static adapter/package;
there is no Unity asset, runtime selection, or device-proven installed-module
load path yet.

The fixed host now has a native-only installed-module start seam. It passes a
bare soname such as `emuxr_mgba_libretro_core.so` to a POSIX/Android
resolver, which opens it with `RTLD_NOW | RTLD_LOCAL`, resolves and validates
the conventional libretro symbols, then starts the generic worker. Any path
separator, drive marker, or non-`.so` name is rejected before `dlopen`, so this
API cannot target `persistentDataPath`, downloaded ZIP output, or arbitrary
filesystem locations. `emuxr_core_host_last_module_status()` and
`emuxr_core_host_copy_last_module_error()` expose bounded, caller-owned status
for a future managed binding. The loader remains live across `stop`; `destroy`
first destroys worker and bridge, then marks its loader session inactive and
unloads the module.

## Delivery design

There are two distinct delivery routes:

| Build flavor | Core location | Loader status |
| --- | --- | --- |
| Development/sideload | APK-installed native-library location used for a controlled loader experiment | Native soname resolver implemented; device packaging/path validation remains pending; downloaded ZIP remains `DownloadedNotLoadable` |
| Google Play | Play Feature Delivery native-code module | Planned; no executable download from app storage |

Android supports dynamic linking, but the downloaded artifact under
`persistentDataPath` is not approved as an executable module location. The
next implementation slice must prove an Android loader backed by an
Android-recognized installed native-library path. It must not enable the
existing developer ZIP download as a runtime code loader.

## Remaining implementation sequence

1. Verify exported-symbol and
   `DT_NEEDED` allowlists for the new `libemuxr_core_host.so` and
   `emuxr_mgba_libretro_core.so` artifacts. The host may not gain a link to
   a provider or core.
2. Prove the Android resolver against the packaged, installed native-library
   path. It must report its bounded loader error without crashing and must
   never replace the host library.
3. Add a development-only synthetic-fixture probe: load, run frame/input/audio/
   state checks, stop, release, unload, reload. The direct adapter remains the
   automatic fallback on every error.
4. Design Play Feature Delivery packaging separately from the developer
   archive flow. Publishing an external downloadable core is not a Play
   distribution mechanism.

## Validation gates

- Host tests use fake resolvers; no device or ROM is needed.
- Android build verifies separate artifacts, symbol visibility, `DT_NEEDED`,
  and 16 KB alignment.
- Galaxy XR validation uses only a synthetic fixture and records scoped
  `GBA XR core module` log markers for load, frame/input/audio/state, and
  teardown/reload outcomes.
- A commercial ROM is outside this milestone's automated validation.

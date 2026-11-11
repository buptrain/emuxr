#ifndef EMUXR_CORE_HOST_H
#define EMUXR_CORE_HOST_H

/*
 * Fixed, core-neutral host facade.
 *
 * This shared library owns the ABI-v3 bridge and the generic libretro worker,
 * but deliberately contains no mGBA provider or libretro core.  Unity will
 * eventually call only emuxr_core_host_*; a platform-native loader supplies a
 * validated emuxr_libretro_api table before a worker can start.
 */

#include "emuxr_libretro_host.h"
#include "emuxr_libretro_module_loader.h"
#include "gba_xr_bridge.h"

#ifdef __cplusplus
extern "C" {
#endif

typedef struct emuxr_core_host emuxr_core_host;

enum {
  EMUXR_CORE_HOST_STARTING = 0,
  EMUXR_CORE_HOST_RUNNING = 1,
  EMUXR_CORE_HOST_MODULE_NOT_ATTEMPTED = -4,
  EMUXR_CORE_HOST_STOPPED = -1
};

GBAXR_API emuxr_core_host *emuxr_core_host_create(
    uint32_t requested_bridge_abi_version);

/* Native-host integration seam, not a Unity/libretro binding. The caller must
 * keep `api` valid only for this call; the worker copies its table before its
 * producer thread starts. A future module loader owns API resolution and must
 * not unload its module until this host is stopped and destroyed. */
GBAXR_API int emuxr_core_host_start_with_api(
    emuxr_core_host *host,
    const emuxr_libretro_api *api,
    const char *utf8_rom_path,
    uint32_t frames_per_second);
GBAXR_API int emuxr_core_host_state(const emuxr_core_host *host);

/* Installed-module path. `module_soname` must be a bare `.so` soname (for
 * example `emuxr_mgba_libretro_core.so`), never a path. This forces Android
 * to use its installed native-library lookup and rejects persistent-data or
 * downloaded storage outright. The host keeps the module loaded until destroy
 * has stopped/destroyed the worker and bridge. */
GBAXR_API int emuxr_core_host_start_module(
    emuxr_core_host *host,
    const char *module_soname,
    const char *expected_core_id,
    const char *utf8_rom_path,
    uint32_t frames_per_second);
/* Returns emuxr_core_module_load_status, or MODULE_NOT_ATTEMPTED before the
 * first module request. It is numeric and allocation-free for future managed
 * reporting. */
GBAXR_API int emuxr_core_host_last_module_status(const emuxr_core_host *host);
/* Copies the latest bounded module-loader error text, including dlopen/dlsym
 * failures, into caller storage and always NUL terminates when capacity > 0. */
GBAXR_API uint32_t emuxr_core_host_copy_last_module_error(
    const emuxr_core_host *host,
    char *out_utf8,
    uint32_t out_capacity);

/* The opaque bridge is host-private. Consumers use this facade exclusively;
 * mixing its leases with gbaxr_* symbols from another shared object is invalid.
 */
GBAXR_API gbaxr_status emuxr_core_host_acquire_latest_frame(
    emuxr_core_host *host,
    gbaxr_frame_lease *out_lease);
GBAXR_API gbaxr_status emuxr_core_host_release_frame(
    emuxr_core_host *host,
    const gbaxr_frame_lease *lease);
GBAXR_API void emuxr_core_host_set_joypad_mask(emuxr_core_host *host,
                                                uint16_t mask);
GBAXR_API uint32_t emuxr_core_host_read_audio_pcm_s16(
    emuxr_core_host *host,
    int16_t *stereo_interleaved,
    uint32_t max_frames);
GBAXR_API uint32_t emuxr_core_host_audio_available_frames(
    const emuxr_core_host *host);
GBAXR_API uint32_t emuxr_core_host_audio_sample_rate(
    const emuxr_core_host *host);
GBAXR_API uint32_t emuxr_core_host_source_audio_sample_rate(
    const emuxr_core_host *host);
GBAXR_API uint64_t emuxr_core_host_audio_dropped_frames(
    const emuxr_core_host *host);
GBAXR_API int emuxr_core_host_save_state(emuxr_core_host *host,
                                         const char *utf8_state_path);
GBAXR_API int emuxr_core_host_load_state(emuxr_core_host *host,
                                         const char *utf8_state_path);
GBAXR_API void emuxr_core_host_stop(emuxr_core_host *host);
GBAXR_API void emuxr_core_host_destroy(emuxr_core_host *host);

#ifdef __cplusplus
}
#endif

#endif

#include "emuxr_core_host.h"

#include "emuxr_gba_libretro_worker.h"
#include "emuxr_posix_module_resolver.h"

#include <stdlib.h>
#include <string.h>

struct emuxr_core_host {
  gbaxr_context *bridge;
  emuxr_gba_libretro_worker *worker;
  emuxr_posix_module_resolver module_resolver;
  emuxr_core_module_loader module_loader;
  int last_module_status;
  int module_started;
  int started;
};

static int is_bare_module_soname(const char *module_soname) {
  size_t length;
  if (module_soname == NULL || module_soname[0] == '\0') return 0;
  length = strlen(module_soname);
  if (length < 4U || strcmp(module_soname + length - 3U, ".so") != 0) {
    return 0;
  }
  return strpbrk(module_soname, "/\\:") == NULL;
}

emuxr_core_host *emuxr_core_host_create(uint32_t requested_bridge_abi_version) {
  emuxr_core_host *host = (emuxr_core_host *)calloc(1U, sizeof(*host));
  emuxr_core_module_resolver resolver;
  if (host == NULL) return NULL;
  host->bridge = gbaxr_create_unbound(requested_bridge_abi_version);
  if (host->bridge == NULL) {
    free(host);
    return NULL;
  }
  emuxr_posix_module_resolver_init(&host->module_resolver, &resolver);
  emuxr_core_module_loader_init(&host->module_loader, &resolver);
  host->last_module_status = EMUXR_CORE_HOST_MODULE_NOT_ATTEMPTED;
  return host;
}

int emuxr_core_host_start_with_api(emuxr_core_host *host,
                                   const emuxr_libretro_api *api,
                                   const char *utf8_rom_path,
                                   uint32_t frames_per_second) {
  if (host == NULL || api == NULL || utf8_rom_path == NULL ||
      utf8_rom_path[0] == '\0' || frames_per_second == 0U ||
      frames_per_second > 120U) {
    return GBAXR_INVALID_ARGUMENT;
  }
  if (host->started || host->worker != NULL) return GBAXR_STATE_ERROR;
  host->worker = emuxr_gba_libretro_worker_start(host->bridge, api,
                                                  utf8_rom_path,
                                                  frames_per_second);
  if (host->worker == NULL) return GBAXR_STATE_ERROR;
  host->started = 1;
  return GBAXR_OK;
}

int emuxr_core_host_state(const emuxr_core_host *host) {
  return host == NULL || host->worker == NULL ? EMUXR_CORE_HOST_STOPPED :
      emuxr_gba_libretro_worker_state(host->worker);
}

int emuxr_core_host_start_module(emuxr_core_host *host,
                                 const char *module_soname,
                                 const char *expected_core_id,
                                 const char *utf8_rom_path,
                                 uint32_t frames_per_second) {
  int status;
  if (host == NULL || utf8_rom_path == NULL || utf8_rom_path[0] == '\0' ||
      frames_per_second == 0U || frames_per_second > 120U) {
    return GBAXR_INVALID_ARGUMENT;
  }
  if (!is_bare_module_soname(module_soname)) {
    host->last_module_status = EMUXR_CORE_MODULE_LOAD_INVALID_ARGUMENT;
    return GBAXR_INVALID_ARGUMENT;
  }
  if (host->started || host->worker != NULL || host->module_loader.handle != NULL) {
    return GBAXR_STATE_ERROR;
  }
  status = emuxr_core_module_loader_load(&host->module_loader, module_soname,
                                         expected_core_id);
  host->last_module_status = status;
  if (status != EMUXR_CORE_MODULE_LOAD_OK) return status;
  status = emuxr_core_host_start_with_api(
      host, emuxr_core_module_loader_api(&host->module_loader), utf8_rom_path,
      frames_per_second);
  if (status != GBAXR_OK) {
    (void)emuxr_core_module_loader_unload(&host->module_loader);
    return status;
  }
  emuxr_core_module_loader_set_session_active(&host->module_loader, true);
  host->module_started = 1;
  return GBAXR_OK;
}

int emuxr_core_host_last_module_status(const emuxr_core_host *host) {
  return host == NULL ? EMUXR_CORE_MODULE_LOAD_INVALID_ARGUMENT :
      host->last_module_status;
}

uint32_t emuxr_core_host_copy_last_module_error(
    const emuxr_core_host *host,
    char *out_utf8,
    uint32_t out_capacity) {
  if (host == NULL) {
    if (out_utf8 != NULL && out_capacity > 0U) out_utf8[0] = '\0';
    return 0U;
  }
  return emuxr_posix_module_resolver_copy_last_error(&host->module_resolver,
                                                      out_utf8, out_capacity);
}

gbaxr_status emuxr_core_host_acquire_latest_frame(emuxr_core_host *host,
                                                   gbaxr_frame_lease *out_lease) {
  return host == NULL ? GBAXR_INVALID_ARGUMENT :
      gbaxr_acquire_latest_frame(host->bridge, out_lease);
}

gbaxr_status emuxr_core_host_release_frame(emuxr_core_host *host,
                                            const gbaxr_frame_lease *lease) {
  return host == NULL ? GBAXR_INVALID_ARGUMENT :
      gbaxr_release_frame(host->bridge, lease);
}

void emuxr_core_host_set_joypad_mask(emuxr_core_host *host, uint16_t mask) {
  if (host != NULL) emuxr_gba_libretro_worker_set_joypad_mask(host->worker, mask);
}

uint32_t emuxr_core_host_read_audio_pcm_s16(emuxr_core_host *host,
                                            int16_t *stereo_interleaved,
                                            uint32_t max_frames) {
  return host == NULL ? 0U : emuxr_gba_libretro_worker_read_audio_pcm_s16(
      host->worker, stereo_interleaved, max_frames);
}

uint32_t emuxr_core_host_audio_available_frames(const emuxr_core_host *host) {
  return host == NULL ? 0U :
      emuxr_gba_libretro_worker_audio_available_frames(host->worker);
}

uint32_t emuxr_core_host_audio_sample_rate(const emuxr_core_host *host) {
  return host == NULL ? 0U :
      emuxr_gba_libretro_worker_audio_sample_rate(host->worker);
}

uint32_t emuxr_core_host_source_audio_sample_rate(const emuxr_core_host *host) {
  return host == NULL ? 0U :
      emuxr_gba_libretro_worker_source_audio_sample_rate(host->worker);
}

uint64_t emuxr_core_host_audio_dropped_frames(const emuxr_core_host *host) {
  return host == NULL ? 0U :
      emuxr_gba_libretro_worker_audio_dropped_frames(host->worker);
}

int emuxr_core_host_save_state(emuxr_core_host *host,
                               const char *utf8_state_path) {
  return host == NULL ? GBAXR_INVALID_ARGUMENT :
      emuxr_gba_libretro_worker_save_state(host->worker, utf8_state_path);
}

int emuxr_core_host_load_state(emuxr_core_host *host,
                               const char *utf8_state_path) {
  return host == NULL ? GBAXR_INVALID_ARGUMENT :
      emuxr_gba_libretro_worker_load_state(host->worker, utf8_state_path);
}

void emuxr_core_host_stop(emuxr_core_host *host) {
  if (host != NULL) emuxr_gba_libretro_worker_stop(host->worker);
}

void emuxr_core_host_destroy(emuxr_core_host *host) {
  if (host != NULL) {
    emuxr_gba_libretro_worker_destroy(host->worker);
    gbaxr_destroy(host->bridge);
    if (host->module_started) {
      emuxr_core_module_loader_set_session_active(&host->module_loader, false);
    }
    (void)emuxr_core_module_loader_unload(&host->module_loader);
    free(host);
  }
}

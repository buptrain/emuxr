#include "emuxr_core_host.h"

#include <stdio.h>
#include <string.h>

#define CHECK(expr) do { if (!(expr)) { \
  fprintf(stderr, "failed: %s (%d)\n", #expr, __LINE__); return 1; \
} } while (0)

int main(void) {
  emuxr_core_host *host;
  gbaxr_frame_lease lease;

  CHECK(emuxr_core_host_create(GBAXR_ABI_VERSION + 1U) == NULL);
  CHECK(emuxr_core_host_start_with_api(NULL, NULL, "fixture.gba", 60U) ==
        GBAXR_INVALID_ARGUMENT);
  CHECK(emuxr_core_host_start_module(NULL, "libcore.so", NULL, "fixture.gba", 60U) ==
        GBAXR_INVALID_ARGUMENT);
  CHECK(emuxr_core_host_state(NULL) == EMUXR_CORE_HOST_STOPPED);
  CHECK(emuxr_core_host_acquire_latest_frame(NULL, &lease) ==
        GBAXR_INVALID_ARGUMENT);
  CHECK(emuxr_core_host_release_frame(NULL, &lease) == GBAXR_INVALID_ARGUMENT);
  CHECK(emuxr_core_host_audio_available_frames(NULL) == 0U);
  CHECK(emuxr_core_host_read_audio_pcm_s16(NULL, NULL, 0U) == 0U);
  CHECK(emuxr_core_host_save_state(NULL, "state.ss0") == GBAXR_INVALID_ARGUMENT);
  CHECK(emuxr_core_host_load_state(NULL, "state.ss0") == GBAXR_INVALID_ARGUMENT);
  emuxr_core_host_stop(NULL);
  emuxr_core_host_destroy(NULL);

  host = emuxr_core_host_create(GBAXR_ABI_VERSION);
  CHECK(host != NULL);
  memset(&lease, 0, sizeof(lease));
  CHECK(emuxr_core_host_acquire_latest_frame(host, NULL) == GBAXR_INVALID_ARGUMENT);
  CHECK(emuxr_core_host_release_frame(host, NULL) == GBAXR_INVALID_ARGUMENT);
  CHECK(emuxr_core_host_acquire_latest_frame(host, &lease) == GBAXR_NO_FRAME);
  CHECK(emuxr_core_host_release_frame(host, &lease) == GBAXR_STATE_ERROR);
  CHECK(emuxr_core_host_start_with_api(host, NULL, "fixture.gba", 60U) ==
        GBAXR_INVALID_ARGUMENT);
  CHECK(emuxr_core_host_start_with_api(host, NULL, "", 60U) ==
        GBAXR_INVALID_ARGUMENT);
  CHECK(emuxr_core_host_audio_sample_rate(host) == 0U);
  CHECK(emuxr_core_host_source_audio_sample_rate(host) == 0U);
  CHECK(emuxr_core_host_audio_dropped_frames(host) == 0U);
  CHECK(emuxr_core_host_last_module_status(host) == EMUXR_CORE_HOST_MODULE_NOT_ATTEMPTED);
  CHECK(emuxr_core_host_start_module(host, "/data/local/tmp/libcore.so", NULL,
                                     "fixture.gba", 60U) == GBAXR_INVALID_ARGUMENT);
  CHECK(emuxr_core_host_start_module(host, "..\\libcore.so", NULL,
                                     "fixture.gba", 60U) == GBAXR_INVALID_ARGUMENT);
  CHECK(emuxr_core_host_last_module_status(host) == EMUXR_CORE_MODULE_LOAD_INVALID_ARGUMENT);
  CHECK(emuxr_core_host_start_module(host, "missing-core.so", NULL,
                                     "fixture.gba", 60U) ==
        EMUXR_CORE_MODULE_LOAD_OPEN_FAILED);
  CHECK(emuxr_core_host_last_module_status(host) == EMUXR_CORE_MODULE_LOAD_OPEN_FAILED);
  emuxr_core_host_stop(host);
  emuxr_core_host_stop(host);
  emuxr_core_host_destroy(host);
  puts("fixed core host ABI tests passed");
  return 0;
}

#include "gba_xr_mgba_adapter.h"

#include <fcntl.h>
#include <stddef.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>

#include <pthread.h>
#include <stdatomic.h>
#include <time.h>

#include <mgba/core/core.h>
#include <mgba/gba/core.h>
#include <mgba/internal/gba/gba.h>
#include <mgba/internal/gba/renderers/video-software.h>
#include <mgba-util/image.h>
#include <mgba-util/vfs.h>

enum { GBAXR_FRAME_PIXELS = GBAXR_GBA_WIDTH * GBAXR_GBA_HEIGHT };

struct gbaxr_mgba_session {
  gbaxr_context *bridge;
  struct mCore *core;
  mColor resolved_native[GBAXR_FRAME_PIXELS];
  uint8_t resolved_rgba[GBAXR_FRAME_PIXELS * 4U];
  gbaxr_pixel_provenance provenance[GBAXR_FRAME_PIXELS];
  int rom_loaded;
};

struct gbaxr_mgba_worker {
  pthread_t thread;
  atomic_int stop_requested;
  atomic_int state;
  gbaxr_context *bridge;
  char *rom_path;
  uint32_t frames_per_second;
  int thread_started;
};

static void convert_native_to_rgba8(const mColor *source, uint8_t *destination) {
  uint32_t index;
#ifdef COLOR_16_BIT
  for (index = 0U; index < GBAXR_FRAME_PIXELS; ++index) {
    const uint16_t pixel = source[index];
    destination[index * 4U] = (uint8_t)M_R8(pixel);
    destination[index * 4U + 1U] = (uint8_t)M_G8(pixel);
    destination[index * 4U + 2U] = (uint8_t)M_B8(pixel);
    destination[index * 4U + 3U] = 255U;
  }
#else
  /* mGBA's default mColor is XBGR8: in little-endian memory its bytes are
   * R, G, B, X. The adapter normalizes it to explicit RGBA8. */
  for (index = 0U; index < GBAXR_FRAME_PIXELS; ++index) {
    const uint32_t pixel = source[index];
    destination[index * 4U] = (uint8_t)(pixel & 0xFFU);
    destination[index * 4U + 1U] = (uint8_t)((pixel >> 8U) & 0xFFU);
    destination[index * 4U + 2U] = (uint8_t)((pixel >> 16U) & 0xFFU);
    destination[index * 4U + 3U] = 255U;
  }
#endif
}

gbaxr_mgba_session *gbaxr_mgba_create(gbaxr_context *bridge) {
  gbaxr_mgba_session *session;
  if (bridge == NULL) {
    return NULL;
  }
  session = (gbaxr_mgba_session *)calloc(1U, sizeof(*session));
  if (session == NULL) {
    return NULL;
  }
  session->bridge = bridge;
  session->core = GBACoreCreate();
  if (session->core == NULL || !session->core->init(session->core)) {
    free(session);
    return NULL;
  }
  mCoreInitConfig(session->core, "gba-xr");
  mCoreLoadConfig(session->core);
  session->core->setVideoBuffer(session->core, session->resolved_native, GBAXR_GBA_WIDTH);
  return session;
}

void gbaxr_mgba_destroy(gbaxr_mgba_session *session) {
  if (session == NULL) {
    return;
  }
  if (session->core != NULL) {
    if (session->rom_loaded) {
      session->core->unloadROM(session->core);
    }
    mCoreConfigDeinit(&session->core->config);
    session->core->deinit(session->core);
  }
  free(session);
}

gbaxr_status gbaxr_mgba_load_rom(gbaxr_mgba_session *session,
                                  const char *utf8_rom_path) {
  struct VFile *rom;
  if (session == NULL || session->core == NULL || utf8_rom_path == NULL || utf8_rom_path[0] == '\0') {
    return GBAXR_INVALID_ARGUMENT;
  }
  if (session->rom_loaded) {
    session->core->unloadROM(session->core);
    session->rom_loaded = 0;
  }
  rom = VFileOpen(utf8_rom_path, O_RDONLY);
  if (rom == NULL) {
    return GBAXR_STATE_ERROR;
  }
  if (!session->core->loadROM(session->core, rom)) {
    rom->close(rom);
    return GBAXR_STATE_ERROR;
  }
  session->core->reset(session->core);
  /* mGBA associates its real software renderer during ROM load/reset. Do not
   * cast the pre-load dummy renderer: its smaller layout contains callbacks. */
  GBAVideoSoftwareRendererSetProvenanceBuffer(
      (struct GBAVideoSoftwareRenderer *)((struct GBA *)session->core->board)->video.renderer,
      (uint32_t *)session->provenance,
      GBAXR_GBA_WIDTH);
  session->rom_loaded = 1;
  return GBAXR_OK;
}

void gbaxr_mgba_unload_rom(gbaxr_mgba_session *session) {
  if (session != NULL && session->core != NULL && session->rom_loaded) {
    session->core->unloadROM(session->core);
    session->rom_loaded = 0;
  }
}

gbaxr_status gbaxr_mgba_run_frame(gbaxr_mgba_session *session) {
  gbaxr_image_view image;
  if (session == NULL || session->core == NULL || !session->rom_loaded) {
    return GBAXR_STATE_ERROR;
  }
  session->core->runFrame(session->core);
  convert_native_to_rgba8(session->resolved_native, session->resolved_rgba);
  image.rgba8 = session->resolved_rgba;
  image.width = GBAXR_GBA_WIDTH;
  image.height = GBAXR_GBA_HEIGHT;
  image.row_stride_bytes = GBAXR_GBA_WIDTH * 4U;
  return gbaxr_publish_frame(session->bridge,
                             &image,
                             session->provenance,
                             GBAXR_GBA_WIDTH * (uint32_t)sizeof(*session->provenance));
}

static void sleep_for_frame(uint32_t frames_per_second) {
  struct timespec delay;
  uint64_t nanoseconds = 1000000000ULL / frames_per_second;
  delay.tv_sec = (time_t)(nanoseconds / 1000000000ULL);
  delay.tv_nsec = (long)(nanoseconds % 1000000000ULL);
  nanosleep(&delay, NULL);
}

static void *mgba_worker_main(void *opaque) {
  gbaxr_mgba_worker *worker = (gbaxr_mgba_worker *)opaque;
  gbaxr_mgba_session *session = gbaxr_mgba_create(worker->bridge);
  if (session == NULL) {
    atomic_store(&worker->state, GBAXR_MGBA_WORKER_INIT_FAILED);
    return NULL;
  }
  if (gbaxr_mgba_load_rom(session, worker->rom_path) != GBAXR_OK) {
    atomic_store(&worker->state, GBAXR_MGBA_WORKER_ROM_LOAD_FAILED);
    gbaxr_mgba_destroy(session);
    return NULL;
  }

  atomic_store(&worker->state, GBAXR_MGBA_WORKER_RUNNING);
  while (!atomic_load(&worker->stop_requested)) {
    gbaxr_status status = gbaxr_mgba_run_frame(session);
    if (status != GBAXR_OK && status != GBAXR_NO_FRAME) {
      atomic_store(&worker->state, GBAXR_MGBA_WORKER_FRAME_FAILED);
      break;
    }
    sleep_for_frame(worker->frames_per_second);
  }
  gbaxr_mgba_destroy(session);
  return NULL;
}

gbaxr_mgba_worker *gbaxr_mgba_worker_start(gbaxr_context *bridge,
                                             const char *utf8_rom_path,
                                             uint32_t frames_per_second) {
  gbaxr_mgba_worker *worker;
  size_t path_length;
  if (bridge == NULL || utf8_rom_path == NULL || utf8_rom_path[0] == '\0' ||
      frames_per_second == 0U) {
    return NULL;
  }
  worker = (gbaxr_mgba_worker *)calloc(1U, sizeof(*worker));
  if (worker == NULL) {
    return NULL;
  }
  path_length = strlen(utf8_rom_path) + 1U;
  worker->rom_path = (char *)malloc(path_length);
  if (worker->rom_path == NULL) {
    free(worker);
    return NULL;
  }
  memcpy(worker->rom_path, utf8_rom_path, path_length);
  worker->bridge = bridge;
  worker->frames_per_second = frames_per_second;
  atomic_init(&worker->stop_requested, 0);
  atomic_init(&worker->state, GBAXR_MGBA_WORKER_STARTING);
  if (pthread_create(&worker->thread, NULL, mgba_worker_main, worker) != 0) {
    free(worker->rom_path);
    free(worker);
    return NULL;
  }
  worker->thread_started = 1;
  return worker;
}

int gbaxr_mgba_worker_state(const gbaxr_mgba_worker *worker) {
  if (worker == NULL) {
    return GBAXR_MGBA_WORKER_INIT_FAILED;
  }
  return atomic_load(&worker->state);
}

void gbaxr_mgba_worker_stop(gbaxr_mgba_worker *worker) {
  if (worker != NULL && worker->thread_started) {
    atomic_store(&worker->stop_requested, 1);
    pthread_join(worker->thread, NULL);
    worker->thread_started = 0;
  }
}

void gbaxr_mgba_worker_destroy(gbaxr_mgba_worker *worker) {
  if (worker != NULL) {
    gbaxr_mgba_worker_stop(worker);
    free(worker->rom_path);
    free(worker);
  }
}

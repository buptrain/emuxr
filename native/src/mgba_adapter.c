#include "gba_xr_mgba_adapter.h"
#include "gba_xr_independent_candidate.h"
#include "gba_xr_visible_partition.h"
#include "gba_xr_audio_ring.h"

#include <fcntl.h>
#include <stddef.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>

#include <pthread.h>
#include <stdatomic.h>
#include <time.h>

#if defined(__ANDROID__)
#include <android/log.h>
#endif

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
  mColor bg0_candidate_native[GBAXR_FRAME_PIXELS];
  uint8_t bg0_candidate_rgba[GBAXR_FRAME_PIXELS * 4U];
  uint8_t bg0_candidate_coverage[GBAXR_FRAME_PIXELS];
  mColor bg1_candidate_native[GBAXR_FRAME_PIXELS];
  uint8_t bg1_candidate_rgba[GBAXR_FRAME_PIXELS * 4U];
  uint8_t bg1_candidate_coverage[GBAXR_FRAME_PIXELS];
  mColor obj_candidate_native[GBAXR_FRAME_PIXELS];
  uint8_t obj_candidate_rgba[GBAXR_FRAME_PIXELS * 4U];
  uint8_t obj_candidate_coverage[GBAXR_FRAME_PIXELS];
  gbaxr_visible_partition visible_partition;
  int logged_candidate_coverage;
  int rom_loaded;
  gbaxr_audio_ring *audio;
};

struct gbaxr_mgba_worker {
  pthread_t thread;
  atomic_int stop_requested;
  atomic_int state;
  atomic_uint keys;
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
  session->audio = gbaxr_audio_ring_create(8192U);
  session->core = GBACoreCreate();
  if (session->core == NULL || !session->core->init(session->core)) {
    gbaxr_audio_ring_destroy(session->audio);
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
  gbaxr_audio_ring_destroy(session->audio);
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
  /* Let mGBA create/load the conventional sibling .sav file now that the ROM
   * is in Unity's app-private persistent directory. */
  if (!mCoreAutoloadSave(session->core)) {
#if defined(__ANDROID__)
    __android_log_print(ANDROID_LOG_WARN, "GbaXr", "could not auto-load save data");
#endif
  }
  session->core->reset(session->core);
  /* Start each loaded ROM with no candidate coverage. The software renderer
   * forces candidate scanlines dirty, and run_frame clears these masks again
   * before every snapshot. */
  memset(session->bg0_candidate_coverage, 0, sizeof(session->bg0_candidate_coverage));
  memset(session->bg1_candidate_coverage, 0, sizeof(session->bg1_candidate_coverage));
  memset(session->obj_candidate_coverage, 0, sizeof(session->obj_candidate_coverage));
  /* mGBA associates its real software renderer during ROM load/reset. Do not
   * cast the pre-load dummy renderer: its smaller layout contains callbacks. */
  GBAVideoSoftwareRendererSetProvenanceBuffer(
      (struct GBAVideoSoftwareRenderer *)((struct GBA *)session->core->board)->video.renderer,
      (uint32_t *)session->provenance,
      GBAXR_GBA_WIDTH);
  GBAVideoSoftwareRendererSetBackgroundCandidateBuffer(
      (struct GBAVideoSoftwareRenderer *)((struct GBA *)session->core->board)->video.renderer,
      0U, session->bg0_candidate_native, session->bg0_candidate_coverage, GBAXR_GBA_WIDTH);
  GBAVideoSoftwareRendererSetBackgroundCandidateBuffer(
      (struct GBAVideoSoftwareRenderer *)((struct GBA *)session->core->board)->video.renderer,
      1U, session->bg1_candidate_native, session->bg1_candidate_coverage, GBAXR_GBA_WIDTH);
  GBAVideoSoftwareRendererSetObjCandidateBuffer(
      (struct GBAVideoSoftwareRenderer *)((struct GBA *)session->core->board)->video.renderer,
      session->obj_candidate_native, session->obj_candidate_coverage, GBAXR_GBA_WIDTH);
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
  gbaxr_layer_view layers[GBAXR_MAX_LAYERS];
  uint32_t source;
  uint32_t layer_count = 0U;
  if (session == NULL || session->core == NULL || !session->rom_loaded) {
    return GBAXR_STATE_ERROR;
  }
  memset(session->bg0_candidate_coverage, 0, sizeof(session->bg0_candidate_coverage));
  memset(session->bg1_candidate_coverage, 0, sizeof(session->bg1_candidate_coverage));
  memset(session->obj_candidate_coverage, 0, sizeof(session->obj_candidate_coverage));
  session->core->runFrame(session->core);
  convert_native_to_rgba8(session->resolved_native, session->resolved_rgba);
  convert_native_to_rgba8(session->bg0_candidate_native, session->bg0_candidate_rgba);
  convert_native_to_rgba8(session->bg1_candidate_native, session->bg1_candidate_rgba);
  convert_native_to_rgba8(session->obj_candidate_native, session->obj_candidate_rgba);
  image.rgba8 = session->resolved_rgba;
  image.width = GBAXR_GBA_WIDTH;
  image.height = GBAXR_GBA_HEIGHT;
  image.row_stride_bytes = GBAXR_GBA_WIDTH * 4U;
  /* A raw candidate may be called independent only when it exactly explains
   * every resolved pixel without composition effects. Anything broader falls
   * back to the diagnostic visible partition. */
  {
    uint32_t bg0_priority;
    if (gbaxr_bg0_candidate_recomposes_resolved(session->resolved_rgba, session->provenance,
                                                 session->bg0_candidate_rgba,
                                                 session->bg0_candidate_coverage,
                                                 &bg0_priority)) {
      layers[0].source = GBAXR_SOURCE_BG0;
      layers[0].color.rgba8 = session->bg0_candidate_rgba;
      layers[0].color.width = GBAXR_GBA_WIDTH;
      layers[0].color.height = GBAXR_GBA_HEIGHT;
      layers[0].color.row_stride_bytes = GBAXR_GBA_WIDTH * 4U;
      layers[0].coverage_mask = session->bg0_candidate_coverage;
      layers[0].priority = bg0_priority;
      return gbaxr_publish_frame_layers(session->bridge, &image, session->provenance,
                                        GBAXR_GBA_WIDTH * (uint32_t)sizeof(*session->provenance),
                                        layers, 1U, GBAXR_LAYERS_INDEPENDENT_CANDIDATES);
    }
    if (gbaxr_bg0_bg1_candidates_recompose_resolved(session->resolved_rgba, session->provenance,
                                                     session->bg0_candidate_rgba, session->bg0_candidate_coverage,
                                                     session->bg1_candidate_rgba, session->bg1_candidate_coverage,
                                                     &bg0_priority)) {
      layers[0].source = GBAXR_SOURCE_BG0;
      layers[0].color = image;
      layers[0].color.rgba8 = session->bg0_candidate_rgba;
      layers[0].coverage_mask = session->bg0_candidate_coverage;
      layers[0].priority = bg0_priority;
      layers[1].source = GBAXR_SOURCE_BG1;
      layers[1].color = image;
      layers[1].color.rgba8 = session->bg1_candidate_rgba;
      layers[1].coverage_mask = session->bg1_candidate_coverage;
      layers[1].priority = bg0_priority;
      return gbaxr_publish_frame_layers(session->bridge, &image, session->provenance,
                                        GBAXR_GBA_WIDTH * (uint32_t)sizeof(*session->provenance),
                                        layers, 2U, GBAXR_LAYERS_INDEPENDENT_CANDIDATES);
    }
    if (gbaxr_bg0_obj_candidates_recompose_resolved(session->resolved_rgba, session->provenance,
                                                     session->bg0_candidate_rgba, session->bg0_candidate_coverage,
                                                     session->obj_candidate_rgba, session->obj_candidate_coverage,
                                                     &bg0_priority)) {
      layers[0].source = GBAXR_SOURCE_BG0; layers[0].color = image; layers[0].color.rgba8 = session->bg0_candidate_rgba; layers[0].coverage_mask = session->bg0_candidate_coverage; layers[0].priority = bg0_priority;
      layers[1].source = GBAXR_SOURCE_OBJ; layers[1].color = image; layers[1].color.rgba8 = session->obj_candidate_rgba; layers[1].coverage_mask = session->obj_candidate_coverage; layers[1].priority = bg0_priority;
      return gbaxr_publish_frame_layers(session->bridge, &image, session->provenance, GBAXR_GBA_WIDTH * (uint32_t)sizeof(*session->provenance), layers, 2U, GBAXR_LAYERS_INDEPENDENT_CANDIDATES);
    }
  }
  if (gbaxr_build_visible_partition(session->resolved_rgba, session->provenance,
                                    &session->visible_partition) != 0) {
    return GBAXR_STATE_ERROR;
  }
#if defined(__ANDROID__)
  if (!session->logged_candidate_coverage && (session->provenance[0].source == GBAXR_SOURCE_BG0 ||
      session->provenance[0].source == GBAXR_SOURCE_BG1)) {
    uint32_t pixel, bg0_count = 0U, bg1_count = 0U;
    for (pixel = 0U; pixel < GBAXR_FRAME_PIXELS; ++pixel) {
      bg0_count += session->bg0_candidate_coverage[pixel] != 0U;
      bg1_count += session->bg1_candidate_coverage[pixel] != 0U;
    }
    __android_log_print(ANDROID_LOG_INFO, "GbaXr", "raw candidate coverage: bg0=%u bg1=%u", bg0_count, bg1_count);
    session->logged_candidate_coverage = 1;
  }
#endif
  for (source = 0U; source < GBAXR_MAX_LAYERS; ++source) {
    gbaxr_layer_view *layer;
    if (!session->visible_partition.present[source]) {
      continue;
    }
    layer = &layers[layer_count++];
    layer->source = (gbaxr_source_kind)source;
    layer->color.rgba8 = session->visible_partition.rgba[source];
    layer->color.width = GBAXR_GBA_WIDTH;
    layer->color.height = GBAXR_GBA_HEIGHT;
    layer->color.row_stride_bytes = GBAXR_GBA_WIDTH * 4U;
    layer->coverage_mask = session->visible_partition.coverage[source];
    layer->priority = session->visible_partition.priority[source];
  }
  return gbaxr_publish_frame_layers(session->bridge,
                                    &image,
                                    session->provenance,
                                    GBAXR_GBA_WIDTH * (uint32_t)sizeof(*session->provenance),
                                    layers,
                                    layer_count,
                                    GBAXR_LAYERS_VISIBLE_PARTITION);
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
    session->core->setKeys(session->core, atomic_load(&worker->keys));
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
  atomic_init(&worker->keys, 0U);
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

void gbaxr_mgba_worker_set_keys(gbaxr_mgba_worker *worker, uint32_t keys) {
  if (worker != NULL) {
    atomic_store(&worker->keys, keys & 0x3FFU);
  }
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

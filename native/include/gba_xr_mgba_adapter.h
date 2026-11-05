#ifndef GBA_XR_MGBA_ADAPTER_H
#define GBA_XR_MGBA_ADAPTER_H

#include "gba_xr_bridge.h"

#ifdef __cplusplus
extern "C" {
#endif

typedef struct gbaxr_mgba_session gbaxr_mgba_session;
typedef struct gbaxr_mgba_worker gbaxr_mgba_worker;

enum {
  GBAXR_MGBA_WORKER_STARTING = 0,
  GBAXR_MGBA_WORKER_RUNNING = 1,
  GBAXR_MGBA_WORKER_INIT_FAILED = -1,
  GBAXR_MGBA_WORKER_ROM_LOAD_FAILED = -2,
  GBAXR_MGBA_WORKER_FRAME_FAILED = -3
};

/* Must be called from the bridge producer thread. This adapter owns one mGBA
 * GBA core instance but never exposes mGBA headers/types to Unity. */
GBAXR_API gbaxr_mgba_session *gbaxr_mgba_create(gbaxr_context *bridge);
GBAXR_API void gbaxr_mgba_destroy(gbaxr_mgba_session *session);

/* Loads a user-selected UTF-8 ROM path. The ROM file remains owned by mGBA
 * until unload/destroy. ROMs and BIOS files must never be committed here. */
GBAXR_API gbaxr_status gbaxr_mgba_load_rom(gbaxr_mgba_session *session,
                                           const char *utf8_rom_path);
GBAXR_API void gbaxr_mgba_unload_rom(gbaxr_mgba_session *session);

/* Runs exactly one emulated frame, converts the resolved mGBA framebuffer to
 * non-premultiplied RGBA8, then publishes an immutable mailbox snapshot. */
GBAXR_API gbaxr_status gbaxr_mgba_run_frame(gbaxr_mgba_session *session);

/* Owns a session on one native producer thread. The bridge must be created
 * with gbaxr_create_unbound so its producer affinity belongs to that thread. */
GBAXR_API gbaxr_mgba_worker *gbaxr_mgba_worker_start(gbaxr_context *bridge,
                                                       const char *utf8_rom_path,
                                                       uint32_t frames_per_second);
GBAXR_API int gbaxr_mgba_worker_state(const gbaxr_mgba_worker *worker);
GBAXR_API void gbaxr_mgba_worker_stop(gbaxr_mgba_worker *worker);
GBAXR_API void gbaxr_mgba_worker_destroy(gbaxr_mgba_worker *worker);

#ifdef __cplusplus
}
#endif

#endif

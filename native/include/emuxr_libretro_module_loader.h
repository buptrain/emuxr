#ifndef EMUXR_LIBRETRO_MODULE_LOADER_H
#define EMUXR_LIBRETRO_MODULE_LOADER_H

/*
 * Fixed-host boundary for separately delivered libretro cores.
 *
 * The host never exposes a core's symbols to Unity.  A platform-specific
 * resolver supplies open/symbol/close (dlopen/dlsym/dlclose on Android) and
 * this file validates the conventional libretro ABI into emuxr_libretro_api.
 * Keeping the resolver injected makes the contract unit-testable and avoids
 * treating a downloaded archive as executable code.
 */

#include "emuxr_libretro_host.h"

#ifdef __cplusplus
extern "C" {
#endif

enum {
  EMUXR_CORE_MODULE_ABI_V1 = 1U
};

#define EMUXR_CORE_MODULE_DESCRIPTOR_SYMBOL_V1 \
  "emuxr_core_module_get_descriptor_v1"

typedef struct emuxr_core_module_descriptor_v1 {
  uint32_t abi_version;
  uint32_t struct_size;
  const char *core_id;
  emuxr_libretro_rgb565_conversion rgb565_conversion;
} emuxr_core_module_descriptor_v1;

/* A module can export `emuxr_core_module_get_descriptor_v1` in addition to libretro's
 * standard retro_* ABI. It exists only for host conversion metadata; it does
 * not make PPU provenance a libretro requirement. */
typedef const emuxr_core_module_descriptor_v1 *
    (*emuxr_core_module_descriptor_query_v1)(void);

typedef void *(*emuxr_core_module_open_fn)(void *opaque, const char *path);
/* A generic function pointer keeps the injected resolver portable. The
 * Android implementation is the sole dlsym/object-pointer conversion site. */
typedef void (*emuxr_core_module_symbol_t)(void);
typedef emuxr_core_module_symbol_t (*emuxr_core_module_symbol_fn)(void *opaque,
                                                                   void *handle,
                                                                   const char *name);
typedef void (*emuxr_core_module_close_fn)(void *opaque, void *handle);

typedef struct emuxr_core_module_resolver {
  emuxr_core_module_open_fn open;
  emuxr_core_module_symbol_fn symbol;
  emuxr_core_module_close_fn close;
  void *opaque;
} emuxr_core_module_resolver;

typedef enum emuxr_core_module_load_status {
  EMUXR_CORE_MODULE_LOAD_OK = 0,
  EMUXR_CORE_MODULE_LOAD_INVALID_ARGUMENT = -1,
  EMUXR_CORE_MODULE_LOAD_OPEN_FAILED = -2,
  EMUXR_CORE_MODULE_LOAD_REQUIRED_SYMBOL_MISSING = -3,
  EMUXR_CORE_MODULE_LOAD_DESCRIPTOR_INVALID = -4,
  EMUXR_CORE_MODULE_LOAD_CORE_ID_MISMATCH = -5,
  EMUXR_CORE_MODULE_LOAD_SESSION_ACTIVE = -6
} emuxr_core_module_load_status;

typedef struct emuxr_core_module_loader {
  emuxr_core_module_resolver resolver;
  void *handle;
  emuxr_libretro_api api;
  const emuxr_core_module_descriptor_v1 *descriptor;
  bool session_active;
} emuxr_core_module_loader;

void emuxr_core_module_loader_init(emuxr_core_module_loader *loader,
                                   const emuxr_core_module_resolver *resolver);

/* expected_core_id is optional. When supplied, only an extension descriptor
 * with that exact ID is accepted. A module without the descriptor remains a
 * normal resolved-frame libretro core when no ID check is requested. */
emuxr_core_module_load_status emuxr_core_module_loader_load(
    emuxr_core_module_loader *loader,
    const char *module_path,
    const char *expected_core_id);

const emuxr_libretro_api *emuxr_core_module_loader_api(
    const emuxr_core_module_loader *loader);
const emuxr_core_module_descriptor_v1 *emuxr_core_module_loader_descriptor(
    const emuxr_core_module_loader *loader);

/* The fixed host must set this after its worker starts. Unload is refused
 * until it has stopped the worker and released every frame/audio lease. */
void emuxr_core_module_loader_set_session_active(
    emuxr_core_module_loader *loader,
    bool active);
emuxr_core_module_load_status emuxr_core_module_loader_unload(
    emuxr_core_module_loader *loader);

#ifdef __cplusplus
}
#endif

#endif

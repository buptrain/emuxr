#include "emuxr_libretro_module_loader.h"

#include <stdio.h>
#include <string.h>

#define CHECK(expr) do { if (!(expr)) { fprintf(stderr, "failed: %s (%d)\n", #expr, __LINE__); return 1; } } while (0)

typedef struct fake_module {
  int opens;
  int closes;
  const char *missing;
  int descriptor_enabled;
  emuxr_core_module_descriptor_v1 descriptor;
} fake_module;

static void noop(void) {}
static bool load_game(const emuxr_libretro_game_info *game) { return game != NULL; }
static void get_av(emuxr_libretro_system_av_info *info) { memset(info, 0, sizeof(*info)); }
static size_t state_size(void) { return 0U; }
static bool serialize(void *data, size_t size) { (void)data; return size == 0U; }
static bool unserialize(const void *data, size_t size) { (void)data; return size == 0U; }
static const emuxr_core_module_descriptor_v1 *descriptor_v1(void);

static void *fake_open(void *opaque, const char *path) {
  fake_module *module = (fake_module *)opaque;
  ++module->opens;
  return strcmp(path, "module.so") == 0 ? module : NULL;
}
static void fake_close(void *opaque, void *handle) {
  fake_module *module = (fake_module *)opaque;
  if (handle != module) return;
  ++module->closes;
}
static emuxr_core_module_symbol_t fake_symbol(void *opaque, void *handle, const char *name) {
  fake_module *module = (fake_module *)opaque;
  (void)handle;
  if (module->missing != NULL && strcmp(module->missing, name) == 0) return NULL;
  if (strcmp(name, EMUXR_CORE_MODULE_DESCRIPTOR_SYMBOL_V1) == 0)
    return module->descriptor_enabled ? (emuxr_core_module_symbol_t)descriptor_v1 : NULL;
  if (strcmp(name, "retro_load_game") == 0) return (emuxr_core_module_symbol_t)load_game;
  if (strcmp(name, "retro_get_system_av_info") == 0) return (emuxr_core_module_symbol_t)get_av;
  if (strcmp(name, "retro_serialize_size") == 0) return (emuxr_core_module_symbol_t)state_size;
  if (strcmp(name, "retro_serialize") == 0) return (emuxr_core_module_symbol_t)serialize;
  if (strcmp(name, "retro_unserialize") == 0) return (emuxr_core_module_symbol_t)unserialize;
  return noop;
}
static fake_module *active_module;
static const emuxr_core_module_descriptor_v1 *descriptor_v1(void) {
  return &active_module->descriptor;
}

int main(void) {
  fake_module module;
  emuxr_core_module_loader loader;
  emuxr_core_module_resolver resolver;
  memset(&module, 0, sizeof(module));
  module.descriptor.abi_version = EMUXR_CORE_MODULE_ABI_V1;
  module.descriptor.struct_size = sizeof(module.descriptor);
  module.descriptor.core_id = "mgba-parallax";
  module.descriptor.rgb565_conversion = EMUXR_LIBRETRO_RGB565_MGBA_GBA555;
  active_module = &module;
  resolver.open = fake_open;
  resolver.symbol = fake_symbol;
  resolver.close = fake_close;
  resolver.opaque = &module;
  emuxr_core_module_loader_init(&loader, &resolver);

  CHECK(emuxr_core_module_loader_load(&loader, "missing.so", NULL) ==
        EMUXR_CORE_MODULE_LOAD_OPEN_FAILED);
  module.missing = "retro_run";
  CHECK(emuxr_core_module_loader_load(&loader, "module.so", NULL) ==
        EMUXR_CORE_MODULE_LOAD_REQUIRED_SYMBOL_MISSING);
  CHECK(module.closes == 1);

  module.missing = NULL;
  module.descriptor_enabled = 1;
  CHECK(emuxr_core_module_loader_load(&loader, "module.so", "mgba-parallax") ==
        EMUXR_CORE_MODULE_LOAD_OK);
  CHECK(emuxr_core_module_loader_api(&loader) != NULL);
  CHECK(emuxr_core_module_loader_api(&loader)->rgb565_conversion ==
        EMUXR_LIBRETRO_RGB565_MGBA_GBA555);
  CHECK(emuxr_core_module_loader_descriptor(&loader) == &module.descriptor);
  emuxr_core_module_loader_set_session_active(&loader, true);
  CHECK(emuxr_core_module_loader_unload(&loader) == EMUXR_CORE_MODULE_LOAD_SESSION_ACTIVE);
  emuxr_core_module_loader_set_session_active(&loader, false);
  CHECK(emuxr_core_module_loader_unload(&loader) == EMUXR_CORE_MODULE_LOAD_OK);
  CHECK(emuxr_core_module_loader_api(&loader) == NULL);

  module.descriptor_enabled = 0;
  CHECK(emuxr_core_module_loader_load(&loader, "module.so", "mgba-parallax") ==
        EMUXR_CORE_MODULE_LOAD_CORE_ID_MISMATCH);
  CHECK(emuxr_core_module_loader_load(&loader, "module.so", NULL) ==
        EMUXR_CORE_MODULE_LOAD_OK);
  CHECK(emuxr_core_module_loader_descriptor(&loader) == NULL);
  CHECK(emuxr_core_module_loader_unload(&loader) == EMUXR_CORE_MODULE_LOAD_OK);

  module.descriptor_enabled = 1;
  module.descriptor.abi_version = 2U;
  CHECK(emuxr_core_module_loader_load(&loader, "module.so", NULL) ==
        EMUXR_CORE_MODULE_LOAD_DESCRIPTOR_INVALID);
  return 0;
}

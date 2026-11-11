#include "emuxr_libretro_module_loader.h"

#include <string.h>

static void clear_module(emuxr_core_module_loader *loader) {
  loader->handle = NULL;
  memset(&loader->api, 0, sizeof(loader->api));
  loader->descriptor = NULL;
  loader->session_active = false;
}

/* Symbol addresses originate with dlsym on Android. Copying their object
 * representation into the typed ABI slots keeps the platform-specific cast at
 * the resolver boundary and lets fake resolvers exercise this code on host. */
#define EMUXR_ASSIGN_SYMBOL(destination, address) \
  do { \
    emuxr_core_module_symbol_t emuxr_address_ = (address); \
    if (emuxr_address_ == NULL) return EMUXR_CORE_MODULE_LOAD_REQUIRED_SYMBOL_MISSING; \
    memcpy(&(destination), &emuxr_address_, sizeof(destination)); \
  } while (0)

static emuxr_core_module_load_status resolve_api(
    emuxr_core_module_loader *loader) {
  const emuxr_core_module_resolver *resolver = &loader->resolver;
  EMUXR_ASSIGN_SYMBOL(loader->api.set_environment,
                      resolver->symbol(resolver->opaque, loader->handle, "retro_set_environment"));
  EMUXR_ASSIGN_SYMBOL(loader->api.set_video_refresh,
                      resolver->symbol(resolver->opaque, loader->handle, "retro_set_video_refresh"));
  EMUXR_ASSIGN_SYMBOL(loader->api.set_audio_sample,
                      resolver->symbol(resolver->opaque, loader->handle, "retro_set_audio_sample"));
  EMUXR_ASSIGN_SYMBOL(loader->api.set_audio_sample_batch,
                      resolver->symbol(resolver->opaque, loader->handle, "retro_set_audio_sample_batch"));
  EMUXR_ASSIGN_SYMBOL(loader->api.set_input_poll,
                      resolver->symbol(resolver->opaque, loader->handle, "retro_set_input_poll"));
  EMUXR_ASSIGN_SYMBOL(loader->api.set_input_state,
                      resolver->symbol(resolver->opaque, loader->handle, "retro_set_input_state"));
  EMUXR_ASSIGN_SYMBOL(loader->api.init,
                      resolver->symbol(resolver->opaque, loader->handle, "retro_init"));
  EMUXR_ASSIGN_SYMBOL(loader->api.deinit,
                      resolver->symbol(resolver->opaque, loader->handle, "retro_deinit"));
  EMUXR_ASSIGN_SYMBOL(loader->api.load_game,
                      resolver->symbol(resolver->opaque, loader->handle, "retro_load_game"));
  EMUXR_ASSIGN_SYMBOL(loader->api.unload_game,
                      resolver->symbol(resolver->opaque, loader->handle, "retro_unload_game"));
  EMUXR_ASSIGN_SYMBOL(loader->api.run,
                      resolver->symbol(resolver->opaque, loader->handle, "retro_run"));
  EMUXR_ASSIGN_SYMBOL(loader->api.get_system_av_info,
                      resolver->symbol(resolver->opaque, loader->handle, "retro_get_system_av_info"));
  EMUXR_ASSIGN_SYMBOL(loader->api.serialize_size,
                      resolver->symbol(resolver->opaque, loader->handle, "retro_serialize_size"));
  EMUXR_ASSIGN_SYMBOL(loader->api.serialize,
                      resolver->symbol(resolver->opaque, loader->handle, "retro_serialize"));
  EMUXR_ASSIGN_SYMBOL(loader->api.unserialize,
                      resolver->symbol(resolver->opaque, loader->handle, "retro_unserialize"));
  return EMUXR_CORE_MODULE_LOAD_OK;
}

void emuxr_core_module_loader_init(emuxr_core_module_loader *loader,
                                   const emuxr_core_module_resolver *resolver) {
  if (loader == NULL) return;
  memset(loader, 0, sizeof(*loader));
  if (resolver != NULL) loader->resolver = *resolver;
}

emuxr_core_module_load_status emuxr_core_module_loader_load(
    emuxr_core_module_loader *loader,
    const char *module_path,
    const char *expected_core_id) {
  emuxr_core_module_load_status status;
  emuxr_core_module_descriptor_query_v1 descriptor_query = NULL;
  emuxr_core_module_symbol_t symbol;
  if (loader == NULL || module_path == NULL || module_path[0] == '\0' ||
      loader->resolver.open == NULL || loader->resolver.symbol == NULL ||
      loader->resolver.close == NULL) {
    return EMUXR_CORE_MODULE_LOAD_INVALID_ARGUMENT;
  }
  if (loader->session_active) return EMUXR_CORE_MODULE_LOAD_SESSION_ACTIVE;
  if (loader->handle != NULL) {
    loader->resolver.close(loader->resolver.opaque, loader->handle);
    clear_module(loader);
  }
  loader->handle = loader->resolver.open(loader->resolver.opaque, module_path);
  if (loader->handle == NULL) return EMUXR_CORE_MODULE_LOAD_OPEN_FAILED;
  status = resolve_api(loader);
  if (status != EMUXR_CORE_MODULE_LOAD_OK) goto fail;

  symbol = loader->resolver.symbol(loader->resolver.opaque, loader->handle,
                                   EMUXR_CORE_MODULE_DESCRIPTOR_SYMBOL_V1);
  if (symbol != NULL) {
    memcpy(&descriptor_query, &symbol, sizeof(descriptor_query));
    loader->descriptor = descriptor_query();
    if (loader->descriptor == NULL ||
        loader->descriptor->abi_version != EMUXR_CORE_MODULE_ABI_V1 ||
        loader->descriptor->struct_size < sizeof(*loader->descriptor) ||
        loader->descriptor->core_id == NULL || loader->descriptor->core_id[0] == '\0') {
      status = EMUXR_CORE_MODULE_LOAD_DESCRIPTOR_INVALID;
      goto fail;
    }
    loader->api.rgb565_conversion = loader->descriptor->rgb565_conversion;
  }
  if (expected_core_id != NULL && expected_core_id[0] != '\0' &&
      (loader->descriptor == NULL ||
       strcmp(loader->descriptor->core_id, expected_core_id) != 0)) {
    status = EMUXR_CORE_MODULE_LOAD_CORE_ID_MISMATCH;
    goto fail;
  }
  return EMUXR_CORE_MODULE_LOAD_OK;

fail:
  loader->resolver.close(loader->resolver.opaque, loader->handle);
  clear_module(loader);
  return status;
}

const emuxr_libretro_api *emuxr_core_module_loader_api(
    const emuxr_core_module_loader *loader) {
  return loader != NULL && loader->handle != NULL ? &loader->api : NULL;
}

const emuxr_core_module_descriptor_v1 *emuxr_core_module_loader_descriptor(
    const emuxr_core_module_loader *loader) {
  return loader != NULL && loader->handle != NULL ? loader->descriptor : NULL;
}

void emuxr_core_module_loader_set_session_active(
    emuxr_core_module_loader *loader,
    bool active) {
  if (loader != NULL && loader->handle != NULL) loader->session_active = active;
}

emuxr_core_module_load_status emuxr_core_module_loader_unload(
    emuxr_core_module_loader *loader) {
  if (loader == NULL) return EMUXR_CORE_MODULE_LOAD_INVALID_ARGUMENT;
  if (loader->session_active) return EMUXR_CORE_MODULE_LOAD_SESSION_ACTIVE;
  if (loader->handle != NULL) loader->resolver.close(loader->resolver.opaque, loader->handle);
  clear_module(loader);
  return EMUXR_CORE_MODULE_LOAD_OK;
}

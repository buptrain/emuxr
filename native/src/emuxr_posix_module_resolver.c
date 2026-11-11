#include "emuxr_posix_module_resolver.h"

#include <string.h>

#if defined(__ANDROID__) || defined(__APPLE__) || defined(__unix__)
#include <dlfcn.h>
#define EMUXR_HAS_POSIX_MODULE_RESOLVER 1
#else
#define EMUXR_HAS_POSIX_MODULE_RESOLVER 0
#endif

static void copy_error(emuxr_posix_module_resolver *resolver,
                       const char *message) {
  size_t length;
  if (resolver == NULL) return;
  if (message == NULL) message = "native module resolver failed";
  length = strlen(message);
  if (length >= sizeof(resolver->last_error)) {
    length = sizeof(resolver->last_error) - 1U;
  }
  memcpy(resolver->last_error, message, length);
  resolver->last_error[length] = '\0';
}

#if EMUXR_HAS_POSIX_MODULE_RESOLVER
_Static_assert(sizeof(emuxr_core_module_symbol_t) == sizeof(void *),
               "Android/POSIX dlsym function pointers must fit void pointers");

static void *posix_open(void *opaque, const char *path) {
  emuxr_posix_module_resolver *resolver = (emuxr_posix_module_resolver *)opaque;
  void *handle;
  const char *error;
  (void)dlerror();
  handle = dlopen(path, RTLD_NOW | RTLD_LOCAL);
  error = dlerror();
  if (handle == NULL) copy_error(resolver, error);
  return handle;
}

static emuxr_core_module_symbol_t posix_symbol(void *opaque,
                                                void *handle,
                                                const char *name) {
  emuxr_core_module_symbol_t result = NULL;
  void *address;
  const char *error;
  emuxr_posix_module_resolver *resolver = (emuxr_posix_module_resolver *)opaque;
  (void)dlerror();
  address = dlsym(handle, name);
  error = dlerror();
  if (error != NULL) {
    copy_error(resolver, error);
    return NULL;
  }
  memcpy(&result, &address, sizeof(result));
  return result;
}

static void posix_close(void *opaque, void *handle) {
  emuxr_posix_module_resolver *resolver = (emuxr_posix_module_resolver *)opaque;
  const char *error;
  if (handle == NULL) return;
  (void)dlerror();
  if (dlclose(handle) != 0) {
    error = dlerror();
    copy_error(resolver, error);
  }
}
#else
static void *posix_open(void *opaque, const char *path) {
  (void)path;
  copy_error((emuxr_posix_module_resolver *)opaque,
             "POSIX native module loading is unavailable on this platform");
  return NULL;
}

static emuxr_core_module_symbol_t posix_symbol(void *opaque,
                                                void *handle,
                                                const char *name) {
  (void)handle;
  (void)name;
  copy_error((emuxr_posix_module_resolver *)opaque,
             "POSIX native module loading is unavailable on this platform");
  return NULL;
}

static void posix_close(void *opaque, void *handle) {
  (void)opaque;
  (void)handle;
}
#endif

void emuxr_posix_module_resolver_init(emuxr_posix_module_resolver *resolver,
                                      emuxr_core_module_resolver *out_resolver) {
  if (resolver != NULL) memset(resolver, 0, sizeof(*resolver));
  if (out_resolver == NULL) return;
  memset(out_resolver, 0, sizeof(*out_resolver));
  out_resolver->open = posix_open;
  out_resolver->symbol = posix_symbol;
  out_resolver->close = posix_close;
  out_resolver->opaque = resolver;
}

uint32_t emuxr_posix_module_resolver_copy_last_error(
    const emuxr_posix_module_resolver *resolver,
    char *out_utf8,
    uint32_t out_capacity) {
  size_t length;
  if (out_utf8 == NULL || out_capacity == 0U) return 0U;
  out_utf8[0] = '\0';
  if (resolver == NULL) return 0U;
  length = strlen(resolver->last_error);
  if (length >= (size_t)out_capacity) length = (size_t)out_capacity - 1U;
  memcpy(out_utf8, resolver->last_error, length);
  out_utf8[length] = '\0';
  return (uint32_t)length;
}

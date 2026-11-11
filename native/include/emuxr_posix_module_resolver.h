#ifndef EMUXR_POSIX_MODULE_RESOLVER_H
#define EMUXR_POSIX_MODULE_RESOLVER_H

/* Android/POSIX resolver for modules already installed in the application's
 * native-library search path. It deliberately has no filesystem-copy or
 * persistent-storage behavior. */

#include "emuxr_libretro_module_loader.h"

#ifdef __cplusplus
extern "C" {
#endif

enum { EMUXR_POSIX_MODULE_RESOLVER_ERROR_CAPACITY = 256U };

typedef struct emuxr_posix_module_resolver {
  char last_error[EMUXR_POSIX_MODULE_RESOLVER_ERROR_CAPACITY];
} emuxr_posix_module_resolver;

void emuxr_posix_module_resolver_init(emuxr_posix_module_resolver *resolver,
                                      emuxr_core_module_resolver *out_resolver);
uint32_t emuxr_posix_module_resolver_copy_last_error(
    const emuxr_posix_module_resolver *resolver,
    char *out_utf8,
    uint32_t out_capacity);

#ifdef __cplusplus
}
#endif

#endif

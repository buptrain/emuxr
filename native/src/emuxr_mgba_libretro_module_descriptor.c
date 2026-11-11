#include "emuxr_libretro_module_loader.h"

/* This optional symbol is compiled into the separately named mGBA module
 * only. It describes conversion metadata to the fixed host and intentionally
 * exports neither a bridge context nor an adapter lifecycle. */
const emuxr_core_module_descriptor_v1 *emuxr_core_module_get_descriptor_v1(void) {
  static const emuxr_core_module_descriptor_v1 descriptor = {
      EMUXR_CORE_MODULE_ABI_V1,
      sizeof(emuxr_core_module_descriptor_v1),
      "mgba-parallax",
      EMUXR_LIBRETRO_RGB565_MGBA_GBA555};
  return &descriptor;
}

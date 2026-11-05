#include "gba_xr_bridge.h"

#include <stdio.h>
#include <string.h>

#if defined(_WIN32)
  #include <windows.h>
#else
  #include <pthread.h>
#endif

#define CHECK(expr) do { if (!(expr)) { fprintf(stderr, "failed: %s (%d)\n", #expr, __LINE__); return 1; } } while (0)

typedef struct thread_probe {
  gbaxr_context *context;
  gbaxr_image_view image;
  gbaxr_status acquire_status;
  gbaxr_status publish_status;
} thread_probe;

#if defined(_WIN32)
static DWORD WINAPI wrong_thread_attempt(LPVOID opaque) {
  thread_probe *probe = (thread_probe *)opaque;
  gbaxr_frame_lease lease;
  probe->acquire_status = gbaxr_acquire_latest_frame(probe->context, &lease);
  probe->publish_status = gbaxr_publish_resolved_rgba(probe->context, &probe->image);
  return 0U;
}
#else
static void *wrong_thread_attempt(void *opaque) {
  thread_probe *probe = (thread_probe *)opaque;
  gbaxr_frame_lease lease;
  probe->acquire_status = gbaxr_acquire_latest_frame(probe->context, &lease);
  probe->publish_status = gbaxr_publish_resolved_rgba(probe->context, &probe->image);
  return NULL;
}
#endif

/* An unbound context must adopt its producer from the native emulator worker,
 * while its consumer remains the Unity/render thread. */
#if defined(_WIN32)
static DWORD WINAPI publish_once(LPVOID opaque) {
  thread_probe *probe = (thread_probe *)opaque;
  probe->publish_status = gbaxr_publish_resolved_rgba(probe->context, &probe->image);
  return 0U;
}
#else
static void *publish_once(void *opaque) {
  thread_probe *probe = (thread_probe *)opaque;
  probe->publish_status = gbaxr_publish_resolved_rgba(probe->context, &probe->image);
  return NULL;
}
#endif

int main(void) {
  static uint8_t pixels[GBAXR_GBA_WIDTH * GBAXR_GBA_HEIGHT * 4U];
  static gbaxr_pixel_provenance provenance[GBAXR_GBA_WIDTH * GBAXR_GBA_HEIGHT];
  gbaxr_context *context = gbaxr_create(GBAXR_ABI_VERSION);
  gbaxr_image_view image = { pixels, GBAXR_GBA_WIDTH, GBAXR_GBA_HEIGHT, GBAXR_GBA_WIDTH * 4U };
  gbaxr_frame_lease lease;
  thread_probe probe;
  uint32_t frame;

  CHECK(context != NULL);
  CHECK(gbaxr_create(GBAXR_ABI_VERSION + 1U) == NULL);
  CHECK(gbaxr_step_frame(context) == GBAXR_STATE_ERROR);

  for (frame = 1U; frame <= 5U; ++frame) {
    memset(pixels, (int)frame, sizeof(pixels));
    CHECK(gbaxr_publish_resolved_rgba(context, &image) == GBAXR_OK);
  }
  CHECK(gbaxr_acquire_latest_frame(context, &lease) == GBAXR_OK);
  CHECK(lease.sequence == 5U);
  CHECK(lease.resolved_rgba.rgba8[0] == 5U);
  CHECK(gbaxr_acquire_latest_frame(context, &lease) == GBAXR_STATE_ERROR);
  CHECK(gbaxr_release_frame(context, &lease) == GBAXR_OK);
  CHECK(gbaxr_release_frame(context, &lease) == GBAXR_STATE_ERROR);

  probe.context = context;
  probe.image = image;
  probe.acquire_status = GBAXR_OK;
  probe.publish_status = GBAXR_OK;
#if defined(_WIN32)
  {
    HANDLE worker = CreateThread(NULL, 0U, wrong_thread_attempt, &probe, 0U, NULL);
    CHECK(worker != NULL);
    CHECK(WaitForSingleObject(worker, INFINITE) == WAIT_OBJECT_0);
    CHECK(CloseHandle(worker) != 0);
  }
#else
  {
    pthread_t worker;
    CHECK(pthread_create(&worker, NULL, wrong_thread_attempt, &probe) == 0);
    CHECK(pthread_join(worker, NULL) == 0);
  }
#endif
  CHECK(probe.acquire_status == GBAXR_WRONG_THREAD);
  CHECK(probe.publish_status == GBAXR_WRONG_THREAD);
  gbaxr_destroy(context);

  context = gbaxr_create_unbound(GBAXR_ABI_VERSION);
  CHECK(context != NULL);
  probe.context = context;
  probe.image = image;
  probe.publish_status = GBAXR_STATE_ERROR;
#if defined(_WIN32)
  {
    HANDLE worker = CreateThread(NULL, 0U, publish_once, &probe, 0U, NULL);
    CHECK(worker != NULL);
    CHECK(WaitForSingleObject(worker, INFINITE) == WAIT_OBJECT_0);
    CHECK(CloseHandle(worker) != 0);
  }
#else
  {
    pthread_t worker;
    CHECK(pthread_create(&worker, NULL, publish_once, &probe) == 0);
    CHECK(pthread_join(worker, NULL) == 0);
  }
#endif
  CHECK(probe.publish_status == GBAXR_OK);
  CHECK(gbaxr_acquire_latest_frame(context, &lease) == GBAXR_OK);
  CHECK(gbaxr_release_frame(context, &lease) == GBAXR_OK);
  gbaxr_destroy(context);

  context = gbaxr_create(GBAXR_ABI_VERSION);
  CHECK(context != NULL);
  provenance[0].source = GBAXR_SOURCE_BG2;
  provenance[0].priority = 1U;
  provenance[0].blend_effect = GBAXR_BLEND_ALPHA;
  provenance[0].window = 2U;
  CHECK(gbaxr_publish_frame(context, &image, provenance,
                            GBAXR_GBA_WIDTH * (uint32_t)sizeof(*provenance)) == GBAXR_OK);
  CHECK(gbaxr_acquire_latest_frame(context, &lease) == GBAXR_OK);
  CHECK(lease.provenance != NULL);
  CHECK(lease.provenance_stride_bytes == GBAXR_GBA_WIDTH * sizeof(*provenance));
  CHECK(((const gbaxr_pixel_provenance *)lease.provenance)[0].source == GBAXR_SOURCE_BG2);
  CHECK(((const gbaxr_pixel_provenance *)lease.provenance)[0].blend_effect == GBAXR_BLEND_ALPHA);
  CHECK(gbaxr_release_frame(context, &lease) == GBAXR_OK);
  gbaxr_destroy(context);
  puts("bridge mailbox tests passed");
  return 0;
}

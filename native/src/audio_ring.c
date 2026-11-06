#include "gba_xr_audio_ring.h"
#include <stdatomic.h>
#include <stdlib.h>

struct gbaxr_audio_ring { int16_t *samples; uint32_t capacity; atomic_uint read; atomic_uint write; };
gbaxr_audio_ring *gbaxr_audio_ring_create(uint32_t capacity_frames) {
  gbaxr_audio_ring *r = capacity_frames ? calloc(1, sizeof(*r)) : NULL;
  if (!r) return NULL; r->samples = calloc((size_t)capacity_frames * 2U, sizeof(*r->samples));
  if (!r->samples) { free(r); return NULL; } r->capacity = capacity_frames; atomic_init(&r->read, 0U); atomic_init(&r->write, 0U); return r;
}
void gbaxr_audio_ring_destroy(gbaxr_audio_ring *r) { if (r) { free(r->samples); free(r); } }
uint32_t gbaxr_audio_ring_available(const gbaxr_audio_ring *r) { uint32_t w=atomic_load(&r->write), q=atomic_load(&r->read); return w-q; }
uint32_t gbaxr_audio_ring_write(gbaxr_audio_ring *r, const int16_t *s, uint32_t n) { uint32_t i; if (!r || !s) return 0; for(i=0;i<n;i++){uint32_t w=atomic_load(&r->write), q=atomic_load(&r->read); if(w-q==r->capacity) atomic_store(&r->read,q+1U); r->samples[(w%r->capacity)*2]=s[i*2]; r->samples[(w%r->capacity)*2+1]=s[i*2+1]; atomic_store(&r->write,w+1U);} return n; }
uint32_t gbaxr_audio_ring_read(gbaxr_audio_ring *r, int16_t *s, uint32_t n) { uint32_t i=0; if(!r||!s)return 0; for(;i<n;i++){uint32_t q=atomic_load(&r->read),w=atomic_load(&r->write);if(q==w)break;s[i*2]=r->samples[(q%r->capacity)*2];s[i*2+1]=r->samples[(q%r->capacity)*2+1];atomic_store(&r->read,q+1U);}return i; }

# Experimental mGBA-libretro Android plug-ins

This folder contains exactly one Android ARM64 dependency pair:

1. `mgba_libretro.so` — the opt-in mGBA libretro core, preloaded by Unity.
2. `libemuxr_mgba_libretro.so` — the isolated adapter imported by the future
   `GbaLibretroNativeBindings` P/Invoke declaration.

The adapter ELF records `mgba_libretro.so` as a `DT_NEEDED` dependency. Its
load correctness therefore comes from Android's dynamic linker, not a
Unity-plugin enumeration order; preloading only makes the dependency available
before the first future P/Invoke call.

This pair is **experimental packaging only**. It does not change
`GbaNativeFrameDriver`, `libgba_xr.so`, console selection, or default core
selection. The managed policy remains fail-closed until independent
resolved-frame, input, audio, save-state, and Galaxy XR device certification
are recorded.

Use `native/tools/verify_unity_libretro_package.ps1` after every rebuild. The
machine-readable artifact inventory is `package-manifest.json`.

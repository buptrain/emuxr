using System;
using System.Runtime.InteropServices;

namespace GbaXr
{
    /// <summary>
    /// Opt-in binding declaration for the isolated mGBA-libretro adapter.
    /// It is reached only through the certified backend session; packaging or
    /// this declaration alone never changes the direct-mGBA default.
    /// </summary>
    internal static class GbaLibretroNativeBindings
    {
        // Keep this distinct from GbaNativeBindings' direct "gba_xr" import.
        internal const string LibraryName = "emuxr_mgba_libretro";

        internal const int Ok = 0;
        internal const int NoFrame = 1;

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "emuxr_mgba_libretro_adapter_create")]
        internal static extern IntPtr Create(uint requestedAbiVersion);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "emuxr_mgba_libretro_adapter_start")]
        internal static extern int Start(
            IntPtr adapter,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string utf8RomPath,
            uint framesPerSecond);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "emuxr_mgba_libretro_adapter_state")]
        internal static extern int State(IntPtr adapter);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "emuxr_mgba_libretro_adapter_set_joypad_mask")]
        internal static extern void SetJoypadMask(IntPtr adapter, ushort mask);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "emuxr_mgba_libretro_adapter_read_audio_pcm_s16")]
        internal static extern uint ReadAudioPcmS16(
            IntPtr adapter,
            IntPtr stereoInterleaved,
            uint maxFrames);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "emuxr_mgba_libretro_adapter_audio_available_frames")]
        internal static extern uint AudioAvailableFrames(IntPtr adapter);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "emuxr_mgba_libretro_adapter_audio_sample_rate")]
        internal static extern uint AudioSampleRate(IntPtr adapter);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "emuxr_mgba_libretro_adapter_source_audio_sample_rate")]
        internal static extern uint SourceAudioSampleRate(IntPtr adapter);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "emuxr_mgba_libretro_adapter_audio_dropped_frames")]
        internal static extern ulong AudioDroppedFrames(IntPtr adapter);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "emuxr_mgba_libretro_adapter_save_state")]
        internal static extern int SaveState(
            IntPtr adapter,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string utf8StatePath);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "emuxr_mgba_libretro_adapter_load_state")]
        internal static extern int LoadState(
            IntPtr adapter,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string utf8StatePath);

        // These are adapter facade exports, never gbaxr_* bridge exports.
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "emuxr_mgba_libretro_adapter_acquire_latest_frame")]
        internal static extern int AcquireLatestFrame(
            IntPtr adapter,
            out GbaNativeBindings.FrameLease lease);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "emuxr_mgba_libretro_adapter_release_frame")]
        internal static extern int ReleaseFrame(
            IntPtr adapter,
            ref GbaNativeBindings.FrameLease lease);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "emuxr_mgba_libretro_adapter_stop")]
        internal static extern void Stop(IntPtr adapter);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "emuxr_mgba_libretro_adapter_destroy")]
        internal static extern void Destroy(IntPtr adapter);
    }

    /// <summary>
    /// The optional adapter and its mGBA-libretro dependency are packaged as
    /// reviewed Android ARM64 experimental plug-ins. Packaging alone never
    /// makes the route launchable: all parity gates and device certification
    /// remain separately fail-closed.
    /// </summary>
    internal static class GbaLibretroUnityPackaging
    {
        // Keep this as a property rather than a const: certification probes
        // must retain their missing-package fallback in compiled players.
        internal static bool IsAdapterPackaged => true;
        internal const bool IsDeviceCertified = false;
    }
}

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace GbaXr
{
    /// <summary>
    /// Native-only fixed-host ABI. This remains separate from both the direct
    /// gba_xr bridge and the legacy static libretro-adapter imports.
    /// </summary>
    internal static class GbaCoreModuleNativeBindings
    {
        internal const string LibraryName = "emuxr_core_host";
        internal const int Ok = 0;
        internal const int NoFrame = 1;
        internal const int Running = 1;

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "emuxr_core_host_create")]
        internal static extern IntPtr Create(uint requestedBridgeAbiVersion);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "emuxr_core_host_start_module")]
        internal static extern int StartModule(
            IntPtr host,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string moduleSoname,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string expectedCoreId,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string utf8RomPath,
            uint framesPerSecond);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "emuxr_core_host_state")]
        internal static extern int State(IntPtr host);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "emuxr_core_host_last_module_status")]
        internal static extern int LastModuleStatus(IntPtr host);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "emuxr_core_host_copy_last_module_error")]
        internal static extern uint CopyLastModuleError(
            IntPtr host,
            [Out] StringBuilder output,
            uint outputCapacity);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "emuxr_core_host_acquire_latest_frame")]
        internal static extern int AcquireLatestFrame(
            IntPtr host,
            out GbaNativeBindings.FrameLease lease);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "emuxr_core_host_release_frame")]
        internal static extern int ReleaseFrame(
            IntPtr host,
            ref GbaNativeBindings.FrameLease lease);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "emuxr_core_host_set_joypad_mask")]
        internal static extern void SetJoypadMask(IntPtr host, ushort mask);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "emuxr_core_host_audio_sample_rate")]
        internal static extern uint AudioSampleRate(IntPtr host);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "emuxr_core_host_audio_available_frames")]
        internal static extern uint AudioAvailableFrames(IntPtr host);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "emuxr_core_host_save_state")]
        internal static extern int SaveState(
            IntPtr host,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string utf8StatePath);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "emuxr_core_host_load_state")]
        internal static extern int LoadState(
            IntPtr host,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string utf8StatePath);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "emuxr_core_host_destroy")]
        internal static extern void Destroy(IntPtr host);
    }

    /// <summary>Packaging alone never changes current driver selection.</summary>
    internal static class GbaCoreModuleUnityPackaging
    {
        internal const bool IsHostPackagePresent = true;
    }
}

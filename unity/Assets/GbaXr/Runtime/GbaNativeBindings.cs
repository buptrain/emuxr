using System;
using System.Runtime.InteropServices;

namespace GbaXr
{
    internal static class GbaNativeBindings
    {
        internal const uint AbiVersion = 3;
        internal const int Ok = 0;

        [StructLayout(LayoutKind.Sequential)]
        internal struct ImageView
        {
            internal IntPtr Rgba8;
            internal uint Width;
            internal uint Height;
            internal uint RowStrideBytes;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct FrameLease
        {
            internal ulong Sequence;
            internal ImageView ResolvedRgba;
            internal IntPtr Layers;
            internal uint LayerCount;
            internal uint LayerSemantics;
            internal IntPtr Provenance;
            internal uint ProvenanceStrideBytes;
            internal IntPtr BlendMetadata;
            internal uint BlendMetadataStrideBytes;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PixelProvenance
        {
            internal byte Source;
            internal byte Priority;
            internal byte BlendEffect;
            internal byte Window;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct LayerView
        {
            internal int Source;
            internal ImageView Color;
            internal IntPtr CoverageMask;
            internal uint Priority;
        }

        [DllImport("gba_xr", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr gbaxr_create(uint requestedAbiVersion);

        [DllImport("gba_xr", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr gbaxr_create_unbound(uint requestedAbiVersion);

        [DllImport("gba_xr", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void gbaxr_destroy(IntPtr bridge);

        [DllImport("gba_xr", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr gbaxr_mgba_worker_start(
            IntPtr bridge,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string utf8RomPath,
            uint framesPerSecond);

        [DllImport("gba_xr", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int gbaxr_mgba_worker_state(IntPtr worker);

        [DllImport("gba_xr", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void gbaxr_mgba_worker_set_keys(IntPtr worker, uint keys);

        [DllImport("gba_xr", CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint gbaxr_mgba_worker_read_audio_pcm_s16(
            IntPtr worker,
            IntPtr stereoInterleaved,
            uint maxFrames);

        [DllImport("gba_xr", CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint gbaxr_mgba_worker_audio_available_frames(IntPtr worker);

        [DllImport("gba_xr", CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint gbaxr_mgba_worker_audio_pcm_sample_rate(IntPtr worker);

        [DllImport("gba_xr", CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint gbaxr_mgba_worker_mgba_audio_sample_rate(IntPtr worker);

        [DllImport("gba_xr", CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong gbaxr_mgba_worker_audio_dropped_frames(IntPtr worker);

        [DllImport("gba_xr", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int gbaxr_mgba_worker_save_state(
            IntPtr worker,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string utf8StatePath);

        [DllImport("gba_xr", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int gbaxr_mgba_worker_load_state(
            IntPtr worker,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string utf8StatePath);

        [DllImport("gba_xr", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void gbaxr_mgba_worker_destroy(IntPtr worker);

        [DllImport("gba_xr", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int gbaxr_acquire_latest_frame(IntPtr bridge, out FrameLease lease);

        [DllImport("gba_xr", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int gbaxr_release_frame(IntPtr bridge, ref FrameLease lease);
    }
}

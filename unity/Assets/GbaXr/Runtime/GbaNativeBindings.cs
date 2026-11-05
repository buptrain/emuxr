using System;
using System.Runtime.InteropServices;

namespace GbaXr
{
    internal static class GbaNativeBindings
    {
        internal const uint AbiVersion = 2;
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
        internal static extern void gbaxr_mgba_worker_destroy(IntPtr worker);

        [DllImport("gba_xr", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int gbaxr_acquire_latest_frame(IntPtr bridge, out FrameLease lease);

        [DllImport("gba_xr", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int gbaxr_release_frame(IntPtr bridge, ref FrameLease lease);
    }
}

using System;

namespace GbaXr
{
    internal enum GbaNativeSessionBackend { DirectMgba, LibretroAdapter }

    /// <summary>Translates the project’s direct-mGBA GBA key bits to RetroPad IDs.</summary>
    internal static class GbaLibretroJoypadPolicy
    {
        internal const uint GbaA = 1U << 0;
        internal const uint GbaB = 1U << 1;
        internal const uint GbaSelect = 1U << 2;
        internal const uint GbaStart = 1U << 3;
        internal const uint GbaRight = 1U << 4;
        internal const uint GbaLeft = 1U << 5;
        internal const uint GbaUp = 1U << 6;
        internal const uint GbaDown = 1U << 7;
        internal const uint GbaR = 1U << 8;
        internal const uint GbaL = 1U << 9;

        // mGBA libretro's native RetroPad contract (also used by the parity
        // runner): A/B map to GBA A/B at IDs 8/0, then Select/Start,
        // Up/Down/Left/Right, L/R at their standard IDs.
        internal static ushort TranslateGbaMask(uint gbaMask)
        {
            ushort retroMask = 0;
            if ((gbaMask & GbaA) != 0U) retroMask |= 1 << 8;  // RetroPad A
            if ((gbaMask & GbaB) != 0U) retroMask |= 1 << 0;  // RetroPad B
            if ((gbaMask & GbaSelect) != 0U) retroMask |= 1 << 2;
            if ((gbaMask & GbaStart) != 0U) retroMask |= 1 << 3;
            if ((gbaMask & GbaUp) != 0U) retroMask |= 1 << 4;
            if ((gbaMask & GbaDown) != 0U) retroMask |= 1 << 5;
            if ((gbaMask & GbaLeft) != 0U) retroMask |= 1 << 6;
            if ((gbaMask & GbaRight) != 0U) retroMask |= 1 << 7;
            if ((gbaMask & GbaL) != 0U) retroMask |= 1 << 10;
            if ((gbaMask & GbaR) != 0U) retroMask |= 1 << 11;
            return retroMask;
        }
    }

    /// <summary>Small backend boundary; Unity never mixes direct bridge calls with an adapter handle.</summary>
    internal interface IGbaNativeSession : IDisposable
    {
        GbaCoreKind CoreKind { get; }
        GbaNativeSessionBackend AudioBackend { get; }
        IntPtr AudioHandle { get; }
        int State { get; }
        void SetKeys(uint keys);
        int Acquire(out GbaNativeBindings.FrameLease lease);
        int Release(ref GbaNativeBindings.FrameLease lease);
        uint AudioAvailableFrames { get; }
        uint SourceAudioSampleRate { get; }
        ulong AudioDroppedFrames { get; }
        int SaveState(string path);
        int LoadState(string path);
    }

    internal static class GbaNativeSessionFactory
    {
        internal static bool TryStart(GbaCoreKind core, string path, uint fps, out IGbaNativeSession session, out string error)
        {
            session = null;
            error = null;
            try
            {
                session = core == GbaCoreKind.LibretroResolvedExperimental
                    ? new GbaLibretroAdapterSession(path, fps)
                    : new GbaDirectMgbaSession(path, fps);
                return true;
            }
            catch (Exception exception)
            {
                session?.Dispose();
                session = null;
                error = exception.Message;
                return false;
            }
        }
    }

    internal sealed class GbaDirectMgbaSession : IGbaNativeSession
    {
        private IntPtr _bridge;
        private IntPtr _worker;
        internal GbaDirectMgbaSession(string path, uint fps)
        {
            try { _bridge = GbaNativeBindings.gbaxr_create_unbound(GbaNativeBindings.AbiVersion); if (_bridge == IntPtr.Zero) throw new InvalidOperationException("Could not create direct frame bridge."); _worker = GbaNativeBindings.gbaxr_mgba_worker_start(_bridge, path, fps); if (_worker == IntPtr.Zero) throw new InvalidOperationException("Could not start direct mGBA worker."); }
            catch { Dispose(); throw; }
        }
        public GbaCoreKind CoreKind => GbaCoreKind.DirectMgba;
        public GbaNativeSessionBackend AudioBackend => GbaNativeSessionBackend.DirectMgba;
        public IntPtr AudioHandle => _worker;
        public int State => _worker == IntPtr.Zero ? -1 : GbaNativeBindings.gbaxr_mgba_worker_state(_worker);
        public void SetKeys(uint keys) => GbaNativeBindings.gbaxr_mgba_worker_set_keys(_worker, keys);
        public int Acquire(out GbaNativeBindings.FrameLease lease) => GbaNativeBindings.gbaxr_acquire_latest_frame(_bridge, out lease);
        public int Release(ref GbaNativeBindings.FrameLease lease) => GbaNativeBindings.gbaxr_release_frame(_bridge, ref lease);
        public uint AudioAvailableFrames => GbaNativeBindings.gbaxr_mgba_worker_audio_available_frames(_worker);
        public uint SourceAudioSampleRate => GbaNativeBindings.gbaxr_mgba_worker_mgba_audio_sample_rate(_worker);
        public ulong AudioDroppedFrames => GbaNativeBindings.gbaxr_mgba_worker_audio_dropped_frames(_worker);
        public int SaveState(string path) => GbaNativeBindings.gbaxr_mgba_worker_save_state(_worker, path);
        public int LoadState(string path) => GbaNativeBindings.gbaxr_mgba_worker_load_state(_worker, path);
        public void Dispose() { if (_worker != IntPtr.Zero) { GbaNativeBindings.gbaxr_mgba_worker_destroy(_worker); _worker = IntPtr.Zero; } if (_bridge != IntPtr.Zero) { GbaNativeBindings.gbaxr_destroy(_bridge); _bridge = IntPtr.Zero; } }
    }

    internal sealed class GbaLibretroAdapterSession : IGbaNativeSession
    {
        private IntPtr _adapter;
        internal GbaLibretroAdapterSession(string path, uint fps)
        {
            try { _adapter = GbaLibretroNativeBindings.Create(GbaNativeBindings.AbiVersion); if (_adapter == IntPtr.Zero) throw new InvalidOperationException("Could not create isolated libretro adapter."); if (GbaLibretroNativeBindings.Start(_adapter, path, fps) != GbaLibretroNativeBindings.Ok) throw new InvalidOperationException("Could not start isolated libretro adapter."); }
            catch { Dispose(); throw; }
        }
        public GbaCoreKind CoreKind => GbaCoreKind.LibretroResolvedExperimental;
        public GbaNativeSessionBackend AudioBackend => GbaNativeSessionBackend.LibretroAdapter;
        public IntPtr AudioHandle => _adapter;
        public int State => _adapter == IntPtr.Zero ? -1 : GbaLibretroNativeBindings.State(_adapter);
        public void SetKeys(uint keys) => GbaLibretroNativeBindings.SetJoypadMask(
            _adapter,
            GbaLibretroJoypadPolicy.TranslateGbaMask(keys));
        public int Acquire(out GbaNativeBindings.FrameLease lease) => GbaLibretroNativeBindings.AcquireLatestFrame(_adapter, out lease);
        public int Release(ref GbaNativeBindings.FrameLease lease) => GbaLibretroNativeBindings.ReleaseFrame(_adapter, ref lease);
        public uint AudioAvailableFrames => GbaLibretroNativeBindings.AudioAvailableFrames(_adapter);
        public uint SourceAudioSampleRate => GbaLibretroNativeBindings.SourceAudioSampleRate(_adapter);
        public ulong AudioDroppedFrames => GbaLibretroNativeBindings.AudioDroppedFrames(_adapter);
        public int SaveState(string path) => GbaLibretroNativeBindings.SaveState(_adapter, path);
        public int LoadState(string path) => GbaLibretroNativeBindings.LoadState(_adapter, path);
        public void Dispose() { if (_adapter != IntPtr.Zero) { GbaLibretroNativeBindings.Destroy(_adapter); _adapter = IntPtr.Zero; } }
    }
}

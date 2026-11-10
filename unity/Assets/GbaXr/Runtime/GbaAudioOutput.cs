using System;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

namespace GbaXr
{
    internal enum GbaAudioFaultKind
    {
        None = 0,
        UnityOutputDormant = 1,
        NativePcmStarvation = 2,
        NativePcmReadStalled = 3
    }

    /// <summary>Main-thread snapshot only; never read from Unity's audio callback.</summary>
    internal readonly struct GbaAudioOutputTelemetry
    {
        internal GbaAudioOutputTelemetry(
            bool sourceEnabled,
            bool sourceActive,
            bool sourcePlaying,
            bool streamingClipAttached,
            int outputSampleRate,
            int dspBufferSize,
            ulong callbackCount,
            ulong nativePcmFramesRequested,
            ulong nativePcmFramesPulled,
            ulong nativePcmEmptyReads,
            ulong nonSilentPcmFramesPulled)
        {
            SourceEnabled = sourceEnabled;
            SourceActive = sourceActive;
            SourcePlaying = sourcePlaying;
            StreamingClipAttached = streamingClipAttached;
            OutputSampleRate = outputSampleRate;
            DspBufferSize = dspBufferSize;
            CallbackCount = callbackCount;
            NativePcmFramesRequested = nativePcmFramesRequested;
            NativePcmFramesPulled = nativePcmFramesPulled;
            NativePcmEmptyReads = nativePcmEmptyReads;
            NonSilentPcmFramesPulled = nonSilentPcmFramesPulled;
        }

        internal bool SourceEnabled { get; }
        internal bool SourceActive { get; }
        internal bool SourcePlaying { get; }
        internal bool StreamingClipAttached { get; }
        internal int OutputSampleRate { get; }
        internal int DspBufferSize { get; }
        internal ulong CallbackCount { get; }
        /// <summary>Frames requested from the native ring, including scratch prefetch.</summary>
        internal ulong NativePcmFramesRequested { get; }
        internal ulong NativePcmFramesPulled { get; }
        internal ulong NativePcmEmptyReads { get; }
        internal ulong NonSilentPcmFramesPulled { get; }
    }

    internal static class GbaAudioFaultPolicy
    {
        internal static GbaAudioFaultKind Classify(
            GbaAudioOutputTelemetry telemetry,
            ulong callbackDelta,
            ulong pcmFramesRequestedDelta,
            ulong pcmFramesPulledDelta,
            uint nativeAvailableFrames)
        {
            if (!telemetry.SourceEnabled || !telemetry.SourceActive ||
                !telemetry.StreamingClipAttached || !telemetry.SourcePlaying || callbackDelta == 0U)
            {
                return GbaAudioFaultKind.UnityOutputDormant;
            }
            if (pcmFramesPulledDelta != 0U)
            {
                return GbaAudioFaultKind.None;
            }

            // A non-empty native ring must produce samples for a managed read.
            // This identifies an ABI/read-path problem separately from a normal
            // producer underrun, where the ring is actually empty.
            return pcmFramesRequestedDelta != 0U && nativeAvailableFrames != 0U
                ? GbaAudioFaultKind.NativePcmReadStalled
                : GbaAudioFaultKind.NativePcmStarvation;
        }
    }

    /// <summary>Pure PCM conversion and channel-routing rules for GBA audio.</summary>
    internal static class GbaAudioPcmPolicy
    {
        internal static float Int16ToFloat(short sample) => sample / 32768f;

        internal static void WriteStereoFrame(float[] output, int offset, int channels, float left, float right)
        {
            for (var channel = 0; channel < channels; channel++)
            {
                output[offset + channel] = 0f;
            }

            if (channels == 1)
            {
                output[offset] = (left + right) * 0.5f;
            }
            else if (channels >= 2)
            {
                output[offset] = left;
                output[offset + 1] = right;
            }
        }
    }

    /// <summary>Pure source-rate math used by Unity's audio-thread resampler.</summary>
    internal static class GbaAudioResamplePolicy
    {
        internal static double NativeFramesPerOutputFrame(int nativeSampleRate, int outputSampleRate)
        {
            return outputSampleRate > 0
                ? (double)nativeSampleRate / outputSampleRate
                : 1d;
        }

        internal static float Lerp(float first, float second, double fraction)
        {
            return first + (float)((second - first) * fraction);
        }
    }

    /// <summary>
    /// Pulls worker-owned, 48 kHz stereo PCM into Unity's direct audio-filter
    /// callback. A long silent clip keeps the 2D AudioSource alive, but never
    /// supplies emulation PCM. The callback performs no Unity API calls,
    /// locks, logging, or managed allocations.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class GbaAudioOutput : MonoBehaviour
    {
        private const int ScratchFrames = 4096;
        private const int StereoChannels = 2;
        private const int FilterHostClipFrames = 65536;
        private const int NativePcmSampleRate = 48000;

        private readonly short[] _pcmScratch = new short[ScratchFrames * StereoChannels];
        private AudioSource _audioSource;
        private AudioClip _filterHostClip;
        private GCHandle _pinnedScratch;
        private IntPtr _pinnedScratchPointer;
        private long _workerHandle;
        private int _callbacksInFlight;
        private long _underflowFrames;
        private long _streamingCallbackCount;
        private long _nativePcmFramesRequested;
        private long _nativePcmFramesPulled;
        private long _nativePcmEmptyReads;
        private long _nonSilentPcmFramesPulled;
        private int _scratchFramesAvailable;
        private int _scratchFrameIndex;
        // Written on Unity's main thread only and read atomically by the audio
        // thread. Never call AudioSettings from OnAudioFilterRead.
        private int _cachedOutputSampleRate = NativePcmSampleRate;
        private int _callbackOutputSampleRate;
        private double _nativeFramesPerOutputFrame = 1d;
        private bool _resamplerPrimed;
        private double _sourcePhase;
        private float _firstLeft;
        private float _firstRight;
        private float _secondLeft;
        private float _secondRight;
        private ulong _lastDroppedFrames;
        private uint _lastMgbaSourceSampleRate;

        private void Awake()
        {
            _pinnedScratch = GCHandle.Alloc(_pcmScratch, GCHandleType.Pinned);
            _pinnedScratchPointer = _pinnedScratch.AddrOfPinnedObject();
            _audioSource = GetComponent<AudioSource>();
            _audioSource.spatialBlend = 0f;
            _audioSource.loop = true;
            _audioSource.mute = false;
            _audioSource.volume = 1f;
            // This is a direct, non-spatial emulator output path. Keep the
            // filter voice independent of scene/listener effects and XR
            // spatialization, which can otherwise alter or silence a 2D clip.
            _audioSource.bypassEffects = true;
            _audioSource.bypassListenerEffects = true;
            _audioSource.bypassReverbZones = true;
            _audioSource.ignoreListenerVolume = true;
            _audioSource.ignoreListenerPause = true;
            _audioSource.spatialize = false;
            _audioSource.pitch = 1f;
            _audioSource.panStereo = 0f;
            _audioSource.dopplerLevel = 0f;
            // Keep the filter source from being virtualized behind unrelated
            // voices; the actual PCM is supplied by OnAudioFilterRead.
            _audioSource.priority = 0;
            // This silent non-streaming clip only keeps Unity's source voice
            // active so OnAudioFilterRead receives the device-channel buffer.
            _filterHostClip = AudioClip.Create(
                "GBA XR Audio Filter Host",
                FilterHostClipFrames,
                StereoChannels,
                48000,
                false);
            _audioSource.clip = _filterHostClip;
            RefreshCachedOutputSampleRate();
        }

        private void OnEnable()
        {
            EnsureStreamingSourcePlaying();
        }

        private void OnDisable()
        {
            DetachWorker();
            if (_audioSource != null)
            {
                _audioSource.Stop();
            }
        }

        private void OnDestroy()
        {
            DetachWorker();
            if (_pinnedScratch.IsAllocated)
            {
                _pinnedScratch.Free();
            }
        }

        internal void AttachWorker(IntPtr worker)
        {
            DetachWorker();
            ResetReadStateAfterCallbacksStopped();
            RefreshCachedOutputSampleRate();
            Interlocked.Exchange(ref _workerHandle, worker.ToInt64());
            EnsureStreamingSourcePlaying();
        }

        private void Update()
        {
            // XR output can resume without a new configuration callback. While
            // a worker is attached, reassert the filter host on the main
            // thread; this is a no-allocation no-op while it is already playing.
            if (Interlocked.Read(ref _workerHandle) != 0L)
            {
                RefreshCachedOutputSampleRate();
                EnsureStreamingSourcePlaying();
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus && Interlocked.Read(ref _workerHandle) != 0L)
            {
                RefreshCachedOutputSampleRate();
                EnsureStreamingSourcePlaying();
            }
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (!pauseStatus && Interlocked.Read(ref _workerHandle) != 0L)
            {
                RefreshCachedOutputSampleRate();
                EnsureStreamingSourcePlaying();
            }
        }

        private void OnAudioConfigurationChanged(bool deviceWasChanged)
        {
            RefreshCachedOutputSampleRate();
            if (Interlocked.Read(ref _workerHandle) != 0L)
            {
                EnsureStreamingSourcePlaying();
            }
        }

        /// <summary>
        /// Stops future callback reads and waits only for an in-flight native
        /// pull before the frame driver destroys the native worker.
        /// </summary>
        internal void DetachWorker()
        {
            Interlocked.Exchange(ref _workerHandle, 0L);
            while (Volatile.Read(ref _callbacksInFlight) != 0)
            {
                Thread.Yield();
            }
            ResetReadStateAfterCallbacksStopped();
        }

        internal void UpdateNativeDiagnostics(uint availableFrames, uint mgbaSourceSampleRate, ulong droppedFrames)
        {
            if (mgbaSourceSampleRate != 0U && mgbaSourceSampleRate != _lastMgbaSourceSampleRate)
            {
                Debug.Log($"GBA XR audio: mGBA mixer is {mgbaSourceSampleRate} Hz; worker resamples stereo PCM to 48000 Hz.");
                _lastMgbaSourceSampleRate = mgbaSourceSampleRate;
            }
            if (droppedFrames != _lastDroppedFrames)
            {
                Debug.LogWarning(
                    $"GBA XR audio: native ring dropped {droppedFrames - _lastDroppedFrames} frame(s); " +
                    $"available={availableFrames}, source={mgbaSourceSampleRate} Hz.");
                _lastDroppedFrames = droppedFrames;
            }
        }

        /// <summary>Monotonic count of Unity output frames left silent by PCM underflow.</summary>
        internal ulong TotalUnderflowFrames => unchecked((ulong)Interlocked.Read(ref _underflowFrames));
        internal ulong TotalStreamingCallbacks => unchecked((ulong)Interlocked.Read(ref _streamingCallbackCount));
        internal ulong TotalNativePcmFramesRequested => unchecked((ulong)Interlocked.Read(ref _nativePcmFramesRequested));
        internal ulong TotalNativePcmFramesPulled => unchecked((ulong)Interlocked.Read(ref _nativePcmFramesPulled));
        internal ulong TotalNativePcmEmptyReads => unchecked((ulong)Interlocked.Read(ref _nativePcmEmptyReads));
        internal ulong TotalNonSilentPcmFramesPulled => unchecked((ulong)Interlocked.Read(ref _nonSilentPcmFramesPulled));

        internal GbaAudioOutputTelemetry GetTelemetry()
        {
            var configuration = AudioSettings.GetConfiguration();
            var sourceEnabled = _audioSource != null && _audioSource.enabled;
            var sourceActive = sourceEnabled && _audioSource.gameObject.activeInHierarchy;
            return new GbaAudioOutputTelemetry(
                sourceEnabled,
                sourceActive,
                sourceActive && _audioSource.isPlaying,
                _audioSource != null && _audioSource.clip == _filterHostClip,
                AudioSettings.outputSampleRate,
                configuration.dspBufferSize,
                TotalStreamingCallbacks,
                TotalNativePcmFramesRequested,
                TotalNativePcmFramesPulled,
                TotalNativePcmEmptyReads,
                TotalNonSilentPcmFramesPulled);
        }

        private void EnsureStreamingSourcePlaying()
        {
            if (_audioSource == null || !isActiveAndEnabled || !gameObject.activeInHierarchy)
            {
                return;
            }
            if (!_audioSource.enabled)
            {
                _audioSource.enabled = true;
            }
            if (_audioSource.clip != _filterHostClip)
            {
                _audioSource.clip = _filterHostClip;
            }
            if (!_audioSource.isPlaying)
            {
                _audioSource.Play();
            }
        }

        /// <summary>
        /// Unity invokes this on its audio thread for the active source. Its
        /// channel count is the current output layout, not the host clip's
        /// stereo layout; native PCM remains stereo and is mixed here.
        /// </summary>
        private void OnAudioFilterRead(float[] data, int channels)
        {
            Array.Clear(data, 0, data.Length);
            Interlocked.Increment(ref _streamingCallbackCount);
            Interlocked.Increment(ref _callbacksInFlight);
            try
            {
                var worker = new IntPtr(Interlocked.Read(ref _workerHandle));
                if (worker == IntPtr.Zero)
                {
                    return;
                }

                if (channels <= 0)
                {
                    return;
                }

                var outputSampleRate = Volatile.Read(ref _cachedOutputSampleRate);
                if (outputSampleRate <= 0)
                {
                    outputSampleRate = NativePcmSampleRate;
                }
                if (outputSampleRate != _callbackOutputSampleRate)
                {
                    // The source ring stays at 48 kHz. Reset only the audio
                    // thread's interpolation state when output format changes.
                    _callbackOutputSampleRate = outputSampleRate;
                    _nativeFramesPerOutputFrame = GbaAudioResamplePolicy.NativeFramesPerOutputFrame(
                        NativePcmSampleRate,
                        outputSampleRate);
                    ResetAudioThreadResamplerState();
                }

                var frameCount = data.Length / channels;
                for (var frame = 0; frame < frameCount; frame++)
                {
                    if (!TryReadResampledFrame(worker, out var left, out var right))
                    {
                        Interlocked.Add(ref _underflowFrames, frameCount - frame);
                        return;
                    }

                    GbaAudioPcmPolicy.WriteStereoFrame(data, frame * channels, channels, left, right);
                }
            }
            finally
            {
                Interlocked.Decrement(ref _callbacksInFlight);
            }
        }

        private bool TryReadResampledFrame(IntPtr worker, out float left, out float right)
        {
            if (!_resamplerPrimed)
            {
                if (!TryReadNativeFrame(worker, out _firstLeft, out _firstRight) ||
                    !TryReadNativeFrame(worker, out _secondLeft, out _secondRight))
                {
                    left = 0f;
                    right = 0f;
                    ResetAudioThreadResamplerState();
                    return false;
                }
                _sourcePhase = 0d;
                _resamplerPrimed = true;
            }

            while (_sourcePhase >= 1d)
            {
                _firstLeft = _secondLeft;
                _firstRight = _secondRight;
                if (!TryReadNativeFrame(worker, out _secondLeft, out _secondRight))
                {
                    left = 0f;
                    right = 0f;
                    ResetAudioThreadResamplerState();
                    return false;
                }
                _sourcePhase -= 1d;
            }

            left = GbaAudioResamplePolicy.Lerp(_firstLeft, _secondLeft, _sourcePhase);
            right = GbaAudioResamplePolicy.Lerp(_firstRight, _secondRight, _sourcePhase);
            _sourcePhase += _nativeFramesPerOutputFrame;
            return true;
        }

        private bool TryReadNativeFrame(IntPtr worker, out float left, out float right)
        {
            if (_scratchFrameIndex >= _scratchFramesAvailable)
            {
                Interlocked.Add(ref _nativePcmFramesRequested, ScratchFrames);
                _scratchFramesAvailable = (int)GbaNativeBindings.gbaxr_mgba_worker_read_audio_pcm_s16(
                    worker,
                    _pinnedScratchPointer,
                    ScratchFrames);
                _scratchFrameIndex = 0;
                Interlocked.Add(ref _nativePcmFramesPulled, _scratchFramesAvailable);
                if (_scratchFramesAvailable == 0)
                {
                    Interlocked.Increment(ref _nativePcmEmptyReads);
                }
                else
                {
                    var nonSilentFrames = 0;
                    for (var frame = 0; frame < _scratchFramesAvailable; frame++)
                    {
                        var sampleOffset = frame * StereoChannels;
                        if (_pcmScratch[sampleOffset] != 0 || _pcmScratch[sampleOffset + 1] != 0)
                        {
                            nonSilentFrames++;
                        }
                    }
                    Interlocked.Add(ref _nonSilentPcmFramesPulled, nonSilentFrames);
                }
            }

            if (_scratchFrameIndex >= _scratchFramesAvailable)
            {
                left = 0f;
                right = 0f;
                return false;
            }

            var offset = _scratchFrameIndex++ * StereoChannels;
            left = GbaAudioPcmPolicy.Int16ToFloat(_pcmScratch[offset]);
            right = GbaAudioPcmPolicy.Int16ToFloat(_pcmScratch[offset + 1]);
            return true;
        }

        private void RefreshCachedOutputSampleRate()
        {
            var outputSampleRate = AudioSettings.outputSampleRate;
            Interlocked.Exchange(
                ref _cachedOutputSampleRate,
                outputSampleRate > 0 ? outputSampleRate : NativePcmSampleRate);
        }

        private void ResetReadStateAfterCallbacksStopped()
        {
            _scratchFramesAvailable = 0;
            _scratchFrameIndex = 0;
            _callbackOutputSampleRate = 0;
            _nativeFramesPerOutputFrame = 1d;
            ResetAudioThreadResamplerState();
        }

        private void ResetAudioThreadResamplerState()
        {
            _resamplerPrimed = false;
            _sourcePhase = 0d;
            _firstLeft = 0f;
            _firstRight = 0f;
            _secondLeft = 0f;
            _secondRight = 0f;
        }
    }
}

using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Networking;

namespace GbaXr
{
    internal enum GbaCoreModuleProbeResult
    {
        Pending,
        Disabled,
        UnsupportedPlatform,
        PackageUnavailable,
        AnotherProbeIsActive,
        FixtureUnavailable,
        FixtureHashMismatch,
        HostCreateFailed,
        ModuleStartFailed,
        WorkerFailed,
        RunningTimeout,
        FrameTimeout,
        LeaseFailed,
        InvalidFrame,
        InputNotObserved,
        AudioRateFailed,
        StateSaveFailed,
        StateLoadFailed,
        StateReplayFailed,
        ReloadFailed,
        NativeLibraryMissing,
        EntryPointMissing,
        NativeCallFailed,
        Success
    }

    /// <summary>Pure policy for the fixed-host module smoke probe.</summary>
    internal static class GbaCoreModuleProbePolicy
    {
        internal const string ModuleSoname = "emuxr_mgba_libretro_core.so";
        internal const string ExpectedCoreId = "mgba-parallax";
        internal const uint FramesPerSecond = 60;
        internal const uint ExpectedAudioSampleRate = 48000;

        internal static GbaCoreModuleProbeResult EvaluatePreflight(
            bool enabled, bool isAndroidPlayer, bool packagePresent, bool anotherProbeIsActive)
        {
            if (!enabled) return GbaCoreModuleProbeResult.Disabled;
            if (!isAndroidPlayer) return GbaCoreModuleProbeResult.UnsupportedPlatform;
            if (!packagePresent) return GbaCoreModuleProbeResult.PackageUnavailable;
            return anotherProbeIsActive
                ? GbaCoreModuleProbeResult.AnotherProbeIsActive
                : GbaCoreModuleProbeResult.Pending;
        }

        internal static bool IsBareModuleSoname(string soname) =>
            !string.IsNullOrEmpty(soname) && soname.EndsWith(".so", StringComparison.Ordinal) &&
            soname.IndexOfAny(new[] { '/', '\\', ':' }) < 0;

        internal static bool HasExpectedAudioRate(uint sampleRate) =>
            sampleRate == ExpectedAudioSampleRate;

        internal static GbaCoreModuleProbeResult ValidateLease(GbaNativeBindings.FrameLease lease) =>
            lease.Sequence != 0 && lease.ResolvedRgba.Rgba8 != IntPtr.Zero &&
            lease.ResolvedRgba.Width == GbaLibretroFixtureProbePolicy.Width &&
            lease.ResolvedRgba.Height == GbaLibretroFixtureProbePolicy.Height &&
            lease.ResolvedRgba.RowStrideBytes == GbaLibretroFixtureProbePolicy.RgbaRowStrideBytes
                ? GbaCoreModuleProbeResult.Pending
                : GbaCoreModuleProbeResult.InvalidFrame;

        internal static GbaCoreModuleProbeResult FromException(Exception exception) =>
            exception switch
            {
                DllNotFoundException _ => GbaCoreModuleProbeResult.NativeLibraryMissing,
                EntryPointNotFoundException _ => GbaCoreModuleProbeResult.EntryPointMissing,
                _ => GbaCoreModuleProbeResult.NativeCallFailed
            };

        internal static string Describe(GbaCoreModuleProbeResult result) => result switch
        {
            GbaCoreModuleProbeResult.Disabled => "disabled by the serialized opt-in toggle",
            GbaCoreModuleProbeResult.UnsupportedPlatform => "not an Android player build",
            GbaCoreModuleProbeResult.PackageUnavailable => "the fixed-host module package is unavailable",
            GbaCoreModuleProbeResult.AnotherProbeIsActive => "another isolated libretro probe is active",
            GbaCoreModuleProbeResult.FixtureUnavailable => "the generated fixture could not be staged",
            GbaCoreModuleProbeResult.FixtureHashMismatch => "the staged fixture SHA-256 did not match",
            GbaCoreModuleProbeResult.HostCreateFailed => "fixed host create returned null",
            GbaCoreModuleProbeResult.ModuleStartFailed => "installed core module start failed",
            GbaCoreModuleProbeResult.WorkerFailed => "the core worker entered a failed state",
            GbaCoreModuleProbeResult.RunningTimeout => "the core worker did not reach running before the deadline",
            GbaCoreModuleProbeResult.FrameTimeout => "the running core did not publish a frame before the deadline",
            GbaCoreModuleProbeResult.LeaseFailed => "frame lease acquire or release failed",
            GbaCoreModuleProbeResult.InvalidFrame => "the acquired frame was not a 240x160 RGBA8 lease",
            GbaCoreModuleProbeResult.InputNotObserved => "the input fixture did not produce a stable observable A-button transition",
            GbaCoreModuleProbeResult.AudioRateFailed => "the host did not expose 48 kHz output PCM",
            GbaCoreModuleProbeResult.StateSaveFailed => "worker-owned state save failed",
            GbaCoreModuleProbeResult.StateLoadFailed => "worker-owned state load failed",
            GbaCoreModuleProbeResult.StateReplayFailed => "state replay did not reproduce the advanced frame hash",
            GbaCoreModuleProbeResult.ReloadFailed => "host destroy/recreate module reload failed",
            GbaCoreModuleProbeResult.NativeLibraryMissing => "the fixed host or installed core module could not be loaded",
            GbaCoreModuleProbeResult.EntryPointMissing => "the fixed-host ABI entry point was unavailable",
            GbaCoreModuleProbeResult.NativeCallFailed => "an unexpected native interop exception occurred",
            GbaCoreModuleProbeResult.Success => "observable input, non-empty 48 kHz PCM, state replay, and destroy/reload completed",
            _ => "probe has not started"
        };
    }

    /// <summary>
    /// Default-off physical probe for the future installed-module route. It is
    /// deliberately not part of the driver, scene, certification store, or
    /// core-selection flow, and accepts only the generated input_state_replay ROM.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GbaCoreModuleProbe : MonoBehaviour
    {
        private const float DefaultTimeoutSeconds = 10f;

        [SerializeField]
        [Tooltip("Default off. Runs the installed-module probe with only generated input_state_replay.gba.")]
        private bool runOnAndroidAwake;

        [SerializeField, Min(1f)]
        private float timeoutSeconds = DefaultTimeoutSeconds;

        private IntPtr _host;
        private bool _ownsProbeGate;
        private bool _attempted;
        private GbaCoreModuleProbeResult _result;
        private readonly byte[] _frameCopy = new byte[240 * 160 * 4];
        private ulong _lastSequence;
        private uint _lastFrameHash;

        private void Awake()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            TryStart(isAndroidPlayer: true);
#else
            TryStart(isAndroidPlayer: false);
#endif
        }

        private void OnDestroy()
        {
            DestroyHost();
            ExitProbeGate();
        }

        private void TryStart(bool isAndroidPlayer)
        {
            if (_attempted) return;
            var preflight = GbaCoreModuleProbePolicy.EvaluatePreflight(
                runOnAndroidAwake, isAndroidPlayer,
                GbaCoreModuleUnityPackaging.IsHostPackagePresent,
                GbaLibretroAdapterProbeGate.IsActive);
            if (preflight != GbaCoreModuleProbeResult.Pending)
            {
                if (runOnAndroidAwake)
                    Debug.LogWarning($"GBA XR core module probe: skipped; {GbaCoreModuleProbePolicy.Describe(preflight)}.");
                return;
            }

            _attempted = true;
            _ownsProbeGate = GbaLibretroAdapterProbeGate.TryEnter();
            if (!_ownsProbeGate)
            {
                Debug.LogWarning("GBA XR core module probe: skipped; another isolated libretro probe is active.");
                return;
            }
            StartCoroutine(RunProbe());
        }

        private IEnumerator RunProbe()
        {
            _result = GbaCoreModuleProbeResult.NativeCallFailed;
            string detail = null;
            try
            {
                var fixture = GbaLibretroCertificationBatchPolicy.InputStateReplayFixture;
                var fixturePath = Path.Combine(Application.persistentDataPath,
                    "gba-core-module-probe", "input_state_replay.gba");
                yield return StageFixture(fixture, fixturePath);
                if (_result != GbaCoreModuleProbeResult.Pending) yield break;

                _result = CreateAndStart(fixturePath, out detail);
                if (_result != GbaCoreModuleProbeResult.Pending) yield break;
                yield return CaptureStableMask(0);
                if (_result != GbaCoreModuleProbeResult.Pending) yield break;
                var released = _lastFrameHash;
                yield return CaptureStableMask(1);
                if (_result != GbaCoreModuleProbeResult.Pending) yield break;
                var pressed = _lastFrameHash;
                yield return CaptureStableMask(0);
                if (_result != GbaCoreModuleProbeResult.Pending) yield break;
                var releasedAfter = _lastFrameHash;
                if (!GbaLibretroCertificationBatchPolicy.HasObservableInputChange(released, pressed, releasedAfter))
                { _result = GbaCoreModuleProbeResult.InputNotObserved; yield break; }
                yield return WaitForAudio();
                if (_result != GbaCoreModuleProbeResult.Pending) yield break;

                var statePath = Path.Combine(Application.persistentDataPath,
                    "gba-core-module-probe", "module-probe.ss0");
                if (GbaCoreModuleNativeBindings.SaveState(_host, statePath) != GbaCoreModuleNativeBindings.Ok)
                {
                    _result = GbaCoreModuleProbeResult.StateSaveFailed;
                    yield break;
                }
                yield return CaptureStableMask(0);
                if (_result != GbaCoreModuleProbeResult.Pending) yield break;
                yield return CaptureStableMask(1);
                if (_result != GbaCoreModuleProbeResult.Pending) yield break;
                yield return CaptureStableMask(0);
                if (_result != GbaCoreModuleProbeResult.Pending) yield break;
                var advancedHash = _lastFrameHash;
                if (GbaCoreModuleNativeBindings.LoadState(_host, statePath) != GbaCoreModuleNativeBindings.Ok)
                { _result = GbaCoreModuleProbeResult.StateLoadFailed; yield break; }
                yield return CaptureStableMask(0);
                if (_result != GbaCoreModuleProbeResult.Pending) yield break;
                yield return CaptureStableMask(1);
                if (_result != GbaCoreModuleProbeResult.Pending) yield break;
                yield return CaptureStableMask(0);
                if (_result != GbaCoreModuleProbeResult.Pending || advancedHash != _lastFrameHash)
                { _result = GbaCoreModuleProbeResult.StateReplayFailed; yield break; }

                DestroyHost();
                _result = CreateAndStart(fixturePath, out detail);
                if (_result != GbaCoreModuleProbeResult.Pending)
                {
                    _result = GbaCoreModuleProbeResult.ReloadFailed;
                    yield break;
                }
                yield return WaitForFrame();
                if (_result == GbaCoreModuleProbeResult.Pending)
                    _result = GbaCoreModuleProbeResult.Success;
                else
                    _result = GbaCoreModuleProbeResult.ReloadFailed;
            }
            finally
            {
                DestroyHost();
                ExitProbeGate();
                var suffix = string.IsNullOrEmpty(detail) ? string.Empty : $" detail={detail}";
                if (_result == GbaCoreModuleProbeResult.Success)
                    Debug.Log($"GBA XR core module probe: PASS; {GbaCoreModuleProbePolicy.Describe(_result)}. Direct mGBA remains selected.{suffix}");
                else
                    Debug.LogError($"GBA XR core module probe: FAIL; {GbaCoreModuleProbePolicy.Describe(_result)}.{suffix}");
            }
        }

        private IEnumerator StageFixture(GbaLibretroFixtureContract fixture, string fixturePath)
        {
            _result = GbaCoreModuleProbeResult.Pending;
            var source = Path.Combine(Application.streamingAssetsPath, fixture.RelativePath);
            using var request = UnityWebRequest.Get(source);
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                _result = GbaCoreModuleProbeResult.FixtureUnavailable;
                yield break;
            }
            if (!GbaLibretroFixtureProbePolicy.MatchesFixtureContract(request.downloadHandler.data, fixture))
            {
                _result = GbaCoreModuleProbeResult.FixtureHashMismatch;
                yield break;
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(fixturePath));
                File.WriteAllBytes(fixturePath, request.downloadHandler.data);
            }
            catch (Exception)
            {
                _result = GbaCoreModuleProbeResult.FixtureUnavailable;
            }
        }

        private GbaCoreModuleProbeResult CreateAndStart(string fixturePath, out string detail)
        {
            detail = null;
            if (!GbaCoreModuleProbePolicy.IsBareModuleSoname(GbaCoreModuleProbePolicy.ModuleSoname))
                return GbaCoreModuleProbeResult.ModuleStartFailed;
            // A recreated host starts its producer sequence over. Do not let a
            // lease from the destroyed host block the reload evidence.
            _lastSequence = 0;
            _lastFrameHash = 0;
            _host = GbaCoreModuleNativeBindings.Create(GbaNativeBindings.AbiVersion);
            if (_host == IntPtr.Zero) return GbaCoreModuleProbeResult.HostCreateFailed;
            var status = GbaCoreModuleNativeBindings.StartModule(_host,
                GbaCoreModuleProbePolicy.ModuleSoname,
                GbaCoreModuleProbePolicy.ExpectedCoreId,
                fixturePath,
                GbaCoreModuleProbePolicy.FramesPerSecond);
            if (status == GbaCoreModuleNativeBindings.Ok) return GbaCoreModuleProbeResult.Pending;
            detail = $"moduleStatus={GbaCoreModuleNativeBindings.LastModuleStatus(_host)} {ReadModuleError()}";
            return GbaCoreModuleProbeResult.ModuleStartFailed;
        }

        private IEnumerator WaitForFrame()
        {
            _result = GbaCoreModuleProbeResult.Pending;
            var deadline = Time.realtimeSinceStartup + Mathf.Max(1f, timeoutSeconds);
            var observedRunning = false;
            while (Time.realtimeSinceStartup < deadline)
            {
                var state = GbaCoreModuleNativeBindings.State(_host);
                if (state < 0)
                {
                    _result = GbaCoreModuleProbeResult.WorkerFailed;
                    yield break;
                }
                observedRunning |= state == GbaCoreModuleNativeBindings.Running;
                if (!observedRunning)
                {
                    yield return null;
                    continue;
                }
                var status = GbaCoreModuleNativeBindings.AcquireLatestFrame(_host, out var lease);
                if (status == GbaCoreModuleNativeBindings.NoFrame)
                {
                    yield return null;
                    continue;
                }
                if (status != GbaCoreModuleNativeBindings.Ok)
                {
                    _result = GbaCoreModuleProbeResult.LeaseFailed;
                    yield break;
                }
                var isNewerSequence = lease.Sequence > _lastSequence;
                _result = GbaCoreModuleProbePolicy.ValidateLease(lease);
                if (_result == GbaCoreModuleProbeResult.Pending && isNewerSequence)
                {
                    _lastSequence = lease.Sequence;
                    Marshal.Copy(lease.ResolvedRgba.Rgba8, _frameCopy, 0, _frameCopy.Length);
                    _lastFrameHash = FrameHash.ComputeFnv1a32(_frameCopy);
                }
                if (GbaCoreModuleNativeBindings.ReleaseFrame(_host, ref lease) != GbaCoreModuleNativeBindings.Ok &&
                    _result == GbaCoreModuleProbeResult.Pending)
                    _result = GbaCoreModuleProbeResult.LeaseFailed;
                if (_result != GbaCoreModuleProbeResult.Pending)
                    yield break;
                if (isNewerSequence)
                    yield break;
                yield return null;
            }
            _result = observedRunning
                ? GbaCoreModuleProbeResult.FrameTimeout
                : GbaCoreModuleProbeResult.RunningTimeout;
        }

        private IEnumerator CaptureStableMask(uint gbaMask)
        {
            _result = TrySetJoypadMask(gbaMask);
            if (_result != GbaCoreModuleProbeResult.Pending) yield break;
            yield return WaitForFrame();
            if (_result != GbaCoreModuleProbeResult.Pending) yield break;
            yield return WaitForFrame();
        }

        private GbaCoreModuleProbeResult TrySetJoypadMask(uint gbaMask)
        {
            try
            {
                GbaCoreModuleNativeBindings.SetJoypadMask(_host,
                    GbaLibretroJoypadPolicy.TranslateGbaMask(gbaMask));
                return GbaCoreModuleProbeResult.Pending;
            }
            catch (Exception exception)
            {
                return GbaCoreModuleProbePolicy.FromException(exception);
            }
        }

        private IEnumerator WaitForAudio()
        {
            var deadline = Time.realtimeSinceStartup + Mathf.Max(1f, timeoutSeconds);
            while (Time.realtimeSinceStartup < deadline)
            {
                if (GbaCoreModuleProbePolicy.HasExpectedAudioRate(GbaCoreModuleNativeBindings.AudioSampleRate(_host)) &&
                    GbaCoreModuleNativeBindings.AudioAvailableFrames(_host) > 0)
                { _result = GbaCoreModuleProbeResult.Pending; yield break; }
                yield return null;
            }
            _result = GbaCoreModuleProbeResult.AudioRateFailed;
        }

        private string ReadModuleError()
        {
            var error = new StringBuilder(256);
            GbaCoreModuleNativeBindings.CopyLastModuleError(_host, error, 256);
            return error.ToString();
        }

        private void DestroyHost()
        {
            if (_host == IntPtr.Zero) return;
            try { GbaCoreModuleNativeBindings.Destroy(_host); }
            catch (Exception exception) { Debug.LogError($"GBA XR core module probe: cleanup exception {exception.GetType().Name}."); }
            finally { _host = IntPtr.Zero; }
        }

        private void ExitProbeGate()
        {
            if (!_ownsProbeGate) return;
            _ownsProbeGate = false;
            GbaLibretroAdapterProbeGate.Exit();
        }
    }
}

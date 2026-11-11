using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.Networking;

namespace GbaXr
{
    /// <summary>One-shot outcome of the isolated synthetic-fixture lease probe.</summary>
    internal enum GbaLibretroFixtureProbeResult
    {
        Pending,
        Disabled,
        UnsupportedPlatform,
        AdapterNotPackaged,
        AnotherProbeIsActive,
        FixtureUnavailable,
        FixtureHashMismatch,
        AdapterCreateFailed,
        AdapterStartFailed,
        WorkerFailed,
        RunningTimeout,
        FrameTimeout,
        LeaseAcquireFailed,
        InvalidFrameLayout,
        LeaseReleaseFailed,
        NativeLibraryMissing,
        AdapterEntryPointMissing,
        NativeCallFailed,
        Success
    }

    /// <summary>Fixed, generated fixture contract for the adapter-only probe.</summary>
    internal readonly struct GbaLibretroFixtureContract
    {
        internal GbaLibretroFixtureContract(string relativePath, int byteCount, string sha256)
        {
            RelativePath = relativePath;
            ByteCount = byteCount;
            Sha256 = sha256;
        }

        internal string RelativePath { get; }
        internal int ByteCount { get; }
        internal string Sha256 { get; }
    }

    /// <summary>Pure gate, fixture, timeout, and lease-layout policy.</summary>
    internal static class GbaLibretroFixtureProbePolicy
    {
        internal const uint FramesPerSecond = 60;
        internal const uint Width = 240;
        internal const uint Height = 160;
        internal const uint RgbaRowStrideBytes = Width * 4;

        // Built from fixtures/gba/frame_sequence.s with fixtures/gba/build.ps1.
        // This contains only synthetic Mode 3 checkerboard pixels.
        internal static readonly GbaLibretroFixtureContract FrameSequenceFixture =
            new("gba-fixtures/frame_sequence.gba", 280,
                "760CEF4F09635F762EDDD04473DBF0E6D2B227F0BC4AAECAF1A55EE00C8DC450");

        internal static GbaLibretroFixtureProbeResult EvaluatePreflight(
            bool enabled,
            bool isAndroidPlayer,
            bool adapterPackaged,
            bool anotherProbeIsActive)
        {
            if (!enabled)
            {
                return GbaLibretroFixtureProbeResult.Disabled;
            }

            if (!isAndroidPlayer)
            {
                return GbaLibretroFixtureProbeResult.UnsupportedPlatform;
            }

            if (!adapterPackaged)
            {
                return GbaLibretroFixtureProbeResult.AdapterNotPackaged;
            }

            return anotherProbeIsActive
                ? GbaLibretroFixtureProbeResult.AnotherProbeIsActive
                : GbaLibretroFixtureProbeResult.Pending;
        }

        internal static bool MatchesFixtureContract(byte[] bytes, GbaLibretroFixtureContract contract)
        {
            if (bytes == null || bytes.Length != contract.ByteCount)
            {
                return false;
            }

            using var sha256 = SHA256.Create();
            var hash = BitConverter.ToString(sha256.ComputeHash(bytes)).Replace("-", string.Empty);
            return string.Equals(hash, contract.Sha256, StringComparison.Ordinal);
        }

        internal static GbaLibretroFixtureProbeResult GetTimeoutResult(bool observedRunning) =>
            observedRunning
                ? GbaLibretroFixtureProbeResult.FrameTimeout
                : GbaLibretroFixtureProbeResult.RunningTimeout;

        internal static GbaLibretroFixtureProbeResult ValidateLease(
            ulong sequence,
            IntPtr rgba8,
            uint width,
            uint height,
            uint rowStrideBytes)
        {
            return sequence != 0 && rgba8 != IntPtr.Zero &&
                   width == Width && height == Height && rowStrideBytes == RgbaRowStrideBytes
                ? GbaLibretroFixtureProbeResult.Success
                : GbaLibretroFixtureProbeResult.InvalidFrameLayout;
        }

        internal static GbaLibretroFixtureProbeResult FromException(Exception exception)
        {
            return exception switch
            {
                DllNotFoundException _ => GbaLibretroFixtureProbeResult.NativeLibraryMissing,
                EntryPointNotFoundException _ => GbaLibretroFixtureProbeResult.AdapterEntryPointMissing,
                _ => GbaLibretroFixtureProbeResult.NativeCallFailed
            };
        }

        internal static bool ShouldAttempt(GbaLibretroFixtureProbeResult result) =>
            result == GbaLibretroFixtureProbeResult.Pending;

        internal static string Describe(GbaLibretroFixtureProbeResult result)
        {
            return result switch
            {
                GbaLibretroFixtureProbeResult.Disabled => "disabled by the serialized opt-in toggle",
                GbaLibretroFixtureProbeResult.UnsupportedPlatform => "not an Android player build",
                GbaLibretroFixtureProbeResult.AdapterNotPackaged => "the isolated adapter package is unavailable",
                GbaLibretroFixtureProbeResult.AnotherProbeIsActive => "another isolated libretro probe is active",
                GbaLibretroFixtureProbeResult.FixtureUnavailable => "the packaged synthetic frame_sequence fixture could not be read",
                GbaLibretroFixtureProbeResult.FixtureHashMismatch => "the packaged fixture did not match its approved SHA-256 contract",
                GbaLibretroFixtureProbeResult.AdapterCreateFailed => "adapter create returned null",
                GbaLibretroFixtureProbeResult.AdapterStartFailed => "adapter start rejected the approved fixture",
                GbaLibretroFixtureProbeResult.WorkerFailed => "the adapter worker entered its failed state",
                GbaLibretroFixtureProbeResult.RunningTimeout => "the adapter did not reach running state before the deadline",
                GbaLibretroFixtureProbeResult.FrameTimeout => "the running adapter did not publish a frame before the deadline",
                GbaLibretroFixtureProbeResult.LeaseAcquireFailed => "adapter frame acquire returned an unexpected status",
                GbaLibretroFixtureProbeResult.InvalidFrameLayout => "the acquired lease was not a non-null 240x160 RGBA8 frame",
                GbaLibretroFixtureProbeResult.LeaseReleaseFailed => "the acquired adapter-owned lease could not be released",
                GbaLibretroFixtureProbeResult.NativeLibraryMissing => "the adapter or its ELF dependency could not be loaded",
                GbaLibretroFixtureProbeResult.AdapterEntryPointMissing => "the adapter ABI entry point was unavailable",
                GbaLibretroFixtureProbeResult.NativeCallFailed => "an unexpected native interop exception occurred",
                GbaLibretroFixtureProbeResult.Success => "the isolated adapter started the synthetic fixture and released one valid frame lease",
                _ => "probe has not started"
            };
        }
    }

    /// <summary>
    /// Explicit, default-off Android smoke probe for a single adapter-owned
    /// resolved-frame lease. It only starts the hash-pinned synthetic fixture;
    /// no texture upload, input, audio, state, console, or core selection is
    /// involved. It is deliberately not added by <see cref="GbaNativeFrameDriver"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GbaLibretroFixtureFrameProbe : MonoBehaviour
    {
        private const float DefaultTimeoutSeconds = 10f;

        [SerializeField]
        [Tooltip("Experimental Android fixture/frame-lease probe. Default off; uses only hash-pinned frame_sequence.gba.")]
        private bool runOnAndroidAwake;

        [SerializeField, Min(1f)]
        [Tooltip("Bounded wait for worker running state and one resolved frame lease.")]
        private float timeoutSeconds = DefaultTimeoutSeconds;

        private bool _attempted;
        private bool _ownsProbeGate;
        private IntPtr _adapter;

        private void Awake()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            TryStartProbe(isAndroidPlayer: true);
#else
            TryStartProbe(isAndroidPlayer: false);
#endif
        }

        private void OnDestroy()
        {
            DestroyAdapter();
            ExitProbeGate();
        }

        private void TryStartProbe(bool isAndroidPlayer)
        {
            if (_attempted)
            {
                return;
            }

            var preflight = GbaLibretroFixtureProbePolicy.EvaluatePreflight(
                runOnAndroidAwake,
                isAndroidPlayer,
                GbaLibretroUnityPackaging.IsAdapterPackaged,
                GbaLibretroAdapterProbeGate.IsActive);
            if (!GbaLibretroFixtureProbePolicy.ShouldAttempt(preflight))
            {
                if (runOnAndroidAwake)
                {
                    Debug.LogWarning($"GBA XR libretro fixture probe: skipped; {GbaLibretroFixtureProbePolicy.Describe(preflight)}.");
                }
                return;
            }

            if (!GbaLibretroAdapterProbeGate.TryEnter())
            {
                Debug.LogWarning("GBA XR libretro fixture probe: skipped; another isolated libretro probe is active.");
                return;
            }
            _attempted = true;
            _ownsProbeGate = true;
            StartCoroutine(RunProbe());
        }

        private IEnumerator RunProbe()
        {
            var result = GbaLibretroFixtureProbeResult.NativeCallFailed;
            try
            {
                var fixture = GbaLibretroFixtureProbePolicy.FrameSequenceFixture;
                var localFixturePath = Path.Combine(
                    Application.persistentDataPath,
                    "gba-libretro-fixture-probe",
                    "frame_sequence.gba");
                var sourcePath = Path.Combine(Application.streamingAssetsPath, fixture.RelativePath);
                using (var request = UnityWebRequest.Get(sourcePath))
                {
                    yield return request.SendWebRequest();
                    if (request.result != UnityWebRequest.Result.Success ||
                        !GbaLibretroFixtureProbePolicy.MatchesFixtureContract(request.downloadHandler.data, fixture))
                    {
                        result = request.result == UnityWebRequest.Result.Success
                            ? GbaLibretroFixtureProbeResult.FixtureHashMismatch
                            : GbaLibretroFixtureProbeResult.FixtureUnavailable;
                        yield break;
                    }

                    result = TryPersistFixture(request.downloadHandler.data, localFixturePath);
                    if (result != GbaLibretroFixtureProbeResult.Pending)
                    {
                        yield break;
                    }
                }

                result = TryCreateAndStartAdapter(localFixturePath);
                if (result != GbaLibretroFixtureProbeResult.Pending)
                {
                    yield break;
                }

                var deadline = Time.realtimeSinceStartup + Mathf.Max(1f, timeoutSeconds);
                var observedRunning = false;
                while (Time.realtimeSinceStartup < deadline)
                {
                    result = TryGetWorkerState(out var workerState);
                    if (result != GbaLibretroFixtureProbeResult.Pending)
                    {
                        yield break;
                    }

                    if (workerState < 0)
                    {
                        result = GbaLibretroFixtureProbeResult.WorkerFailed;
                        yield break;
                    }

                    observedRunning |= workerState == 1;
                    if (!observedRunning)
                    {
                        yield return null;
                        continue;
                    }

                    result = TryAcquireLease(out var lease, out var hasLease);
                    if (result != GbaLibretroFixtureProbeResult.Pending)
                    {
                        yield break;
                    }
                    if (!hasLease)
                    {
                        yield return null;
                        continue;
                    }

                    var leaseResult = GbaLibretroFixtureProbePolicy.ValidateLease(
                        lease.Sequence,
                        lease.ResolvedRgba.Rgba8,
                        lease.ResolvedRgba.Width,
                        lease.ResolvedRgba.Height,
                        lease.ResolvedRgba.RowStrideBytes);
                    result = TryReleaseLease(ref lease);
                    if (result != GbaLibretroFixtureProbeResult.Pending)
                    {
                        yield break;
                    }

                    result = leaseResult;
                    yield break;
                }

                result = GbaLibretroFixtureProbePolicy.GetTimeoutResult(observedRunning);
            }
            finally
            {
                DestroyAdapter();
                ExitProbeGate();
                if (result == GbaLibretroFixtureProbeResult.Success)
                {
                    GbaLibretroReadinessStore.RecordApprovedEvidence(GbaLibretroCertification.FixtureFrameProbe);
                    Debug.Log($"GBA XR libretro fixture probe: success; {GbaLibretroFixtureProbePolicy.Describe(result)}. Direct mGBA remains selected.");
                }
                else
                {
                    Debug.LogError($"GBA XR libretro fixture probe: failure; {GbaLibretroFixtureProbePolicy.Describe(result)}.");
                }
            }
        }

        private static GbaLibretroFixtureProbeResult TryPersistFixture(byte[] bytes, string localFixturePath)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(localFixturePath));
                File.WriteAllBytes(localFixturePath, bytes);
                return GbaLibretroFixtureProbeResult.Pending;
            }
            catch (Exception)
            {
                return GbaLibretroFixtureProbeResult.FixtureUnavailable;
            }
        }

        private GbaLibretroFixtureProbeResult TryCreateAndStartAdapter(string localFixturePath)
        {
            try
            {
                _adapter = GbaLibretroNativeBindings.Create(GbaNativeBindings.AbiVersion);
                if (_adapter == IntPtr.Zero)
                {
                    return GbaLibretroFixtureProbeResult.AdapterCreateFailed;
                }

                return GbaLibretroNativeBindings.Start(
                           _adapter,
                           localFixturePath,
                           GbaLibretroFixtureProbePolicy.FramesPerSecond) == GbaLibretroNativeBindings.Ok
                    ? GbaLibretroFixtureProbeResult.Pending
                    : GbaLibretroFixtureProbeResult.AdapterStartFailed;
            }
            catch (Exception exception)
            {
                return GbaLibretroFixtureProbePolicy.FromException(exception);
            }
        }

        private GbaLibretroFixtureProbeResult TryGetWorkerState(out int workerState)
        {
            workerState = 0;
            try
            {
                workerState = GbaLibretroNativeBindings.State(_adapter);
                return GbaLibretroFixtureProbeResult.Pending;
            }
            catch (Exception exception)
            {
                return GbaLibretroFixtureProbePolicy.FromException(exception);
            }
        }

        private GbaLibretroFixtureProbeResult TryAcquireLease(
            out GbaNativeBindings.FrameLease lease,
            out bool hasLease)
        {
            lease = default;
            hasLease = false;
            try
            {
                var status = GbaLibretroNativeBindings.AcquireLatestFrame(_adapter, out lease);
                if (status == GbaLibretroNativeBindings.NoFrame)
                {
                    return GbaLibretroFixtureProbeResult.Pending;
                }
                if (status != GbaLibretroNativeBindings.Ok)
                {
                    return GbaLibretroFixtureProbeResult.LeaseAcquireFailed;
                }

                hasLease = true;
                return GbaLibretroFixtureProbeResult.Pending;
            }
            catch (Exception exception)
            {
                return GbaLibretroFixtureProbePolicy.FromException(exception);
            }
        }

        private GbaLibretroFixtureProbeResult TryReleaseLease(ref GbaNativeBindings.FrameLease lease)
        {
            try
            {
                return GbaLibretroNativeBindings.ReleaseFrame(_adapter, ref lease) == GbaLibretroNativeBindings.Ok
                    ? GbaLibretroFixtureProbeResult.Pending
                    : GbaLibretroFixtureProbeResult.LeaseReleaseFailed;
            }
            catch (Exception exception)
            {
                return GbaLibretroFixtureProbePolicy.FromException(exception);
            }
        }

        private void DestroyAdapter()
        {
            if (_adapter == IntPtr.Zero)
            {
                return;
            }

            try
            {
                GbaLibretroNativeBindings.Destroy(_adapter);
            }
            catch (Exception exception)
            {
                var result = GbaLibretroFixtureProbePolicy.FromException(exception);
                Debug.LogError($"GBA XR libretro fixture probe: failure during cleanup; {GbaLibretroFixtureProbePolicy.Describe(result)} ({exception.GetType().Name}).");
            }
            finally
            {
                _adapter = IntPtr.Zero;
            }
        }

        private void ExitProbeGate()
        {
            if (!_ownsProbeGate) return;
            _ownsProbeGate = false;
            GbaLibretroAdapterProbeGate.Exit();
        }
    }
}

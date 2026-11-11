using System;
using System.Collections;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Networking;

namespace GbaXr
{
    /// <summary>One process-global libretro host may own callbacks at a time.</summary>
    internal static class GbaLibretroAdapterProbeGate
    {
        private static bool s_active;
        internal static bool IsActive => s_active;
        internal static bool TryEnter()
        {
            if (s_active) return false;
            s_active = true;
            return true;
        }
        internal static void Exit() => s_active = false;
    }

    [Flags]
    internal enum GbaLibretroCertificationBatchStage
    {
        None = 0,
        ResolvedFrame = 1 << 0,
        Input = 1 << 1,
        Audio = 1 << 2,
        StateReplay = 1 << 3
    }

    /// <summary>Pure acceptance rules for the fixture-only Android batch.</summary>
    internal static class GbaLibretroCertificationBatchPolicy
    {
        internal const uint ExpectedOutputSampleRate = 48000;
        internal const uint GbaAButtonMask = 1;
        // Keep this in lockstep with GbaNativeFrameDriver's synthetic-fixture
        // gate. frame_sequence fills VRAM with ordinary CPU stores, so the
        // physical-device contract deliberately samples a later stable lease.
        internal const int RequiredSettledLeaseCount = 30;
        // Diagnostic only. These later acquired-lease checkpoints determine
        // whether a post-settle mismatch converges or remains stable. They do
        // not relax the 30-lease parity gate above.
        internal static readonly int[] TemporalSweepLeaseCheckpoints = { 30, 60, 120 };
        // Confirmed physical-device resolved-frame contract at lease 30.
        internal const uint FrameSequenceSourceHash = 0xF9838FC5u;
        internal const uint FrameSequenceExpectedHash = FrameSequenceSourceHash;

        // Built from fixtures/gba/input_state_replay.s. This deterministic
        // fixture never contains a user or commercial ROM payload.
        internal static readonly GbaLibretroFixtureContract InputStateReplayFixture =
            new("gba-fixtures/input_state_replay.gba", 428,
                "5100940CC68685A07E841C14217CA2AB515AB7DF188F76D966F2A6D3F4D9BE7A");

        internal static bool HasExpectedResolvedFrame(uint frameHash) =>
            frameHash == FrameSequenceExpectedHash;

        internal static bool HasExpectedAudio(uint outputRate, uint availableFrames) =>
            outputRate == ExpectedOutputSampleRate && availableFrames > 0;

        internal static bool HasObservableInputChange(uint releasedHash, uint pressedHash, uint releasedAfterHash) =>
            releasedHash != pressedHash && pressedHash == releasedAfterHash;

        internal static bool HasStateReplay(uint advancedHash, uint replayedHash) =>
            advancedHash == replayedHash;

        internal static bool IsTemporalSweepCheckpoint(int acquiredLeaseCount)
        {
            foreach (var checkpoint in TemporalSweepLeaseCheckpoints)
            {
                if (checkpoint == acquiredLeaseCount) return true;
            }
            return false;
        }

        internal static string FormatTemporalSweepCheckpoint(int acquiredLeaseCount,
            uint hash, GbaLibretroFrameSequenceDiagnostic diagnostic)
        {
            return $"lease={acquiredLeaseCount}/hash=0x{hash:X8}/mismatches={diagnostic.MismatchCount}/" +
                   $"first=({diagnostic.FirstX},{diagnostic.FirstY})/" +
                   $"expected={diagnostic.FirstExpectedRgba:X8}/actual={diagnostic.FirstActualRgba:X8}/" +
                   $"candidate={diagnostic.MatchingCandidate}";
        }

        internal static GbaLibretroCertification EvidenceFor(GbaLibretroCertificationBatchStage passed)
        {
            var evidence = GbaLibretroCertification.None;
            if (passed.HasFlag(GbaLibretroCertificationBatchStage.ResolvedFrame))
            {
                evidence |= GbaLibretroCertification.FixtureFrameProbe |
                            GbaLibretroCertification.ResolvedFrameParity;
            }
            if (passed.HasFlag(GbaLibretroCertificationBatchStage.Input)) evidence |= GbaLibretroCertification.InputParity;
            if (passed.HasFlag(GbaLibretroCertificationBatchStage.Audio)) evidence |= GbaLibretroCertification.AudioParity;
            if (passed.HasFlag(GbaLibretroCertificationBatchStage.StateReplay)) evidence |= GbaLibretroCertification.StateParity;
            // GalaxyXrDevice is intentionally absent: a human review of the
            // batch evidence is the only authorized final certification step.
            return evidence;
        }

        internal static bool IsComplete(GbaLibretroCertificationBatchStage passed) =>
            passed == (GbaLibretroCertificationBatchStage.ResolvedFrame |
                       GbaLibretroCertificationBatchStage.Input |
                       GbaLibretroCertificationBatchStage.Audio |
                       GbaLibretroCertificationBatchStage.StateReplay);

        internal static GbaLibretroFrameSequenceDiagnostic DiagnoseSourceCheckerboard(byte[] rgba)
        {
            if (rgba == null || rgba.Length != GbaTestFrameSurface.Width * GbaTestFrameSurface.Height * 4)
            {
                return new GbaLibretroFrameSequenceDiagnostic(0, -1, -1, 0U, 0U, "invalid-buffer", 0U, 0U, 0U);
            }

            var mismatchCount = 0;
            var firstX = -1;
            var firstY = -1;
            var firstExpected = 0U;
            var firstActual = 0U;
            var sourceHash = FrameHash.Fnv1a32OffsetBasis;
            var rbSwapHash = FrameHash.Fnv1a32OffsetBasis;
            var transparentAlphaHash = FrameHash.Fnv1a32OffsetBasis;
            var verticallyFlippedHash = FrameHash.Fnv1a32OffsetBasis;
            var rbSwapExact = true;
            var transparentAlphaExact = true;
            var verticallyFlippedExact = true;

            for (var y = 0; y < GbaTestFrameSurface.Height; y++)
            {
                for (var x = 0; x < GbaTestFrameSurface.Width; x++)
                {
                    var offset = (y * GbaTestFrameSurface.Width + x) * 4;
                    var white = (((x >> 4) ^ (y >> 4)) & 1) == 0;
                    var expectedR = (byte)255;
                    var expectedG = white ? (byte)255 : (byte)0;
                    var expectedB = white ? (byte)255 : (byte)0;
                    const byte expectedA = 255;
                    var actual = PackRgba(rgba[offset], rgba[offset + 1], rgba[offset + 2], rgba[offset + 3]);
                    var expected = PackRgba(expectedR, expectedG, expectedB, expectedA);
                    if (actual != expected)
                    {
                        mismatchCount++;
                        if (firstX < 0)
                        {
                            firstX = x;
                            firstY = y;
                            firstExpected = expected;
                            firstActual = actual;
                        }
                    }

                    sourceHash = HashRgba(sourceHash, expectedR, expectedG, expectedB, expectedA);
                    rbSwapHash = HashRgba(rbSwapHash, expectedB, expectedG, expectedR, expectedA);
                    transparentAlphaHash = HashRgba(transparentAlphaHash, expectedR, expectedG, expectedB, 0);
                    var flippedWhite = (((x >> 4) ^ ((GbaTestFrameSurface.Height - 1 - y) >> 4)) & 1) == 0;
                    verticallyFlippedHash = HashRgba(
                        verticallyFlippedHash,
                        255,
                        flippedWhite ? (byte)255 : (byte)0,
                        flippedWhite ? (byte)255 : (byte)0,
                        255);
                    rbSwapExact &= actual == PackRgba(expectedB, expectedG, expectedR, expectedA);
                    transparentAlphaExact &= actual == PackRgba(expectedR, expectedG, expectedB, 0);
                    verticallyFlippedExact &= actual == PackRgba(
                        255,
                        flippedWhite ? (byte)255 : (byte)0,
                        flippedWhite ? (byte)255 : (byte)0,
                        255);
                }
            }

            var candidate = mismatchCount == 0
                ? "source-rgba"
                : rbSwapExact ? "source-rb-swap"
                : transparentAlphaExact ? "source-alpha-zero"
                : verticallyFlippedExact ? "source-vertical-flip"
                : "none";
            return new GbaLibretroFrameSequenceDiagnostic(
                mismatchCount, firstX, firstY, firstExpected, firstActual, candidate,
                sourceHash, rbSwapHash, transparentAlphaHash, verticallyFlippedHash);
        }

        private static uint HashRgba(uint hash, byte r, byte g, byte b, byte a)
        {
            hash = HashByte(hash, r);
            hash = HashByte(hash, g);
            hash = HashByte(hash, b);
            return HashByte(hash, a);
        }

        private static uint HashByte(uint hash, byte value) =>
            (hash ^ value) * FrameHash.Fnv1a32Prime;

        private static uint PackRgba(byte r, byte g, byte b, byte a) =>
            ((uint)r << 24) | ((uint)g << 16) | ((uint)b << 8) | a;
    }

    /// <summary>Source-pattern evidence only; it contains no ROM/user content.</summary>
    internal readonly struct GbaLibretroFrameSequenceDiagnostic
    {
        internal GbaLibretroFrameSequenceDiagnostic(
            int mismatchCount, int firstX, int firstY, uint firstExpectedRgba, uint firstActualRgba,
            string matchingCandidate, uint sourceHash, uint rbSwapHash, uint transparentAlphaHash,
            uint verticallyFlippedHash = 0U)
        {
            MismatchCount = mismatchCount;
            FirstX = firstX;
            FirstY = firstY;
            FirstExpectedRgba = firstExpectedRgba;
            FirstActualRgba = firstActualRgba;
            MatchingCandidate = matchingCandidate;
            SourceHash = sourceHash;
            RbSwapHash = rbSwapHash;
            TransparentAlphaHash = transparentAlphaHash;
            VerticallyFlippedHash = verticallyFlippedHash;
        }

        internal int MismatchCount { get; }
        internal int FirstX { get; }
        internal int FirstY { get; }
        internal uint FirstExpectedRgba { get; }
        internal uint FirstActualRgba { get; }
        internal string MatchingCandidate { get; }
        internal uint SourceHash { get; }
        internal uint RbSwapHash { get; }
        internal uint TransparentAlphaHash { get; }
        internal uint VerticallyFlippedHash { get; }
    }

    /// <summary>
    /// Default-off, fixture-only Galaxy XR validation batch. It is deliberately
    /// separate from <see cref="GbaNativeFrameDriver"/> and never selects a
    /// runtime core, uploads a texture, accepts a user ROM, or invokes 2.5D.
    /// Every native call targets the isolated adapter facade.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GbaLibretroCertificationBatchProbe : MonoBehaviour
    {
        private const float DefaultTimeoutSeconds = 15f;

        [SerializeField]
        [Tooltip("Explicit Android-only fixture certification batch. Default off; never enables runtime core selection by itself.")]
        private bool runOnAndroidAwake;

        [SerializeField, Min(3f)]
        [Tooltip("One watchdog per fixture session, in real seconds.")]
        private float timeoutSeconds = DefaultTimeoutSeconds;

        private readonly byte[] _frameCopy = new byte[GbaTestFrameSurface.Width * GbaTestFrameSurface.Height * 4];
        private IntPtr _adapter;
        private bool _attempted;
        private bool _ownsProbeGate;

        private sealed class FrameCapture
        {
            internal bool Succeeded;
            internal ulong Sequence;
            internal uint Hash;
            internal float Deadline;
            internal string Failure;
            internal string Samples;
            internal GbaLibretroFrameSequenceDiagnostic SourceDiagnostic;
        }

        private sealed class TemporalSweepCapture
        {
            internal readonly FrameCapture[] Checkpoints =
                new FrameCapture[GbaLibretroCertificationBatchPolicy.TemporalSweepLeaseCheckpoints.Length];
            internal string Failure;
        }

        private sealed class AudioObservation
        {
            internal uint Rate;
            internal uint Available;
        }

        private void Awake()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            TryStartBatch(isAndroidPlayer: true);
#else
            TryStartBatch(isAndroidPlayer: false);
#endif
        }

        private void OnDestroy()
        {
            DestroyAdapter();
            ExitProbeGate();
        }

        private void TryStartBatch(bool isAndroidPlayer)
        {
            if (_attempted) return;
            if (!runOnAndroidAwake)
            {
                return;
            }
            if (!isAndroidPlayer)
            {
                Debug.LogWarning("GBA XR libretro certification batch: skipped; not an Android player build.");
                return;
            }
            if (!GbaLibretroUnityPackaging.IsAdapterPackaged)
            {
                Debug.LogError("GBA XR libretro certification batch: skipped; isolated adapter package is unavailable.");
                return;
            }
            if (!GbaLibretroAdapterProbeGate.TryEnter())
            {
                Debug.LogWarning("GBA XR libretro certification batch: skipped; another isolated libretro probe is active.");
                return;
            }

            _attempted = true;
            _ownsProbeGate = true;
            StartCoroutine(RunBatch());
        }

        private IEnumerator RunBatch()
        {
            var passed = GbaLibretroCertificationBatchStage.None;
            var detail = string.Empty;
            var resolvedHash = 0U;
            var inputReleasedHash = 0U;
            var inputPressedHash = 0U;
            var stateAdvancedHash = 0U;
            var stateReplayedHash = 0U;
            try
            {
                var localFrameFixture = Path.Combine(Application.persistentDataPath, "gba-libretro-certification-batch", "frame_sequence.gba");
                var frameFixtureReady = false;
                yield return CopyPinnedFixture(
                    GbaLibretroFixtureProbePolicy.FrameSequenceFixture,
                    localFrameFixture,
                    success => frameFixtureReady = success);
                if (!frameFixtureReady)
                {
                    detail = "frame_sequence fixture was unavailable or did not match its SHA-256 contract";
                    yield break;
                }

                var frameCapture = new FrameCapture();
                var audio = new AudioObservation();
                yield return RunResolvedFixture(localFrameFixture, frameCapture);
                if (!frameCapture.Succeeded)
                {
                    detail = frameCapture.Failure;
                    yield break;
                }
                resolvedHash = frameCapture.Hash;
                if (!GbaLibretroCertificationBatchPolicy.HasExpectedResolvedFrame(resolvedHash))
                {
                    var sweep = new TemporalSweepCapture();
                    yield return CaptureTemporalStabilitySweep(
                        frameCapture,
                        frameCapture.Deadline,
                        sweep);
                    detail = $"resolved fixture hash mismatch after {GbaLibretroCertificationBatchPolicy.RequiredSettledLeaseCount} leases " +
                             $"(0x{resolvedHash:X8}, sequence={frameCapture.Sequence}, samples={frameCapture.Samples}, " +
                             $"sourceMismatches={frameCapture.SourceDiagnostic.MismatchCount}, " +
                             $"first=({frameCapture.SourceDiagnostic.FirstX},{frameCapture.SourceDiagnostic.FirstY}) " +
                             $"expected={frameCapture.SourceDiagnostic.FirstExpectedRgba:X8} actual={frameCapture.SourceDiagnostic.FirstActualRgba:X8}, " +
                             $"candidate={frameCapture.SourceDiagnostic.MatchingCandidate}, " +
                             $"sourceHash=0x{frameCapture.SourceDiagnostic.SourceHash:X8}, " +
                             $"rbSwapHash=0x{frameCapture.SourceDiagnostic.RbSwapHash:X8}, " +
                             $"alpha0Hash=0x{frameCapture.SourceDiagnostic.TransparentAlphaHash:X8}, " +
                             $"verticalFlipHash=0x{frameCapture.SourceDiagnostic.VerticallyFlippedHash:X8}); " +
                             $"temporalSweep={DescribeTemporalSweep(sweep)}";
                    yield break;
                }
                passed |= GbaLibretroCertificationBatchStage.ResolvedFrame;
                var audioCapture = new FrameCapture();
                yield return WaitForAudio(frameCapture.Deadline, audio, audioCapture);
                if (audioCapture.Succeeded &&
                    GbaLibretroCertificationBatchPolicy.HasExpectedAudio(audio.Rate, audio.Available))
                {
                    passed |= GbaLibretroCertificationBatchStage.Audio;
                }
                else
                {
                    detail = $"adapter PCM was not ready at 48 kHz (rate={audio.Rate}, available={audio.Available})";
                    yield break;
                }

                // The first fixture owns the active process-global libretro
                // callbacks. Release it before beginning the independent
                // input/state fixture session.
                DestroyAdapter();

                var localInputFixture = Path.Combine(Application.persistentDataPath, "gba-libretro-certification-batch", "input_state_replay.gba");
                var inputFixtureReady = false;
                yield return CopyPinnedFixture(
                    GbaLibretroCertificationBatchPolicy.InputStateReplayFixture,
                    localInputFixture,
                    success => inputFixtureReady = success);
                if (!inputFixtureReady)
                {
                    detail = "input_state_replay fixture was unavailable or did not match its SHA-256 contract";
                    yield break;
                }

                var inputResult = new InputStateResult();
                yield return RunInputAndStateFixture(localInputFixture, inputResult);
                if (inputResult.InputObserved)
                {
                    passed |= GbaLibretroCertificationBatchStage.Input;
                }
                if (!inputResult.Succeeded)
                {
                    detail = inputResult.Failure;
                    yield break;
                }
                inputReleasedHash = inputResult.ReleasedHash;
                inputPressedHash = inputResult.PressedHash;
                stateAdvancedHash = inputResult.AdvancedHash;
                stateReplayedHash = inputResult.ReplayedHash;
                if (!GbaLibretroCertificationBatchPolicy.HasStateReplay(stateAdvancedHash, stateReplayedHash))
                {
                    detail = $"state replay mismatch (advance=0x{stateAdvancedHash:X8}, replay=0x{stateReplayedHash:X8})";
                    yield break;
                }
                passed |= GbaLibretroCertificationBatchStage.StateReplay;
            }
            finally
            {
                DestroyAdapter();
                ExitProbeGate();
                var evidence = GbaLibretroCertificationBatchPolicy.EvidenceFor(passed);
                if (evidence != GbaLibretroCertification.None)
                {
                    GbaLibretroReadinessStore.RecordApprovedEvidence(evidence);
                }

                var outcome = GbaLibretroCertificationBatchPolicy.IsComplete(passed) ? "PASS" : "FAIL";
                var reason = string.IsNullOrEmpty(detail) ? "all fixture contracts succeeded" : detail;
                Debug.Log(
                    $"GBA XR libretro certification batch: {outcome}; resolved={passed.HasFlag(GbaLibretroCertificationBatchStage.ResolvedFrame)}, " +
                    $"input={passed.HasFlag(GbaLibretroCertificationBatchStage.Input)}, audio={passed.HasFlag(GbaLibretroCertificationBatchStage.Audio)}, " +
                    $"state={passed.HasFlag(GbaLibretroCertificationBatchStage.StateReplay)}, evidence={evidence}, " +
                    $"GalaxyXrDevice=not-recorded, resolvedHash=0x{resolvedHash:X8}, input={inputReleasedHash:X8}->{inputPressedHash:X8}, " +
                    $"state={stateAdvancedHash:X8}/{stateReplayedHash:X8}; {reason}.");
            }
        }

        private IEnumerator CopyPinnedFixture(GbaLibretroFixtureContract fixture, string destination, Action<bool> completed)
        {
            var source = Path.Combine(Application.streamingAssetsPath, fixture.RelativePath);
            using (var request = UnityWebRequest.Get(source))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success ||
                    !GbaLibretroFixtureProbePolicy.MatchesFixtureContract(request.downloadHandler.data, fixture))
                {
                    completed(false);
                    yield break;
                }
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    File.WriteAllBytes(destination, request.downloadHandler.data);
                    completed(true);
                }
                catch (Exception)
                {
                    completed(false);
                }
            }
        }

        private IEnumerator RunResolvedFixture(string fixturePath, FrameCapture capture)
        {
            var started = TryCreateAndStart(fixturePath, out var startError);
            if (!started)
            {
                capture.Failure = startError;
                yield break;
            }
            var deadline = Time.realtimeSinceStartup + Mathf.Max(3f, timeoutSeconds);
            capture.Deadline = deadline;
            yield return WaitForRunning(deadline, capture);
            if (!capture.Succeeded)
            {
                DestroyAdapter();
                yield break;
            }
            capture.Succeeded = false;
            FrameCapture settled = null;
            yield return WaitForSettledFrame(0UL, deadline, value => settled = value);
            CopyFrameCapture(settled, capture);
            if (!capture.Succeeded)
            {
                DestroyAdapter();
                yield break;
            }
        }

        private sealed class InputStateResult
        {
            internal bool Succeeded;
            internal bool InputObserved;
            internal string Failure;
            internal uint ReleasedHash;
            internal uint PressedHash;
            internal uint ReleasedAfterHash;
            internal uint AdvancedHash;
            internal uint ReplayedHash;
        }

        private IEnumerator RunInputAndStateFixture(string fixturePath, InputStateResult result)
        {
            if (!TryCreateAndStart(fixturePath, out var startError))
            {
                result.Failure = startError;
                yield break;
            }
            var running = new FrameCapture();
            var deadline = Time.realtimeSinceStartup + Mathf.Max(3f, timeoutSeconds);
            yield return WaitForRunning(deadline, running);
            if (!running.Succeeded)
            {
                result.Failure = running.Failure;
                DestroyAdapter();
                yield break;
            }

            // Discard the same number of completed leases used by the direct
            // fixture contract before setting any input. This avoids sampling
            // the Mode 3 initialization stores as if they were an input state.
            FrameCapture settled = null;
            yield return WaitForSettledFrame(0UL, deadline, value => settled = value);
            if (!settled.Succeeded)
            {
                result.Failure = settled.Failure;
                DestroyAdapter();
                yield break;
            }

            // Establish the fixture's documented canonical state with an
            // unobserved stable press/release, then observe a second press.
            var sequence = settled.Sequence;
            FrameCapture warmupOne = null;
            FrameCapture warmupTwo = null;
            FrameCapture canonicalPress = null;
            FrameCapture canonicalRelease = null;
            yield return CaptureStableMask(0, sequence, deadline, value => warmupOne = value); sequence = warmupOne.Sequence;
            yield return CaptureStableMask(0, sequence, deadline, value => warmupTwo = value); sequence = warmupTwo.Sequence;
            yield return CaptureStableMask(GbaLibretroCertificationBatchPolicy.GbaAButtonMask, sequence, deadline, value => canonicalPress = value); sequence = canonicalPress.Sequence;
            yield return CaptureStableMask(0, sequence, deadline, value => canonicalRelease = value); sequence = canonicalRelease.Sequence;
            if (!AllSucceeded(warmupOne, warmupTwo, canonicalPress, canonicalRelease))
            {
                result.Failure = FirstFailure(warmupOne, warmupTwo, canonicalPress, canonicalRelease);
                DestroyAdapter();
                yield break;
            }

            FrameCapture released = null;
            FrameCapture pressed = null;
            FrameCapture releasedAfter = null;
            yield return CaptureStableMask(0, sequence, deadline, value => released = value); sequence = released.Sequence;
            yield return CaptureStableMask(GbaLibretroCertificationBatchPolicy.GbaAButtonMask, sequence, deadline, value => pressed = value); sequence = pressed.Sequence;
            yield return CaptureStableMask(0, sequence, deadline, value => releasedAfter = value); sequence = releasedAfter.Sequence;
            if (!AllSucceeded(released, pressed, releasedAfter))
            {
                result.Failure = FirstFailure(released, pressed, releasedAfter);
                DestroyAdapter();
                yield break;
            }
            result.ReleasedHash = released.Hash;
            result.PressedHash = pressed.Hash;
            result.ReleasedAfterHash = releasedAfter.Hash;
            result.InputObserved = GbaLibretroCertificationBatchPolicy.HasObservableInputChange(
                result.ReleasedHash, result.PressedHash, result.ReleasedAfterHash);
            if (!result.InputObserved)
            {
                result.Failure = "the deterministic A-button fixture did not produce a stable observable frame change";
                DestroyAdapter();
                yield break;
            }

            var statePath = Path.Combine(Application.persistentDataPath, "gba-libretro-certification-batch", "states", "input-state-replay.ss0");
            if (!TryEnsureDirectory(Path.GetDirectoryName(statePath)))
            {
                result.Failure = "could not create the private fixture state directory";
                DestroyAdapter();
                yield break;
            }
            if (!TrySaveState(statePath, out var saveError))
            {
                result.Failure = saveError;
                DestroyAdapter();
                yield break;
            }

            FrameCapture advanced = null;
            yield return RunInputScript(sequence, deadline, value => advanced = value); sequence = advanced.Sequence;
            if (!advanced.Succeeded)
            {
                result.Failure = advanced.Failure;
                DestroyAdapter();
                yield break;
            }
            result.AdvancedHash = advanced.Hash;

            if (!TryLoadState(statePath, out var loadError))
            {
                result.Failure = loadError;
                DestroyAdapter();
                yield break;
            }
            FrameCapture postLoadDrain = null;
            yield return CaptureStableMask(0, sequence, deadline, value => postLoadDrain = value); sequence = postLoadDrain.Sequence;
            if (!postLoadDrain.Succeeded)
            {
                result.Failure = postLoadDrain.Failure;
                DestroyAdapter();
                yield break;
            }
            FrameCapture replayed = null;
            yield return RunInputScript(sequence, deadline, value => replayed = value);
            if (!replayed.Succeeded)
            {
                result.Failure = replayed.Failure;
                DestroyAdapter();
                yield break;
            }
            result.ReplayedHash = replayed.Hash;
            result.Succeeded = true;
            DestroyAdapter();
        }

        private IEnumerator RunInputScript(ulong sequence, float deadline, Action<FrameCapture> completed)
        {
            FrameCapture released = null;
            FrameCapture pressed = null;
            FrameCapture final = null;
            yield return CaptureStableMask(0, sequence, deadline, value => released = value); sequence = released.Sequence;
            yield return CaptureStableMask(GbaLibretroCertificationBatchPolicy.GbaAButtonMask, sequence, deadline, value => pressed = value); sequence = pressed.Sequence;
            yield return CaptureStableMask(0, sequence, deadline, value => final = value);
            if (!AllSucceeded(released, pressed, final) && final != null && string.IsNullOrEmpty(final.Failure))
            {
                final.Failure = FirstFailure(released, pressed, final);
            }
            completed(final);
        }

        private IEnumerator CaptureStableMask(uint mask, ulong sequence, float deadline, Action<FrameCapture> completed)
        {
            var stable = new FrameCapture();
            if (!TrySetJoypadMask(mask, out var error))
            {
                stable.Failure = error;
                completed(stable);
                yield break;
            }
            var discarded = new FrameCapture();
            yield return WaitForNextFrame(sequence, deadline, discarded);
            if (!discarded.Succeeded)
            {
                stable.Failure = discarded.Failure;
                completed(stable);
                yield break;
            }
            yield return WaitForNextFrame(discarded.Sequence, deadline, stable);
            completed(stable);
        }

        private IEnumerator WaitForSettledFrame(ulong sequence, float deadline, Action<FrameCapture> completed)
        {
            FrameCapture latest = null;
            for (var index = 0; index < GbaLibretroCertificationBatchPolicy.RequiredSettledLeaseCount; index++)
            {
                latest = new FrameCapture();
                yield return WaitForNextFrame(sequence, deadline, latest);
                if (!latest.Succeeded)
                {
                    completed(latest);
                    yield break;
                }
                sequence = latest.Sequence;
            }
            completed(latest);
        }

        private IEnumerator CaptureTemporalStabilitySweep(
            FrameCapture settled,
            float deadline,
            TemporalSweepCapture sweep)
        {
            var acquiredLeaseCount = GbaLibretroCertificationBatchPolicy.RequiredSettledLeaseCount;
            var sequence = settled.Sequence;
            for (var checkpointIndex = 0;
                 checkpointIndex < GbaLibretroCertificationBatchPolicy.TemporalSweepLeaseCheckpoints.Length;
                 checkpointIndex++)
            {
                var targetLeaseCount = GbaLibretroCertificationBatchPolicy.TemporalSweepLeaseCheckpoints[checkpointIndex];
                if (targetLeaseCount == acquiredLeaseCount)
                {
                    sweep.Checkpoints[checkpointIndex] = settled;
                    continue;
                }
                while (acquiredLeaseCount < targetLeaseCount)
                {
                    var next = new FrameCapture();
                    yield return WaitForNextFrame(sequence, deadline, next);
                    if (!next.Succeeded)
                    {
                        sweep.Failure = next.Failure;
                        yield break;
                    }
                    sequence = next.Sequence;
                    acquiredLeaseCount++;
                    if (acquiredLeaseCount == targetLeaseCount)
                    {
                        sweep.Checkpoints[checkpointIndex] = next;
                    }
                }
            }
        }

        private static string DescribeTemporalSweep(TemporalSweepCapture sweep)
        {
            if (sweep == null) return "unavailable";
            var details = new string[sweep.Checkpoints.Length];
            for (var index = 0; index < sweep.Checkpoints.Length; index++)
            {
                var capture = sweep.Checkpoints[index];
                if (capture == null || !capture.Succeeded)
                {
                    details[index] = $"lease={GbaLibretroCertificationBatchPolicy.TemporalSweepLeaseCheckpoints[index]}/missing";
                    continue;
                }
                details[index] = GbaLibretroCertificationBatchPolicy.FormatTemporalSweepCheckpoint(
                    GbaLibretroCertificationBatchPolicy.TemporalSweepLeaseCheckpoints[index],
                    capture.Hash,
                    capture.SourceDiagnostic);
            }
            var summary = string.Join("|", details);
            return string.IsNullOrEmpty(sweep.Failure) ? summary : $"{summary}|failed:{sweep.Failure}";
        }

        private IEnumerator WaitForRunning(float deadline, FrameCapture result)
        {
            while (Time.realtimeSinceStartup < deadline)
            {
                if (!TryState(out var state, out var error))
                {
                    result.Failure = error;
                    yield break;
                }
                if (state < 0)
                {
                    result.Failure = "adapter worker entered its failed state";
                    yield break;
                }
                if (state == 1)
                {
                    result.Succeeded = true;
                    yield break;
                }
                yield return null;
            }
            result.Failure = "adapter worker did not reach running before the deadline";
        }

        private IEnumerator WaitForAudio(float deadline, AudioObservation observation, FrameCapture result)
        {
            while (Time.realtimeSinceStartup < deadline)
            {
                if (!TryGetAudio(out var rate, out var available, out var error))
                {
                    result.Failure = error;
                    yield break;
                }
                if (rate == GbaLibretroCertificationBatchPolicy.ExpectedOutputSampleRate && available > 0)
                {
                    observation.Rate = rate;
                    observation.Available = available;
                    result.Succeeded = true;
                    yield break;
                }
                observation.Rate = rate;
                observation.Available = available;
                yield return null;
            }
            result.Failure = "adapter did not expose non-empty 48 kHz PCM before the deadline";
        }

        private IEnumerator WaitForNextFrame(ulong afterSequence, float deadline, FrameCapture result)
        {
            while (Time.realtimeSinceStartup < deadline)
            {
                if (!TryAcquireFrame(out var status, out var lease, out var error))
                {
                    result.Failure = error;
                    yield break;
                }
                if (status == GbaLibretroNativeBindings.NoFrame)
                {
                    yield return null;
                    continue;
                }
                if (status != GbaLibretroNativeBindings.Ok)
                {
                    result.Failure = "adapter frame acquire returned an unexpected status";
                    yield break;
                }

                var valid = GbaLibretroFixtureProbePolicy.ValidateLease(
                    lease.Sequence, lease.ResolvedRgba.Rgba8, lease.ResolvedRgba.Width,
                    lease.ResolvedRgba.Height, lease.ResolvedRgba.RowStrideBytes) == GbaLibretroFixtureProbeResult.Success;
                if (!valid)
                {
                    TryReleaseFrame(ref lease, out _);
                    result.Failure = "adapter returned an invalid resolved-frame lease";
                    yield break;
                }
                if (lease.Sequence <= afterSequence)
                {
                    if (!TryReleaseFrame(ref lease, out error))
                    {
                        result.Failure = error;
                        yield break;
                    }
                    yield return null;
                    continue;
                }

                var capturedSequence = lease.Sequence;
                Marshal.Copy(lease.ResolvedRgba.Rgba8, _frameCopy, 0, _frameCopy.Length);
                if (!TryReleaseFrame(ref lease, out error))
                {
                    result.Failure = error;
                    yield break;
                }
                result.Sequence = capturedSequence;
                result.Hash = FrameHash.ComputeFnv1a32(_frameCopy);
                result.Samples = DescribeProbeSamples();
                result.SourceDiagnostic = GbaLibretroCertificationBatchPolicy.DiagnoseSourceCheckerboard(_frameCopy);
                result.Succeeded = true;
                yield break;
            }
            result.Failure = "adapter did not publish a new resolved frame before the deadline";
        }

        private bool TryCreateAndStart(string fixturePath, out string error)
        {
            error = null;
            try
            {
                _adapter = GbaLibretroNativeBindings.Create(GbaNativeBindings.AbiVersion);
                if (_adapter == IntPtr.Zero) { error = "adapter create returned null"; return false; }
                if (GbaLibretroNativeBindings.Start(_adapter, fixturePath, GbaLibretroFixtureProbePolicy.FramesPerSecond) != GbaLibretroNativeBindings.Ok)
                {
                    error = "adapter start rejected the approved synthetic fixture";
                    DestroyAdapter();
                    return false;
                }
                return true;
            }
            catch (Exception exception)
            {
                error = DescribeException(exception);
                DestroyAdapter();
                return false;
            }
        }

        private bool TryState(out int state, out string error)
        {
            state = 0; error = null;
            try { state = GbaLibretroNativeBindings.State(_adapter); return true; }
            catch (Exception exception) { error = DescribeException(exception); return false; }
        }

        private bool TrySetJoypadMask(uint mask, out string error)
        {
            error = null;
            try
            {
                // The fixture speaks in the frontend's GBA key bits, while
                // the isolated adapter accepts libretro RetroPad IDs. Keep
                // this batch on the same translation boundary as the normal
                // GbaLibretroAdapterSession; passing GBA A (bit 0) directly
                // would press RetroPad B instead of RetroPad A (bit 8).
                GbaLibretroNativeBindings.SetJoypadMask(
                    _adapter,
                    GbaLibretroJoypadPolicy.TranslateGbaMask(mask));
                return true;
            }
            catch (Exception exception) { error = DescribeException(exception); return false; }
        }

        private bool TrySaveState(string path, out string error)
        {
            error = null;
            try
            {
                if (GbaLibretroNativeBindings.SaveState(_adapter, path) == GbaLibretroNativeBindings.Ok) return true;
                error = "adapter worker rejected the fixture save-state request";
                return false;
            }
            catch (Exception exception) { error = DescribeException(exception); return false; }
        }

        private bool TryLoadState(string path, out string error)
        {
            error = null;
            try
            {
                if (GbaLibretroNativeBindings.LoadState(_adapter, path) == GbaLibretroNativeBindings.Ok) return true;
                error = "adapter worker rejected the fixture load-state request";
                return false;
            }
            catch (Exception exception) { error = DescribeException(exception); return false; }
        }

        private bool TryGetAudio(out uint rate, out uint available, out string error)
        {
            rate = 0U; available = 0U; error = null;
            try
            {
                rate = GbaLibretroNativeBindings.AudioSampleRate(_adapter);
                available = GbaLibretroNativeBindings.AudioAvailableFrames(_adapter);
                return true;
            }
            catch (Exception exception) { error = DescribeException(exception); return false; }
        }

        private bool TryAcquireFrame(out int status, out GbaNativeBindings.FrameLease lease, out string error)
        {
            status = GbaLibretroNativeBindings.NoFrame; lease = default; error = null;
            try { status = GbaLibretroNativeBindings.AcquireLatestFrame(_adapter, out lease); return true; }
            catch (Exception exception) { error = DescribeException(exception); return false; }
        }

        private bool TryReleaseFrame(ref GbaNativeBindings.FrameLease lease, out string error)
        {
            error = null;
            try
            {
                if (GbaLibretroNativeBindings.ReleaseFrame(_adapter, ref lease) == GbaLibretroNativeBindings.Ok) return true;
                error = "adapter rejected the paired frame release";
                return false;
            }
            catch (Exception exception) { error = DescribeException(exception); return false; }
        }

        private static bool TryEnsureDirectory(string directory)
        {
            try { Directory.CreateDirectory(directory); return true; }
            catch (Exception) { return false; }
        }

        private static void CopyFrameCapture(FrameCapture source, FrameCapture destination)
        {
            destination.Succeeded = source != null && source.Succeeded;
            destination.Sequence = source?.Sequence ?? 0UL;
            destination.Hash = source?.Hash ?? 0U;
            destination.Failure = source?.Failure;
            destination.Samples = source?.Samples;
            destination.SourceDiagnostic = source?.SourceDiagnostic ?? default;
        }

        private string DescribeProbeSamples() =>
            $"(0,0)={DescribeProbePixel(0, 0)},(16,0)={DescribeProbePixel(16, 0)}," +
            $"(0,16)={DescribeProbePixel(0, 16)},(16,16)={DescribeProbePixel(16, 16)}";

        private string DescribeProbePixel(int x, int y)
        {
            var offset = (y * GbaTestFrameSurface.Width + x) * 4;
            return $"{_frameCopy[offset]:X2}{_frameCopy[offset + 1]:X2}{_frameCopy[offset + 2]:X2}{_frameCopy[offset + 3]:X2}";
        }

        private static bool AllSucceeded(params FrameCapture[] captures)
        {
            foreach (var capture in captures) if (capture == null || !capture.Succeeded) return false;
            return true;
        }

        private static string FirstFailure(params FrameCapture[] captures)
        {
            foreach (var capture in captures)
            {
                if (capture == null || !capture.Succeeded) return capture?.Failure ?? "missing frame capture";
            }
            return "unknown frame capture failure";
        }

        private static string DescribeException(Exception exception) =>
            $"adapter native call failed ({exception.GetType().Name})";

        private void DestroyAdapter()
        {
            if (_adapter == IntPtr.Zero) return;
            try { GbaLibretroNativeBindings.Destroy(_adapter); }
            catch (Exception) { }
            finally { _adapter = IntPtr.Zero; }
        }

        private void ExitProbeGate()
        {
            if (!_ownsProbeGate) return;
            _ownsProbeGate = false;
            GbaLibretroAdapterProbeGate.Exit();
        }
    }
}

using System;
using System.Collections;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Networking;

namespace GbaXr
{
    /// <summary>
    /// Native mGBA runs on its own producer thread. Unity leases complete resolved
    /// frames on the main thread, uploads them, and checks fixture contracts.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GbaTestFrameSurface))]
    public sealed class GbaNativeFrameDriver : MonoBehaviour
    {
        private const string SyntheticFixturePath = "gba-fixtures/frame_sequence.gba";
        private const string Bg0PriorityFixturePath = "gba-fixtures/bg0_priority.gba";
        private const string BgTieFixturePath = "gba-fixtures/bg_tie.gba";
        private const string ObjPriorityFixturePath = "gba-fixtures/obj_priority_halfword.gba";
        private const string AlphaBlendFixturePath = "gba-fixtures/alpha_blend.gba";
        private const string BrightenFixturePath = "gba-fixtures/brighten.gba";
        private const string DarkenFixturePath = "gba-fixtures/darken.gba";
        private const string WindowAlphaFixturePath = "gba-fixtures/window_alpha.gba";
        private const string AffineBg2FixturePath = "gba-fixtures/affine_bg2.gba";
        private const string ObjAlphaFixturePath = "gba-fixtures/obj_alpha.gba";
        // Captured from the pinned mGBA adapter on the physical Galaxy XR.
        // This is intentionally distinct from the source-model hash in the
        // EditMode test: the device golden is the resolved emulator output.
        private const uint SyntheticFixtureExpectedHash = 0x9B341219u;
        private const uint Bg0PriorityExpectedHash = 0x87A1ADC5u;
        private const uint BgTieExpectedHash = 0x0BB5FDC5u;
        private const uint ObjPriorityExpectedHash = 0xEBBEADC5u;
        private const uint AlphaBlendExpectedHash = 0xC4D18DC5u;
        private const uint BrightenExpectedHash = 0x06A215C5u;
        private const uint DarkenExpectedHash = 0x86158DC5u;
        private const uint WindowAlphaExpectedHash = 0x4738CBC5u;
        private const uint AffineBg2ExpectedHash = 0x87A1ADC5u;
        private const uint ObjAlphaExpectedHash = 0x28C36DC5u;
        private const int HashVerificationFrame = 5;

        [Tooltip("Relative path below Application.persistentDataPath. Use only a user-provided homebrew/test fixture.")]
        [SerializeField] private string romRelativePath = SyntheticFixturePath;

        [SerializeField, Range(1, 120)] private int emulatedFramesPerSecond = 60;

        private readonly byte[] _rgbaFrame = new byte[GbaTestFrameSurface.Width * GbaTestFrameSurface.Height * 4];
        private readonly byte[] _partitionComposite = new byte[GbaTestFrameSurface.Width * GbaTestFrameSurface.Height * 4];
        private readonly byte[] _partitionCounts = new byte[GbaTestFrameSurface.Width * GbaTestFrameSurface.Height];
        private GbaTestFrameSurface _surface;
        private IntPtr _bridge;
        private IntPtr _worker;
        private bool _started;
        private Coroutine _initializationRoutine;
        private int _presentedFrameCount;
        private bool _verifiedFixtureHash;
        private bool _verifiedProvenanceContract;
        private bool _verifiedVisiblePartition;
        private bool _verifiedIndependentBg0;
        private bool _verifiedIndependentBgTie;
        private bool _verifiedIndependentObj;

        private void Awake()
        {
            _surface = GetComponent<GbaTestFrameSurface>();
        }

        private void OnEnable()
        {
            if (Application.platform != RuntimePlatform.Android)
            {
                return;
            }

            _initializationRoutine = StartCoroutine(InitializeFromFixture());
        }

        private IEnumerator InitializeFromFixture()
        {
            var romPath = Path.Combine(Application.persistentDataPath, romRelativePath);
            // Packaged synthetic fixtures are versioned source artifacts. Refresh them on
            // each launch so a rebuilt APK cannot accidentally run an earlier persistent
            // copy. Other paths retain their persistent-file behavior for user fixtures.
            var isPackagedSyntheticFixture = romRelativePath.StartsWith("gba-fixtures/", StringComparison.Ordinal);
            if (!File.Exists(romPath) || isPackagedSyntheticFixture)
            {
                yield return CopyPackagedFixtureIfPresent(romPath);
            }

            if (!File.Exists(romPath))
            {
                Debug.LogWarning($"GBA XR: no fixture ROM at {romPath}. The diagnostic frame remains visible.");
                _initializationRoutine = null;
                yield break;
            }

            try
            {
                _bridge = GbaNativeBindings.gbaxr_create_unbound(GbaNativeBindings.AbiVersion);
                if (_bridge == IntPtr.Zero)
                {
                    Debug.LogError("GBA XR: could not create the native frame bridge.");
                    yield break;
                }

                _worker = GbaNativeBindings.gbaxr_mgba_worker_start(
                    _bridge,
                    romPath,
                    (uint)emulatedFramesPerSecond);
                if (_worker == IntPtr.Zero)
                {
                    Debug.LogError("GBA XR: could not start the native mGBA worker.");
                    DisposeNative();
                    yield break;
                }

                _started = true;
            }
            catch (DllNotFoundException exception)
            {
                Debug.LogError($"GBA XR: native Android plug-in is unavailable. {exception.Message}");
                DisposeNative();
            }
            finally
            {
                _initializationRoutine = null;
            }
        }

        private IEnumerator CopyPackagedFixtureIfPresent(string destinationPath)
        {
            var sourcePath = Path.Combine(Application.streamingAssetsPath, romRelativePath);
            using (var request = UnityWebRequest.Get(sourcePath))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    yield break;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                File.WriteAllBytes(destinationPath, request.downloadHandler.data);
                Debug.Log($"GBA XR: copied packaged fixture to {destinationPath}.");
            }
        }

        private void Update()
        {
            if (!_started)
            {
                return;
            }

            var workerState = GbaNativeBindings.gbaxr_mgba_worker_state(_worker);
            if (workerState < 0)
            {
                Debug.LogError($"GBA XR: native mGBA worker stopped with state {workerState}.");
                _started = false;
                return;
            }

            if (GbaNativeBindings.gbaxr_acquire_latest_frame(_bridge, out var lease) != GbaNativeBindings.Ok)
            {
                return;
            }

            try
            {
                if (lease.ResolvedRgba.Width != GbaTestFrameSurface.Width ||
                    lease.ResolvedRgba.Height != GbaTestFrameSurface.Height ||
                    lease.ResolvedRgba.RowStrideBytes != GbaTestFrameSurface.Width * 4)
                {
                    Debug.LogError("GBA XR: native frame dimensions do not match the GBA contract.");
                    _started = false;
                    return;
                }

                Marshal.Copy(lease.ResolvedRgba.Rgba8, _rgbaFrame, 0, _rgbaFrame.Length);
                _surface.UploadRgba32(_rgbaFrame);
                VerifySyntheticFixtureHash(lease);
                VerifyBg0PriorityProvenanceContract(lease);
                VerifyBgTieProvenanceContract(lease);
                VerifyObjPriorityProvenanceContract(lease);
                VerifyAlphaBlendProvenanceContract(lease);
                VerifyBrightenProvenanceContract(lease);
                VerifyDarkenProvenanceContract(lease);
                VerifyWindowAlphaProvenanceContract(lease);
                VerifyAffineBg2ProvenanceContract(lease);
                VerifyObjAlphaProvenanceContract(lease);
                VerifyIndependentBg0Candidate(lease);
                VerifyIndependentBgTieCandidates(lease);
                VerifyIndependentObjCandidates(lease);
                VerifyVisibleSourcePartition(lease);
            }
            finally
            {
                GbaNativeBindings.gbaxr_release_frame(_bridge, ref lease);
            }
        }

        private void VerifySyntheticFixtureHash(GbaNativeBindings.FrameLease lease)
        {
            _presentedFrameCount++;
            if (_verifiedFixtureHash || _presentedFrameCount < HashVerificationFrame ||
                !string.Equals(romRelativePath, SyntheticFixturePath, StringComparison.Ordinal))
            {
                return;
            }

            _verifiedFixtureHash = true;
            var actualHash = FrameHash.ComputeFnv1a32(_rgbaFrame);
            if (actualHash == SyntheticFixtureExpectedHash)
            {
                Debug.Log($"GBA XR: synthetic fixture golden hash verified: 0x{actualHash:X8}.");
                LogProvenanceProbe(lease);
                return;
            }

            Debug.LogError(
                $"GBA XR: synthetic fixture golden mismatch. Expected 0x{SyntheticFixtureExpectedHash:X8}, " +
                $"received 0x{actualHash:X8}. " +
                $"Samples: (0,0)={DescribePixel(0, 0)}, (16,0)={DescribePixel(16, 0)}, " +
                $"(0,16)={DescribePixel(0, 16)}, (16,16)={DescribePixel(16, 16)}.");
        }

        private static void LogProvenanceProbe(GbaNativeBindings.FrameLease lease)
        {
            if (lease.Provenance == IntPtr.Zero || lease.ProvenanceStrideBytes != GbaTestFrameSurface.Width * 4)
            {
                Debug.LogError("GBA XR: provenance plane is unavailable or has an invalid stride.");
                return;
            }

            var source = Marshal.ReadByte(lease.Provenance, 0);
            var priority = Marshal.ReadByte(lease.Provenance, 1);
            var blend = Marshal.ReadByte(lease.Provenance, 2);
            var window = Marshal.ReadByte(lease.Provenance, 3);
            Debug.Log(
                $"GBA XR: provenance probe (0,0): source={source}, priority={priority}, " +
                $"blend={blend}, window=0x{window:X2}.");
        }

        private void VerifyBg0PriorityProvenanceContract(GbaNativeBindings.FrameLease lease)
        {
            if (_verifiedProvenanceContract || _presentedFrameCount < HashVerificationFrame ||
                !string.Equals(romRelativePath, Bg0PriorityFixturePath, StringComparison.Ordinal))
            {
                return;
            }

            _verifiedProvenanceContract = true;
            if (lease.Provenance == IntPtr.Zero ||
                lease.ProvenanceStrideBytes != GbaTestFrameSurface.Width * 4)
            {
                Debug.LogError("GBA XR: BG0 fixture provenance plane is unavailable or has an invalid stride.");
                return;
            }

            var actualHash = FrameHash.ComputeFnv1a32(_rgbaFrame);
            var provenance = new byte[_rgbaFrame.Length];
            Marshal.Copy(lease.Provenance, provenance, 0, provenance.Length);
            for (var pixel = 0; pixel < GbaTestFrameSurface.Width * GbaTestFrameSurface.Height; pixel++)
            {
                var offset = pixel * 4;
                if (provenance[offset] == 1 && provenance[offset + 1] == 2 && provenance[offset + 2] == 0 &&
                    _rgbaFrame[offset] == 0 && _rgbaFrame[offset + 1] == 255 &&
                    _rgbaFrame[offset + 2] == 255 && _rgbaFrame[offset + 3] == 255)
                {
                    continue;
                }

                var x = pixel % GbaTestFrameSurface.Width;
                var y = pixel / GbaTestFrameSurface.Width;
                Debug.LogError(
                    $"GBA XR: BG0 provenance contract failed at ({x},{y}). Expected source=1, priority=2, " +
                    $"blend=0, rgba=00FFFFFF; received source={provenance[offset]}, " +
                    $"priority={provenance[offset + 1]}, blend={provenance[offset + 2]}, " +
                    $"rgba={DescribePixel(x, y)}, candidateHash=0x{actualHash:X8}.");
                return;
            }

            if (actualHash == Bg0PriorityExpectedHash)
            {
                Debug.Log(
                    $"GBA XR: BG0 provenance golden verified: all 38400 pixels source=1, priority=2, " +
                    $"blend=0, rgba=00FFFFFF, hash=0x{actualHash:X8}.");
                return;
            }

            Debug.LogError(
                $"GBA XR: BG0 provenance golden mismatch. Expected 0x{Bg0PriorityExpectedHash:X8}, " +
                $"received 0x{actualHash:X8}; source/priority/color contract did pass.");
        }

        private void VerifyBgTieProvenanceContract(GbaNativeBindings.FrameLease lease)
        {
            if (_verifiedProvenanceContract || _presentedFrameCount < HashVerificationFrame ||
                !string.Equals(romRelativePath, BgTieFixturePath, StringComparison.Ordinal))
            {
                return;
            }

            _verifiedProvenanceContract = true;
            if (lease.Provenance == IntPtr.Zero ||
                lease.ProvenanceStrideBytes != GbaTestFrameSurface.Width * 4)
            {
                Debug.LogError("GBA XR: BG tie fixture provenance plane is unavailable or has an invalid stride.");
                return;
            }

            var actualHash = FrameHash.ComputeFnv1a32(_rgbaFrame);
            var provenance = new byte[_rgbaFrame.Length];
            Marshal.Copy(lease.Provenance, provenance, 0, provenance.Length);
            for (var pixel = 0; pixel < GbaTestFrameSurface.Width * GbaTestFrameSurface.Height; pixel++)
            {
                var offset = pixel * 4;
                var x = pixel % GbaTestFrameSurface.Width;
                var expectBg0 = x < GbaTestFrameSurface.Width / 2;
                var expectedSource = expectBg0 ? 1 : 2;
                var expectedRed = expectBg0 ? 0 : 255;
                var expectedGreen = expectBg0 ? 255 : 0;
                var expectedBlue = 255;
                if (provenance[offset] == expectedSource && provenance[offset + 1] == 2 &&
                    provenance[offset + 2] == 0 && _rgbaFrame[offset] == expectedRed &&
                    _rgbaFrame[offset + 1] == expectedGreen && _rgbaFrame[offset + 2] == expectedBlue &&
                    _rgbaFrame[offset + 3] == 255)
                {
                    continue;
                }

                var y = pixel / GbaTestFrameSurface.Width;
                Debug.LogError(
                    $"GBA XR: BG tie provenance contract failed at ({x},{y}). Expected source={expectedSource}, " +
                    $"priority=2, blend=0, rgba={expectedRed:X2}{expectedGreen:X2}{expectedBlue:X2}FF; " +
                    $"received source={provenance[offset]}, priority={provenance[offset + 1]}, " +
                    $"blend={provenance[offset + 2]}, rgba={DescribePixel(x, y)}, " +
                    $"candidateHash=0x{actualHash:X8}.");
                return;
            }

            if (actualHash == BgTieExpectedHash)
            {
                Debug.Log(
                    $"GBA XR: BG tie provenance golden verified: BG0 wins equal-priority left half; " +
                    $"BG1 is visible right half; hash=0x{actualHash:X8}.");
                return;
            }

            Debug.LogError(
                $"GBA XR: BG tie provenance golden mismatch. Expected 0x{BgTieExpectedHash:X8}, " +
                $"received 0x{actualHash:X8}; the source/priority/color contract did pass.");
        }

        private void VerifyObjPriorityProvenanceContract(GbaNativeBindings.FrameLease lease)
        {
            if (_verifiedProvenanceContract || _presentedFrameCount < HashVerificationFrame ||
                !string.Equals(romRelativePath, ObjPriorityFixturePath, StringComparison.Ordinal))
            {
                return;
            }

            _verifiedProvenanceContract = true;
            if (lease.Provenance == IntPtr.Zero ||
                lease.ProvenanceStrideBytes != GbaTestFrameSurface.Width * 4)
            {
                Debug.LogError("GBA XR: OBJ priority fixture provenance plane is unavailable or has an invalid stride.");
                return;
            }

            var actualHash = FrameHash.ComputeFnv1a32(_rgbaFrame);
            var provenance = new byte[_rgbaFrame.Length];
            Marshal.Copy(lease.Provenance, provenance, 0, provenance.Length);
            for (var pixel = 0; pixel < GbaTestFrameSurface.Width * GbaTestFrameSurface.Height; pixel++)
            {
                var offset = pixel * 4;
                var x = pixel % GbaTestFrameSurface.Width;
                var y = pixel / GbaTestFrameSurface.Width;
                var expectObj = x < 64 && y < 64;
                var expectedSource = expectObj ? 5 : 1;
                var expectedRed = expectObj ? 255 : 0;
                var expectedGreen = expectObj ? 0 : 255;
                var expectedBlue = 255;
                if (provenance[offset] == expectedSource && provenance[offset + 1] == 2 &&
                    provenance[offset + 2] == 0 && _rgbaFrame[offset] == expectedRed &&
                    _rgbaFrame[offset + 1] == expectedGreen && _rgbaFrame[offset + 2] == expectedBlue &&
                    _rgbaFrame[offset + 3] == 255)
                {
                    continue;
                }

                Debug.LogError(
                    $"GBA XR: OBJ priority provenance contract failed at ({x},{y}). Expected source={expectedSource}, " +
                    $"priority=2, blend=0, rgba={expectedRed:X2}{expectedGreen:X2}{expectedBlue:X2}FF; " +
                    $"received source={provenance[offset]}, priority={provenance[offset + 1]}, " +
                    $"blend={provenance[offset + 2]}, rgba={DescribePixel(x, y)}, " +
                    $"candidateHash=0x{actualHash:X8}.");
                return;
            }

            if (actualHash == ObjPriorityExpectedHash)
            {
                Debug.Log(
                    $"GBA XR: OBJ priority provenance golden verified: OBJ wins the 64x64 equal-priority square; " +
                    $"BG0 remains visible elsewhere; hash=0x{actualHash:X8}.");
                return;
            }

            Debug.LogError(
                $"GBA XR: OBJ priority provenance golden mismatch. Expected 0x{ObjPriorityExpectedHash:X8}, " +
                $"received 0x{actualHash:X8}; the source/priority/color contract did pass.");
        }

        private void VerifyAlphaBlendProvenanceContract(GbaNativeBindings.FrameLease lease)
        {
            if (_verifiedProvenanceContract || _presentedFrameCount < HashVerificationFrame ||
                !string.Equals(romRelativePath, AlphaBlendFixturePath, StringComparison.Ordinal))
            {
                return;
            }

            _verifiedProvenanceContract = true;
            if (lease.Provenance == IntPtr.Zero ||
                lease.ProvenanceStrideBytes != GbaTestFrameSurface.Width * 4)
            {
                Debug.LogError("GBA XR: alpha-blend fixture provenance plane is unavailable or has an invalid stride.");
                return;
            }

            var actualHash = FrameHash.ComputeFnv1a32(_rgbaFrame);
            var provenance = new byte[_rgbaFrame.Length];
            Marshal.Copy(lease.Provenance, provenance, 0, provenance.Length);
            for (var pixel = 0; pixel < GbaTestFrameSurface.Width * GbaTestFrameSurface.Height; pixel++)
            {
                var offset = pixel * 4;
                if (provenance[offset] == 1 && provenance[offset + 1] == 1 && provenance[offset + 2] == 1 &&
                    _rgbaFrame[offset] == 127 && _rgbaFrame[offset + 1] == 0 &&
                    _rgbaFrame[offset + 2] == 127 && _rgbaFrame[offset + 3] == 255)
                {
                    continue;
                }

                var x = pixel % GbaTestFrameSurface.Width;
                var y = pixel / GbaTestFrameSurface.Width;
                Debug.LogError(
                    $"GBA XR: alpha-blend provenance contract failed at ({x},{y}). Expected source=1, priority=1, " +
                    $"blend=1, rgba=7F007FFF; received source={provenance[offset]}, " +
                    $"priority={provenance[offset + 1]}, blend={provenance[offset + 2]}, " +
                    $"rgba={DescribePixel(x, y)}, candidateHash=0x{actualHash:X8}.");
                return;
            }

            if (actualHash == AlphaBlendExpectedHash)
            {
                Debug.Log(
                    $"GBA XR: alpha-blend provenance golden verified: all 38400 pixels front=BG0, " +
                    $"priority=1, blend=alpha, rgba=7F007FFF, hash=0x{actualHash:X8}.");
                return;
            }

            Debug.LogError(
                $"GBA XR: alpha-blend provenance golden mismatch. Expected 0x{AlphaBlendExpectedHash:X8}, " +
                $"received 0x{actualHash:X8}; the source/priority/blend/color contract did pass.");
        }

        private void VerifyBrightenProvenanceContract(GbaNativeBindings.FrameLease lease)
        {
            if (_verifiedProvenanceContract || _presentedFrameCount < HashVerificationFrame ||
                !string.Equals(romRelativePath, BrightenFixturePath, StringComparison.Ordinal))
            {
                return;
            }

            _verifiedProvenanceContract = true;
            if (lease.Provenance == IntPtr.Zero ||
                lease.ProvenanceStrideBytes != GbaTestFrameSurface.Width * 4)
            {
                Debug.LogError("GBA XR: brighten fixture provenance plane is unavailable or has an invalid stride.");
                return;
            }

            var actualHash = FrameHash.ComputeFnv1a32(_rgbaFrame);
            var provenance = new byte[_rgbaFrame.Length];
            Marshal.Copy(lease.Provenance, provenance, 0, provenance.Length);
            for (var pixel = 0; pixel < GbaTestFrameSurface.Width * GbaTestFrameSurface.Height; pixel++)
            {
                var offset = pixel * 4;
                if (provenance[offset] == 1 && provenance[offset + 1] == 1 && provenance[offset + 2] == 2 &&
                    _rgbaFrame[offset] == 255 && _rgbaFrame[offset + 1] == 127 &&
                    _rgbaFrame[offset + 2] == 127 && _rgbaFrame[offset + 3] == 255)
                {
                    continue;
                }

                var x = pixel % GbaTestFrameSurface.Width;
                var y = pixel / GbaTestFrameSurface.Width;
                Debug.LogError(
                    $"GBA XR: brighten provenance contract failed at ({x},{y}). Expected source=1, priority=1, " +
                    $"blend=2, rgba=FF7F7FFF; received source={provenance[offset]}, " +
                    $"priority={provenance[offset + 1]}, blend={provenance[offset + 2]}, " +
                    $"rgba={DescribePixel(x, y)}, candidateHash=0x{actualHash:X8}.");
                return;
            }

            if (actualHash == BrightenExpectedHash)
            {
                Debug.Log(
                    $"GBA XR: brighten provenance golden verified: all 38400 pixels source=1, " +
                    $"priority=1, blend=brighten, rgba=FF7F7FFF, hash=0x{actualHash:X8}.");
                return;
            }

            Debug.LogError(
                $"GBA XR: brighten provenance golden mismatch. Expected 0x{BrightenExpectedHash:X8}, " +
                $"received 0x{actualHash:X8}; the source/priority/blend/color contract did pass.");
        }

        private void VerifyDarkenProvenanceContract(GbaNativeBindings.FrameLease lease)
        {
            if (_verifiedProvenanceContract || _presentedFrameCount < HashVerificationFrame ||
                !string.Equals(romRelativePath, DarkenFixturePath, StringComparison.Ordinal))
            {
                return;
            }

            _verifiedProvenanceContract = true;
            if (lease.Provenance == IntPtr.Zero ||
                lease.ProvenanceStrideBytes != GbaTestFrameSurface.Width * 4)
            {
                Debug.LogError("GBA XR: darken fixture provenance plane is unavailable or has an invalid stride.");
                return;
            }

            var actualHash = FrameHash.ComputeFnv1a32(_rgbaFrame);
            var provenance = new byte[_rgbaFrame.Length];
            Marshal.Copy(lease.Provenance, provenance, 0, provenance.Length);
            for (var pixel = 0; pixel < GbaTestFrameSurface.Width * GbaTestFrameSurface.Height; pixel++)
            {
                var offset = pixel * 4;
                if (provenance[offset] == 1 && provenance[offset + 1] == 1 && provenance[offset + 2] == 3 &&
                    _rgbaFrame[offset] == 128 && _rgbaFrame[offset + 1] == 0 &&
                    _rgbaFrame[offset + 2] == 0 && _rgbaFrame[offset + 3] == 255)
                {
                    continue;
                }

                var x = pixel % GbaTestFrameSurface.Width;
                var y = pixel / GbaTestFrameSurface.Width;
                Debug.LogError(
                    $"GBA XR: darken provenance contract failed at ({x},{y}). Expected source=1, priority=1, " +
                    $"blend=3, rgba=800000FF; received source={provenance[offset]}, " +
                    $"priority={provenance[offset + 1]}, blend={provenance[offset + 2]}, " +
                    $"rgba={DescribePixel(x, y)}, candidateHash=0x{actualHash:X8}.");
                return;
            }

            if (actualHash == DarkenExpectedHash)
            {
                Debug.Log(
                    $"GBA XR: darken provenance golden verified: all 38400 pixels source=1, " +
                    $"priority=1, blend=darken, rgba=800000FF, hash=0x{actualHash:X8}.");
                return;
            }

            Debug.LogError(
                $"GBA XR: darken provenance golden mismatch. Expected 0x{DarkenExpectedHash:X8}, " +
                $"received 0x{actualHash:X8}; the source/priority/blend/color contract did pass.");
        }

        private void VerifyWindowAlphaProvenanceContract(GbaNativeBindings.FrameLease lease)
        {
            if (_verifiedProvenanceContract || _presentedFrameCount < HashVerificationFrame ||
                !string.Equals(romRelativePath, WindowAlphaFixturePath, StringComparison.Ordinal))
            {
                return;
            }

            _verifiedProvenanceContract = true;
            if (lease.Provenance == IntPtr.Zero ||
                lease.ProvenanceStrideBytes != GbaTestFrameSurface.Width * 4)
            {
                Debug.LogError("GBA XR: window-alpha fixture provenance plane is unavailable or has an invalid stride.");
                return;
            }

            var actualHash = FrameHash.ComputeFnv1a32(_rgbaFrame);
            var provenance = new byte[_rgbaFrame.Length];
            Marshal.Copy(lease.Provenance, provenance, 0, provenance.Length);
            for (var pixel = 0; pixel < GbaTestFrameSurface.Width * GbaTestFrameSurface.Height; pixel++)
            {
                var offset = pixel * 4;
                var x = pixel % GbaTestFrameSurface.Width;
                var inWindow = x >= GbaTestFrameSurface.Width / 2;
                var expectedRed = inWindow ? 127 : 255;
                var expectedBlue = inWindow ? 127 : 0;
                var expectedWindow = inWindow ? 1 : 0;
                if (provenance[offset] == 1 && provenance[offset + 1] == 1 && provenance[offset + 2] == 1 &&
                    provenance[offset + 3] == expectedWindow && _rgbaFrame[offset] == expectedRed &&
                    _rgbaFrame[offset + 1] == 0 && _rgbaFrame[offset + 2] == expectedBlue &&
                    _rgbaFrame[offset + 3] == 255)
                {
                    continue;
                }

                var y = pixel / GbaTestFrameSurface.Width;
                Debug.LogError(
                    $"GBA XR: window-alpha provenance contract failed at ({x},{y}). Expected source=1, priority=1, " +
                    $"blend=1, window=0x{expectedWindow:X2}, rgba={expectedRed:X2}00{expectedBlue:X2}FF; " +
                    $"received source={provenance[offset]}, priority={provenance[offset + 1]}, " +
                    $"blend={provenance[offset + 2]}, window=0x{provenance[offset + 3]:X2}, " +
                    $"rgba={DescribePixel(x, y)}, candidateHash=0x{actualHash:X8}.");
                return;
            }

            if (actualHash == WindowAlphaExpectedHash)
            {
                Debug.Log(
                    $"GBA XR: window-alpha provenance golden verified: BG0 is red with blend disabled left of x=120 " +
                    $"and purple with blend enabled at x>=120; hash=0x{actualHash:X8}.");
                return;
            }

            Debug.LogError(
                $"GBA XR: window-alpha provenance golden mismatch. Expected 0x{WindowAlphaExpectedHash:X8}, " +
                $"received 0x{actualHash:X8}; the source/priority/blend/window/color contract did pass.");
        }

        private void VerifyAffineBg2ProvenanceContract(GbaNativeBindings.FrameLease lease)
        {
            if (_verifiedProvenanceContract || _presentedFrameCount < HashVerificationFrame ||
                !string.Equals(romRelativePath, AffineBg2FixturePath, StringComparison.Ordinal))
            {
                return;
            }

            _verifiedProvenanceContract = true;
            if (lease.Provenance == IntPtr.Zero ||
                lease.ProvenanceStrideBytes != GbaTestFrameSurface.Width * 4)
            {
                Debug.LogError("GBA XR: affine BG2 fixture provenance plane is unavailable or has an invalid stride.");
                return;
            }

            var actualHash = FrameHash.ComputeFnv1a32(_rgbaFrame);
            var provenance = new byte[_rgbaFrame.Length];
            Marshal.Copy(lease.Provenance, provenance, 0, provenance.Length);
            for (var pixel = 0; pixel < GbaTestFrameSurface.Width * GbaTestFrameSurface.Height; pixel++)
            {
                var offset = pixel * 4;
                if (provenance[offset] == 3 && provenance[offset + 1] == 2 && provenance[offset + 2] == 0 &&
                    _rgbaFrame[offset] == 0 && _rgbaFrame[offset + 1] == 255 &&
                    _rgbaFrame[offset + 2] == 255 && _rgbaFrame[offset + 3] == 255)
                {
                    continue;
                }

                var x = pixel % GbaTestFrameSurface.Width;
                var y = pixel / GbaTestFrameSurface.Width;
                Debug.LogError(
                    $"GBA XR: affine BG2 provenance contract failed at ({x},{y}). Expected source=3, priority=2, " +
                    $"blend=0, rgba=00FFFFFF; received source={provenance[offset]}, " +
                    $"priority={provenance[offset + 1]}, blend={provenance[offset + 2]}, " +
                    $"rgba={DescribePixel(x, y)}, candidateHash=0x{actualHash:X8}.");
                return;
            }

            if (actualHash == AffineBg2ExpectedHash)
            {
                Debug.Log(
                    $"GBA XR: affine BG2 provenance golden verified: all 38400 pixels source=3, priority=2, " +
                    $"blend=0, rgba=00FFFFFF, hash=0x{actualHash:X8}.");
                return;
            }

            Debug.LogError(
                $"GBA XR: affine BG2 provenance golden mismatch. Expected 0x{AffineBg2ExpectedHash:X8}, " +
                $"received 0x{actualHash:X8}; the source/priority/blend/color contract did pass.");
        }

        private void VerifyObjAlphaProvenanceContract(GbaNativeBindings.FrameLease lease)
        {
            if (_verifiedProvenanceContract || _presentedFrameCount < HashVerificationFrame ||
                !string.Equals(romRelativePath, ObjAlphaFixturePath, StringComparison.Ordinal))
            {
                return;
            }

            _verifiedProvenanceContract = true;
            if (lease.Provenance == IntPtr.Zero ||
                lease.ProvenanceStrideBytes != GbaTestFrameSurface.Width * 4)
            {
                Debug.LogError("GBA XR: OBJ alpha fixture provenance plane is unavailable or has an invalid stride.");
                return;
            }

            var actualHash = FrameHash.ComputeFnv1a32(_rgbaFrame);
            var provenance = new byte[_rgbaFrame.Length];
            Marshal.Copy(lease.Provenance, provenance, 0, provenance.Length);
            for (var pixel = 0; pixel < GbaTestFrameSurface.Width * GbaTestFrameSurface.Height; pixel++)
            {
                var offset = pixel * 4;
                var x = pixel % GbaTestFrameSurface.Width;
                var y = pixel / GbaTestFrameSurface.Width;
                var expectObj = x < 64 && y < 64;
                var expectedSource = expectObj ? 5 : 1;
                var expectedRed = expectObj ? 127 : 0;
                var expectedGreen = expectObj ? 127 : 255;
                var expectedBlue = 255;
                if (provenance[offset] == expectedSource && provenance[offset + 1] == 2 &&
                    provenance[offset + 2] == 1 && _rgbaFrame[offset] == expectedRed &&
                    _rgbaFrame[offset + 1] == expectedGreen && _rgbaFrame[offset + 2] == expectedBlue &&
                    _rgbaFrame[offset + 3] == 255)
                {
                    continue;
                }

                Debug.LogError(
                    $"GBA XR: OBJ alpha provenance contract failed at ({x},{y}). Expected source={expectedSource}, " +
                    $"priority=2, blend=1, rgba={expectedRed:X2}{expectedGreen:X2}{expectedBlue:X2}FF; " +
                    $"received source={provenance[offset]}, priority={provenance[offset + 1]}, " +
                    $"blend={provenance[offset + 2]}, rgba={DescribePixel(x, y)}, " +
                    $"candidateHash=0x{actualHash:X8}.");
                return;
            }

            if (actualHash == ObjAlphaExpectedHash)
            {
                Debug.Log(
                    $"GBA XR: OBJ alpha provenance golden verified: a semi-transparent magenta OBJ blends over cyan BG0 " +
                    $"in the 64x64 upper-left square; hash=0x{actualHash:X8}.");
                return;
            }

            Debug.LogError(
                $"GBA XR: OBJ alpha provenance golden mismatch. Expected 0x{ObjAlphaExpectedHash:X8}, " +
                $"received 0x{actualHash:X8}; the source/priority/blend/color contract did pass.");
        }

        private void VerifyVisibleSourcePartition(GbaNativeBindings.FrameLease lease)
        {
            if (_verifiedVisiblePartition || _presentedFrameCount < HashVerificationFrame ||
                lease.LayerSemantics != 1)
            {
                return;
            }

            _verifiedVisiblePartition = true;
            if (lease.Layers == IntPtr.Zero || lease.LayerCount == 0 || lease.LayerSemantics != 1 ||
                lease.LayerCount > 6 || lease.Provenance == IntPtr.Zero ||
                lease.ProvenanceStrideBytes != GbaTestFrameSurface.Width * 4)
            {
                Debug.LogError("GBA XR: visible-source partition is unavailable, not marked zero-parallax-only, or has an invalid lease shape.");
                return;
            }

            var provenance = new byte[_rgbaFrame.Length];
            Marshal.Copy(lease.Provenance, provenance, 0, provenance.Length);
            System.Array.Clear(_partitionCounts, 0, _partitionCounts.Length);
            var seenSources = 0;
            var layerSize = Marshal.SizeOf<GbaNativeBindings.LayerView>();
            var planes = new VisibleSourcePartitionCompositor.Plane[(int)lease.LayerCount];
            for (var layerIndex = 0; layerIndex < (int)lease.LayerCount; layerIndex++)
            {
                var layerPointer = IntPtr.Add(lease.Layers, layerIndex * layerSize);
                var layer = Marshal.PtrToStructure<GbaNativeBindings.LayerView>(layerPointer);
                if (layer.Source < 0 || layer.Source > 5 ||
                    layer.Color.Rgba8 == IntPtr.Zero || layer.CoverageMask == IntPtr.Zero ||
                    layer.Color.Width != GbaTestFrameSurface.Width ||
                    layer.Color.Height != GbaTestFrameSurface.Height ||
                    layer.Color.RowStrideBytes != GbaTestFrameSurface.Width * 4 ||
                    (seenSources & (1 << layer.Source)) != 0)
                {
                    Debug.LogError("GBA XR: visible-source partition contains an invalid or duplicate layer descriptor.");
                    return;
                }

                seenSources |= 1 << layer.Source;
                var color = new byte[_rgbaFrame.Length];
                var coverage = new byte[_partitionCounts.Length];
                Marshal.Copy(layer.Color.Rgba8, color, 0, color.Length);
                Marshal.Copy(layer.CoverageMask, coverage, 0, coverage.Length);
                planes[layerIndex] = new VisibleSourcePartitionCompositor.Plane(layer.Source, color, coverage);
                for (var pixel = 0; pixel < coverage.Length; pixel++)
                {
                    if (coverage[pixel] == 0)
                    {
                        continue;
                    }

                    var rgbaOffset = pixel * 4;
                    if (_partitionCounts[pixel] != 0 || provenance[rgbaOffset] != layer.Source ||
                        color[rgbaOffset] != _rgbaFrame[rgbaOffset] ||
                        color[rgbaOffset + 1] != _rgbaFrame[rgbaOffset + 1] ||
                        color[rgbaOffset + 2] != _rgbaFrame[rgbaOffset + 2] ||
                        color[rgbaOffset + 3] != _rgbaFrame[rgbaOffset + 3])
                    {
                        var x = pixel % GbaTestFrameSurface.Width;
                        var y = pixel / GbaTestFrameSurface.Width;
                        Debug.LogError(
                            $"GBA XR: visible-source partition mismatch at ({x},{y}); layer source={layer.Source}, " +
                            $"provenance source={provenance[rgbaOffset]}, resolved={DescribePixel(x, y)}.");
                        return;
                    }

                    _partitionCounts[pixel] = 1;
                }
            }

            for (var pixel = 0; pixel < _partitionCounts.Length; pixel++)
            {
                if (_partitionCounts[pixel] == 1)
                {
                    continue;
                }

                var x = pixel % GbaTestFrameSurface.Width;
                var y = pixel / GbaTestFrameSurface.Width;
                Debug.LogError($"GBA XR: visible-source partition leaves ({x},{y}) uncovered.");
                return;
            }

            if (!VisibleSourcePartitionCompositor.TryComposite(planes, _partitionComposite, out var compositorError) ||
                !FramesMatch(_partitionComposite, _rgbaFrame))
            {
                Debug.LogError($"GBA XR: visible-source reference compositor failed: {compositorError ?? "output differs from resolved frame"}.");
                return;
            }

            Debug.Log(
                $"GBA XR: visible-source partition verified: {lease.LayerCount} disjoint lease plane(s) " +
                "recompose the resolved frame at zero parallax.");
        }

        private void VerifyIndependentBg0Candidate(GbaNativeBindings.FrameLease lease)
        {
            if (_verifiedIndependentBg0 || _presentedFrameCount < HashVerificationFrame ||
                !string.Equals(romRelativePath, Bg0PriorityFixturePath, StringComparison.Ordinal))
            {
                return;
            }

            _verifiedIndependentBg0 = true;
            if (lease.LayerSemantics != 2 || lease.Layers == IntPtr.Zero || lease.LayerCount != 1 ||
                lease.Provenance == IntPtr.Zero || lease.ProvenanceStrideBytes != GbaTestFrameSurface.Width * 4)
            {
                Debug.LogError("GBA XR: BG0 raw candidate is unavailable or was not labeled as an independent candidate.");
                return;
            }

            var layer = Marshal.PtrToStructure<GbaNativeBindings.LayerView>(lease.Layers);
            if (layer.Source != 1 || layer.Priority != 2 || layer.Color.Rgba8 == IntPtr.Zero ||
                layer.CoverageMask == IntPtr.Zero || layer.Color.Width != GbaTestFrameSurface.Width ||
                layer.Color.Height != GbaTestFrameSurface.Height ||
                layer.Color.RowStrideBytes != GbaTestFrameSurface.Width * 4)
            {
                Debug.LogError("GBA XR: BG0 independent candidate lease has an invalid descriptor.");
                return;
            }

            var candidate = new byte[_rgbaFrame.Length];
            var coverage = new byte[_partitionCounts.Length];
            Marshal.Copy(layer.Color.Rgba8, candidate, 0, candidate.Length);
            Marshal.Copy(layer.CoverageMask, coverage, 0, coverage.Length);
            for (var pixel = 0; pixel < coverage.Length; pixel++)
            {
                if (coverage[pixel] == 255)
                {
                    continue;
                }

                var x = pixel % GbaTestFrameSurface.Width;
                var y = pixel / GbaTestFrameSurface.Width;
                Debug.LogError($"GBA XR: BG0 independent candidate has incomplete coverage at ({x},{y}).");
                return;
            }

            if (!FramesMatch(candidate, _rgbaFrame))
            {
                Debug.LogError("GBA XR: BG0 independent candidate does not byte-recompose the resolved frame.");
                return;
            }

            Debug.Log("GBA XR: raw BG0 candidate verified: complete coverage, priority=2, and byte-exact zero-parallax recomposition.");
        }

        private void VerifyIndependentBgTieCandidates(GbaNativeBindings.FrameLease lease)
        {
            if (_verifiedIndependentBgTie || _presentedFrameCount < HashVerificationFrame ||
                !string.Equals(romRelativePath, BgTieFixturePath, StringComparison.Ordinal)) return;
            _verifiedIndependentBgTie = true;
            if (lease.LayerSemantics != 2 || lease.LayerCount != 2 || lease.Layers == IntPtr.Zero)
            {
                Debug.LogError("GBA XR: BG tie raw candidates are unavailable or not marked independent."); return;
            }
            var size = Marshal.SizeOf<GbaNativeBindings.LayerView>();
            var bg0 = Marshal.PtrToStructure<GbaNativeBindings.LayerView>(lease.Layers);
            var bg1 = Marshal.PtrToStructure<GbaNativeBindings.LayerView>(IntPtr.Add(lease.Layers, size));
            if (bg0.Source != 1 || bg1.Source != 2 || bg0.Priority != 2 || bg1.Priority != 2 ||
                bg0.Color.Rgba8 == IntPtr.Zero || bg1.Color.Rgba8 == IntPtr.Zero ||
                bg0.CoverageMask == IntPtr.Zero || bg1.CoverageMask == IntPtr.Zero) { Debug.LogError("GBA XR: BG tie candidate descriptors are invalid."); return; }
            var c0 = new byte[_rgbaFrame.Length]; var c1 = new byte[_rgbaFrame.Length];
            var m0 = new byte[_partitionCounts.Length]; var m1 = new byte[_partitionCounts.Length];
            Marshal.Copy(bg0.Color.Rgba8, c0, 0, c0.Length); Marshal.Copy(bg1.Color.Rgba8, c1, 0, c1.Length);
            Marshal.Copy(bg0.CoverageMask, m0, 0, m0.Length); Marshal.Copy(bg1.CoverageMask, m1, 0, m1.Length);
            for (var pixel = 0; pixel < m0.Length; pixel++)
            {
                var selected = m0[pixel] != 0 ? c0 : c1;
                var offset = pixel * 4;
                if (m1[pixel] == 0 || selected[offset] != _rgbaFrame[offset] || selected[offset + 1] != _rgbaFrame[offset + 1] || selected[offset + 2] != _rgbaFrame[offset + 2]) { Debug.LogError($"GBA XR: BG tie raw candidate recomposition failed at pixel {pixel}."); return; }
            }
            Debug.Log("GBA XR: raw BG0/BG1 candidates verified: BG1 retains occluded pixels; BG0 wins the equal-priority tie at zero parallax.");
        }

        private void VerifyIndependentObjCandidates(GbaNativeBindings.FrameLease lease)
        {
            if (_verifiedIndependentObj || _presentedFrameCount < HashVerificationFrame ||
                !string.Equals(romRelativePath, ObjPriorityFixturePath, StringComparison.Ordinal)) return;
            _verifiedIndependentObj = true;
            if (lease.LayerSemantics != 2 || lease.LayerCount != 2 || lease.Layers == IntPtr.Zero) { Debug.LogError("GBA XR: OBJ/BG0 raw candidates are unavailable or not marked independent."); return; }
            var size = Marshal.SizeOf<GbaNativeBindings.LayerView>();
            var bg0 = Marshal.PtrToStructure<GbaNativeBindings.LayerView>(lease.Layers);
            var obj = Marshal.PtrToStructure<GbaNativeBindings.LayerView>(IntPtr.Add(lease.Layers, size));
            if (bg0.Source != 1 || obj.Source != 5 || bg0.Priority != 2 || obj.Priority != 2 || bg0.Color.Rgba8 == IntPtr.Zero || obj.Color.Rgba8 == IntPtr.Zero || bg0.CoverageMask == IntPtr.Zero || obj.CoverageMask == IntPtr.Zero) { Debug.LogError("GBA XR: OBJ/BG0 candidate descriptors are invalid."); return; }
            var bg = new byte[_rgbaFrame.Length]; var sprite = new byte[_rgbaFrame.Length];
            var bgMask = new byte[_partitionCounts.Length]; var objMask = new byte[_partitionCounts.Length];
            Marshal.Copy(bg0.Color.Rgba8, bg, 0, bg.Length); Marshal.Copy(obj.Color.Rgba8, sprite, 0, sprite.Length);
            Marshal.Copy(bg0.CoverageMask, bgMask, 0, bgMask.Length); Marshal.Copy(obj.CoverageMask, objMask, 0, objMask.Length);
            for (var pixel = 0; pixel < bgMask.Length; pixel++)
            {
                var selected = objMask[pixel] != 0 ? sprite : bg;
                var offset = pixel * 4;
                if (bgMask[pixel] == 0 || selected[offset] != _rgbaFrame[offset] || selected[offset + 1] != _rgbaFrame[offset + 1] || selected[offset + 2] != _rgbaFrame[offset + 2] || selected[offset + 3] != _rgbaFrame[offset + 3]) { Debug.LogError($"GBA XR: OBJ/BG0 raw candidate recomposition failed at pixel {pixel}."); return; }
            }
            Debug.Log("GBA XR: raw OBJ/BG0 candidates verified: OBJ wins the equal-priority 64x64 square with byte-exact zero-parallax recomposition.");
        }

        private static bool FramesMatch(byte[] left, byte[] right)
        {
            for (var index = 0; index < left.Length; index++)
            {
                if (left[index] != right[index])
                {
                    return false;
                }
            }
            return true;
        }

        private string DescribePixel(int x, int y)
        {
            var offset = (y * GbaTestFrameSurface.Width + x) * 4;
            return $"{_rgbaFrame[offset]:X2}{_rgbaFrame[offset + 1]:X2}{_rgbaFrame[offset + 2]:X2}{_rgbaFrame[offset + 3]:X2}";
        }

        private void OnDisable()
        {
            if (_initializationRoutine != null)
            {
                StopCoroutine(_initializationRoutine);
                _initializationRoutine = null;
            }
            DisposeNative();
        }

        private void DisposeNative()
        {
            _started = false;
            _presentedFrameCount = 0;
            _verifiedFixtureHash = false;
            _verifiedProvenanceContract = false;
            _verifiedVisiblePartition = false;
            _verifiedIndependentBg0 = false;
            _verifiedIndependentBgTie = false;
            _verifiedIndependentObj = false;
            if (_worker != IntPtr.Zero)
            {
                GbaNativeBindings.gbaxr_mgba_worker_destroy(_worker);
                _worker = IntPtr.Zero;
            }
            if (_bridge != IntPtr.Zero)
            {
                GbaNativeBindings.gbaxr_destroy(_bridge);
                _bridge = IntPtr.Zero;
            }
        }
    }
}

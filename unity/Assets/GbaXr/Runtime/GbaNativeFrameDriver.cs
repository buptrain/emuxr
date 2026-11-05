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
        // Captured from the pinned mGBA adapter on the physical Galaxy XR.
        // This is intentionally distinct from the source-model hash in the
        // EditMode test: the device golden is the resolved emulator output.
        private const uint SyntheticFixtureExpectedHash = 0x9B341219u;
        private const uint Bg0PriorityExpectedHash = 0x87A1ADC5u;
        private const int HashVerificationFrame = 5;

        [Tooltip("Relative path below Application.persistentDataPath. Use only a user-provided homebrew/test fixture.")]
        [SerializeField] private string romRelativePath = SyntheticFixturePath;

        [SerializeField, Range(1, 120)] private int emulatedFramesPerSecond = 60;

        private readonly byte[] _rgbaFrame = new byte[GbaTestFrameSurface.Width * GbaTestFrameSurface.Height * 4];
        private GbaTestFrameSurface _surface;
        private IntPtr _bridge;
        private IntPtr _worker;
        private bool _started;
        private Coroutine _initializationRoutine;
        private int _presentedFrameCount;
        private bool _verifiedFixtureHash;
        private bool _verifiedProvenanceContract;

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
            if (!File.Exists(romPath))
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

            Debug.Log(
                $"GBA XR: BG tie provenance contract verified: BG0 wins equal-priority left half; " +
                $"BG1 is visible right half; candidateHash=0x{actualHash:X8}.");
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

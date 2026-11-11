using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.InputSystem;

namespace GbaXr
{
    /// <summary>
    /// Native mGBA runs on its own producer thread. Unity leases complete resolved
    /// frames on the main thread, uploads them, and checks fixture contracts.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GbaTestFrameSurface))]
    [RequireComponent(typeof(GbaLayeredSurface))]
    public sealed class GbaNativeFrameDriver : MonoBehaviour
    {
        public enum PresentationMode
        {
            Auto = 0,
            Original = 1,
            // Value 2 was ForceProfile. Preserve the gap so existing scenes
            // with ForceUnprofiledCandidates and ForcePartition retain meaning.
            ForceUnprofiledCandidates = 3,
            ForcePartition = 4,
            // Value 5 was ForcePartitionExtended; retired scenes fall back to Auto.
            ForcePartitionParallax = 6,
            ForcePartitionExtendedParallax = 7
        }
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
        // Confirmed on physical Galaxy XR at the later settled lease below.
        // It intentionally matches the source-model RGBA checkerboard.
        private const uint SyntheticFixtureExpectedHash = 0xF9838FC5u;
        private const uint Bg0PriorityExpectedHash = 0x87A1ADC5u;
        private const uint BgTieExpectedHash = 0x0BB5FDC5u;
        private const uint ObjPriorityExpectedHash = 0xEBBEADC5u;
        private const uint AlphaBlendExpectedHash = 0xC4D18DC5u;
        private const uint BrightenExpectedHash = 0x06A215C5u;
        private const uint DarkenExpectedHash = 0x86158DC5u;
        private const uint WindowAlphaExpectedHash = 0x4738CBC5u;
        private const uint AffineBg2ExpectedHash = 0x87A1ADC5u;
        private const uint ObjAlphaExpectedHash = 0x28C36DC5u;
        // The resolved frame_sequence fixture uses a later evidence gate than
        // PPU/provenance fixture contracts below.
        private const int SyntheticFixtureVerificationLease = 30;
        private const int HashVerificationFrame = 5;
        // ~85 ms at 48 kHz: enough margin for mGBA's ~31 ms postAudioBuffer
        // cadence, while remaining below the 8,192-frame native ring capacity.
        private const uint AudioPrimeFrames = 4096U;
        private const uint FixedEmulationFramesPerSecond = 60U;
        private const float AudioLivenessIntervalSeconds = 2f;
        private int AdminConsoleRowCount => IsLibretroCoreSelectionAvailable ? 7 : 6;
        private int AdminCoreRow => 3;
        private int AdminLibraryRow => IsLibretroCoreSelectionAvailable ? 4 : 3;
        private int AdminApplyRow => IsLibretroCoreSelectionAvailable ? 5 : 4;
        private int AdminCloseRow => IsLibretroCoreSelectionAvailable ? 6 : 5;
        private bool IsLibretroCoreSelectionAvailable => GbaCoreSelectionPolicy.CanExposeCoreSelection(
            GbaLibretroReadinessStore.Load(),
            IsUncertifiedDiagnosticPreviewEnabled);
        private bool IsUncertifiedDiagnosticPreviewEnabled =>
            _diagnosticPreview != null && _diagnosticPreview.IsEnabled &&
            !GbaLibretroReadinessStore.Load().IsLaunchEligible;

        [Tooltip("Relative path below Application.persistentDataPath. Use only a user-provided homebrew/test fixture.")]
        [SerializeField] private string romRelativePath = SyntheticFixturePath;

        [SerializeField]
        [Tooltip("Diagnostic only: for frame_sequence.gba, also log direct resolved-frame evidence at leases 30, 60, and 120. Default off; never runs for user ROMs.")]
        private bool enableSyntheticFixtureTemporalSweep;

        private readonly byte[] _rgbaFrame = new byte[GbaTestFrameSurface.Width * GbaTestFrameSurface.Height * 4];
        private readonly byte[] _partitionComposite = new byte[GbaTestFrameSurface.Width * GbaTestFrameSurface.Height * 4];
        private readonly byte[] _partitionCounts = new byte[GbaTestFrameSurface.Width * GbaTestFrameSurface.Height];
        private GbaTestFrameSurface _surface;
        private GbaLayeredSurface _layeredSurface;
        private IGbaNativeSession _nativeSession;
        private bool _started;
        private Coroutine _initializationRoutine;
        private int _presentedFrameCount;
        private bool _verifiedFixtureHash;
        private bool _syntheticFixtureTemporalSweepActive;
        private bool _verifiedProvenanceContract;
        private bool _verifiedVisiblePartition;
        private bool _forcePartitionVerified;
        private bool _verifiedIndependentBg0;
        private bool _verifiedIndependentBgTie;
        private bool _verifiedIndependentObj;
        [SerializeField] private bool showDeveloperOverlay = true;
        [SerializeField] private PresentationMode presentationMode = PresentationMode.Auto;
        private bool _profileActive;
        private bool _usingForcePartition;
        private bool _usingExtendedForcePartition;
        private bool _usingForcePartitionParallax;
        private bool _hasForcePartitionParallaxReference;
        private float _forcePartitionParallaxReferenceCameraLocalX;
        private float _filteredForcePartitionParallaxCameraLocalX;
        private Vector3 _forcePartitionParallaxReferenceScreenPosition;
        private Quaternion _forcePartitionParallaxReferenceScreenRotation;
        private bool _loggedUnprofiledRomFallback;
        private bool _loggedForcedUnprofiledCandidates;
        private bool _loggedForcePartition;
        private PresentationMode _lastNonResolvedPresentationMode;
        private GbaAdminConsoleModel _adminConsole;
        private GbaAdminConsoleView _adminConsoleView;
        private int _adminConsoleSelectedRow;
        private GbaStartupMenuModel _startupMenu;
        private GbaStartupMenuView _startupMenuView;
        private GbaControllerMapping _controllerMapping;
        private GbaControllerMapping _controllerDraft;
        private bool _mappingAwaitRelease;
        private GbaRomLibraryConsoleModel _romLibraryConsole;
        private GbaScreenRoot _screenRoot;
        private GbaFrontendRuntimeModel _frontendRuntime;
        private GbaRomImportService _romImportService;
        private GbaAudioOutput _audioOutput;
        private GbaDeveloperCoreReleaseProvider _developerCoreReleaseProvider;
        private GbaLibretroDiagnosticPreview _diagnosticPreview;
        private GbaImportedRomMetadata _activeLibraryRom;
        private GbaRomLaunchState _romLaunchState = GbaRomLaunchState.Idle;
        private string _romLaunchStatus = "No library ROM launch requested.";
        private string _stateStatus = "Quick slot 0 is ready.";
        private ulong _lastAudioUnderflowFrames;
        private ulong _lastAudioStreamingCallbacks;
        private ulong _lastAudioPcmFramesRequested;
        private ulong _lastAudioPcmFramesPulled;
        private ulong _lastAudioPcmEmptyReads;
        private ulong _lastAudioNonSilentPcmFramesPulled;
        private float _nextAudioUnderflowLogTime;
        private float _nextAudioLivenessCheckTime;
        private GbaAudioFaultKind _lastAudioFaultKind;
        private bool _audioOutputPrimed;
        private readonly GbaRollingFrameRateMeter _leaseFrameRate = new();

        /// <summary>Direct-mGBA frontend capabilities and transient audio state.</summary>
        public GbaFrontendRuntimeModel Frontend => _frontendRuntime;

        /// <summary>App-private ROM-library import seam; it never changes the active worker.</summary>
        public GbaRomImportService RomImport => _romImportService;

        /// <summary>Current explicit ROM-switch state for the local library view.</summary>
        public GbaRomLaunchState RomLaunchState => _romLaunchState;

        private void Awake()
        {
            presentationMode = NormalizePresentationMode(presentationMode);
            _frontendRuntime = GbaFrontendRuntimeModel.CreateDirectMgba();
            _screenRoot = CreateScreenRoot();
            _surface = GetComponent<GbaTestFrameSurface>();
            _layeredSurface = GetComponent<GbaLayeredSurface>() ?? gameObject.AddComponent<GbaLayeredSurface>();
            _romImportService = GetComponent<GbaRomImportService>() ?? gameObject.AddComponent<GbaRomImportService>();
            _romImportService.ImportCompleted += OnRomPickerCompleted;
            _audioOutput = GetComponent<GbaAudioOutput>() ?? gameObject.AddComponent<GbaAudioOutput>();
            _developerCoreReleaseProvider = GetComponent<GbaDeveloperCoreReleaseProvider>();
            _diagnosticPreview = GetComponent<GbaLibretroDiagnosticPreview>();
            _lastNonResolvedPresentationMode = presentationMode == PresentationMode.Original
                ? PresentationMode.Auto
                : presentationMode;
            _adminConsole = new GbaAdminConsoleModel(new GbaAdminConsoleSettings
            {
                PresentationMode = presentationMode,
                PlacementMode = _screenRoot.PlacementMode,
                RequestedCore = GbaCoreKind.DirectMgba
            });
            _romLibraryConsole = new GbaRomLibraryConsoleModel();
            var consoleObject = new GameObject("GBA XR Admin Console", typeof(RectTransform));
            consoleObject.transform.SetParent(transform, false);
            _adminConsoleView = consoleObject.AddComponent<GbaAdminConsoleView>();
            _adminConsoleView.Initialize(ApplyAdminConsole, CloseAdminConsole);
            _startupMenu = new GbaStartupMenuModel();
            _controllerMapping = GbaControllerMapping.Load();
            var startupObject = new GameObject("Parallax Emu Startup Menu", typeof(RectTransform));
            startupObject.transform.SetParent(transform, false);
            _startupMenuView = startupObject.AddComponent<GbaStartupMenuView>();
            _startupMenuView.Initialize();
            Debug.Log("GBA XR: press Xbox R3 to open the Parallax Emu menu. Plane placement is hand-only.");
        }

        private GbaScreenRoot CreateScreenRoot()
        {
            if (transform.parent != null && transform.parent.TryGetComponent<GbaScreenRoot>(out var existingRoot))
            {
                return existingRoot;
            }

            var screenRootObject = new GameObject("GBA Screen Root");
            var screenRootTransform = screenRootObject.transform;
            screenRootTransform.SetParent(transform.parent, true);
            screenRootTransform.SetPositionAndRotation(transform.position, transform.rotation);
            screenRootTransform.localScale = transform.lossyScale;
            transform.SetParent(screenRootTransform, true);
            return screenRootObject.AddComponent<GbaScreenRoot>();
        }

        private void OnEnable()
        {
            if (ShouldOpenConsoleAtStartup(romRelativePath))
            {
                OpenStartupMenu();
                return;
            }

            if (Application.platform != RuntimePlatform.Android)
            {
                return;
            }

            _initializationRoutine = StartCoroutine(InitializeFromFixture());
        }

        internal static bool ShouldOpenConsoleAtStartup(string path) => string.IsNullOrWhiteSpace(path);

        private IEnumerator InitializeFromFixture()
        {
            var romPath = Path.Combine(Application.persistentDataPath, romRelativePath);
            // Packaged synthetic fixtures are versioned source artifacts. Refresh them on
            // each launch so a rebuilt APK cannot accidentally run an earlier persistent
            // copy. Other paths retain their persistent-file behavior for user fixtures.
            var isPackagedStagedRom = romRelativePath.StartsWith("gba-fixtures/", StringComparison.Ordinal) ||
                                     romRelativePath.StartsWith("gba-user/", StringComparison.Ordinal);
            if (!File.Exists(romPath) || isPackagedStagedRom)
            {
                yield return CopyPackagedFixtureIfPresent(romPath);
            }

            if (!File.Exists(romPath))
            {
                Debug.LogWarning($"GBA XR: no staged ROM at {romPath}. The diagnostic frame remains visible.");
                _initializationRoutine = null;
                yield break;
            }

            if (TryStartNativeSession(romPath, out var error))
            {
                _activeLibraryRom = null;
                _romLaunchState = GbaRomLaunchState.Running;
                _romLaunchStatus = $"Running staged ROM: {romRelativePath}";
            }
            else
            {
                _romLaunchState = GbaRomLaunchState.FailedStopped;
                _romLaunchStatus = error;
                Debug.LogError($"GBA XR: could not start staged ROM. {error}");
            }
            _initializationRoutine = null;
        }

        /// <summary>
        /// Starts a direct mGBA worker from an already-local app-private path.
        /// On every failure it destroys partial native handles and leaves the
        /// driver stopped, so a console user can safely choose another ROM.
        /// </summary>
        private bool TryStartNativeSession(string romPath, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(romPath) || !File.Exists(romPath))
            {
                error = "The selected ROM file is not available in app-private storage.";
                return false;
            }

            try
            {
                var requested = _adminConsole?.Active.RequestedCore ?? GbaCoreKind.DirectMgba;
                var readiness = GbaLibretroReadinessStore.Load();
                var plan = GbaCoreSelectionPolicy.SelectWithDiagnosticPreview(
                    requested,
                    readiness,
                    IsUncertifiedDiagnosticPreviewEnabled);
                if (plan.FellBack)
                {
                    Debug.LogWarning($"GBA XR core selection: {plan.FallbackReason}");
                }
                else if (requested == GbaCoreKind.LibretroResolvedExperimental && !readiness.IsLaunchEligible)
                {
                    Debug.LogWarning(
                        "GBA XR core selection: UNRESOLVED DIAGNOSTIC PREVIEW active. " +
                        "Using adapter resolved frames only; parity and Galaxy XR certification remain incomplete.");
                }
                if (!GbaNativeSessionFactory.TryStart(
                        plan.Effective,
                        romPath,
                        FixedEmulationFramesPerSecond,
                        out _nativeSession,
                        out error))
                {
                    string directError = null;
                    // A certified package can still fail to initialize on a
                    // particular device. Keep the established direct path as
                    // the recoverable runtime fallback rather than leaving a
                    // user-selected ROM in a partially started state.
                    if (plan.Effective == GbaCoreKind.LibretroResolvedExperimental &&
                        GbaNativeSessionFactory.TryStart(
                            GbaCoreKind.DirectMgba,
                            romPath,
                            FixedEmulationFramesPerSecond,
                            out _nativeSession,
                            out directError))
                    {
                        Debug.LogWarning(
                            $"GBA XR core selection: libretro initialization failed ({error}); " +
                            "continuing with the direct mGBA fallback.");
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(directError))
                        {
                            error = $"{error} Direct fallback also failed: {directError}";
                        }
                        return false;
                    }
                }

                ResetSessionPresentationState();
                _started = true;
                _audioOutputPrimed = false;
                _frontendRuntime.UpdateAudioStatus(new GbaFrontendAudioStatus(
                    GbaFrontendAudioState.Priming,
                    0U,
                    0U));
                _leaseFrameRate.Reset(Time.unscaledTime);
                return true;
            }
            catch (Exception exception)
            {
                error = $"Native plug-in start failed: {exception.Message}";
                return false;
            }
            finally
            {
                if (!_started)
                {
                    DisposeNative();
                }
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
                if (_startupMenu != null && _startupMenu.IsOpen)
                {
                    HandleStartupMenuInput();
                }
                // A failed explicit library launch leaves the advanced console
                // open so the user can inspect the error and select another ROM.
                else if (_adminConsole != null && _adminConsole.IsOpen)
                {
                    HandleAdminConsoleInput();
                }
                else if (_startupMenu != null && WasStartupMenuOpenPressed())
                {
                    OpenStartupMenu();
                }
                return;
            }

            var workerState = GetWorkerState();
            if (workerState < 0)
            {
                Debug.LogError($"GBA XR: native mGBA worker stopped with state {workerState}.");
                _frontendRuntime.UpdateAudioStatus(new GbaFrontendAudioStatus(
                    GbaFrontendAudioState.Unavailable,
                    0U,
                    0U));
                DisposeNative();
                _activeLibraryRom = null;
                _romLaunchState = GbaRomLaunchState.FailedStopped;
                _romLaunchStatus = $"The active native worker stopped with state {workerState}.";
                return;
            }
            UpdateAudioDiagnostics();
            if (_startupMenu != null && _startupMenu.IsOpen)
            {
                _nativeSession.SetKeys(0U);
                HandleStartupMenuInput();
            }
            else if (_adminConsole.IsOpen)
            {
                _nativeSession.SetKeys(0U);
                HandleAdminConsoleInput();
            }
            else
            {
                if (WasStartupMenuOpenPressed())
                {
                    OpenStartupMenu();
                }
                if ((_startupMenu != null && _startupMenu.IsOpen) || _adminConsole.IsOpen)
                {
                    _nativeSession.SetKeys(0U);
                }
                else
                {
                    _nativeSession.SetKeys(ReadGamepadKeys());
                }
            }

            if (!_started || _nativeSession == null)
            {
                return;
            }

            if (_nativeSession.Acquire(out var lease) != GbaNativeBindings.Ok)
            {
                return;
            }

            var completedLease = false;
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
                // The experimental adapter is certified only for the resolved
                // framebuffer route. It deliberately never reuses direct-mGBA
                // provenance/candidate pointers from a foreign native handle.
                var resolvedOnlySession = _nativeSession.CoreKind == GbaCoreKind.LibretroResolvedExperimental;
                if (resolvedOnlySession)
                {
                    _profileActive = false;
                    _layeredSurface?.Hide();
                }
                else
                {
                    UpdatePresentationProfile(lease);
                }
                _surface.UploadRgba32(_rgbaFrame);
                var forcePartitionParallaxCameraLocalX = 0f;
                var forcePartitionParallaxCameraLocalZ = 0f;
                if (_usingForcePartitionParallax)
                {
                    forcePartitionParallaxCameraLocalX = GetForcePartitionParallaxCameraLocalX(
                        out forcePartitionParallaxCameraLocalZ);
                }
                var presentedLayeredContent = _profileActive && _layeredSurface != null &&
                                              _layeredSurface.Present(
                                                  lease,
                                                  _usingForcePartition,
                                                  _usingExtendedForcePartition,
                                                  _usingForcePartitionParallax,
                                                  forcePartitionParallaxCameraLocalX,
                                                  forcePartitionParallaxCameraLocalZ);
                var resolvedRenderer = _surface.GetComponent<Renderer>();
                if (resolvedRenderer != null)
                {
                    resolvedRenderer.enabled = !presentedLayeredContent;
                }
                if (!presentedLayeredContent && _layeredSurface != null)
                {
                    _layeredSurface.Hide();
                }
                if (!resolvedOnlySession)
                {
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
                completedLease = true;
            }
            finally
            {
                if (_nativeSession != null && _nativeSession.Release(ref lease) == GbaNativeBindings.Ok && completedLease)
                {
                    _leaseFrameRate.RecordCompletedLease(Time.unscaledTime);
                }
            }
        }

        private void VerifySyntheticFixtureHash(GbaNativeBindings.FrameLease lease)
        {
            _presentedFrameCount++;
            var isSyntheticFixture = string.Equals(romRelativePath, SyntheticFixturePath, StringComparison.Ordinal);
            var temporalCheckpointLogged = false;
            if (isSyntheticFixture && _syntheticFixtureTemporalSweepActive &&
                GbaDirectFrameSequenceSweepPolicy.IsCheckpoint(_presentedFrameCount))
            {
                LogSyntheticFixtureTemporalSweep();
                temporalCheckpointLogged = true;
            }
            if (_verifiedFixtureHash || _presentedFrameCount < SyntheticFixtureVerificationLease ||
                !isSyntheticFixture)
            {
                return;
            }

            _verifiedFixtureHash = true;
            var actualHash = FrameHash.ComputeFnv1a32(_rgbaFrame);
            if (actualHash == SyntheticFixtureExpectedHash)
            {
                Debug.Log($"GBA XR: synthetic fixture golden hash verified: 0x{actualHash:X8}.");
                _syntheticFixtureTemporalSweepActive = GbaDirectFrameSequenceSweepPolicy.ShouldStart(
                    isSyntheticFixture,
                    enableSyntheticFixtureTemporalSweep,
                    goldenMismatch: false);
                if (_syntheticFixtureTemporalSweepActive &&
                    GbaDirectFrameSequenceSweepPolicy.IsCheckpoint(_presentedFrameCount) &&
                    !temporalCheckpointLogged)
                {
                    LogSyntheticFixtureTemporalSweep();
                }
                LogProvenanceProbe(lease);
                return;
            }

            // Four corners cannot distinguish a pitch/geometry/content
            // discrepancy, so use the same source-only diagnostic as the
            // isolated adapter batch. This contains no user-ROM data and lets
            // a direct run be compared to the adapter at the first differing
            // pixel without weakening the confirmed lease-30 golden.
            var sourceDiagnostic = GbaLibretroCertificationBatchPolicy.DiagnoseSourceCheckerboard(_rgbaFrame);
            _syntheticFixtureTemporalSweepActive = GbaDirectFrameSequenceSweepPolicy.ShouldStart(
                isSyntheticFixture,
                enableSyntheticFixtureTemporalSweep,
                goldenMismatch: true);
            if (_syntheticFixtureTemporalSweepActive &&
                GbaDirectFrameSequenceSweepPolicy.IsCheckpoint(_presentedFrameCount) &&
                !temporalCheckpointLogged)
            {
                LogSyntheticFixtureTemporalSweep();
            }
            Debug.LogError(
                $"GBA XR: synthetic fixture golden mismatch. Expected 0x{SyntheticFixtureExpectedHash:X8}, " +
                $"received 0x{actualHash:X8}. " +
                $"Samples: (0,0)={DescribePixel(0, 0)}, (16,0)={DescribePixel(16, 0)}, " +
                $"(0,16)={DescribePixel(0, 16)}, (16,16)={DescribePixel(16, 16)}. " +
                $"Source diagnostic: mismatches={sourceDiagnostic.MismatchCount}, " +
                $"first=({sourceDiagnostic.FirstX},{sourceDiagnostic.FirstY}) " +
                $"expected={sourceDiagnostic.FirstExpectedRgba:X8} actual={sourceDiagnostic.FirstActualRgba:X8}, " +
                $"candidate={sourceDiagnostic.MatchingCandidate}, sourceHash=0x{sourceDiagnostic.SourceHash:X8}, " +
                $"rbSwapHash=0x{sourceDiagnostic.RbSwapHash:X8}, alpha0Hash=0x{sourceDiagnostic.TransparentAlphaHash:X8}, " +
                $"verticalFlipHash=0x{sourceDiagnostic.VerticallyFlippedHash:X8}.");
        }

        private void LogSyntheticFixtureTemporalSweep()
        {
            var sweepHash = FrameHash.ComputeFnv1a32(_rgbaFrame);
            var sweepDiagnostic = GbaLibretroCertificationBatchPolicy.DiagnoseSourceCheckerboard(_rgbaFrame);
            Debug.Log(
                "GBA XR: synthetic fixture direct temporal sweep: " +
                GbaDirectFrameSequenceSweepPolicy.FormatCheckpoint(
                    _presentedFrameCount,
                    sweepHash,
                    sweepDiagnostic));
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
            _forcePartitionVerified = true;
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

        private void UpdatePresentationProfile(GbaNativeBindings.FrameLease lease)
        {
            var forcePartitionRequested = presentationMode is PresentationMode.ForcePartition or
                PresentationMode.ForcePartitionParallax or
                PresentationMode.ForcePartitionExtendedParallax;
            var extendedForcePartitionRequested = presentationMode == PresentationMode.ForcePartitionExtendedParallax;
            var forcePartitionParallaxRequested = presentationMode is PresentationMode.ForcePartitionParallax or
                PresentationMode.ForcePartitionExtendedParallax;
            var forcePartitionAvailable = forcePartitionRequested && _forcePartitionVerified &&
                                          lease.LayerSemantics == 1 &&
                                          HasValidForcePartition(lease);
            if (forcePartitionRequested && !_loggedForcePartition)
            {
                _loggedForcePartition = true;
                Debug.LogWarning(
                    $"GBA XR: {presentationMode} maps final source pixels to depth bands. " +
                    "It can reveal missing occluded pixels, break blending, or show holes during head motion.");
            }

            var forcingUnprofiledCandidates = presentationMode == PresentationMode.ForceUnprofiledCandidates;
            var profileAllowedByRom = GbaPresentationProfilePolicy.AllowsCandidateDepth(
                romRelativePath,
                forcingUnprofiledCandidates);
            if (forcingUnprofiledCandidates && !_loggedForcedUnprofiledCandidates)
            {
                _loggedForcedUnprofiledCandidates = true;
                Debug.LogWarning(
                    "GBA XR: developer force mode bypasses the ROM profile allow-list. " +
                    "Only native independent-candidate frames may enter 2.5D; all other frames remain resolved.");
            }
            else if (!forcePartitionRequested && !profileAllowedByRom && !_loggedUnprofiledRomFallback)
            {
                _loggedUnprofiledRomFallback = true;
                Debug.Log("GBA XR: no approved 2.5D profile for this ROM; keeping the resolved-frame presentation.");
            }

            var verifiedCandidates = profileAllowedByRom && lease.LayerSemantics == 2 && lease.Layers != IntPtr.Zero &&
                                     lease.LayerCount > 0 && HasSupportedDepthProfile(lease);
            var nextActive = forcePartitionRequested
                ? forcePartitionAvailable
                : presentationMode != PresentationMode.Original && verifiedCandidates;
            var nextUsesForcePartition = nextActive && forcePartitionAvailable;
            var nextUsesExtendedForcePartition = nextUsesForcePartition && extendedForcePartitionRequested;
            var nextUsesForcePartitionParallax = nextUsesForcePartition && forcePartitionParallaxRequested;
            if (nextActive == _profileActive &&
                nextUsesForcePartition == _usingForcePartition &&
                nextUsesExtendedForcePartition == _usingExtendedForcePartition &&
                nextUsesForcePartitionParallax == _usingForcePartitionParallax) return;
            if (nextUsesForcePartitionParallax && !_usingForcePartitionParallax)
            {
                ResetForcePartitionParallaxReference();
            }
            else if (!nextUsesForcePartitionParallax)
            {
                ResetForcePartitionParallaxReference();
            }
            _profileActive = nextActive;
            _usingForcePartition = nextUsesForcePartition;
            _usingExtendedForcePartition = nextUsesExtendedForcePartition;
            _usingForcePartitionParallax = nextUsesForcePartitionParallax;
            if (!_profileActive && _layeredSurface != null) _layeredSurface.Hide();
            Debug.Log(_profileActive
                ? _usingForcePartition
                    ? _usingForcePartitionParallax
                        ? $"GBA XR: {presentationMode} armed for {lease.LayerCount} source plane(s). This unsafe experiment adds viewpoint parallax."
                        : $"GBA XR: {presentationMode} armed for {lease.LayerCount} source plane(s)."
                    : $"GBA XR: 2.5D profile armed for {lease.LayerCount} verified candidate plane(s)."
                : "GBA XR: resolved-frame fallback selected (profile unavailable or disabled).");
        }

        private float GetForcePartitionParallaxCameraLocalX(out float cameraLocalZToBg0)
        {
            cameraLocalZToBg0 = 0f;
            var camera = Camera.main;
            if (camera == null)
            {
                return 0f;
            }

            var cameraLocalPosition = transform.InverseTransformPoint(camera.transform.position);
            var currentCameraLocalX = cameraLocalPosition.x;
            cameraLocalZToBg0 = cameraLocalPosition.z;
            if (!_hasForcePartitionParallaxReference)
            {
                _forcePartitionParallaxReferenceCameraLocalX = currentCameraLocalX;
                _filteredForcePartitionParallaxCameraLocalX = 0f;
                CaptureForcePartitionParallaxScreenPose();
            }
            else if (HasForcePartitionParallaxScreenPoseChanged())
            {
                // Do not snap to zero when an anchor or free drag materially
                // changes the screen pose. Rebase around the current filtered
                // signal so the effective parallax remains continuous.
                _forcePartitionParallaxReferenceCameraLocalX =
                    GbaForcePartitionParallaxFilterPolicy.RebaseReferenceCameraLocalX(
                        currentCameraLocalX,
                        _filteredForcePartitionParallaxCameraLocalX);
                CaptureForcePartitionParallaxScreenPose();
            }

            var rawCameraLocalX = currentCameraLocalX - _forcePartitionParallaxReferenceCameraLocalX;
            _filteredForcePartitionParallaxCameraLocalX =
                GbaForcePartitionParallaxFilterPolicy.AdvanceFilteredCameraLocalX(
                    _filteredForcePartitionParallaxCameraLocalX,
                    rawCameraLocalX,
                    Time.unscaledDeltaTime);
            return GbaForcePartitionParallaxFilterPolicy.ApplyContinuousDeadZone(
                _filteredForcePartitionParallaxCameraLocalX);
        }

        private bool HasForcePartitionParallaxScreenPoseChanged()
        {
            // These thresholds intentionally exceed ordinary anchor-tracking
            // jitter; a normal tracked-plane update must not rebase parallax.
            const float positionThresholdMetres = 0.05f;
            const float rotationThresholdDegrees = 5f;
            return Vector3.Distance(_screenRoot.transform.position, _forcePartitionParallaxReferenceScreenPosition) > positionThresholdMetres ||
                   Quaternion.Angle(_screenRoot.transform.rotation, _forcePartitionParallaxReferenceScreenRotation) > rotationThresholdDegrees;
        }

        private void CaptureForcePartitionParallaxScreenPose()
        {
            _forcePartitionParallaxReferenceScreenPosition = _screenRoot.transform.position;
            _forcePartitionParallaxReferenceScreenRotation = _screenRoot.transform.rotation;
            _hasForcePartitionParallaxReference = true;
        }

        private void ResetForcePartitionParallaxReference()
        {
            _hasForcePartitionParallaxReference = false;
            _forcePartitionParallaxReferenceCameraLocalX = 0f;
            _filteredForcePartitionParallaxCameraLocalX = 0f;
            _forcePartitionParallaxReferenceScreenPosition = default;
            _forcePartitionParallaxReferenceScreenRotation = default;
        }

        private static bool HasValidForcePartition(GbaNativeBindings.FrameLease lease)
        {
            if (lease.Layers == IntPtr.Zero || lease.LayerCount == 0 || lease.LayerCount > 6)
            {
                return false;
            }

            var sources = new HashSet<int>();
            var layerSize = Marshal.SizeOf<GbaNativeBindings.LayerView>();
            for (var index = 0; index < (int)lease.LayerCount; index++)
            {
                var layer = Marshal.PtrToStructure<GbaNativeBindings.LayerView>(IntPtr.Add(lease.Layers, index * layerSize));
                if (layer.Color.Rgba8 == IntPtr.Zero || layer.CoverageMask == IntPtr.Zero ||
                    layer.Color.Width != GbaTestFrameSurface.Width ||
                    layer.Color.Height != GbaTestFrameSurface.Height ||
                    layer.Color.RowStrideBytes != GbaTestFrameSurface.Width * 4 ||
                    !sources.Add(layer.Source) ||
                    !GbaForcePartitionProfile.TryGetDepth(layer.Source, false, out _))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool HasSupportedDepthProfile(GbaNativeBindings.FrameLease lease)
        {
            var sources = new HashSet<int>();
            var layerSize = Marshal.SizeOf<GbaNativeBindings.LayerView>();
            uint? sharedPriority = null;
            for (var index = 0; index < (int)lease.LayerCount; index++)
            {
                var layer = Marshal.PtrToStructure<GbaNativeBindings.LayerView>(IntPtr.Add(lease.Layers, index * layerSize));
                if (!GbaDepthProfile.TryGetDepth(layer.Source, layer.Priority, out _) ||
                    !sources.Add(layer.Source))
                {
                    return false;
                }
                if (sharedPriority.HasValue && sharedPriority.Value != layer.Priority)
                {
                    return false;
                }
                sharedPriority = layer.Priority;
            }
            return GbaDepthProfile.SupportsSourceSet(sources);
        }

        private void OpenStartupMenu()
        {
            if (_startupMenu == null || _startupMenuView == null) return;
            if (_adminConsole != null && _adminConsole.IsOpen) CloseAdminConsole();
            _startupMenu.Open(
                _adminConsole?.Active.RequestedCore ?? GbaCoreKind.DirectMgba,
                _screenRoot.PlacementMode,
                IsLibretroCoreSelectionAvailable);
            _startupMenuView.Show(_startupMenu, IsLibretroCoreSelectionAvailable);
            Debug.Log("GBA XR: Parallax Emu startup menu opened; gameplay input is suspended.");
        }

        private void CloseStartupMenu()
        {
            if (_startupMenu == null || !_startupMenu.IsOpen) return;
            _startupMenu.Close();
            _startupMenuView?.Hide();
            Debug.Log("GBA XR: Parallax Emu startup menu closed; gameplay input restored.");
        }

        private void HandleStartupMenuInput()
        {
            var gamepad = Gamepad.current;
            if (gamepad == null || _startupMenu == null || !_startupMenu.IsOpen) return;

            if (_startupMenu.Page == GbaStartupMenuPage.ControllerMapping &&
                (_startupMenu.IsCapturingController ||
                 (_startupMenu.IsReviewingController && gamepad.rightStickButton.wasPressedThisFrame)))
            {
                if (gamepad.rightStickButton.wasPressedThisFrame)
                {
                    _controllerDraft = _controllerMapping.Clone();
                    _startupMenu.CancelControllerCapture();
                    Debug.Log("GBA XR: controller calibration cancelled; active mapping unchanged.");
                }
                else if (_mappingAwaitRelease)
                {
                    _mappingAwaitRelease = GbaControllerMapping.AnyCapturePressed(gamepad);
                }
                else if (GbaControllerMapping.TryCapture(gamepad, out var binding))
                {
                    _controllerDraft.SetDraft(_startupMenu.CaptureControl, binding);
                    Debug.Log($"GBA XR: captured {_startupMenu.CaptureControl} = {GbaControllerMapping.Describe(binding)} in draft.");
                    _startupMenu.AdvanceControllerCapture();
                    _mappingAwaitRelease = true;
                }
                _startupMenuView.Refresh(_startupMenu, IsLibretroCoreSelectionAvailable);
                return;
            }

            if (gamepad.buttonEast.wasPressedThisFrame)
            {
                ExecuteStartupMenuAction(_startupMenu.Back());
            }
            if (_startupMenu.IsOpen && gamepad.dpad.up.wasPressedThisFrame) _startupMenu.Move(-1);
            if (_startupMenu.IsOpen && gamepad.dpad.down.wasPressedThisFrame) _startupMenu.Move(1);
            if (_startupMenu.IsOpen && gamepad.buttonSouth.wasPressedThisFrame)
            {
                ExecuteStartupMenuAction(_startupMenu.Activate());
            }
            if (_startupMenu.IsOpen)
            {
                _startupMenuView.Refresh(_startupMenu, IsLibretroCoreSelectionAvailable);
            }
            else
            {
                _startupMenuView.Hide();
            }
        }

        private void ExecuteStartupMenuAction(GbaStartupMenuAction action)
        {
            switch (action)
            {
                case GbaStartupMenuAction.LoadRom:
                    RequestRomImport();
                    break;
                case GbaStartupMenuAction.OpenStateSave:
                case GbaStartupMenuAction.OpenStateLoad:
                    RefreshStartupStateSlots();
                    break;
                case GbaStartupMenuAction.SaveStateSlot:
                    if (RequestState(true, _startupMenu.SelectedStateSlot)) CloseStartupMenu();
                    else RefreshStartupStateSlots();
                    break;
                case GbaStartupMenuAction.LoadStateSlot:
                    if (RequestState(false, _startupMenu.SelectedStateSlot)) CloseStartupMenu();
                    else RefreshStartupStateSlots();
                    break;
                case GbaStartupMenuAction.CommitCore:
                    CommitStartupCore(_startupMenu.SelectedCore);
                    break;
                case GbaStartupMenuAction.ApplyViewMode:
                    CommitStartupViewMode(_startupMenu.SelectedViewMode);
                    break;
                case GbaStartupMenuAction.OpenAdvancedConsole:
                    CloseStartupMenu();
                    OpenAdminConsole(GetWorkerState());
                    break;
                case GbaStartupMenuAction.BeginControllerCapture:
                    _controllerDraft = _controllerMapping.Clone();
                    _mappingAwaitRelease = true;
                    break;
                case GbaStartupMenuAction.ApplyControllerMapping:
                    if (_controllerDraft != null && _controllerDraft.TryApply())
                    {
                        _controllerMapping = _controllerDraft;
                        Debug.Log("GBA XR: direct mGBA controller mapping applied.");
                    }
                    else Debug.LogWarning("GBA XR: controller mapping draft is incomplete or has duplicate controls.");
                    break;
                case GbaStartupMenuAction.ResetControllerMapping:
                    break;
                case GbaStartupMenuAction.CancelControllerMapping:
                    _controllerDraft = null;
                    _mappingAwaitRelease = false;
                    Debug.Log("GBA XR: controller mapping draft discarded.");
                    break;
            }
        }

        private void CommitStartupCore(GbaCoreKind core)
        {
            // A is the explicit confirmation. Reuse the existing transaction
            // path so an active worker is fenced and restarted only here.
            _adminConsole.Open();
            _adminConsole.SetDraftRequestedCore(core);
            ApplyAdminConsole();
            _adminConsole.Close();
            Debug.Log($"GBA XR: startup menu confirmed requested core {core}.");
        }

        private void CommitStartupViewMode(GbaScreenPlacementMode mode)
        {
            _adminConsole.Open();
            _adminConsole.SetDraftPlacementMode(mode);
            ApplyAdminConsole();
            _adminConsole.Close();
            Debug.Log($"GBA XR: startup menu applied view mode {mode}.");
        }

        private void OpenAdminConsole(int workerState)
        {
            _adminConsoleSelectedRow = 0;
            _adminConsole.Open();
            _adminConsoleView.Show(
                _adminConsole.Draft,
                workerState,
                _leaseFrameRate.FramesPerSecond,
                _adminConsoleSelectedRow,
                _romImportService.Entries?.Count ?? 0,
                DescribeRomImportStatus(),
                _stateStatus,
                IsLibretroCoreSelectionAvailable,
                IsUncertifiedDiagnosticPreviewEnabled);
            Debug.Log("GBA XR: admin console opened; gameplay input is suspended.");
        }

        private static bool WasStartupMenuOpenPressed()
        {
            // R3 is not forwarded to GBA key input and opens the startup menu in
            // either placement mode. Plane placement remains hand-only.
            return Gamepad.current != null && Gamepad.current.rightStickButton.wasPressedThisFrame;
        }

        private void CloseAdminConsole()
        {
            if (_adminConsole == null || !_adminConsole.IsOpen)
            {
                return;
            }
            _romLibraryConsole?.Close();
            _adminConsole.Close();
            _adminConsoleView.Hide();
            Debug.Log("GBA XR: admin console closed; gameplay input restored.");
        }

        private void HandleAdminConsoleInput()
        {
            var gamepad = Gamepad.current;
            if (gamepad == null)
            {
                return;
            }

            if (gamepad.buttonEast.wasPressedThisFrame)
            {
                if (_romLibraryConsole.IsOpen)
                {
                    CloseRomLibrary();
                    return;
                }
                CloseAdminConsole();
                return;
            }
            if (_romLibraryConsole.IsOpen)
            {
                HandleRomLibraryInput(gamepad);
                return;
            }
            if (gamepad.dpad.up.wasPressedThisFrame)
            {
                _adminConsoleSelectedRow = (_adminConsoleSelectedRow + AdminConsoleRowCount - 1) % AdminConsoleRowCount;
            }
            if (gamepad.dpad.down.wasPressedThisFrame)
            {
                _adminConsoleSelectedRow = (_adminConsoleSelectedRow + 1) % AdminConsoleRowCount;
            }
            if (gamepad.dpad.left.wasPressedThisFrame)
            {
                AdjustAdminConsoleSelection(-1);
            }
            if (gamepad.dpad.right.wasPressedThisFrame)
            {
                AdjustAdminConsoleSelection(1);
            }
            if (gamepad.buttonSouth.wasPressedThisFrame)
            {
                if (_adminConsoleSelectedRow == AdminLibraryRow)
                {
                    RequestRomImport();
                    return;
                }
                else if (_adminConsoleSelectedRow == AdminApplyRow)
                {
                    ApplyAdminConsole();
                }
                else if (_adminConsoleSelectedRow == AdminCloseRow)
                {
                    CloseAdminConsole();
                    return;
                }
            }

            if (_adminConsole.IsOpen)
            {
                RefreshAdminConsoleView();
            }
        }

        private void AdjustAdminConsoleSelection(int direction)
        {
            var draft = _adminConsole.Draft;
            if (_adminConsoleSelectedRow == 1)
            {
                var modes = (PresentationMode[])Enum.GetValues(typeof(PresentationMode));
                var currentIndex = Array.IndexOf(modes, NormalizePresentationMode(draft.PresentationMode));
                var nextIndex = (currentIndex + direction + modes.Length) % modes.Length;
                _adminConsole.SetDraftPresentationMode(modes[nextIndex]);
            }
            else if (_adminConsoleSelectedRow == 2)
            {
                var modes = (GbaScreenPlacementMode[])Enum.GetValues(typeof(GbaScreenPlacementMode));
                var nextIndex = ((int)draft.PlacementMode + direction + modes.Length) % modes.Length;
                _adminConsole.SetDraftPlacementMode(modes[nextIndex]);
            }
            else if (IsLibretroCoreSelectionAvailable && _adminConsoleSelectedRow == AdminCoreRow)
            {
                var cores = (GbaCoreKind[])Enum.GetValues(typeof(GbaCoreKind));
                var currentIndex = Array.IndexOf(cores, draft.RequestedCore);
                _adminConsole.SetDraftRequestedCore(cores[(currentIndex + direction + cores.Length) % cores.Length]);
            }
        }

        private void OpenRomLibrary()
        {
            _romLibraryConsole.Open(_romImportService?.Entries);
            _adminConsoleView.ShowLibrary(
                _romLibraryConsole,
                GetWorkerState(),
                DescribeRomImportStatus(),
                DescribeCurrentRom(),
                _romLaunchStatus);
            Debug.Log("GBA XR: local ROM library opened; launch requires explicit A on a selected entry.");
        }

        private void CloseRomLibrary()
        {
            _romLibraryConsole.Close();
            if (_adminConsole.IsOpen)
            {
                RefreshAdminConsoleView();
            }
        }

        private void HandleRomLibraryInput(Gamepad gamepad)
        {
            _romLibraryConsole.UpdateEntries(_romImportService?.Entries);
            if (gamepad.dpad.up.wasPressedThisFrame)
            {
                _romLibraryConsole.MoveSelection(-1);
            }
            if (gamepad.dpad.down.wasPressedThisFrame)
            {
                _romLibraryConsole.MoveSelection(1);
            }
            if (gamepad.buttonSouth.wasPressedThisFrame)
            {
                switch (_romLibraryConsole.ActivateSelection())
                {
                    case GbaRomLibraryConsoleAction.Import:
                        RequestRomImport();
                        break;
                    case GbaRomLibraryConsoleAction.LaunchSelected:
                        if (RequestLaunchSelectedLibraryRom())
                        {
                            CloseAdminConsole();
                            return;
                        }
                        break;
                    case GbaRomLibraryConsoleAction.LaunchUnavailable:
                        Debug.LogWarning(
                            "GBA XR: select a local ROM before requesting an explicit launch.");
                        break;
                    case GbaRomLibraryConsoleAction.Back:
                        CloseRomLibrary();
                        return;
                }
            }
            RefreshAdminConsoleView();
        }

        private void RefreshAdminConsoleView()
        {
            var workerState = GetWorkerState();
            var importStatus = DescribeRomImportStatus();
            if (_romLibraryConsole.IsOpen)
            {
                _romLibraryConsole.UpdateEntries(_romImportService?.Entries);
                _adminConsoleView.RefreshLibrary(
                    _romLibraryConsole,
                    workerState,
                    importStatus,
                    DescribeCurrentRom(),
                    _romLaunchStatus);
                return;
            }

            _adminConsoleView.Refresh(
                _adminConsole.Draft,
                workerState,
                _leaseFrameRate.FramesPerSecond,
                _adminConsoleSelectedRow,
                _romImportService.Entries?.Count ?? 0,
                importStatus,
                _stateStatus,
                IsLibretroCoreSelectionAvailable,
                IsUncertifiedDiagnosticPreviewEnabled);
        }

        private string DescribeRomImportStatus()
        {
            var outcome = _romImportService?.LastOutcome;
            if (outcome?.Kind == GbaRomImportOutcomeKind.Success &&
                _romLaunchState == GbaRomLaunchState.FailedStopped)
            {
                return _romLaunchStatus;
            }
            return GbaRomImportPresentationPolicy.Describe(
                _romImportService != null && _romImportService.ImportInFlight,
                outcome?.Kind,
                outcome?.Message);
        }

        private string DescribeCurrentRom()
        {
            if (_activeLibraryRom != null)
            {
                return _activeLibraryRom.DisplayName;
            }
            return _started ? romRelativePath : "No active ROM (worker stopped)";
        }

        private bool RequestLaunchSelectedLibraryRom(GbaImportedRomMetadata selected = null)
        {
            if (_romLaunchState is GbaRomLaunchState.StoppingCurrentWorker or GbaRomLaunchState.StartingSelectedWorker)
            {
                return false;
            }

            _romLaunchState = GbaRomLaunchState.ValidatingSelection;
            var paths = GbaAppPrivateStoragePaths.Create(Application.persistentDataPath);
            if (!GbaRomLaunchPolicy.TryCreateRequest(
                    paths,
                    selected ?? _romLibraryConsole.SelectedEntry,
                    File.Exists,
                    out var request,
                    out var validationError))
            {
                _romLaunchState = _started ? GbaRomLaunchState.Running : GbaRomLaunchState.FailedStopped;
                _romLaunchStatus = validationError;
                Debug.LogWarning($"GBA XR: ROM launch rejected. {validationError}");
                return false;
            }

            // Update's frame lease is local and released in its finally block.
            // This input branch runs before a new lease is acquired, so fencing
            // _started first guarantees no bridge frame is accessed while the
            // audio callback is detached and the worker/bridge are destroyed.
            if (_initializationRoutine != null)
            {
                StopCoroutine(_initializationRoutine);
                _initializationRoutine = null;
            }
            _romLaunchState = GbaRomLaunchState.StoppingCurrentWorker;
            _started = false;
            DisposeNative();

            _romLaunchState = GbaRomLaunchState.StartingSelectedWorker;
            if (!TryStartNativeSession(request.AbsolutePath, out var startError))
            {
                _activeLibraryRom = null;
                _romLaunchState = GbaRomLaunchState.FailedStopped;
                _romLaunchStatus = $"Launch failed; worker is stopped. Choose another ROM. {startError}";
                Debug.LogError($"GBA XR: {_romLaunchStatus}");
                return false;
            }

            _activeLibraryRom = request.Metadata;
            romRelativePath = "roms/" + request.Metadata.RelativePath;
            _romLaunchState = GbaRomLaunchState.Running;
            _romLaunchStatus = $"Running {request.Metadata.DisplayName}.";
            Debug.Log($"GBA XR: launched selected app-private ROM {request.Metadata.DisplayName}.");
            return true;
        }

        private void ApplyAdminConsole()
        {
            var activeCore = _nativeSession?.CoreKind ?? GbaCoreKind.DirectMgba;
            var transaction = _adminConsole.Apply();
            var settings = transaction.Settings;
            presentationMode = NormalizePresentationMode(settings.PresentationMode);
            _screenRoot.SetPlacementMode(settings.PlacementMode);
            if (presentationMode != PresentationMode.Original)
            {
                _lastNonResolvedPresentationMode = presentationMode;
            }
            if (_started && settings.RequestedCore != activeCore)
            {
                TrySwitchNativeCore(settings.RequestedCore);
            }
            if (transaction.Includes(GbaAdminOperation.ImportRom))
            {
                RequestRomImport();
            }
            Debug.Log($"GBA XR: admin console applied {settings.PresentationMode} presentation mode and {settings.PlacementMode} screen placement. Requested core={settings.RequestedCore}; emulation cadence remains fixed at {FixedEmulationFramesPerSecond} FPS.");
            _adminConsoleView.Refresh(
                _adminConsole.Active,
                GetWorkerState(),
                _leaseFrameRate.FramesPerSecond,
                _adminConsoleSelectedRow,
                _romImportService.Entries?.Count ?? 0,
                DescribeRomImportStatus(),
                _stateStatus,
                IsLibretroCoreSelectionAvailable,
                IsUncertifiedDiagnosticPreviewEnabled);
        }

        /// <summary>
        /// Applies a certified core choice only between leases. The caller is in
        /// the admin-input branch, before Update acquires its next lease.
        /// Detaching audio before destroying the session prevents its callback
        /// from observing either backend's freed native PCM ring.
        /// </summary>
        private bool TrySwitchNativeCore(GbaCoreKind requestedCore)
        {
            var plan = GbaCoreSelectionPolicy.SelectWithDiagnosticPreview(
                requestedCore,
                GbaLibretroReadinessStore.Load(),
                IsUncertifiedDiagnosticPreviewEnabled);
            if (plan.Effective == (_nativeSession?.CoreKind ?? GbaCoreKind.DirectMgba))
            {
                return true;
            }

            if (!TryGetActiveRomPath(out var romPath, out var pathError))
            {
                _romLaunchStatus = $"Core switch rejected; {pathError}";
                Debug.LogWarning($"GBA XR core switch: {_romLaunchStatus}");
                return false;
            }

            _romLaunchState = GbaRomLaunchState.StoppingCurrentWorker;
            _started = false;
            DisposeNative();
            _romLaunchState = GbaRomLaunchState.StartingSelectedWorker;
            if (!TryStartNativeSession(romPath, out var startError))
            {
                _romLaunchState = GbaRomLaunchState.FailedStopped;
                _romLaunchStatus = $"Core switch failed; worker is stopped. {startError}";
                Debug.LogError($"GBA XR core switch: {_romLaunchStatus}");
                return false;
            }

            _romLaunchState = GbaRomLaunchState.Running;
            _romLaunchStatus = $"Running {DescribeCurrentRom()} with {_nativeSession.CoreKind}.";
            Debug.Log($"GBA XR core switch: {_romLaunchStatus}");
            return true;
        }

        private bool TryGetActiveRomPath(out string romPath, out string error)
        {
            romPath = null;
            error = null;
            var paths = GbaAppPrivateStoragePaths.Create(Application.persistentDataPath);
            if (_activeLibraryRom != null)
            {
                if (!GbaRomLaunchPolicy.TryCreateRequest(
                        paths,
                        _activeLibraryRom,
                        File.Exists,
                        out var request,
                        out error))
                {
                    return false;
                }
                romPath = request.AbsolutePath;
                return true;
            }

            if (string.IsNullOrWhiteSpace(romRelativePath))
            {
                error = "the active staged ROM path is empty";
                return false;
            }
            romPath = Path.Combine(Application.persistentDataPath, romRelativePath);
            if (!File.Exists(romPath))
            {
                error = "the active staged ROM file is unavailable";
                return false;
            }
            return true;
        }

        private void RequestQuickState(bool save) => RequestState(save, GbaSaveStatePolicy.QuickSlot);

        private bool RequestState(bool save, int slot)
        {
            if (_nativeSession == null || !_started)
            {
                _stateStatus = "No active ROM worker is available.";
                Debug.LogWarning($"GBA XR state: {_stateStatus}");
                return false;
            }

            var paths = GbaAppPrivateStoragePaths.Create(Application.persistentDataPath);
            var identity = _activeLibraryRom?.StableId ?? romRelativePath;
            if (!GbaSaveStatePolicy.TryCreateSlotPath(
                    paths,
                    identity,
                    slot,
                    out var statePath,
                    out var error))
            {
                _stateStatus = error;
                Debug.LogWarning($"GBA XR state: {error}");
                return false;
            }

            if (save)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(statePath));
            }
            else if (!File.Exists(statePath))
            {
                _stateStatus = $"Slot {slot + 1} is empty for this ROM.";
                Debug.LogWarning($"GBA XR state: {_stateStatus}");
                return false;
            }

            var status = save ? _nativeSession.SaveState(statePath) : _nativeSession.LoadState(statePath);
            if (status != GbaNativeBindings.Ok)
            {
                _stateStatus = save
                    ? $"Saving slot {slot + 1} failed; the prior state was retained."
                    : $"Loading slot {slot + 1} failed; the active session was retained.";
                Debug.LogError($"GBA XR state: {_stateStatus} Native status={status}.");
                return false;
            }

            _stateStatus = save
                ? $"Saved slot {slot + 1}."
                : $"Loaded slot {slot + 1}.";
            if (!save)
            {
                _presentedFrameCount = 0;
                _verifiedFixtureHash = false;
                _syntheticFixtureTemporalSweepActive = false;
                _verifiedProvenanceContract = false;
            }
            Debug.Log($"GBA XR state: {_stateStatus}");
            return true;
        }

        private void RefreshStartupStateSlots()
        {
            var descriptions = new string[GbaSaveStatePolicy.SlotCount];
            var paths = GbaAppPrivateStoragePaths.Create(Application.persistentDataPath);
            var identity = _activeLibraryRom?.StableId ?? romRelativePath;
            for (var slot = 0; slot < descriptions.Length; slot++)
            {
                descriptions[slot] = $"Slot {slot + 1}: Empty";
                if (GbaSaveStatePolicy.TryCreateSlotPath(paths, identity, slot, out var path, out _) && File.Exists(path))
                {
                    descriptions[slot] = $"Slot {slot + 1}: {File.GetLastWriteTimeUtc(path):yyyy-MM-dd HH:mm:ss} UTC";
                }
            }
            _startupMenu?.SetStateSlots(descriptions);
        }

        /// <summary>
        /// Opens the Android picker. A valid selection starts the selected ROM.
        /// </summary>
        public bool RequestRomImport()
        {
            return _romImportService != null && _romImportService.BeginImport();
        }

        /// <summary>
        /// Explicit developer/admin API only. A staged release remains
        /// DownloadedNotLoadable; it never replaces this Unity process's host
        /// bridge or alters normal core selection.
        /// </summary>
        public bool RequestDeveloperCoreReleaseDownload()
        {
            if (_developerCoreReleaseProvider == null)
            {
                Debug.LogWarning("GBA XR developer core: add and explicitly configure GbaDeveloperCoreReleaseProvider first.");
                return false;
            }
            return _developerCoreReleaseProvider.RequestConfiguredDownload();
        }

        public string DeveloperCoreReleaseStatus => _developerCoreReleaseProvider?.Status ?? "Developer core provider is not configured.";

        public bool RequestDeveloperCoreReleaseRollback(string coreId) =>
            _developerCoreReleaseProvider != null && _developerCoreReleaseProvider.RequestRollback(coreId);

        private void OnRomPickerCompleted(GbaRomImportOutcome outcome)
        {
            if (outcome.Kind == GbaRomImportOutcomeKind.Success && outcome.Metadata != null)
            {
                if (RequestLaunchSelectedLibraryRom(outcome.Metadata))
                {
                    CloseAdminConsole();
                    CloseStartupMenu();
                }
                else if (_adminConsole != null && _adminConsole.IsOpen)
                {
                    Debug.LogError($"GBA XR: selected ROM could not be started: {_romLaunchStatus}");
                    RefreshAdminConsoleView();
                }
                else if (_startupMenu != null && _startupMenu.IsOpen)
                {
                    Debug.LogError($"GBA XR: selected ROM could not be started: {_romLaunchStatus}");
                    _startupMenuView.Refresh(_startupMenu, IsLibretroCoreSelectionAvailable);
                }
            }
            else if (_adminConsole != null && _adminConsole.IsOpen)
            {
                RefreshAdminConsoleView();
            }
            else if (_startupMenu != null && _startupMenu.IsOpen)
            {
                _startupMenuView.Refresh(_startupMenu, IsLibretroCoreSelectionAvailable);
            }
        }

        private void OnDestroy()
        {
            if (_romImportService != null) _romImportService.ImportCompleted -= OnRomPickerCompleted;
        }

        private void OnValidate()
        {
            presentationMode = NormalizePresentationMode(presentationMode);
        }

        private static PresentationMode NormalizePresentationMode(PresentationMode mode)
        {
            return Enum.IsDefined(typeof(PresentationMode), mode)
                ? mode
                : PresentationMode.Auto;
        }

        private uint ReadGamepadKeys()
        {
            var gamepad = Gamepad.current;
            if (gamepad == null) return 0U;
            // Rebinding applies only to the direct mGBA key-bit contract. The
            // libretro adapter retains its independently certified translation.
            if (_nativeSession?.CoreKind == GbaCoreKind.DirectMgba)
                return _controllerMapping?.ReadKeys(gamepad) ?? 0U;
            uint keys = 0U;
            if (gamepad.buttonSouth.isPressed) keys |= 1U << 0; // Xbox A
            if (gamepad.buttonEast.isPressed) keys |= 1U << 1;  // Xbox B
            if (gamepad.selectButton.isPressed) keys |= 1U << 2;
            if (gamepad.startButton.isPressed) keys |= 1U << 3;
            if (gamepad.dpad.right.isPressed) keys |= 1U << 4;
            if (gamepad.dpad.left.isPressed) keys |= 1U << 5;
            if (gamepad.dpad.up.isPressed) keys |= 1U << 6;
            if (gamepad.dpad.down.isPressed) keys |= 1U << 7;
            keys |= MapShoulderKeys(gamepad.buttonWest.isPressed, gamepad.buttonNorth.isPressed,
                gamepad.leftShoulder.isPressed, gamepad.rightShoulder.isPressed);
            return keys;
        }

        internal static uint MapShoulderKeys(bool xboxX, bool xboxY, bool leftShoulder, bool rightShoulder)
        {
            uint keys = 0U;
            if (xboxY || rightShoulder) keys |= 1U << 8; // GBA R
            if (xboxX || leftShoulder) keys |= 1U << 9;  // GBA L
            return keys;
        }

        private int GetWorkerState()
        {
            return _nativeSession?.State ?? -1;
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
            _activeLibraryRom = null;
            _romLaunchState = GbaRomLaunchState.Idle;
            _romLaunchStatus = "Native session stopped.";
        }

        private void OnGUI()
        {
            if (!showDeveloperOverlay || Application.platform != RuntimePlatform.Android) return;
            GUI.Box(new Rect(16, 16, 520, 120), "GBA XR Test Console");
            GUI.Label(new Rect(28, 44, 495, 24), $"ROM: {DescribeCurrentRom()}");
            GUI.Label(new Rect(28, 68, 495, 24), $"Worker: {GetWorkerState()}");
            GUI.Label(new Rect(28, 92, 495, 24), $"Gamepad: {(Gamepad.current == null ? "none" : Gamepad.current.displayName)}");
        }

        private void ResetSessionPresentationState()
        {
            _presentedFrameCount = 0;
            _verifiedFixtureHash = false;
            _syntheticFixtureTemporalSweepActive = false;
            _verifiedProvenanceContract = false;
            _verifiedVisiblePartition = false;
            _forcePartitionVerified = false;
            _verifiedIndependentBg0 = false;
            _verifiedIndependentBgTie = false;
            _verifiedIndependentObj = false;
            _profileActive = false;
            _usingForcePartition = false;
            _usingExtendedForcePartition = false;
            _usingForcePartitionParallax = false;
            _loggedUnprofiledRomFallback = false;
            _loggedForcedUnprofiledCandidates = false;
            _loggedForcePartition = false;
            ResetForcePartitionParallaxReference();
            _layeredSurface?.Hide();
            var resolvedRenderer = _surface?.GetComponent<Renderer>();
            if (resolvedRenderer != null)
            {
                resolvedRenderer.enabled = true;
            }
        }

        private void DisposeNative()
        {
            _started = false;
            ResetSessionPresentationState();
            _leaseFrameRate.Reset(Time.unscaledTime);
            _lastAudioUnderflowFrames = 0U;
            _lastAudioStreamingCallbacks = 0U;
            _lastAudioPcmFramesRequested = 0U;
            _lastAudioPcmFramesPulled = 0U;
            _lastAudioPcmEmptyReads = 0U;
            _lastAudioNonSilentPcmFramesPulled = 0U;
            _nextAudioUnderflowLogTime = 0f;
            _nextAudioLivenessCheckTime = 0f;
            _lastAudioFaultKind = GbaAudioFaultKind.None;
            _audioOutputPrimed = false;
            _frontendRuntime?.UpdateAudioStatus(new GbaFrontendAudioStatus(
                GbaFrontendAudioState.Stopped,
                0U,
                0U));
            // Detach even after a partially failed native start. The audio
            // callback may never retain a pointer beyond this point.
            _audioOutput?.DetachWorker();
            if (_nativeSession != null)
            {
                // The audio callback can run on Unity's audio thread. Gate it
                // before native worker destruction so it cannot read freed PCM.
                _nativeSession.Dispose();
                _nativeSession = null;
            }
        }

        private void UpdateAudioDiagnostics()
        {
            if (_audioOutput == null || _nativeSession == null)
            {
                return;
            }

            var availableFrames = _nativeSession.AudioAvailableFrames;
            var mgbaSourceSampleRate = _nativeSession.SourceAudioSampleRate;
            var droppedFrames = _nativeSession.AudioDroppedFrames;
            if (!_audioOutputPrimed)
            {
                _frontendRuntime.UpdateAudioStatus(new GbaFrontendAudioStatus(
                    GbaFrontendAudioState.Priming,
                    availableFrames,
                    droppedFrames));
                if (availableFrames < AudioPrimeFrames)
                {
                    return;
                }

                _audioOutput.AttachWorker(_nativeSession.AudioHandle, _nativeSession.AudioBackend);
                _lastAudioUnderflowFrames = _audioOutput.TotalUnderflowFrames;
                _lastAudioStreamingCallbacks = _audioOutput.TotalStreamingCallbacks;
                _lastAudioPcmFramesRequested = _audioOutput.TotalNativePcmFramesRequested;
                _lastAudioPcmFramesPulled = _audioOutput.TotalNativePcmFramesPulled;
                _lastAudioPcmEmptyReads = _audioOutput.TotalNativePcmEmptyReads;
                _lastAudioNonSilentPcmFramesPulled = _audioOutput.TotalNonSilentPcmFramesPulled;
                _nextAudioUnderflowLogTime = 0f;
                _nextAudioLivenessCheckTime = Time.unscaledTime + AudioLivenessIntervalSeconds;
                _lastAudioFaultKind = GbaAudioFaultKind.None;
                _audioOutputPrimed = true;
                _frontendRuntime.UpdateAudioStatus(new GbaFrontendAudioStatus(
                    GbaFrontendAudioState.Active,
                    availableFrames,
                    droppedFrames));
                Debug.Log(
                    $"GBA XR audio: attached 48 kHz PCM output after priming {availableFrames} frame(s); " +
                    $"mGBA source rate is reported separately as it changes. {DescribeAudioTelemetry(_audioOutput.GetTelemetry())}");
                return;
            }
            var underflowFrames = _audioOutput.TotalUnderflowFrames;
            var hasNewUnderflow = underflowFrames != _lastAudioUnderflowFrames;
            var audioState = hasNewUnderflow
                ? GbaFrontendAudioState.Underflowing
                : GbaFrontendAudioState.Active;
            if (hasNewUnderflow && Time.unscaledTime >= _nextAudioUnderflowLogTime)
            {
                Debug.LogWarning(
                    $"GBA XR audio: Unity output underflowed {underflowFrames - _lastAudioUnderflowFrames} frame(s); " +
                    $"available={availableFrames}, nativeDropped={droppedFrames}, source={mgbaSourceSampleRate} Hz.");
                _lastAudioUnderflowFrames = underflowFrames;
                _nextAudioUnderflowLogTime = Time.unscaledTime + 1f;
            }

            var streamingCallbacks = _audioOutput.TotalStreamingCallbacks;
            var pcmFramesRequested = _audioOutput.TotalNativePcmFramesRequested;
            var pcmFramesPulled = _audioOutput.TotalNativePcmFramesPulled;
            var pcmEmptyReads = _audioOutput.TotalNativePcmEmptyReads;
            var nonSilentPcmFramesPulled = _audioOutput.TotalNonSilentPcmFramesPulled;
            if (Time.unscaledTime >= _nextAudioLivenessCheckTime)
            {
                var callbackDelta = streamingCallbacks - _lastAudioStreamingCallbacks;
                var pcmFramesRequestedDelta = pcmFramesRequested - _lastAudioPcmFramesRequested;
                var pcmFramesPulledDelta = pcmFramesPulled - _lastAudioPcmFramesPulled;
                var pcmEmptyReadsDelta = pcmEmptyReads - _lastAudioPcmEmptyReads;
                var nonSilentPcmFramesPulledDelta = nonSilentPcmFramesPulled - _lastAudioNonSilentPcmFramesPulled;
                var telemetry = _audioOutput.GetTelemetry();
                var fault = GbaAudioFaultPolicy.Classify(
                    telemetry,
                    callbackDelta,
                    pcmFramesRequestedDelta,
                    pcmFramesPulledDelta,
                    availableFrames);
                var ringNeedsConsumer = availableFrames >= AudioPrimeFrames || droppedFrames != 0U;
                if (ringNeedsConsumer && fault != GbaAudioFaultKind.None && fault != _lastAudioFaultKind)
                {
                    Debug.LogError(
                        $"GBA XR audio fault={fault}; callbacks={callbackDelta}, pcmRequested={pcmFramesRequestedDelta}, " +
                        $"pcmPulled={pcmFramesPulledDelta}, emptyReads={pcmEmptyReadsDelta}, " +
                        $"nonSilent={nonSilentPcmFramesPulledDelta}, " +
                        $"available={availableFrames}, nativeDropped={droppedFrames}, source={mgbaSourceSampleRate} Hz. " +
                        DescribeAudioTelemetry(telemetry));
                }
                else if (_lastAudioFaultKind != GbaAudioFaultKind.None && fault == GbaAudioFaultKind.None)
                {
                    Debug.Log($"GBA XR audio: output liveness recovered. {DescribeAudioTelemetry(telemetry)}");
                }

                // The counters are intentionally only observed on the main
                // thread. A zero nonSilent delta is informational because a
                // game may legitimately be quiet; it is never a fault alone.
                Debug.Log(
                    $"GBA XR audio diagnostic: callbacks={callbackDelta}, pcmRequested={pcmFramesRequestedDelta}, " +
                    $"pcmPulled={pcmFramesPulledDelta}, emptyReads={pcmEmptyReadsDelta}, " +
                    $"nonSilent={nonSilentPcmFramesPulledDelta}, available={availableFrames}, " +
                    $"nativeDropped={droppedFrames}, source={mgbaSourceSampleRate} Hz. " +
                    DescribeAudioTelemetry(telemetry));

                _lastAudioFaultKind = fault;
                _lastAudioStreamingCallbacks = streamingCallbacks;
                _lastAudioPcmFramesRequested = pcmFramesRequested;
                _lastAudioPcmFramesPulled = pcmFramesPulled;
                _lastAudioPcmEmptyReads = pcmEmptyReads;
                _lastAudioNonSilentPcmFramesPulled = nonSilentPcmFramesPulled;
                _nextAudioLivenessCheckTime = Time.unscaledTime + AudioLivenessIntervalSeconds;
            }

            _audioOutput.UpdateNativeDiagnostics(availableFrames, mgbaSourceSampleRate, droppedFrames);
            _frontendRuntime.UpdateAudioStatus(new GbaFrontendAudioStatus(
                audioState,
                availableFrames,
                droppedFrames));
        }

        private static string DescribeAudioTelemetry(GbaAudioOutputTelemetry telemetry)
        {
            return $"sourceEnabled={telemetry.SourceEnabled}, sourceActive={telemetry.SourceActive}, " +
                   $"sourcePlaying={telemetry.SourcePlaying}, clipAttached={telemetry.StreamingClipAttached}, " +
                   $"outputRate={telemetry.OutputSampleRate}, dspBuffer={telemetry.DspBufferSize}, " +
                   $"callbacks={telemetry.CallbackCount}, pcmRequested={telemetry.NativePcmFramesRequested}, " +
                   $"pcmPulled={telemetry.NativePcmFramesPulled}, emptyReads={telemetry.NativePcmEmptyReads}, " +
                   $"nonSilent={telemetry.NonSilentPcmFramesPulled}.";
        }
    }
}

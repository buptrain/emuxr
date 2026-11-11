using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.OpenXR.Features.Interactions;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace GbaXr
{
    /// <summary>
    /// Owns the Android XR AR Foundation components required to place the GBA
    /// display upright on a horizontal floor or table plane. The scan starts only in
    /// PlaneAnchoring mode and stops after a successful placement.
    /// </summary>
    public sealed class GbaPlaneAnchorController : MonoBehaviour
    {
        // Keep the display visibly above the detected surface. This avoids
        // visual intersection caused by the physical plane estimate jitter.
        private const float FloorSurfaceClearanceMeters = 0.05f;
        private const float MinimumScale = 0.5f;
        private const float MaximumScale = 3f;
        private const string SceneUnderstandingPermission = "android.permission.SCENE_UNDERSTANDING_COARSE";
        private readonly List<ARRaycastHit> _hits = new();
        private readonly List<HandPinch> _pinchingHands = new();
        private readonly Dictionary<int, HandPinchState> _handPinchStates = new();

        private GbaScreenRoot _screenRoot;
        private XROrigin _xrOrigin;
        private ARPlaneManager _planeManager;
        private ARRaycastManager _raycastManager;
        private ARAnchorManager _anchorManager;
        private ARCameraManager _cameraManager;
        private ARAnchor _activeAnchor;
        private GameObject _surfaceHighlightObject;
        private LineRenderer _surfaceHighlight;
        private Camera _camera;
        private bool _permissionRequestInFlight;
        private bool _arComponentsInitialized;
        private int _draggingHandDeviceId = -1;
        private ARPlane _previewPlane;
        private Pose _previewPose;
        private bool _hasPreviewPose;
        private Vector3 _grabOffsetFromPrimaryPinch;
        private bool _hasGrabOffset;
        private float _twoHandStartDistance;
        private float _twoHandStartScale;
        private bool _twoHandScaling;
        private int _primaryReleaseFrames;
        private PreviewRayOutcome _lastPointerRayOutcome = PreviewRayOutcome.NotAttempted;
        private PreviewRayOutcome _lastDownwardRayOutcome = PreviewRayOutcome.NotAttempted;

        public void Initialize(GbaScreenRoot screenRoot)
        {
            _screenRoot = screenRoot ?? throw new ArgumentNullException(nameof(screenRoot));
        }

        private void Update()
        {
            if (_screenRoot == null || _screenRoot.PlacementMode != GbaScreenPlacementMode.PlaneAnchoring)
            {
                HideSurfaceHighlight();
                return;
            }

            CollectPinchingHands();
            if (_draggingHandDeviceId < 0)
            {
                TryBeginHandDrag();
                return;
            }

            var draggingHand = FindPinchingHand(_draggingHandDeviceId);
            if (!draggingHand.HasValue)
            {
                if (IsPrimaryPinchReleaseCandidate())
                {
                    _primaryReleaseFrames++;
                    HideSurfaceHighlight();
                    if (_primaryReleaseFrames == 1)
                    {
                        Debug.Log("GBA XR: primary hand pinch release detected; confirming before drop.");
                    }
                    if (GbaHandPinchReleasePolicy.IsReleaseConfirmed(_primaryReleaseFrames))
                    {
                        CommitOrCancelHandDrag();
                    }
                }
                else
                {
                    _primaryReleaseFrames = 0;
                }
                return;
            }

            _primaryReleaseFrames = 0;
            UpdateTwoHandScale(draggingHand.Value);
            TryUpdatePreview(draggingHand.Value);
        }

        public void BeginScanning()
        {
            if (!EnsureArFoundationComponents())
            {
                return;
            }

            RequestSceneUnderstandingThenBeginScan();
        }

        private void CollectPinchingHands()
        {
            _pinchingHands.Clear();
            _handPinchStates.Clear();
            foreach (var device in InputSystem.devices)
            {
                if (device is not HandInteractionProfile.HandInteraction hand)
                {
                    continue;
                }

                var state = new HandPinchState(hand.isTracked.isPressed, hand.pinchValue.ReadValue());
                _handPinchStates[hand.deviceId] = state;
                if (!GbaHandPinchReleasePolicy.IsEngaged(state.IsTracked, state.Value))
                {
                    continue;
                }

                var rotation = hand.pointerRotation.ReadValue();
                if (rotation == default)
                {
                    rotation = hand.pinchRotation.ReadValue();
                }
                _pinchingHands.Add(new HandPinch(
                    hand.deviceId,
                    hand.pinchPosition.ReadValue(),
                    rotation));
            }
        }

        private void TryBeginHandDrag()
        {
            foreach (var hand in _pinchingHands)
            {
                if (!IsPointingAtScreen(hand.PointerRay))
                {
                    continue;
                }

                if (!EnsureArFoundationComponents())
                {
                    return;
                }

                _draggingHandDeviceId = hand.DeviceId;
                _hasPreviewPose = false;
                _previewPlane = null;
                CaptureGrabOffset(hand);
                _twoHandScaling = false;
                _primaryReleaseFrames = 0;
                _lastPointerRayOutcome = PreviewRayOutcome.NotAttempted;
                _lastDownwardRayOutcome = PreviewRayOutcome.NotAttempted;
                HideSurfaceHighlight();
                _screenRoot.DetachFromAnchorPreservingWorldTransform();
                BeginScanAfterPermission();
                Debug.Log("GBA XR: hand drag started. Move the pinch over a floor or table, then release to attach.");
                return;
            }
        }

        private void UpdateTwoHandScale(HandPinch draggingHand)
        {
            if (_pinchingHands.Count < 2)
            {
                _twoHandScaling = false;
                return;
            }

            var secondHand = _pinchingHands[0].DeviceId == draggingHand.DeviceId
                ? _pinchingHands[1]
                : _pinchingHands[0];
            var distance = Vector3.Distance(draggingHand.PinchPosition, secondHand.PinchPosition);
            if (!_twoHandScaling)
            {
                if (distance < 0.02f)
                {
                    return;
                }

                _twoHandScaling = true;
                _twoHandStartDistance = distance;
                _twoHandStartScale = _screenRoot.UniformScale;
                Debug.Log("GBA XR: two-hand scale started.");
            }

            _screenRoot.SetUniformScale(GbaPlanePlacementPolicy.ClampScale(
                _twoHandStartScale,
                _twoHandStartDistance,
                distance,
                MinimumScale,
                MaximumScale));
        }

        private void TryUpdatePreview(HandPinch hand)
        {
            var pointerOutcome = TryFindPlacementCandidate(hand.PointerRay, out var candidate);
            LogPreviewOutcome("pointer", pointerOutcome, ref _lastPointerRayOutcome);
            if (pointerOutcome == PreviewRayOutcome.Valid)
            {
                ApplyPreview(candidate);
                return;
            }

            var downwardRay = GbaHandPlacementPolicy.CreateDownwardSurfaceRay(hand.PinchPosition);
            var downwardOutcome = TryFindPlacementCandidate(downwardRay, out candidate);
            LogPreviewOutcome("downward", downwardOutcome, ref _lastDownwardRayOutcome);
            if (downwardOutcome == PreviewRayOutcome.Valid)
            {
                ApplyPreview(candidate);
                return;
            }

            if (ClearPreviewCandidate())
            {
                // Preserve continuity when a magnetic target disappears.
                CaptureGrabOffset(hand);
                Debug.Log("GBA XR: magnetic surface target lost; continuing free-air drag.");
            }
            UpdateFreeAirPose(hand);
        }

        private void CaptureGrabOffset(HandPinch hand)
        {
            _grabOffsetFromPrimaryPinch = _screenRoot.transform.position - hand.PinchPosition;
            _hasGrabOffset = true;
        }

        private void UpdateFreeAirPose(HandPinch hand)
        {
            if (!_hasGrabOffset)
            {
                CaptureGrabOffset(hand);
            }

            _screenRoot.SetWorldPosition(GbaMagneticAnchorPolicy.GetFreeAirPosition(
                hand.PinchPosition,
                _grabOffsetFromPrimaryPinch));
            UpdateDraggedScreenFacingUser();
        }

        private void UpdateDraggedScreenFacingUser()
        {
            if (_camera == null ||
                !GbaFacingRotationPolicy.TryBuildUprightFacingRotation(
                    _screenRoot.transform.position,
                    Vector3.up,
                    _camera.transform.position,
                    out var rotation))
            {
                return;
            }

            _screenRoot.SetWorldRotation(rotation);
        }

        private PreviewRayOutcome TryFindPlacementCandidate(Ray ray, out PlacementCandidate candidate)
        {
            candidate = default;
            _hits.Clear();
            if (_raycastManager == null ||
                !_raycastManager.Raycast(ray, _hits, TrackableType.PlaneWithinPolygon))
            {
                return PreviewRayOutcome.RaycastMiss;
            }

            var rejectedClassification = false;
            var rejectedPose = false;
            var rejectedMagneticRange = false;
            foreach (var hit in _hits)
            {
                var plane = _planeManager.GetPlane(hit.trackableId);
                if (plane == null || !GbaPlanePlacementPolicy.IsSupportedSurface(plane.classifications))
                {
                    rejectedClassification = true;
                    continue;
                }

                if (!GbaPlanePlacementPolicy.TryBuildFloorScreenAnchorPose(
                        hit.pose.position,
                        plane.transform.up,
                        _camera.transform.position,
                        FloorSurfaceClearanceMeters,
                        out var pose))
                {
                    rejectedPose = true;
                    continue;
                }

                if (!GbaMagneticAnchorPolicy.IsWithinMagneticRange(ray.origin, hit.pose.position))
                {
                    rejectedMagneticRange = true;
                    continue;
                }

                candidate = new PlacementCandidate(plane, hit.pose.position, pose);
                return PreviewRayOutcome.Valid;
            }

            return rejectedPose
                ? PreviewRayOutcome.PoseRejected
                : rejectedClassification
                    ? PreviewRayOutcome.ClassificationRejected
                    : rejectedMagneticRange
                        ? PreviewRayOutcome.MagneticRangeRejected
                    : PreviewRayOutcome.RaycastMiss;
        }

        private void ApplyPreview(PlacementCandidate candidate)
        {
            var acquiredNewMagneticTarget = !_hasPreviewPose || _previewPlane != candidate.Plane;
            _previewPlane = candidate.Plane;
            _previewPose = candidate.AnchorPose;
            _hasPreviewPose = true;
            _screenRoot.SetWorldPoseFromBottomAnchor(candidate.AnchorPose, GetScreenHeightMeters() * 0.5f);
            ShowSurfaceHighlight(
                candidate.HitPosition,
                candidate.Plane.transform.up,
                GetScreenWidthMeters(),
                candidate.Plane.classifications.HasFlag(PlaneClassifications.Floor));
            if (acquiredNewMagneticTarget)
            {
                Debug.Log("GBA XR: magnetic floor/table target acquired; screen preview snapped to the surface.");
            }
        }

        private bool ClearPreviewCandidate()
        {
            var hadCandidate = _hasPreviewPose || _previewPlane != null;
            _hasPreviewPose = false;
            _previewPlane = null;
            HideSurfaceHighlight();
            return hadCandidate;
        }

        private static void LogPreviewOutcome(
            string rayName,
            PreviewRayOutcome outcome,
            ref PreviewRayOutcome previousOutcome)
        {
            if (outcome == previousOutcome)
            {
                return;
            }

            previousOutcome = outcome;
            switch (outcome)
            {
                case PreviewRayOutcome.RaycastMiss:
                    Debug.Log($"GBA XR: {rayName} placement ray did not hit a tracked plane.");
                    break;
                case PreviewRayOutcome.ClassificationRejected:
                    Debug.Log($"GBA XR: {rayName} placement ray hit a plane that is not classified as Floor or Table.");
                    break;
                case PreviewRayOutcome.PoseRejected:
                    Debug.Log($"GBA XR: {rayName} placement ray hit Floor/Table, but its upright screen pose was rejected.");
                    break;
                case PreviewRayOutcome.MagneticRangeRejected:
                    Debug.Log($"GBA XR: {rayName} placement ray hit Floor/Table outside the 18 cm magnetic range; continuing free-air drag.");
                    break;
            }
        }

        private void CommitOrCancelHandDrag()
        {
            _draggingHandDeviceId = -1;
            _twoHandScaling = false;
            _primaryReleaseFrames = 0;
            if (!_hasPreviewPose || _previewPlane == null)
            {
                var retainedScale = _screenRoot.UniformScale;
                if (_activeAnchor != null)
                {
                    RemoveActiveAnchor();
                }
                ClearPreviewCandidate();
                Debug.LogWarning($"GBA XR: hand drag released without a magnetic Floor/Table target. Kept the free-air pose and retained scale {retainedScale:F2}x.");
                return;
            }

            ARAnchor newAnchor;
            try
            {
                newAnchor = _anchorManager.AttachAnchor(_previewPlane, _previewPose);
            }
            catch (InvalidOperationException exception)
            {
                Debug.LogWarning($"GBA XR: hand drop could not create an anchor: {exception.Message}");
                RetainFreeAirPoseAfterFailedDrop();
                return;
            }

            if (newAnchor == null)
            {
                Debug.LogWarning("GBA XR: hand drop was rejected by Android XR.");
                RetainFreeAirPoseAfterFailedDrop();
                return;
            }

            var oldAnchor = _activeAnchor;
            _activeAnchor = newAnchor;
            _screenRoot.AttachToAnchor(newAnchor.transform, GetScreenHeightMeters() * 0.5f);
            RemoveAnchor(oldAnchor);
            _hasPreviewPose = false;
            _previewPlane = null;
            _planeManager.enabled = false;
            _raycastManager.enabled = false;
            HideSurfaceHighlight();
            Debug.Log($"GBA XR: hand drop attached the upright screen to the detected floor or table plane at scale {_screenRoot.UniformScale:F2}x.");
        }

        private void RetainFreeAirPoseAfterFailedDrop()
        {
            var retainedScale = _screenRoot.UniformScale;
            if (_activeAnchor != null)
            {
                RemoveActiveAnchor();
            }
            ClearPreviewCandidate();
            Debug.LogWarning($"GBA XR: hand drop failed; kept the free-air pose and retained scale {retainedScale:F2}x.");
        }

        private HandPinch? FindPinchingHand(int deviceId)
        {
            foreach (var hand in _pinchingHands)
            {
                if (hand.DeviceId == deviceId)
                {
                    return hand;
                }
            }
            return null;
        }

        private bool IsPrimaryPinchReleaseCandidate()
        {
            return !_handPinchStates.TryGetValue(_draggingHandDeviceId, out var state) ||
                   GbaHandPinchReleasePolicy.IsReleaseCandidate(state.IsTracked, state.Value);
        }

        private bool IsPointingAtScreen(Ray ray)
        {
            var frameDriver = _screenRoot.GetComponentInChildren<GbaNativeFrameDriver>();
            var renderer = frameDriver != null ? frameDriver.GetComponent<MeshRenderer>() : null;
            if (renderer == null)
            {
                return false;
            }

            var screenPlane = new Plane(renderer.transform.forward, renderer.transform.position);
            if (!screenPlane.Raycast(ray, out var distance))
            {
                return false;
            }

            var localPoint = renderer.transform.InverseTransformPoint(ray.GetPoint(distance));
            var bounds = renderer.localBounds;
            return Mathf.Abs(localPoint.x - bounds.center.x) <= bounds.extents.x + 0.06f &&
                   Mathf.Abs(localPoint.y - bounds.center.y) <= bounds.extents.y + 0.06f;
        }

        public void ReturnToVirtualField()
        {
            if (_planeManager != null) _planeManager.enabled = false;
            if (_raycastManager != null) _raycastManager.enabled = false;
            if (_cameraManager != null) _cameraManager.enabled = false;
            _screenRoot.RestoreVirtualFieldTransform();
            RemoveActiveAnchor();
            if (_anchorManager != null) _anchorManager.enabled = false;
            HideSurfaceHighlight();
        }

        private bool EnsureArFoundationComponents()
        {
            if (_screenRoot == null)
            {
                Debug.LogError("GBA XR: plane anchor controller has no screen root.");
                return false;
            }

            _xrOrigin ??= UnityEngine.Object.FindFirstObjectByType<XROrigin>();
            if (_xrOrigin == null)
            {
                Debug.LogError("GBA XR: cannot start plane scanning because no XR Origin exists in the scene.");
                return false;
            }

            _camera ??= _xrOrigin.Camera != null ? _xrOrigin.Camera : Camera.main;
            if (_camera == null)
            {
                Debug.LogError("GBA XR: cannot start plane scanning because the XR Origin has no camera.");
                return false;
            }

            if (UnityEngine.Object.FindFirstObjectByType<ARSession>() == null)
            {
                var sessionObject = new GameObject("GBA XR AR Session");
                var session = sessionObject.AddComponent<ARSession>();
                session.matchFrameRateRequested = false;
            }

            _planeManager ??= _xrOrigin.GetComponent<ARPlaneManager>() ?? _xrOrigin.gameObject.AddComponent<ARPlaneManager>();
            _raycastManager ??= _xrOrigin.GetComponent<ARRaycastManager>() ?? _xrOrigin.gameObject.AddComponent<ARRaycastManager>();
            _anchorManager ??= _xrOrigin.GetComponent<ARAnchorManager>() ?? _xrOrigin.gameObject.AddComponent<ARAnchorManager>();
            _cameraManager ??= _camera.GetComponent<ARCameraManager>() ?? _camera.gameObject.AddComponent<ARCameraManager>();
            if (!_arComponentsInitialized)
            {
                // Android XR requires scene-understanding consent before these
                // components can create a tracker. They are enabled only from
                // BeginScanAfterPermission.
                _planeManager.enabled = false;
                _raycastManager.enabled = false;
                _anchorManager.enabled = false;
                _cameraManager.enabled = false;
                _arComponentsInitialized = true;
            }
            return true;
        }

        private void RequestSceneUnderstandingThenBeginScan()
        {
#if UNITY_ANDROID
            if (!Permission.HasUserAuthorizedPermission(SceneUnderstandingPermission))
            {
                if (_permissionRequestInFlight)
                {
                    return;
                }

                _permissionRequestInFlight = true;
                var callbacks = new PermissionCallbacks();
                callbacks.PermissionGranted += OnSceneUnderstandingPermissionGranted;
                callbacks.PermissionDenied += OnSceneUnderstandingPermissionDenied;
                Debug.Log("GBA XR: requesting Scene Understanding permission for surface placement.");
                Permission.RequestUserPermission(SceneUnderstandingPermission, callbacks);
                return;
            }
#endif
            BeginScanAfterPermission();
        }

#if UNITY_ANDROID
        private void OnSceneUnderstandingPermissionGranted(string permission)
        {
            _permissionRequestInFlight = false;
            if (permission != SceneUnderstandingPermission)
            {
                return;
            }

            Debug.Log("GBA XR: Scene Understanding permission granted.");
            BeginScanAfterPermission();
        }

        private void OnSceneUnderstandingPermissionDenied(string permission)
        {
            _permissionRequestInFlight = false;
            if (permission == SceneUnderstandingPermission)
            {
                Debug.LogWarning("GBA XR: Scene Understanding permission was denied; PlaneAnchoring remains in VirtualField behavior.");
            }
        }
#endif

        private void BeginScanAfterPermission()
        {
            if (!EnsureArFoundationComponents())
            {
                return;
            }

            _planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;
            _anchorManager.enabled = true;
            _cameraManager.enabled = true;
            _planeManager.enabled = true;
            _raycastManager.enabled = true;
            Debug.Log("GBA XR: surface scan enabled. Pinch the screen, drag it over a detected floor or table, then release to attach.");
        }

        private float GetScreenHeightMeters()
        {
            var frameDriver = _screenRoot.GetComponentInChildren<GbaNativeFrameDriver>();
            var renderer = frameDriver != null ? frameDriver.GetComponent<MeshRenderer>() : null;
            return renderer != null ? Mathf.Max(renderer.bounds.size.y, 0.01f) : 1f;
        }

        private float GetScreenWidthMeters()
        {
            var frameDriver = _screenRoot.GetComponentInChildren<GbaNativeFrameDriver>();
            var renderer = frameDriver != null ? frameDriver.GetComponent<MeshRenderer>() : null;
            return renderer != null ? Mathf.Max(renderer.bounds.size.x, 0.01f) : 1.5f;
        }

        private void ShowSurfaceHighlight(Vector3 position, Vector3 normal, float screenWidthMeters, bool isFloor)
        {
            EnsureSurfaceHighlight();
            if (_surfaceHighlight == null)
            {
                return;
            }

            var up = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up;
            if (Vector3.Dot(up, Vector3.up) < 0f)
            {
                up = -up;
            }
            var forward = Vector3.ProjectOnPlane(_camera.transform.forward, up);
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }

            var width = Mathf.Max(0.25f, screenWidthMeters);
            var depth = Mathf.Max(0.20f, width * 0.4f);
            var halfWidth = width * 0.5f;
            var halfDepth = depth * 0.5f;
            var color = isFloor
                ? new Color(0.1f, 0.9f, 1f, 1f)
                : new Color(1f, 0.72f, 0.1f, 1f);
            _surfaceHighlightObject.transform.SetPositionAndRotation(position + up * 0.008f, Quaternion.LookRotation(forward, up));
            _surfaceHighlight.startColor = color;
            _surfaceHighlight.endColor = color;
            _surfaceHighlight.SetPositions(new[]
            {
                new Vector3(-halfWidth, 0f, -halfDepth),
                new Vector3(-halfWidth, 0f, halfDepth),
                new Vector3(halfWidth, 0f, halfDepth),
                new Vector3(halfWidth, 0f, -halfDepth)
            });
            _surfaceHighlightObject.SetActive(true);
        }

        private void EnsureSurfaceHighlight()
        {
            if (_surfaceHighlightObject != null)
            {
                return;
            }

            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogWarning("GBA XR: surface placement highlight shader is unavailable.");
                return;
            }

            _surfaceHighlightObject = new GameObject("GBA XR Surface Placement Highlight");
            _surfaceHighlight = _surfaceHighlightObject.AddComponent<LineRenderer>();
            _surfaceHighlight.loop = true;
            _surfaceHighlight.useWorldSpace = false;
            _surfaceHighlight.positionCount = 4;
            _surfaceHighlight.widthMultiplier = 0.018f;
            _surfaceHighlight.numCornerVertices = 4;
            _surfaceHighlight.material = new Material(shader);
            _surfaceHighlight.material.color = Color.white;
            _surfaceHighlightObject.SetActive(false);
        }

        private void HideSurfaceHighlight()
        {
            if (_surfaceHighlightObject != null)
            {
                _surfaceHighlightObject.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (_surfaceHighlightObject != null)
            {
                Destroy(_surfaceHighlightObject);
            }
        }

        private void RemoveActiveAnchor()
        {
            if (_activeAnchor == null)
            {
                return;
            }

            RemoveAnchor(_activeAnchor);
            _activeAnchor = null;
        }

        private void RemoveAnchor(ARAnchor anchor)
        {
            if (anchor != null && _anchorManager != null && _anchorManager.enabled)
            {
                _anchorManager.TryRemoveAnchor(anchor);
            }
        }

        private readonly struct HandPinch
        {
            public HandPinch(int deviceId, Vector3 pinchPosition, Quaternion pointerRotation)
            {
                DeviceId = deviceId;
                PinchPosition = pinchPosition;
                PointerRay = new Ray(pinchPosition, pointerRotation * Vector3.forward);
            }

            public int DeviceId { get; }
            public Vector3 PinchPosition { get; }
            public Ray PointerRay { get; }
        }

        private readonly struct HandPinchState
        {
            public HandPinchState(bool isTracked, float value)
            {
                IsTracked = isTracked;
                Value = value;
            }

            public bool IsTracked { get; }
            public float Value { get; }
        }

        private readonly struct PlacementCandidate
        {
            public PlacementCandidate(ARPlane plane, Vector3 hitPosition, Pose anchorPose)
            {
                Plane = plane;
                HitPosition = hitPosition;
                AnchorPose = anchorPose;
            }

            public ARPlane Plane { get; }
            public Vector3 HitPosition { get; }
            public Pose AnchorPose { get; }
        }

        private enum PreviewRayOutcome
        {
            NotAttempted,
            Valid,
            RaycastMiss,
            ClassificationRejected,
            PoseRejected,
            MagneticRangeRejected
        }
    }

    /// <summary>Pure hand-placement helpers for EditMode coverage.</summary>
    public static class GbaHandPlacementPolicy
    {
        public static Ray CreateDownwardSurfaceRay(Vector3 pinchPosition) =>
            new(pinchPosition, Vector3.down);
    }

    /// <summary>Hysteresis and edge confirmation for a hand drag's primary pinch.</summary>
    public static class GbaHandPinchReleasePolicy
    {
        public const float EngagementThreshold = 0.70f;
        public const float ReleaseThreshold = 0.45f;
        public const int ReleaseConfirmationFrames = 2;

        public static bool IsEngaged(bool isTracked, float pinchValue) =>
            isTracked && !float.IsNaN(pinchValue) && pinchValue >= EngagementThreshold;

        public static bool IsReleaseCandidate(bool isTracked, float pinchValue) =>
            !isTracked || float.IsNaN(pinchValue) || pinchValue < ReleaseThreshold;

        public static bool IsReleaseConfirmed(int consecutiveReleaseFrames) =>
            consecutiveReleaseFrames >= ReleaseConfirmationFrames;
    }

    /// <summary>Pure magnetic surface-range policy for free-air hand drag.</summary>
    public static class GbaMagneticAnchorPolicy
    {
        public const float MaximumSnapDistanceMetres = 0.18f;

        public static bool IsWithinMagneticRange(Vector3 rayOrigin, Vector3 surfaceHitPosition) =>
            Vector3.Distance(rayOrigin, surfaceHitPosition) <= MaximumSnapDistanceMetres;

        public static Vector3 GetFreeAirPosition(Vector3 primaryPinchPosition, Vector3 grabOffset) =>
            primaryPinchPosition + grabOffset;
    }

    /// <summary>Pure placement math, isolated for edit-mode tests.</summary>
    public static class GbaPlanePlacementPolicy
    {
        public static bool IsSupportedSurface(PlaneClassifications classifications) =>
            classifications.HasFlag(PlaneClassifications.Floor) ||
            classifications.HasFlag(PlaneClassifications.Table);

        public static float ClampScale(
            float startingScale,
            float startingPinchDistance,
            float currentPinchDistance,
            float minimumScale,
            float maximumScale)
        {
            if (startingScale <= 0f || startingPinchDistance <= 0f ||
                currentPinchDistance < 0f || minimumScale <= 0f || maximumScale < minimumScale)
            {
                throw new ArgumentOutOfRangeException();
            }

            return Mathf.Clamp(
                startingScale * currentPinchDistance / startingPinchDistance,
                minimumScale,
                maximumScale);
        }

        /// <summary>
        /// Produces an AR anchor pose at the display's bottom-center point.
        /// Consumers with a center-pivoted screen must offset the visual upward
        /// by half its rendered height before presenting it.
        /// </summary>
        public static bool TryBuildFloorScreenAnchorPose(
            Vector3 hitPosition,
            Vector3 planeNormal,
            Vector3 cameraPosition,
            float floorClearanceMeters,
            out Pose pose)
        {
            pose = default;
            if (planeNormal.sqrMagnitude < 0.0001f || floorClearanceMeters < 0f)
            {
                return false;
            }

            var normalizedNormal = planeNormal.normalized;
            if (Mathf.Abs(Vector3.Dot(normalizedNormal, Vector3.up)) < 0.5f)
            {
                return false;
            }
            if (Vector3.Dot(normalizedNormal, Vector3.up) < 0f)
            {
                normalizedNormal = -normalizedNormal;
            }

            if (!GbaFacingRotationPolicy.TryBuildUprightFacingRotation(
                    hitPosition,
                    normalizedNormal,
                    cameraPosition,
                    out var rotation))
            {
                return false;
            }

            pose = new Pose(
                hitPosition + normalizedNormal * floorClearanceMeters,
                rotation);
            return true;
        }
    }

    /// <summary>Pure upright, camera-facing rotation math for free hand drag and anchors.</summary>
    public static class GbaFacingRotationPolicy
    {
        public static bool TryBuildUprightFacingRotation(
            Vector3 screenPosition,
            Vector3 upDirection,
            Vector3 userPosition,
            out Quaternion rotation)
        {
            rotation = default;
            if (upDirection.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            var up = upDirection.normalized;
            if (Vector3.Dot(up, Vector3.up) < 0f)
            {
                up = -up;
            }

            var facingUser = Vector3.ProjectOnPlane(userPosition - screenPosition, up);
            if (facingUser.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            // Unity's Quad primitive renders its local -Z face. Aim local +Z
            // away from the user so the rendered front (-Z) faces the camera.
            rotation = Quaternion.LookRotation(-facingUser.normalized, up);
            return true;
        }
    }
}

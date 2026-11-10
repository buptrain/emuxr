using UnityEngine;

namespace GbaXr
{
    public enum GbaScreenPlacementMode
    {
        VirtualField,
        PlaneAnchoring
    }

    /// <summary>
    /// Stable parent for the display, candidate planes, and console. It owns
    /// the reversible transition between the virtual field and a tracked
    /// Android XR plane anchor.
    /// </summary>
    public sealed class GbaScreenRoot : MonoBehaviour
    {
        [SerializeField] private GbaScreenPlacementMode placementMode = GbaScreenPlacementMode.VirtualField;
        private Transform _virtualParent;
        private Vector3 _virtualLocalPosition;
        private Quaternion _virtualLocalRotation;
        private Vector3 _virtualLocalScale;
        private GbaPlaneAnchorController _planeAnchorController;

        public GbaScreenPlacementMode PlacementMode => placementMode;

        private void Awake()
        {
            _virtualParent = transform.parent;
            _virtualLocalPosition = transform.localPosition;
            _virtualLocalRotation = transform.localRotation;
            _virtualLocalScale = transform.localScale;
        }

        public void SetPlacementMode(GbaScreenPlacementMode mode)
        {
            placementMode = mode;
            if (mode == GbaScreenPlacementMode.PlaneAnchoring)
            {
                GetPlaneAnchorController().BeginScanning();
                return;
            }

            _planeAnchorController?.ReturnToVirtualField();
            RestoreVirtualFieldTransform();
            Debug.Log("GBA XR: virtual-field screen placement selected.");
        }

        /// <summary>
        /// Parents the center-pivoted screen to an anchor placed at its bottom
        /// center. The offset is expressed in anchor-local meters so the AR
        /// anchor, rather than the screen pivot, remains the contact point.
        /// </summary>
        internal void AttachToAnchor(Transform anchorTransform, float bottomToPivotOffsetMeters)
        {
            var scale = transform.localScale;
            transform.SetParent(anchorTransform, false);
            transform.localPosition = Vector3.up * bottomToPivotOffsetMeters;
            transform.localRotation = Quaternion.identity;
            transform.localScale = scale;
        }

        internal void DetachFromAnchorPreservingWorldTransform()
        {
            transform.SetParent(null, true);
        }

        internal void SetWorldPoseFromBottomAnchor(Pose anchorPose, float bottomToPivotOffsetMeters)
        {
            transform.SetPositionAndRotation(
                anchorPose.position + anchorPose.rotation * Vector3.up * bottomToPivotOffsetMeters,
                anchorPose.rotation);
        }

        internal void SetWorldRotation(Quaternion rotation)
        {
            transform.rotation = rotation;
        }

        internal void SetWorldPosition(Vector3 position)
        {
            transform.position = position;
        }

        internal void SetUniformScale(float multiplier)
        {
            transform.localScale = _virtualLocalScale * multiplier;
        }

        internal float UniformScale => _virtualLocalScale.y > 0f
            ? transform.localScale.y / _virtualLocalScale.y
            : 1f;

        internal void RestoreVirtualFieldTransform()
        {
            transform.SetParent(_virtualParent, false);
            transform.localPosition = _virtualLocalPosition;
            transform.localRotation = _virtualLocalRotation;
            transform.localScale = _virtualLocalScale;
        }

        private GbaPlaneAnchorController GetPlaneAnchorController()
        {
            if (_planeAnchorController == null)
            {
                _planeAnchorController = GetComponent<GbaPlaneAnchorController>() ??
                                         gameObject.AddComponent<GbaPlaneAnchorController>();
                _planeAnchorController.Initialize(this);
            }
            return _planeAnchorController;
        }
    }
}

using UnityEngine;

namespace GbaXr
{
    /// <summary>
    /// Experimental-only viewpoint parallax for final visible-source planes.
    /// It does not reconstruct occluded pixels and must remain opt-in.
    /// </summary>
    public static class GbaForcePartitionParallaxPolicy
    {
        public const float MaximumAbsoluteOffsetMetres = 0.02f;
        // Avoid an unbounded ratio when the eye is effectively on the BG0 plane.
        public const float MinimumEyeDistanceMetres = 0.001f;

        /// <summary>Matches a plane's apparent size to BG0 from the current eye distance.</summary>
        public static bool TryGetExtendedProjectionScale(int source, float cameraLocalZToBg0, out float scale)
        {
            scale = 1f;
            if (!GbaForcePartitionProfile.TryGetDepth(source, true, out var depth) ||
                float.IsNaN(cameraLocalZToBg0) || float.IsInfinity(cameraLocalZToBg0) ||
                cameraLocalZToBg0 >= -MinimumEyeDistanceMetres)
            {
                return false;
            }

            var eyeDistance = -cameraLocalZToBg0;
            var planeDistance = eyeDistance + depth;
            if (planeDistance < MinimumEyeDistanceMetres) return false;
            scale = planeDistance / eyeDistance;
            return !float.IsNaN(scale) && !float.IsInfinity(scale);
        }

        /// <summary>
        /// Computes experimental horizontal parallax from a source's standard
        /// ForcePartition depth relative to BG0 and the eye's current local-Z
        /// distance to BG0. Positive local-Z background bands move opposite a
        /// positive eye X delta; the negative-Z OBJ band moves with it.
        /// </summary>
        public static bool TryGetLocalXOffset(
            int source,
            float cameraLocalZToBg0,
            float filteredCameraLocalXDelta,
            out float localXOffset)
        {
            return TryGetLocalXOffset(source, cameraLocalZToBg0, filteredCameraLocalXDelta, false, out localXOffset);
        }

        public static bool TryGetLocalXOffset(
            int source,
            float cameraLocalZToBg0,
            float filteredCameraLocalXDelta,
            bool extended,
            out float localXOffset)
        {
            if (!GbaForcePartitionProfile.TryGetDepth(source, extended, out var layerLocalZ))
            {
                localXOffset = 0f;
                return false;
            }

            var eyeDistanceToBg0 = Mathf.Abs(cameraLocalZToBg0);
            if (float.IsNaN(eyeDistanceToBg0) || float.IsInfinity(eyeDistanceToBg0) ||
                eyeDistanceToBg0 < MinimumEyeDistanceMetres)
            {
                localXOffset = 0f;
                return false;
            }

            localXOffset = Mathf.Clamp(
                (-layerLocalZ / eyeDistanceToBg0) * filteredCameraLocalXDelta,
                -MaximumAbsoluteOffsetMetres,
                MaximumAbsoluteOffsetMetres);
            return true;
        }
    }

    /// <summary>
    /// Keeps ForcePartitionParallax responsive while suppressing small headset
    /// tracking jitter. The state is raw screen-local X so a reference rebase
    /// can preserve continuity exactly.
    /// </summary>
    public static class GbaForcePartitionParallaxFilterPolicy
    {
        public const float ContinuousDeadZoneMetres = 0.0015f;
        public const float TimeConstantSeconds = 0.08f;

        public static float AdvanceFilteredCameraLocalX(
            float currentFilteredCameraLocalX,
            float rawCameraLocalX,
            float unscaledDeltaTime)
        {
            if (unscaledDeltaTime <= 0f)
            {
                return currentFilteredCameraLocalX;
            }

            var alpha = 1f - Mathf.Exp(-unscaledDeltaTime / TimeConstantSeconds);
            return Mathf.Lerp(currentFilteredCameraLocalX, rawCameraLocalX, alpha);
        }

        public static float ApplyContinuousDeadZone(float filteredCameraLocalX)
        {
            var magnitude = Mathf.Abs(filteredCameraLocalX);
            if (magnitude <= ContinuousDeadZoneMetres)
            {
                return 0f;
            }

            return Mathf.Sign(filteredCameraLocalX) * (magnitude - ContinuousDeadZoneMetres);
        }

        /// <summary>
        /// Produces the new reference coordinate that preserves the filtered
        /// screen-local signal when the screen's pose is rebased.
        /// </summary>
        public static float RebaseReferenceCameraLocalX(
            float currentCameraLocalX,
            float currentFilteredCameraLocalX) =>
            currentCameraLocalX - currentFilteredCameraLocalX;
    }
}

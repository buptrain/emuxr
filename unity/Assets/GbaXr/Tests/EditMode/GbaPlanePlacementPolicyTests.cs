using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace GbaXr.Tests
{
    public sealed class GbaPlanePlacementPolicyTests
    {
        [Test]
        public void HorizontalFloorPlacesBottomCenterAnchorOnFloor()
        {
            var placed = GbaPlanePlacementPolicy.TryBuildFloorScreenAnchorPose(
                new Vector3(2f, 0f, 4f),
                Vector3.up,
                new Vector3(2f, 1.5f, 0f),
                0f,
                out var pose);

            Assert.That(placed, Is.True);
            Assert.That(Vector3.Distance(pose.position, new Vector3(2f, 0f, 4f)), Is.LessThan(0.0001f));
            Assert.That(Vector3.Dot(pose.rotation * Vector3.back, Vector3.back), Is.GreaterThan(0.999f));
        }

        [Test]
        public void ClearanceLiftsTheScreenAboveTheDetectedSurface()
        {
            var placed = GbaPlanePlacementPolicy.TryBuildFloorScreenAnchorPose(
                Vector3.zero,
                Vector3.up,
                new Vector3(0f, 1.5f, -2f),
                0.05f,
                out var pose);

            Assert.That(placed, Is.True);
            Assert.That(pose.position.y, Is.EqualTo(0.05f).Within(0.0001f));
        }

        [Test]
        public void VerticalWallIsRejectedForFloorScreenPlacement()
        {
            var placed = GbaPlanePlacementPolicy.TryBuildFloorScreenAnchorPose(
                Vector3.zero,
                Vector3.forward,
                Vector3.back,
                0.02f,
                out _);

            Assert.That(placed, Is.False);
        }

        [Test]
        public void FloorAndTableClassificationsQualifyForSurfaceAnchoring()
        {
            Assert.That(GbaPlanePlacementPolicy.IsSupportedSurface(PlaneClassifications.Floor), Is.True);
            Assert.That(GbaPlanePlacementPolicy.IsSupportedSurface(PlaneClassifications.Table), Is.True);
            Assert.That(GbaPlanePlacementPolicy.IsSupportedSurface(PlaneClassifications.None), Is.False);
        }

        [Test]
        public void DownwardFallbackRayStartsAtThePinchAndTargetsHorizontalSurfaces()
        {
            var pinchPosition = new Vector3(1f, 1.25f, -0.5f);
            var ray = GbaHandPlacementPolicy.CreateDownwardSurfaceRay(pinchPosition);

            Assert.That(ray.origin, Is.EqualTo(pinchPosition));
            Assert.That(ray.direction, Is.EqualTo(Vector3.down));
        }

        [Test]
        public void UprightFacingRotationFacesTheUserAndPreservesUp()
        {
            var screenPosition = new Vector3(2f, 0.8f, 4f);
            var userPosition = new Vector3(0f, 1.7f, 0f);

            var built = GbaFacingRotationPolicy.TryBuildUprightFacingRotation(
                screenPosition,
                Vector3.up,
                userPosition,
                out var rotation);

            var expectedQuadFront = Vector3.ProjectOnPlane(userPosition - screenPosition, Vector3.up).normalized;
            Assert.That(built, Is.True);
            Assert.That(Vector3.Dot(rotation * Vector3.back, expectedQuadFront), Is.GreaterThan(0.999f));
            Assert.That(Vector3.Dot(rotation * Vector3.up, Vector3.up), Is.GreaterThan(0.999f));
        }

        [Test]
        public void UprightFacingRotationRejectsAUserDirectlyAboveTheScreen()
        {
            var built = GbaFacingRotationPolicy.TryBuildUprightFacingRotation(
                Vector3.zero,
                Vector3.up,
                Vector3.up,
                out _);

            Assert.That(built, Is.False);
        }

        [Test]
        public void PinchReleaseUsesHysteresisAndTwoFrameConfirmation()
        {
            Assert.That(GbaHandPinchReleasePolicy.IsEngaged(true, 0.70f), Is.True);
            Assert.That(GbaHandPinchReleasePolicy.IsReleaseCandidate(true, 0.60f), Is.False);
            Assert.That(GbaHandPinchReleasePolicy.IsReleaseCandidate(true, 0.44f), Is.True);
            Assert.That(GbaHandPinchReleasePolicy.IsReleaseCandidate(false, 1f), Is.True);
            Assert.That(GbaHandPinchReleasePolicy.IsReleaseConfirmed(1), Is.False);
            Assert.That(GbaHandPinchReleasePolicy.IsReleaseConfirmed(2), Is.True);
        }

        [Test]
        public void MagneticAnchoringOnlySnapsWithinEighteenCentimetres()
        {
            Assert.That(
                GbaMagneticAnchorPolicy.IsWithinMagneticRange(Vector3.zero, new Vector3(0f, 0f, 0.18f)),
                Is.True);
            Assert.That(
                GbaMagneticAnchorPolicy.IsWithinMagneticRange(Vector3.zero, new Vector3(0f, 0f, 0.181f)),
                Is.False);
        }

        [Test]
        public void FreeAirDragKeepsTheInitialGrabOffset()
        {
            var position = GbaMagneticAnchorPolicy.GetFreeAirPosition(
                new Vector3(1f, 2f, 3f),
                new Vector3(-0.25f, 0.5f, 0.75f));

            Assert.That(position, Is.EqualTo(new Vector3(0.75f, 2.5f, 3.75f)));
        }

        [Test]
        public void TwoHandScaleUsesPinchDistanceAndClampsToSafeRange()
        {
            Assert.That(
                GbaPlanePlacementPolicy.ClampScale(1f, 0.2f, 0.4f, 0.5f, 3f),
                Is.EqualTo(2f));
            Assert.That(
                GbaPlanePlacementPolicy.ClampScale(1f, 0.2f, 2f, 0.5f, 3f),
                Is.EqualTo(3f));
            Assert.That(
                GbaPlanePlacementPolicy.ClampScale(1f, 0.2f, 0.01f, 0.5f, 3f),
                Is.EqualTo(0.5f));
        }

        [Test]
        public void AttachingScreenDoesNotResetTheAdjustedScreenScale()
        {
            var anchorObject = new GameObject("anchor");
            var screenObject = new GameObject("screen");
            try
            {
                screenObject.transform.localScale = new Vector3(1.5f, 1f, 1f);
                var screenRoot = screenObject.AddComponent<GbaScreenRoot>();
                screenRoot.SetUniformScale(2.25f);

                screenRoot.AttachToAnchor(anchorObject.transform, 0.5f);

                Assert.That(screenRoot.UniformScale, Is.EqualTo(2.25f).Within(0.0001f));
                Assert.That(screenObject.transform.parent, Is.EqualTo(anchorObject.transform));
            }
            finally
            {
                Object.DestroyImmediate(screenObject);
                Object.DestroyImmediate(anchorObject);
            }
        }
    }
}

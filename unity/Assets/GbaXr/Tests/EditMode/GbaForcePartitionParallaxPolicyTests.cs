using NUnit.Framework;

namespace GbaXr.Tests
{
    public sealed class GbaForcePartitionParallaxPolicyTests
    {
        [TestCase(5, 0.95f)]
        [TestCase(1, 1f)]
        [TestCase(2, 1.05f)]
        [TestCase(0, 1.2f)]
        public void ExtendedProjectionMatchesApparentSizeAtOneMetre(int source, float expected)
        {
            Assert.That(GbaForcePartitionParallaxPolicy.TryGetExtendedProjectionScale(source, -1f, out var scale), Is.True);
            Assert.That(scale, Is.EqualTo(expected).Within(0.0001f));
        }

        [TestCase(-0.05f)]
        [TestCase(-0.02f)]
        [TestCase(0f)]
        [TestCase(1f)]
        public void ExtendedProjectionRejectsEyeOnOrBehindForegroundPlane(float eyeZ)
        {
            Assert.That(GbaForcePartitionParallaxPolicy.TryGetExtendedProjectionScale(5, eyeZ, out _), Is.False);
        }

        [Test]
        public void ExtendedDepthBandsUseTheirActualDepthForParallax()
        {
            Assert.That(GbaForcePartitionParallaxPolicy.TryGetLocalXOffset(5, 1f, 0.05f, true, out var obj), Is.True);
            Assert.That(GbaForcePartitionParallaxPolicy.TryGetLocalXOffset(1, 1f, 0.05f, true, out var bg0), Is.True);
            Assert.That(GbaForcePartitionParallaxPolicy.TryGetLocalXOffset(2, 1f, 0.05f, true, out var bg1), Is.True);
            Assert.That(GbaForcePartitionParallaxPolicy.TryGetLocalXOffset(0, 1f, 0.1f, true, out var backdrop), Is.True);
            Assert.That(obj, Is.EqualTo(0.0025f).Within(0.0001f));
            Assert.That(bg0, Is.Zero);
            Assert.That(bg1, Is.EqualTo(-0.0025f).Within(0.0001f));
            Assert.That(backdrop, Is.EqualTo(-0.02f).Within(0.0001f));
        }

        [Test]
        public void StandardDepthBandsAtFiveCentimetresUseGeometricParallaxWithCorrectSigns()
        {
            const float headMoveRight = 0.005f;
            const float eyeDistanceToBg0 = 0.05f;

            Assert.That(GbaForcePartitionParallaxPolicy.TryGetLocalXOffset(5, eyeDistanceToBg0, headMoveRight, out var obj), Is.True);
            Assert.That(GbaForcePartitionParallaxPolicy.TryGetLocalXOffset(1, eyeDistanceToBg0, headMoveRight, out var bg0), Is.True);
            Assert.That(GbaForcePartitionParallaxPolicy.TryGetLocalXOffset(2, eyeDistanceToBg0, headMoveRight, out var bg1), Is.True);
            Assert.That(GbaForcePartitionParallaxPolicy.TryGetLocalXOffset(3, eyeDistanceToBg0, headMoveRight, out var bg2), Is.True);
            Assert.That(GbaForcePartitionParallaxPolicy.TryGetLocalXOffset(4, eyeDistanceToBg0, headMoveRight, out var bg3), Is.True);
            Assert.That(GbaForcePartitionParallaxPolicy.TryGetLocalXOffset(0, eyeDistanceToBg0, headMoveRight, out var backdrop), Is.True);

            Assert.That(obj, Is.EqualTo(0.002f).Within(0.0001f));
            Assert.That(bg0, Is.Zero);
            // BG1 is 1 cm behind BG0 at a 5 cm eye distance: 0.2x head delta.
            Assert.That(bg1, Is.EqualTo(-0.001f).Within(0.0001f));
            Assert.That(bg2, Is.EqualTo(-0.002f).Within(0.0001f));
            Assert.That(bg3, Is.EqualTo(-0.003f).Within(0.0001f));
            Assert.That(backdrop, Is.EqualTo(-0.004f).Within(0.0001f));
        }

        [Test]
        public void OutputIsClampedToTwoCentimetres()
        {
            Assert.That(GbaForcePartitionParallaxPolicy.TryGetLocalXOffset(5, 0.001f, 1f, out var obj), Is.True);
            Assert.That(GbaForcePartitionParallaxPolicy.TryGetLocalXOffset(0, 0.001f, 1f, out var backdrop), Is.True);

            Assert.That(obj, Is.EqualTo(0.02f).Within(0.0001f));
            Assert.That(backdrop, Is.EqualTo(-0.02f).Within(0.0001f));
        }

        [Test]
        public void UnsupportedSourceIsRejected()
        {
            Assert.That(GbaForcePartitionParallaxPolicy.TryGetLocalXOffset(6, 0.05f, 0.01f, out var offset), Is.False);
            Assert.That(offset, Is.Zero);
        }

        [Test]
        public void ZeroOrNearZeroEyeDistanceIsRejected()
        {
            Assert.That(GbaForcePartitionParallaxPolicy.TryGetLocalXOffset(2, 0f, 0.01f, out var zeroDistanceOffset), Is.False);
            Assert.That(zeroDistanceOffset, Is.Zero);
            Assert.That(
                GbaForcePartitionParallaxPolicy.TryGetLocalXOffset(
                    2,
                    GbaForcePartitionParallaxPolicy.MinimumEyeDistanceMetres * 0.5f,
                    0.01f,
                    out var nearZeroOffset),
                Is.False);
            Assert.That(nearZeroOffset, Is.Zero);
        }

        [Test]
        public void EyeDistanceUsesTheAbsoluteScreenLocalZValue()
        {
            Assert.That(
                GbaForcePartitionParallaxPolicy.TryGetLocalXOffset(2, 0.05f, 0.005f, out var positiveZ),
                Is.True);
            Assert.That(
                GbaForcePartitionParallaxPolicy.TryGetLocalXOffset(2, -0.05f, 0.005f, out var negativeZ),
                Is.True);

            Assert.That(negativeZ, Is.EqualTo(positiveZ).Within(0.0001f));
        }

        [Test]
        public void FilterConvergesTowardTheContinuousCameraSignal()
        {
            var filtered = 0f;
            for (var index = 0; index < 20; index++)
            {
                filtered = GbaForcePartitionParallaxFilterPolicy.AdvanceFilteredCameraLocalX(
                    filtered,
                    0.02f,
                    0.016f);
            }

            Assert.That(filtered, Is.GreaterThan(0f));
            Assert.That(filtered, Is.LessThan(0.02f));
            Assert.That(filtered, Is.GreaterThan(0.019f));
        }

        [Test]
        public void ContinuousDeadZoneSuppressesJitterWithoutAJump()
        {
            Assert.That(
                GbaForcePartitionParallaxFilterPolicy.ApplyContinuousDeadZone(0.001f),
                Is.Zero);
            Assert.That(
                GbaForcePartitionParallaxFilterPolicy.ApplyContinuousDeadZone(0.002f),
                Is.EqualTo(0.0005f).Within(0.0001f));
            Assert.That(
                GbaForcePartitionParallaxFilterPolicy.ApplyContinuousDeadZone(-0.002f),
                Is.EqualTo(-0.0005f).Within(0.0001f));
        }

        [Test]
        public void RebasePreservesTheCurrentFilteredDelta()
        {
            const float cameraLocalXAfterScreenMove = 0.30f;
            const float filteredDelta = 0.012f;

            var rebasedReference = GbaForcePartitionParallaxFilterPolicy.RebaseReferenceCameraLocalX(
                cameraLocalXAfterScreenMove,
                filteredDelta);

            Assert.That(
                cameraLocalXAfterScreenMove - rebasedReference,
                Is.EqualTo(filteredDelta).Within(0.0001f));
        }
    }
}

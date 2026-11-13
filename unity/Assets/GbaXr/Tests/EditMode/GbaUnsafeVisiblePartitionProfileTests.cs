using NUnit.Framework;

namespace GbaXr.Tests
{
    public class GbaForcePartitionProfileTests
    {
        [Test]
        public void ExplicitForcePartition_UsesAStructurallyValidVisiblePartitionBeforeDiagnosticVerification()
        {
            Assert.That(
                GbaForcePartitionProfile.ShouldActivateExplicitForcePartition(
                    forcePartitionRequested: true,
                    hasStructurallyValidVisiblePartition: true),
                Is.True);
        }

        [Test]
        public void ExplicitForcePartition_RejectsMissingOrDisabledVisiblePartitions()
        {
            Assert.That(
                GbaForcePartitionProfile.ShouldActivateExplicitForcePartition(
                    forcePartitionRequested: false,
                    hasStructurallyValidVisiblePartition: true),
                Is.False);
            Assert.That(
                GbaForcePartitionProfile.ShouldActivateExplicitForcePartition(
                    forcePartitionRequested: true,
                    hasStructurallyValidVisiblePartition: false),
                Is.False);
        }

        [Test]
        public void SourceBuckets_MapToStableExperimentalDepthBands()
        {
            Assert.That(GbaForcePartitionProfile.TryGetDepth(5, false, out var obj), Is.True);
            Assert.That(GbaForcePartitionProfile.TryGetDepth(1, false, out var bg0), Is.True);
            Assert.That(GbaForcePartitionProfile.TryGetDepth(2, false, out var bg1), Is.True);
            Assert.That(GbaForcePartitionProfile.TryGetDepth(3, false, out var bg2), Is.True);
            Assert.That(GbaForcePartitionProfile.TryGetDepth(4, false, out var bg3), Is.True);
            Assert.That(GbaForcePartitionProfile.TryGetDepth(0, false, out var backdrop), Is.True);

            Assert.That(obj, Is.LessThan(bg0));
            Assert.That(bg0, Is.LessThan(bg1));
            Assert.That(bg1, Is.LessThan(bg2));
            Assert.That(bg2, Is.LessThan(bg3));
            Assert.That(bg3, Is.LessThan(backdrop));
            Assert.That(obj, Is.EqualTo(-0.02f).Within(0.0001f));
        }

        [Test]
        public void UnknownSource_IsRejected()
        {
            Assert.That(GbaForcePartitionProfile.TryGetDepth(6, false, out _), Is.False);
        }

        [Test]
        public void ExtendedModeUsesFiveCentimetreSpacing()
        {
            Assert.That(GbaForcePartitionProfile.TryGetDepth(5, true, out var obj), Is.True);
            Assert.That(GbaForcePartitionProfile.TryGetDepth(1, true, out var bg0), Is.True);
            Assert.That(GbaForcePartitionProfile.TryGetDepth(2, true, out var bg1), Is.True);
            Assert.That(GbaForcePartitionProfile.TryGetDepth(3, true, out var bg2), Is.True);
            Assert.That(GbaForcePartitionProfile.TryGetDepth(4, true, out var bg3), Is.True);
            Assert.That(GbaForcePartitionProfile.TryGetDepth(0, true, out var backdrop), Is.True);

            Assert.That(bg0 - obj, Is.EqualTo(0.05f).Within(0.0001f));
            Assert.That(bg1 - bg0, Is.EqualTo(0.05f).Within(0.0001f));
            Assert.That(bg2 - bg1, Is.EqualTo(0.05f).Within(0.0001f));
            Assert.That(bg3 - bg2, Is.EqualTo(0.05f).Within(0.0001f));
            Assert.That(backdrop - bg3, Is.EqualTo(0.05f).Within(0.0001f));
        }
    }
}

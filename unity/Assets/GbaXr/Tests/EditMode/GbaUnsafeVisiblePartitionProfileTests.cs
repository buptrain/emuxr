using NUnit.Framework;

namespace GbaXr.Tests
{
    public class GbaUnsafeVisiblePartitionProfileTests
    {
        [Test]
        public void SourceBuckets_MapToStableExperimentalDepthBands()
        {
            Assert.That(GbaUnsafeVisiblePartitionProfile.TryGetDepth(5, out var obj), Is.True);
            Assert.That(GbaUnsafeVisiblePartitionProfile.TryGetDepth(1, out var bg0), Is.True);
            Assert.That(GbaUnsafeVisiblePartitionProfile.TryGetDepth(2, out var bg1), Is.True);
            Assert.That(GbaUnsafeVisiblePartitionProfile.TryGetDepth(3, out var bg2), Is.True);
            Assert.That(GbaUnsafeVisiblePartitionProfile.TryGetDepth(4, out var bg3), Is.True);
            Assert.That(GbaUnsafeVisiblePartitionProfile.TryGetDepth(0, out var backdrop), Is.True);

            Assert.That(obj, Is.LessThan(bg0));
            Assert.That(bg0, Is.LessThan(bg1));
            Assert.That(bg1, Is.LessThan(bg2));
            Assert.That(bg2, Is.LessThan(bg3));
            Assert.That(bg3, Is.LessThan(backdrop));
        }

        [Test]
        public void UnknownSource_IsRejected()
        {
            Assert.That(GbaUnsafeVisiblePartitionProfile.TryGetDepth(6, out _), Is.False);
        }
    }
}

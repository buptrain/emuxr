using System.Collections.Generic;
using NUnit.Framework;

namespace GbaXr.Tests
{
    public class GbaDepthProfileTests
    {
        [Test]
        public void EqualPriority_UsesAuthoredObjThenBg0ThenBg1DepthBands()
        {
            Assert.That(GbaDepthProfile.TryGetDepth(5, 2U, out var objZ), Is.True);
            Assert.That(GbaDepthProfile.TryGetDepth(1, 2U, out var bg0Z), Is.True);
            Assert.That(GbaDepthProfile.TryGetDepth(2, 2U, out var bg1Z), Is.True);

            Assert.That(objZ, Is.LessThan(bg0Z));
            Assert.That(bg0Z, Is.LessThan(bg1Z));
            Assert.That(objZ, Is.EqualTo(-0.02f));
            Assert.That(bg0Z, Is.Zero);
            Assert.That(bg1Z, Is.EqualTo(0.01f));
        }

        [Test]
        public void InvalidGbaPriority_IsRejected()
        {
            Assert.That(GbaDepthProfile.TryGetDepth(1, 4U, out _), Is.False);
        }

        [Test]
        public void Profile_RejectsUnsupportedSourcesAndMissingBg0()
        {
            Assert.That(GbaDepthProfile.SupportsSourceSet(new HashSet<int> { 1, 2 }), Is.True);
            Assert.That(GbaDepthProfile.SupportsSourceSet(new HashSet<int> { 1, 5 }), Is.True);
            Assert.That(GbaDepthProfile.SupportsSourceSet(new HashSet<int> { 1, 3 }), Is.False);
            Assert.That(GbaDepthProfile.SupportsSourceSet(new HashSet<int> { 2 }), Is.False);
        }
    }
}

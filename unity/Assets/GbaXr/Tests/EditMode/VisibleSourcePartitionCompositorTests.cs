using NUnit.Framework;

namespace GbaXr.Tests
{
    public class VisibleSourcePartitionCompositorTests
    {
        private const int PixelCount = GbaTestFrameSurface.Width * GbaTestFrameSurface.Height;

        [Test]
        public void Composite_DisjointCoverage_ReconstructsTheVisibleFrame()
        {
            var firstColor = SolidRgba(1, 2, 3, 255);
            var secondColor = SolidRgba(4, 5, 6, 255);
            var firstCoverage = SolidCoverage(255);
            var secondCoverage = new byte[PixelCount];
            firstCoverage[1] = 0;
            secondCoverage[1] = 255;
            var destination = new byte[PixelCount * 4];

            var result = VisibleSourcePartitionCompositor.TryComposite(new[]
            {
                new VisibleSourcePartitionCompositor.Plane(1, firstColor, firstCoverage),
                new VisibleSourcePartitionCompositor.Plane(5, secondColor, secondCoverage)
            }, destination, out var error);

            Assert.That(result, Is.True, error);
            Assert.That(destination[0], Is.EqualTo(1));
            Assert.That(destination[4], Is.EqualTo(4));
            Assert.That(destination[5], Is.EqualTo(5));
            Assert.That(destination[6], Is.EqualTo(6));
        }

        [Test]
        public void Composite_OverlappingCoverage_FailsExplicitly()
        {
            var color = SolidRgba(1, 2, 3, 255);
            var coverage = SolidCoverage(255);
            var destination = new byte[PixelCount * 4];

            var result = VisibleSourcePartitionCompositor.TryComposite(new[]
            {
                new VisibleSourcePartitionCompositor.Plane(1, color, coverage),
                new VisibleSourcePartitionCompositor.Plane(5, color, coverage)
            }, destination, out var error);

            Assert.That(result, Is.False);
            Assert.That(error, Does.Contain("overlaps"));
        }

        private static byte[] SolidRgba(byte red, byte green, byte blue, byte alpha)
        {
            var rgba = new byte[PixelCount * 4];
            for (var pixel = 0; pixel < PixelCount; pixel++)
            {
                var offset = pixel * 4;
                rgba[offset] = red;
                rgba[offset + 1] = green;
                rgba[offset + 2] = blue;
                rgba[offset + 3] = alpha;
            }
            return rgba;
        }

        private static byte[] SolidCoverage(byte value)
        {
            var coverage = new byte[PixelCount];
            for (var pixel = 0; pixel < PixelCount; pixel++)
            {
                coverage[pixel] = value;
            }
            return coverage;
        }
    }
}

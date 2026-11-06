using NUnit.Framework;

namespace GbaXr.Tests
{
    public class LayerOrderingTests
    {
        [Test]
        public void DiagnosticFrame_HasTheNativeGbaDimensions()
        {
            var pixels = GbaTestFrameSurface.CreateDiagnosticFrame();

            Assert.That(pixels, Has.Length.EqualTo(GbaTestFrameSurface.Width * GbaTestFrameSurface.Height));
            Assert.That(GbaTestFrameSurface.Width, Is.EqualTo(240));
            Assert.That(GbaTestFrameSurface.Height, Is.EqualTo(160));
        }

        [Test]
        public void NativeAbiContract_UsesTheExpectedRgbaFrameByteCount()
        {
            Assert.That(GbaTestFrameSurface.Width * GbaTestFrameSurface.Height * 4, Is.EqualTo(153600));
            Assert.That(GbaNativeBindings.AbiVersion, Is.EqualTo(3));
        }

        [Test]
        public void SyntheticFixture_GoldenFrameHashMatchesMode3Checkerboard()
        {
            var frame = new byte[GbaTestFrameSurface.Width * GbaTestFrameSurface.Height * 4];
            var offset = 0;
            for (var y = 0; y < GbaTestFrameSurface.Height; y++)
            {
                for (var x = 0; x < GbaTestFrameSurface.Width; x++)
                {
                    var white = (((x >> 4) ^ (y >> 4)) & 1) == 0;
                    frame[offset++] = 255;
                    frame[offset++] = white ? (byte)255 : (byte)0;
                    frame[offset++] = white ? (byte)255 : (byte)0;
                    frame[offset++] = 255;
                }
            }

            Assert.That(FrameHash.ComputeFnv1a32(frame), Is.EqualTo(0xF9838FC5u));
        }

        [Test]
        public void FrontToBack_UsesGbaPriorityThenStableOrder()
        {
            var ordered = LayerOrdering.FrontToBack(new[]
            {
                new LayerDescriptor(3, 0), new LayerDescriptor(1, 4), new LayerDescriptor(1, 2)
            });

            Assert.That(ordered[0].StableOrder, Is.EqualTo(2));
            Assert.That(ordered[1].StableOrder, Is.EqualTo(4));
            Assert.That(ordered[2].Priority, Is.EqualTo(3));
        }

        [Test]
        public void NativeTopLeftRows_AreFlippedForUnityTextureUpload()
        {
            var source = new byte[]
            {
                1, 0, 0, 255, 2, 0, 0, 255,
                3, 0, 0, 255, 4, 0, 0, 255
            };
            var destination = new byte[source.Length];

            GbaTestFrameSurface.CopyTopLeftRgbaToUnityRowOrder(source, destination, 2, 2);

            Assert.That(destination[0], Is.EqualTo(3));
            Assert.That(destination[4], Is.EqualTo(4));
            Assert.That(destination[8], Is.EqualTo(1));
            Assert.That(destination[12], Is.EqualTo(2));
        }
    }
}

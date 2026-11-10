using System;
using NUnit.Framework;

namespace GbaXr.Tests
{
    public sealed class GbaPresentationModeTests
    {
        [TestCase(true, false, false, false, 1U << 9)]
        [TestCase(false, true, false, false, 1U << 8)]
        [TestCase(true, true, false, false, (1U << 9) | (1U << 8))]
        [TestCase(false, false, true, true, (1U << 9) | (1U << 8))]
        public void XboxFaceButtonsAndShouldersMapToGbaShoulders(bool x, bool y, bool lb, bool rb, uint expected)
        {
            Assert.That(GbaNativeFrameDriver.MapShoulderKeys(x, y, lb, rb), Is.EqualTo(expected));
        }

        [Test]
        public void ConsoleModesUseTheCurrentNamesWithoutTheRetiredProfileMode()
        {
            var names = Enum.GetNames(typeof(GbaNativeFrameDriver.PresentationMode));

            Assert.That(names, Is.EquivalentTo(new[]
            {
                "Auto",
                "Original",
                "ForceUnprofiledCandidates",
                "ForcePartition",
                "ForcePartitionParallax",
                "ForcePartitionExtendedParallax"
            }));
            Assert.That(names, Does.Not.Contain("ForceProfile"));
            Assert.That(names, Does.Not.Contain("ForceResolved"));
            Assert.That((int)GbaNativeFrameDriver.PresentationMode.Original, Is.EqualTo(1));
            Assert.That((int)GbaNativeFrameDriver.PresentationMode.ForceUnprofiledCandidates, Is.EqualTo(3));
            Assert.That((int)GbaNativeFrameDriver.PresentationMode.ForcePartition, Is.EqualTo(4));
            Assert.That(Enum.IsDefined(typeof(GbaNativeFrameDriver.PresentationMode), 5), Is.False);
            Assert.That((int)GbaNativeFrameDriver.PresentationMode.ForcePartitionParallax, Is.EqualTo(6));
            Assert.That((int)GbaNativeFrameDriver.PresentationMode.ForcePartitionExtendedParallax, Is.EqualTo(7));
        }
    }
}

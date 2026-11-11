using NUnit.Framework;
namespace GbaXr.Tests
{
    public sealed class EmuControllerInputProfileTests
    {
        [Test] public void GbaFamily_ResolvesGbaLayout() => Assert.That(EmuControllerInputProfiles.Resolve(EmuCoreFamily.Gba).CalibrationOrder.Length, Is.EqualTo(10));
        [Test] public void NdsFamily_ResolvesNdsLayout() { var p = EmuControllerInputProfiles.Resolve(EmuCoreFamily.Nds); Assert.That(p.Family, Is.EqualTo(EmuCoreFamily.Nds)); CollectionAssert.Contains(p.CalibrationOrder, EmuControllerAction.X); CollectionAssert.Contains(p.CalibrationOrder, EmuControllerAction.Y); }
    }
}

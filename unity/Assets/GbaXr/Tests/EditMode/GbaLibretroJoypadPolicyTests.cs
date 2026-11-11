using NUnit.Framework;

namespace GbaXr.Tests
{
    public sealed class GbaLibretroJoypadPolicyTests
    {
        [TestCase(GbaLibretroJoypadPolicy.GbaA, 1 << 8)]
        [TestCase(GbaLibretroJoypadPolicy.GbaB, 1 << 0)]
        [TestCase(GbaLibretroJoypadPolicy.GbaSelect, 1 << 2)]
        [TestCase(GbaLibretroJoypadPolicy.GbaStart, 1 << 3)]
        [TestCase(GbaLibretroJoypadPolicy.GbaRight, 1 << 7)]
        [TestCase(GbaLibretroJoypadPolicy.GbaLeft, 1 << 6)]
        [TestCase(GbaLibretroJoypadPolicy.GbaUp, 1 << 4)]
        [TestCase(GbaLibretroJoypadPolicy.GbaDown, 1 << 5)]
        [TestCase(GbaLibretroJoypadPolicy.GbaR, 1 << 11)]
        [TestCase(GbaLibretroJoypadPolicy.GbaL, 1 << 10)]
        public void EveryGbaBit_MapsToTheExpectedRetroPadId(uint gbaMask, int expectedRetroMask)
        {
            Assert.That(GbaLibretroJoypadPolicy.TranslateGbaMask(gbaMask), Is.EqualTo((ushort)expectedRetroMask));
        }

        [Test]
        public void CombinedDirectionsAndButtons_PreserveEveryRequestedKey()
        {
            var gbaMask = GbaLibretroJoypadPolicy.GbaA |
                          GbaLibretroJoypadPolicy.GbaUp |
                          GbaLibretroJoypadPolicy.GbaLeft |
                          GbaLibretroJoypadPolicy.GbaL |
                          GbaLibretroJoypadPolicy.GbaR;

            var retroMask = GbaLibretroJoypadPolicy.TranslateGbaMask(gbaMask);

            Assert.That(retroMask, Is.EqualTo((ushort)((1 << 8) | (1 << 4) | (1 << 6) | (1 << 10) | (1 << 11))));
        }

        [Test]
        public void UnknownBits_AreNotForwardedToTheAdapter()
        {
            Assert.That(GbaLibretroJoypadPolicy.TranslateGbaMask(1U << 20), Is.EqualTo(0));
        }
    }
}

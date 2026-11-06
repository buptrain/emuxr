using NUnit.Framework;

namespace GbaXr.Tests
{
    public class GbaPresentationProfilePolicyTests
    {
        [TestCase("gba-fixtures/bg_tie.gba")]
        [TestCase("gba-fixtures/obj_priority_halfword.gba")]
        public void ApprovedSyntheticFixtures_CanUseTheFirstDepthProfile(string romPath)
        {
            Assert.That(GbaPresentationProfilePolicy.AllowsFirstDepthProfile(romPath), Is.True);
        }

        [TestCase("gba-fixtures/alpha_blend.gba")]
        [TestCase("gba-user/owned-game.gba")]
        [TestCase("")]
        public void UnprofiledContent_RemainsResolvedOnly(string romPath)
        {
            Assert.That(GbaPresentationProfilePolicy.AllowsFirstDepthProfile(romPath), Is.False);
        }

        [Test]
        public void DeveloperForceMode_CanBypassOnlyTheRomAllowList()
        {
            Assert.That(GbaPresentationProfilePolicy.AllowsCandidateDepth("gba-user/owned-game.gba", false), Is.False);
            Assert.That(GbaPresentationProfilePolicy.AllowsCandidateDepth("gba-user/owned-game.gba", true), Is.True);
        }
    }
}

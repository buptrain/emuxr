using NUnit.Framework;

namespace GbaXr.Tests
{
    public sealed class GbaCoreModuleProbePolicyTests
    {
        [Test]
        public void DefaultToggle_IsDisabledAndDoesNotAttemptProbe()
        {
            Assert.That(GbaCoreModuleProbePolicy.EvaluatePreflight(false, true, true, false),
                Is.EqualTo(GbaCoreModuleProbeResult.Disabled));
        }

        [Test]
        public void Preflight_RequiresAndroidPackageAndExclusiveProbeGate()
        {
            Assert.That(GbaCoreModuleProbePolicy.EvaluatePreflight(true, false, true, false),
                Is.EqualTo(GbaCoreModuleProbeResult.UnsupportedPlatform));
            Assert.That(GbaCoreModuleProbePolicy.EvaluatePreflight(true, true, false, false),
                Is.EqualTo(GbaCoreModuleProbeResult.PackageUnavailable));
            Assert.That(GbaCoreModuleProbePolicy.EvaluatePreflight(true, true, true, true),
                Is.EqualTo(GbaCoreModuleProbeResult.AnotherProbeIsActive));
            Assert.That(GbaCoreModuleProbePolicy.EvaluatePreflight(true, true, true, false),
                Is.EqualTo(GbaCoreModuleProbeResult.Pending));
        }

        [Test]
        public void ModuleName_MustRemainBareSoname()
        {
            Assert.That(GbaCoreModuleProbePolicy.IsBareModuleSoname(
                GbaCoreModuleProbePolicy.ModuleSoname), Is.True);
            Assert.That(GbaCoreModuleProbePolicy.IsBareModuleSoname("/data/local/tmp/core.so"), Is.False);
            Assert.That(GbaCoreModuleProbePolicy.IsBareModuleSoname("..\\core.so"), Is.False);
            Assert.That(GbaCoreModuleProbePolicy.IsBareModuleSoname("core.dll"), Is.False);
        }

        [Test]
        public void AudioEvidence_RequiresNative48KhzOutput()
        {
            Assert.That(GbaCoreModuleProbePolicy.HasExpectedAudioRate(48000), Is.True);
            Assert.That(GbaCoreModuleProbePolicy.HasExpectedAudioRate(32768), Is.False);
        }
    }
}

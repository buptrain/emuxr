using System;
using NUnit.Framework;

namespace GbaXr.Tests
{
    public sealed class GbaLibretroFixtureProbePolicyTests
    {
        [Test]
        public void DefaultToggle_DoesNotAttemptTheFixtureAdapter()
        {
            var result = GbaLibretroFixtureProbePolicy.EvaluatePreflight(
                enabled: false,
                isAndroidPlayer: true,
                adapterPackaged: true,
                anotherProbeIsActive: false);

            Assert.That(result, Is.EqualTo(GbaLibretroFixtureProbeResult.Disabled));
            Assert.That(GbaLibretroFixtureProbePolicy.ShouldAttempt(result), Is.False);
        }

        [Test]
        public void FixtureProbe_RequiresAndroidPackageAndExclusiveOwnership()
        {
            Assert.That(
                GbaLibretroFixtureProbePolicy.EvaluatePreflight(true, false, true, false),
                Is.EqualTo(GbaLibretroFixtureProbeResult.UnsupportedPlatform));
            Assert.That(
                GbaLibretroFixtureProbePolicy.EvaluatePreflight(true, true, false, false),
                Is.EqualTo(GbaLibretroFixtureProbeResult.AdapterNotPackaged));
            Assert.That(
                GbaLibretroFixtureProbePolicy.EvaluatePreflight(true, true, true, true),
                Is.EqualTo(GbaLibretroFixtureProbeResult.AnotherProbeIsActive));
            Assert.That(
                GbaLibretroFixtureProbePolicy.ShouldAttempt(
                    GbaLibretroFixtureProbePolicy.EvaluatePreflight(true, true, true, false)),
                Is.True);
        }

        [Test]
        public void ApprovedFixtureContract_IsPinnedToTheGeneratedSyntheticAsset()
        {
            var fixture = GbaLibretroFixtureProbePolicy.FrameSequenceFixture;

            Assert.That(fixture.RelativePath, Is.EqualTo("gba-fixtures/frame_sequence.gba"));
            Assert.That(fixture.ByteCount, Is.EqualTo(280));
            Assert.That(fixture.Sha256, Is.EqualTo("760CEF4F09635F762EDDD04473DBF0E6D2B227F0BC4AAECAF1A55EE00C8DC450"));
        }

        [Test]
        public void FixtureHashValidation_RequiresTheExactByteContract()
        {
            var oneZeroByte = new GbaLibretroFixtureContract(
                "synthetic.gba",
                1,
                "6E340B9CFFB37A989CA544E6BB780A2C78901D3FB33738768511A30617AFA01D");

            Assert.That(
                GbaLibretroFixtureProbePolicy.MatchesFixtureContract(new byte[] { 0 }, oneZeroByte),
                Is.True);
            Assert.That(
                GbaLibretroFixtureProbePolicy.MatchesFixtureContract(new byte[] { 1 }, oneZeroByte),
                Is.False);
        }

        [Test]
        public void LeaseValidation_RequiresOneNonNull240x160Rgba8Lease()
        {
            Assert.That(
                GbaLibretroFixtureProbePolicy.ValidateLease(
                    1, new IntPtr(1), 240, 160, 960),
                Is.EqualTo(GbaLibretroFixtureProbeResult.Success));
            Assert.That(
                GbaLibretroFixtureProbePolicy.ValidateLease(
                    0, new IntPtr(1), 240, 160, 960),
                Is.EqualTo(GbaLibretroFixtureProbeResult.InvalidFrameLayout));
            Assert.That(
                GbaLibretroFixtureProbePolicy.ValidateLease(
                    1, IntPtr.Zero, 240, 160, 960),
                Is.EqualTo(GbaLibretroFixtureProbeResult.InvalidFrameLayout));
            Assert.That(
                GbaLibretroFixtureProbePolicy.ValidateLease(
                    1, new IntPtr(1), 240, 160, 959),
                Is.EqualTo(GbaLibretroFixtureProbeResult.InvalidFrameLayout));
        }

        [Test]
        public void TimeoutOutcome_DistinguishesStartupFromFramePublication()
        {
            Assert.That(
                GbaLibretroFixtureProbePolicy.GetTimeoutResult(false),
                Is.EqualTo(GbaLibretroFixtureProbeResult.RunningTimeout));
            Assert.That(
                GbaLibretroFixtureProbePolicy.GetTimeoutResult(true),
                Is.EqualTo(GbaLibretroFixtureProbeResult.FrameTimeout));
        }
    }
}

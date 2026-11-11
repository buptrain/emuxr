using NUnit.Framework;

namespace GbaXr.Tests
{
    public sealed class GbaDirectFrameSequenceSweepPolicyTests
    {
        [Test]
        public void Sweep_IsOptInForApprovedFixtureButAutomaticallyArmsItsMismatchPath()
        {
            Assert.That(GbaDirectFrameSequenceSweepPolicy.ShouldStart(true, false, false), Is.False);
            Assert.That(GbaDirectFrameSequenceSweepPolicy.ShouldStart(true, true, false), Is.True);
            Assert.That(GbaDirectFrameSequenceSweepPolicy.ShouldStart(true, false, true), Is.True);
            Assert.That(GbaDirectFrameSequenceSweepPolicy.ShouldStart(false, true, true), Is.False);
        }

        [Test]
        public void Sweep_UsesLaterCheckpointsAndFormatsSourceOnlyEvidence()
        {
            CollectionAssert.AreEqual(new[] { 30, 60, 120 }, GbaDirectFrameSequenceSweepPolicy.LeaseCheckpoints);
            Assert.That(GbaDirectFrameSequenceSweepPolicy.IsCheckpoint(5), Is.False);
            Assert.That(GbaDirectFrameSequenceSweepPolicy.IsCheckpoint(30), Is.True);
            Assert.That(GbaDirectFrameSequenceSweepPolicy.IsCheckpoint(60), Is.True);
            Assert.That(GbaDirectFrameSequenceSweepPolicy.IsCheckpoint(120), Is.True);

            var diagnostic = new GbaLibretroFrameSequenceDiagnostic(
                7, 12, 24, 0xFFFFFFFFU, 0xFF0000FFU, "none", 0U, 0U, 0U);

            Assert.That(
                GbaDirectFrameSequenceSweepPolicy.FormatCheckpoint(60, 0xF9838FC5U, diagnostic),
                Is.EqualTo(
                    "lease=60/hash=0xF9838FC5/mismatches=7/first=(12,24)/expected=FFFFFFFF/actual=FF0000FF/candidate=none"));
        }
    }
}

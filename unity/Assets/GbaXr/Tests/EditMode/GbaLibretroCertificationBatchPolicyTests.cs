using NUnit.Framework;

namespace GbaXr.Tests
{
    public sealed class GbaLibretroCertificationBatchPolicyTests
    {
        [Test]
        public void ResolvedFixture_UsesThePinnedDirectBaselineHash()
        {
            Assert.That(GbaLibretroCertificationBatchPolicy.RequiredSettledLeaseCount, Is.EqualTo(30));
            Assert.That(
                GbaLibretroCertificationBatchPolicy.HasExpectedResolvedFrame(0xF9838FC5U),
                Is.True);
            Assert.That(
                GbaLibretroCertificationBatchPolicy.HasExpectedResolvedFrame(0x9B341219U),
                Is.False);
        }

        [Test]
        public void SourceCheckerboardDiagnostic_ReportsCanonicalFormulaAndFirstMismatch()
        {
            var frame = CreateSourceCheckerboard();

            var exact = GbaLibretroCertificationBatchPolicy.DiagnoseSourceCheckerboard(frame);

            Assert.That(exact.MismatchCount, Is.EqualTo(0));
            Assert.That(exact.MatchingCandidate, Is.EqualTo("source-rgba"));
            Assert.That(exact.SourceHash, Is.EqualTo(0xF9838FC5U));

            // x=16 is a red checker cell. Altering only its blue channel
            // verifies that the diagnostic reports source coordinates/colors.
            var offset = 16 * 4;
            frame[offset + 2] = 1;
            var mismatch = GbaLibretroCertificationBatchPolicy.DiagnoseSourceCheckerboard(frame);

            Assert.That(mismatch.MismatchCount, Is.EqualTo(1));
            Assert.That(mismatch.FirstX, Is.EqualTo(16));
            Assert.That(mismatch.FirstY, Is.EqualTo(0));
            Assert.That(mismatch.FirstExpectedRgba, Is.EqualTo(0xFF0000FFU));
            Assert.That(mismatch.FirstActualRgba, Is.EqualTo(0xFF0001FFU));
            Assert.That(mismatch.MatchingCandidate, Is.EqualTo("none"));
        }

        [Test]
        public void SourceCheckerboardDiagnostic_DetectsAnInteriorGeometryMismatchWhenCornersStillMatch()
        {
            var frame = CreateSourceCheckerboard();

            // Preserve all four legacy corner probes while changing an
            // interior red cell. This is the evidence required to distinguish
            // a spatial/pitch discrepancy from a simple corner-visible color
            // conversion issue on either native backend.
            var x = 32;
            var y = 16;
            var offset = (y * GbaTestFrameSurface.Width + x) * 4;
            frame[offset] = 0;
            frame[offset + 1] = 0;
            frame[offset + 2] = 0;
            frame[offset + 3] = 255;

            var mismatch = GbaLibretroCertificationBatchPolicy.DiagnoseSourceCheckerboard(frame);

            Assert.That(mismatch.MismatchCount, Is.EqualTo(1));
            Assert.That(mismatch.FirstX, Is.EqualTo(x));
            Assert.That(mismatch.FirstY, Is.EqualTo(y));
            Assert.That(mismatch.FirstExpectedRgba, Is.EqualTo(0xFF0000FFU));
            Assert.That(mismatch.FirstActualRgba, Is.EqualTo(0x000000FFU));
            Assert.That(mismatch.MatchingCandidate, Is.EqualTo("none"));
        }

        [Test]
        public void TemporalSweep_UsesDiagnosticOnlyLaterLeaseCheckpointsAndCompactFormatting()
        {
            CollectionAssert.AreEqual(
                new[] { 30, 60, 120 },
                GbaLibretroCertificationBatchPolicy.TemporalSweepLeaseCheckpoints);
            Assert.That(GbaLibretroCertificationBatchPolicy.IsTemporalSweepCheckpoint(5), Is.False);
            Assert.That(GbaLibretroCertificationBatchPolicy.IsTemporalSweepCheckpoint(30), Is.True);
            Assert.That(GbaLibretroCertificationBatchPolicy.IsTemporalSweepCheckpoint(60), Is.True);
            Assert.That(GbaLibretroCertificationBatchPolicy.IsTemporalSweepCheckpoint(120), Is.True);

            var diagnostic = new GbaLibretroFrameSequenceDiagnostic(
                12, 4, 8, 0xFFFFFFFFU, 0xFF0000FFU, "none", 0U, 0U, 0U);
            var summary = GbaLibretroCertificationBatchPolicy.FormatTemporalSweepCheckpoint(
                30, 0x62974F11U, diagnostic);

            Assert.That(summary, Is.EqualTo(
                "lease=30/hash=0x62974F11/mismatches=12/first=(4,8)/expected=FFFFFFFF/actual=FF0000FF/candidate=none"));
            Assert.That(GbaLibretroCertificationBatchPolicy.RequiredSettledLeaseCount, Is.EqualTo(30));
        }

        [Test]
        public void AudioRequires48KhzAndNonEmptyAdapterRing()
        {
            Assert.That(GbaLibretroCertificationBatchPolicy.HasExpectedAudio(48000U, 1U), Is.True);
            Assert.That(GbaLibretroCertificationBatchPolicy.HasExpectedAudio(48000U, 0U), Is.False);
            Assert.That(GbaLibretroCertificationBatchPolicy.HasExpectedAudio(24000U, 32U), Is.False);
        }

        [Test]
        public void InputRequiresStablePressedVisualChange()
        {
            Assert.That(GbaLibretroCertificationBatchPolicy.HasObservableInputChange(1U, 2U, 2U), Is.True);
            Assert.That(GbaLibretroCertificationBatchPolicy.HasObservableInputChange(1U, 1U, 1U), Is.False);
            Assert.That(GbaLibretroCertificationBatchPolicy.HasObservableInputChange(1U, 2U, 3U), Is.False);
        }

        [Test]
        public void EvidenceMapsOnlyVerifiedStagesAndNeverFinalDeviceApproval()
        {
            var evidence = GbaLibretroCertificationBatchPolicy.EvidenceFor(
                GbaLibretroCertificationBatchStage.ResolvedFrame |
                GbaLibretroCertificationBatchStage.Audio);

            Assert.That(evidence.HasFlag(GbaLibretroCertification.FixtureFrameProbe), Is.True);
            Assert.That(evidence.HasFlag(GbaLibretroCertification.ResolvedFrameParity), Is.True);
            Assert.That(evidence.HasFlag(GbaLibretroCertification.AudioParity), Is.True);
            Assert.That(evidence.HasFlag(GbaLibretroCertification.InputParity), Is.False);
            Assert.That(evidence.HasFlag(GbaLibretroCertification.GalaxyXrDevice), Is.False);
        }

        [Test]
        public void StateReplayRequiresEqualPostScriptFrames()
        {
            Assert.That(GbaLibretroCertificationBatchPolicy.HasStateReplay(0xAAU, 0xAAU), Is.True);
            Assert.That(GbaLibretroCertificationBatchPolicy.HasStateReplay(0xAAU, 0xBBU), Is.False);
        }

        private static byte[] CreateSourceCheckerboard()
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
            return frame;
        }
    }
}

using NUnit.Framework;

namespace GbaXr.Tests
{
    public sealed class GbaRollingFrameRateMeterTests
    {
        [Test]
        public void CompletedLeasesProduceAOneSecondRollingMeasurement()
        {
            var meter = new GbaRollingFrameRateMeter();
            meter.Reset(0f);

            for (var frame = 1; frame <= 60; frame++)
            {
                meter.RecordCompletedLease(frame / 60f);
            }

            Assert.That(meter.FramesPerSecond, Is.EqualTo(60f).Within(0.01f));
        }

        [Test]
        public void MeasurementRemainsZeroUntilTheFirstRollingIntervalCompletes()
        {
            var meter = new GbaRollingFrameRateMeter();
            meter.Reset(10f);
            meter.RecordCompletedLease(10.5f);

            Assert.That(meter.FramesPerSecond, Is.Zero);
        }
    }
}

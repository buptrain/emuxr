using NUnit.Framework;

namespace GbaXr.Tests
{
    public sealed class GbaAudioFaultPolicyTests
    {
        private static GbaAudioOutputTelemetry Telemetry(
            bool enabled = true,
            bool active = true,
            bool playing = true,
            bool clipAttached = true)
        {
            return new GbaAudioOutputTelemetry(
                enabled,
                active,
                playing,
                clipAttached,
                48000,
                1024,
                100U,
                4000U,
                4000U,
                0U,
                3900U);
        }

        [Test]
        public void NoStreamingCallbacks_ClassifiesUnityOutputAsDormant()
        {
            var fault = GbaAudioFaultPolicy.Classify(Telemetry(), 0U, 0U, 0U, 8192U);

            Assert.That(fault, Is.EqualTo(GbaAudioFaultKind.UnityOutputDormant));
        }

        [Test]
        public void InactiveOrStoppedSource_ClassifiesUnityOutputAsDormant()
        {
            var inactive = GbaAudioFaultPolicy.Classify(Telemetry(active: false), 10U, 10U, 10U, 8192U);
            var stopped = GbaAudioFaultPolicy.Classify(Telemetry(playing: false), 10U, 10U, 10U, 8192U);

            Assert.That(inactive, Is.EqualTo(GbaAudioFaultKind.UnityOutputDormant));
            Assert.That(stopped, Is.EqualTo(GbaAudioFaultKind.UnityOutputDormant));
        }

        [Test]
        public void RunningCallbacksWithoutPcmPull_ClassifiesNativeStarvation()
        {
            var fault = GbaAudioFaultPolicy.Classify(Telemetry(), 10U, 512U, 0U, 0U);

            Assert.That(fault, Is.EqualTo(GbaAudioFaultKind.NativePcmStarvation));
        }

        [Test]
        public void CallbacksAndPcmPulls_ClassifyAsHealthy()
        {
            var fault = GbaAudioFaultPolicy.Classify(Telemetry(), 10U, 512U, 512U, 8192U);

            Assert.That(fault, Is.EqualTo(GbaAudioFaultKind.None));
        }

        [Test]
        public void NonEmptyRingReturningZeroFrames_ClassifiesNativeReadAsStalled()
        {
            var fault = GbaAudioFaultPolicy.Classify(Telemetry(), 10U, 512U, 0U, 8192U);

            Assert.That(fault, Is.EqualTo(GbaAudioFaultKind.NativePcmReadStalled));
        }
    }
}

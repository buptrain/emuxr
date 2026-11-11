using NUnit.Framework;

namespace GbaXr.Tests
{
    public sealed class GbaAudioSourceLifecyclePolicyTests
    {
        [TestCase(false, false, false)]
        [TestCase(false, true, false)]
        [TestCase(true, false, false)]
        [TestCase(true, true, true)]
        public void FilterHostRunsOnlyForAnAttachedWorkerOnAnActiveOutput(
            bool audioOutputIsActive,
            bool workerAttached,
            bool expected)
        {
            Assert.That(
                GbaAudioSourceLifecyclePolicy.ShouldKeepFilterHostAlive(audioOutputIsActive, workerAttached),
                Is.EqualTo(expected));
        }
    }
}

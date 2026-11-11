using System;
using NUnit.Framework;

namespace GbaXr.Tests
{
    public sealed class GbaLibretroSmokeProbePolicyTests
    {
        [Test]
        public void DefaultToggle_NeverAttemptsNativeInterop()
        {
            var result = GbaLibretroSmokeProbePolicy.EvaluatePreflight(
                enabled: false,
                isAndroidPlayer: true,
                adapterPackaged: true);

            Assert.That(result, Is.EqualTo(GbaLibretroSmokeProbeResult.Disabled));
            Assert.That(GbaLibretroSmokeProbePolicy.ShouldAttempt(result), Is.False);
        }

        [Test]
        public void EnabledProbe_RequiresAndroidPlayerAndPackagedAdapter()
        {
            Assert.That(
                GbaLibretroSmokeProbePolicy.EvaluatePreflight(true, false, true),
                Is.EqualTo(GbaLibretroSmokeProbeResult.UnsupportedPlatform));
            Assert.That(
                GbaLibretroSmokeProbePolicy.EvaluatePreflight(true, true, false),
                Is.EqualTo(GbaLibretroSmokeProbeResult.AdapterNotPackaged));

            var allowed = GbaLibretroSmokeProbePolicy.EvaluatePreflight(true, true, true);
            Assert.That(allowed, Is.EqualTo(GbaLibretroSmokeProbeResult.Pending));
            Assert.That(GbaLibretroSmokeProbePolicy.ShouldAttempt(allowed), Is.True);
        }

        [Test]
        public void ProbeOutcome_DistinguishesNullCreateAndSuccessfulCreateDestroy()
        {
            Assert.That(
                GbaLibretroSmokeProbePolicy.FromCreatedAdapter(IntPtr.Zero),
                Is.EqualTo(GbaLibretroSmokeProbeResult.CreateReturnedNull));
            Assert.That(
                GbaLibretroSmokeProbePolicy.FromCreatedAdapter(new IntPtr(1)),
                Is.EqualTo(GbaLibretroSmokeProbeResult.CreatedAndDestroyed));
        }

        [Test]
        public void ProbeOutcome_DistinguishesLoadAndEntryPointFailures()
        {
            Assert.That(
                GbaLibretroSmokeProbePolicy.FromException(new DllNotFoundException()),
                Is.EqualTo(GbaLibretroSmokeProbeResult.NativeLibraryMissing));
            Assert.That(
                GbaLibretroSmokeProbePolicy.FromException(new EntryPointNotFoundException()),
                Is.EqualTo(GbaLibretroSmokeProbeResult.AdapterEntryPointMissing));
        }
    }
}

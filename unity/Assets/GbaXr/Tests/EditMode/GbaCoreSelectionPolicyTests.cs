using NUnit.Framework;

namespace GbaXr.Tests
{
    public sealed class GbaCoreSelectionPolicyTests
    {
        [Test]
        public void DefaultReadiness_FailsClosedToDirectMgba()
        {
            var plan = GbaCoreSelectionPolicy.Select(
                GbaCoreKind.LibretroResolvedExperimental,
                new GbaLibretroReadiness(false, false, false, false, false, false));

            Assert.That(plan.Effective, Is.EqualTo(GbaCoreKind.DirectMgba));
            Assert.That(plan.FellBack, Is.True);
            Assert.That(plan.FallbackReason, Does.Contain("native Unity binding"));
        }

        [Test]
        public void MissingAudioParity_FailsClosedEvenWhenTheCoreIsPackaged()
        {
            var plan = GbaCoreSelectionPolicy.Select(
                GbaCoreKind.LibretroResolvedExperimental,
                new GbaLibretroReadiness(true, true, true, false, true, false));

            Assert.That(plan.Effective, Is.EqualTo(GbaCoreKind.DirectMgba));
            Assert.That(plan.FallbackReason, Does.Contain("audio parity"));
        }

        [Test]
        public void MissingDeviceCertification_FailsClosedEvenWhenTechnicalGatesPass()
        {
            var plan = GbaCoreSelectionPolicy.Select(
                GbaCoreKind.LibretroResolvedExperimental,
                new GbaLibretroReadiness(true, true, true, true, true, false));

            Assert.That(plan.Effective, Is.EqualTo(GbaCoreKind.DirectMgba));
            Assert.That(plan.FallbackReason, Does.Contain("Galaxy XR device certification"));
        }

        [Test]
        public void FullyCertifiedLibretroRoute_CanBeSelected()
        {
            var plan = GbaCoreSelectionPolicy.Select(
                GbaCoreKind.LibretroResolvedExperimental,
                new GbaLibretroReadiness(true, true, true, true, true, true));

            Assert.That(plan.Effective, Is.EqualTo(GbaCoreKind.LibretroResolvedExperimental));
            Assert.That(plan.FellBack, Is.False);
        }

        [Test]
        public void DirectMgba_RemainsTheExplicitDefault()
        {
            var plan = GbaCoreSelectionPolicy.Select(
                GbaCoreKind.DirectMgba,
                new GbaLibretroReadiness(true, true, true, true, true, true));

            Assert.That(plan.Effective, Is.EqualTo(GbaCoreKind.DirectMgba));
            Assert.That(plan.FellBack, Is.False);
        }

        [Test]
        public void ExplicitDiagnosticPreview_AllowsOnlyPackagedExperimentalRouteWithoutChangingReadiness()
        {
            var unresolved = new GbaLibretroReadiness(true, false, false, false, false, false);

            var plan = GbaCoreSelectionPolicy.SelectWithDiagnosticPreview(
                GbaCoreKind.LibretroResolvedExperimental,
                unresolved,
                explicitlyEnabled: true);

            Assert.That(plan.Effective, Is.EqualTo(GbaCoreKind.LibretroResolvedExperimental));
            Assert.That(plan.FallbackReason, Does.Contain("UNRESOLVED DIAGNOSTIC"));
            Assert.That(unresolved.IsLaunchEligible, Is.False);
            Assert.That(GbaCoreSelectionPolicy.CanExposeCoreSelection(unresolved, true), Is.True);
        }

        [Test]
        public void DiagnosticPreview_CannotBypassMissingAdapterPackage()
        {
            var missingPackage = new GbaLibretroReadiness(false, false, false, false, false, false);

            var plan = GbaCoreSelectionPolicy.SelectWithDiagnosticPreview(
                GbaCoreKind.LibretroResolvedExperimental,
                missingPackage,
                explicitlyEnabled: true);

            Assert.That(plan.Effective, Is.EqualTo(GbaCoreKind.DirectMgba));
            Assert.That(GbaCoreSelectionPolicy.CanExposeCoreSelection(missingPackage, true), Is.False);
        }
    }
}

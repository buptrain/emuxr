using NUnit.Framework;

namespace GbaXr.Tests
{
    public sealed class GbaLibretroReadinessStoreTests
    {
        [Test]
        public void PolicyRevision_UsesANewPersistenceKeyToInvalidateFiveLeaseEvidence()
        {
            Assert.That(GbaLibretroReadinessStore.Key, Is.EqualTo("GbaXr.LibretroCertification.v2"));
            var storage = new FakeStorage
            {
                LegacyKey = "GbaXr.LibretroCertification.v1",
                LegacyValue = (int)GbaLibretroCertification.FullyCertified
            };

            Assert.That(GbaLibretroReadinessStore.LoadFlags(storage), Is.EqualTo(GbaLibretroCertification.None));
        }

        [Test]
        public void FixtureProbeAlone_IsPersistableButNotLaunchEligible()
        {
            var readiness = GbaLibretroReadinessStore.ToReadiness(
                GbaLibretroCertification.FixtureFrameProbe | GbaLibretroCertification.GalaxyXrDevice);

            Assert.That(readiness.IsLaunchEligible, Is.False);
            Assert.That(readiness.ResolvedFrameParityVerified, Is.False);
            Assert.That(readiness.DeviceCertificationVerified, Is.True);
        }

        [Test]
        public void FullPersistedEvidence_EnablesSelectionWhenPackageExists()
        {
            var readiness = GbaLibretroReadinessStore.ToReadiness(GbaLibretroCertification.FullyCertified);

            Assert.That(readiness.NativeBindingPackaged, Is.True);
            Assert.That(readiness.IsLaunchEligible, Is.True);
        }

        [Test]
        public void GalaxyXrApproval_RequiresEveryNonDeviceBatchRequirement()
        {
            var completeBatch = GbaLibretroReadinessStore.ToReadiness(
                GbaLibretroCertification.FixtureFrameProbe |
                GbaLibretroCertification.ResolvedFrameParity |
                GbaLibretroCertification.InputParity |
                GbaLibretroCertification.AudioParity |
                GbaLibretroCertification.StateParity);
            var missingState = GbaLibretroReadinessStore.ToReadiness(
                GbaLibretroCertification.FixtureFrameProbe |
                GbaLibretroCertification.ResolvedFrameParity |
                GbaLibretroCertification.InputParity |
                GbaLibretroCertification.AudioParity);

            Assert.That(GbaLibretroGalaxyXrDeviceApprovalPolicy.CanApprove(completeBatch), Is.True);
            Assert.That(GbaLibretroGalaxyXrDeviceApprovalPolicy.CanApprove(missingState), Is.False);
        }

        [Test]
        public void StoredFlags_DefaultToNone()
        {
            var storage = new FakeStorage();

            Assert.That(GbaLibretroReadinessStore.LoadFlags(storage), Is.EqualTo(GbaLibretroCertification.None));
        }

        private sealed class FakeStorage : IGbaLibretroCertificationStorage
        {
            public string LegacyKey;
            public int LegacyValue;
            public int GetInt(string key, int fallback) => key == LegacyKey ? LegacyValue : fallback;
            public void SetInt(string key, int value) { }
            public void Save() { }
        }
    }
}

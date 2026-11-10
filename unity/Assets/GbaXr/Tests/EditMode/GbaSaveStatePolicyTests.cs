using NUnit.Framework;

namespace GbaXr.Tests
{
    public sealed class GbaSaveStatePolicyTests
    {
        [Test]
        public void ImportedRom_UsesContentScopedSlotPath()
        {
            var paths = GbaAppPrivateStoragePaths.Create("C:/gba-xr-test");
            var identity = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

            var accepted = GbaSaveStatePolicy.TryCreateSlotPath(
                paths, identity, GbaSaveStatePolicy.QuickSlot, out var path, out var error);

            Assert.That(accepted, Is.True, error);
            Assert.That(path.Replace('\\', '/'), Does.Contain("/states/sha256-0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef/slot-0.ss0"));
        }

        [Test]
        public void StagedRomIdentity_IsStableAndDoesNotUsePathSeparators()
        {
            var first = GbaSaveStatePolicy.CreateStateKey("gba-fixtures/bg_tie.gba");
            var second = GbaSaveStatePolicy.CreateStateKey("gba-fixtures/bg_tie.gba");

            Assert.That(first, Is.EqualTo(second));
            Assert.That(first, Does.StartWith("staged-"));
            Assert.That(first, Does.Not.Contain("/"));
        }

        [Test]
        public void PathTraversalLikeStagedIdentity_CannotEscapeStatesDirectory()
        {
            var paths = GbaAppPrivateStoragePaths.Create("C:/gba-xr-test");

            var accepted = GbaSaveStatePolicy.TryCreateSlotPath(
                paths, "../../untrusted.gba", 0, out var path, out var error);

            Assert.That(accepted, Is.True, error);
            Assert.That(path.Replace('\\', '/'), Does.StartWith("C:/gba-xr-test/states/staged-"));
            Assert.That(path, Does.Not.Contain(".."));
        }

        [Test]
        public void RejectsInvalidIdentityAndSlot()
        {
            var paths = GbaAppPrivateStoragePaths.Create("C:/gba-xr-test");

            Assert.That(GbaSaveStatePolicy.TryCreateSlotPath(paths, "sha256:not-a-hash", 0, out _, out _), Is.False);
            Assert.That(GbaSaveStatePolicy.TryCreateSlotPath(paths, "fixture", 10, out _, out _), Is.False);
        }
    }
}

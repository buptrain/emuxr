using System;
using System.IO;
using NUnit.Framework;

namespace GbaXr.Tests
{
    public sealed class GbaRomLaunchPolicyTests
    {
        private const string ValidHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        private static GbaImportedRomMetadata CreateEntry(string relativePath = "selected.gba")
        {
            GbaRomImportValidation.TryCreateMetadata(
                "Selected", "Selected.gba", relativePath, ValidHash, 1024L, out var entry, out _);
            return entry;
        }

        [Test]
        public void Request_UsesOnlyValidatedAppPrivateCatalogPath()
        {
            var paths = GbaAppPrivateStoragePaths.Create(Path.Combine(Path.GetTempPath(), "GbaXrLaunchPolicy"));
            var expectedPath = Path.Combine(paths.RomsDirectory, "selected.gba");

            var accepted = GbaRomLaunchPolicy.TryCreateRequest(
                paths,
                CreateEntry(),
                path => string.Equals(path, expectedPath, StringComparison.Ordinal),
                out var request,
                out var error);

            Assert.That(accepted, Is.True);
            Assert.That(error, Is.Null);
            Assert.That(request.AbsolutePath, Is.EqualTo(expectedPath));
            Assert.That(request.Metadata.DisplayName, Is.EqualTo("Selected"));
        }

        [Test]
        public void Request_RejectsMissingSelectedFileWithoutSwitching()
        {
            var paths = GbaAppPrivateStoragePaths.Create(Path.Combine(Path.GetTempPath(), "GbaXrLaunchPolicy"));

            var accepted = GbaRomLaunchPolicy.TryCreateRequest(
                paths,
                CreateEntry(),
                _ => false,
                out _,
                out var error);

            Assert.That(accepted, Is.False);
            Assert.That(error, Does.Contain("missing"));
        }

        [Test]
        public void StateTransitions_AllowOnlyStopThenStartOrRecoverableFailure()
        {
            Assert.That(
                GbaRomLaunchPolicy.IsAllowedTransition(
                    GbaRomLaunchState.Running,
                    GbaRomLaunchState.ValidatingSelection),
                Is.True);
            Assert.That(
                GbaRomLaunchPolicy.IsAllowedTransition(
                    GbaRomLaunchState.ValidatingSelection,
                    GbaRomLaunchState.StoppingCurrentWorker),
                Is.True);
            Assert.That(
                GbaRomLaunchPolicy.IsAllowedTransition(
                    GbaRomLaunchState.StoppingCurrentWorker,
                    GbaRomLaunchState.StartingSelectedWorker),
                Is.True);
            Assert.That(
                GbaRomLaunchPolicy.IsAllowedTransition(
                    GbaRomLaunchState.StartingSelectedWorker,
                    GbaRomLaunchState.Running),
                Is.True);
            Assert.That(
                GbaRomLaunchPolicy.IsAllowedTransition(
                    GbaRomLaunchState.FailedStopped,
                    GbaRomLaunchState.ValidatingSelection),
                Is.True);
            Assert.That(
                GbaRomLaunchPolicy.IsAllowedTransition(
                    GbaRomLaunchState.Running,
                    GbaRomLaunchState.StartingSelectedWorker),
                Is.False);
        }
    }
}

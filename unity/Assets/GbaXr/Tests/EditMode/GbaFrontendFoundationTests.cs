using System;
using System.IO;
using NUnit.Framework;

namespace GbaXr.Tests
{
    public sealed class GbaFrontendFoundationTests
    {
        [Test]
        public void DirectMgbaManifest_DeclaresOnlyImplementedCapabilities()
        {
            var runtime = GbaFrontendRuntimeModel.CreateDirectMgba();
            var manifest = runtime.Capabilities;

            Assert.That(manifest.CoreId, Is.EqualTo("mgba-direct"));
            Assert.That(manifest.CoreDisplayName, Is.EqualTo("mGBA (Direct)"));
            Assert.That(manifest.AbiVersion, Is.EqualTo(3U));
            Assert.That(manifest.UsesDirectNativeMgbaWorker, Is.True);
            Assert.That(manifest.SupportsResolvedFrameRenderer, Is.True);
            Assert.That(manifest.SupportsFinalPixelPpuProvenance, Is.True);
            Assert.That(manifest.SupportsVisibleSourcePartition, Is.True);
            Assert.That(manifest.SupportsGeneralIndependentPpuCandidatePlanes, Is.False);
            Assert.That(manifest.SupportsWorkerPcmPull, Is.True);
            Assert.That(manifest.SupportsPersistentSaveRam, Is.False);
            Assert.That(manifest.SupportsSaveStates, Is.True);
            Assert.That(runtime.Audio.State, Is.EqualTo(GbaFrontendAudioState.NotStarted));
        }

        [Test]
        public void RuntimeModel_UpdatesCurrentAudioStatusWithoutChangingCapabilities()
        {
            var runtime = GbaFrontendRuntimeModel.CreateDirectMgba();
            var originalCapabilities = runtime.Capabilities;

            runtime.UpdateAudioStatus(new GbaFrontendAudioStatus(
                GbaFrontendAudioState.Active,
                4096U,
                7U));

            Assert.That(runtime.Capabilities, Is.SameAs(originalCapabilities));
            Assert.That(runtime.Audio.State, Is.EqualTo(GbaFrontendAudioState.Active));
            Assert.That(runtime.Audio.AvailableFrames, Is.EqualTo(4096U));
            Assert.That(runtime.Audio.NativeDroppedFrames, Is.EqualTo(7U));
        }

        [Test]
        public void StoragePaths_DefinesTheAppPrivateServiceRootsWithoutCreatingThem()
        {
            var root = Path.Combine(Path.GetTempPath(), "GbaXrFrontendFoundationTests", Guid.NewGuid().ToString("N"));
            var paths = GbaAppPrivateStoragePaths.Create(root);

            Assert.That(paths.RootDirectory, Is.EqualTo(Path.GetFullPath(root)));
            Assert.That(paths.RomsDirectory, Is.EqualTo(Path.Combine(paths.RootDirectory, "roms")));
            Assert.That(paths.SavesDirectory, Is.EqualTo(Path.Combine(paths.RootDirectory, "saves")));
            Assert.That(paths.StatesDirectory, Is.EqualTo(Path.Combine(paths.RootDirectory, "states")));
            Assert.That(paths.SystemDirectory, Is.EqualTo(Path.Combine(paths.RootDirectory, "system")));
            Assert.That(paths.TempDirectory, Is.EqualTo(Path.Combine(paths.RootDirectory, "temp")));
            Assert.That(Directory.Exists(paths.RootDirectory), Is.False);
        }

        [Test]
        public void UserRomPath_NormalizesSafeNestedPathsUnderRomsDirectory()
        {
            var paths = GbaAppPrivateStoragePaths.Create(Path.Combine(Path.GetTempPath(), "GbaXrFrontendFoundationTests"));

            var result = paths.TryResolveUserRomPath("library/./sample.gba", out var actual);

            Assert.That(result, Is.True);
            Assert.That(actual, Is.EqualTo(Path.Combine(paths.RomsDirectory, "library", "sample.gba")));
        }

        [TestCase("../escape.gba")]
        [TestCase("library/../escape.gba")]
        [TestCase("/outside.gba")]
        [TestCase("C:\\outside.gba")]
        [TestCase("library/")]
        [TestCase("")]
        [TestCase(".")]
        public void UserRomPath_RejectsTraversalAndNonRelativeInputs(string input)
        {
            var paths = GbaAppPrivateStoragePaths.Create(Path.Combine(Path.GetTempPath(), "GbaXrFrontendFoundationTests"));

            var result = paths.TryResolveUserRomPath(input, out var actual);

            Assert.That(result, Is.False);
            Assert.That(actual, Is.Null);
        }
    }
}

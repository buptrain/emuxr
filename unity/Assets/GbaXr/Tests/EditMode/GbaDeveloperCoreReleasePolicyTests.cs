using System.Collections.Generic;
using NUnit.Framework;

namespace GbaXr.Tests
{
    public sealed class GbaDeveloperCoreReleasePolicyTests
    {
        private const string Hash = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

        [Test]
        public void Delivery_IsFailClosedOutsideExplicitDeveloperSideloadBuild()
        {
            Assert.That(GbaDeveloperCoreReleasePolicy.IsAllowed(false, true), Is.False);
            Assert.That(GbaDeveloperCoreReleasePolicy.IsAllowed(true, false), Is.False);
            Assert.That(GbaDeveloperCoreReleasePolicy.IsAllowed(true, true), Is.True);
        }

        [Test]
        public void Manifest_RequiresSafeIdsHttpsAndSha256()
        {
            var valid = new GbaDeveloperCoreReleaseManifest { coreId = "mgba-libretro", version = "0.10.5", assetUrl = "https://github.com/example/repo/releases/download/v1/core.zip", sha256 = Hash };
            Assert.That(GbaDeveloperCoreReleasePolicy.TryValidate(valid, out _), Is.True);
            valid.assetUrl = "http://example.invalid/core.zip";
            Assert.That(GbaDeveloperCoreReleasePolicy.TryValidate(valid, out _), Is.False);
        }

        [Test]
        public void Staging_VerifiesHashAndAtomicallyRetainsPreviousDescriptor()
        {
            var fs = new FakeFileSystem();
            var old = "{\"version\":\"old\"}";
            fs.Files["root/developer-cores/mgba-libretro/active.json"] = old;
            var manifest = new GbaDeveloperCoreReleaseManifest { coreId = "mgba-libretro", version = "1.0.0", assetUrl = "https://github.com/example/core.zip", sha256 = Hash };
            var bytes = new byte[] { (byte)'t', (byte)'e', (byte)'s', (byte)'t' };

            Assert.That(GbaDeveloperCoreReleaseStore.TryStageAndSelect(fs, "root", manifest, bytes, out var descriptor, out _), Is.True);
            Assert.That(descriptor.downloadedNotLoadable, Is.True);
            Assert.That(fs.Files["root/developer-cores/mgba-libretro/previous.json"], Is.EqualTo(old));
            StringAssert.Contains("\"version\":\"1.0.0\"", fs.Files["root/developer-cores/mgba-libretro/active.json"]);
        }

        [Test]
        public void Rollback_RestoresPreviousDescriptorThroughTheAtomicActivePath()
        {
            var fs = new FakeFileSystem();
            fs.Files["root/developer-cores/mgba-libretro/active.json"] = "new";
            fs.Files["root/developer-cores/mgba-libretro/previous.json"] = "old";

            Assert.That(GbaDeveloperCoreReleaseStore.TryRollback(fs, "root", "mgba-libretro", out _), Is.True);
            Assert.That(fs.Files["root/developer-cores/mgba-libretro/active.json"], Is.EqualTo("old"));
            Assert.That(fs.Files["root/developer-cores/mgba-libretro/rollback.json"], Is.EqualTo("new"));
        }

        private sealed class FakeFileSystem : IGbaDeveloperCoreFileSystem
        {
            internal readonly Dictionary<string, string> Files = new();
            public bool Exists(string path) => Files.ContainsKey(path);
            public void CreateDirectory(string path) { }
            public void WriteBytes(string path, byte[] bytes) => Files[path] = System.Text.Encoding.UTF8.GetString(bytes);
            public void WriteText(string path, string text) => Files[path] = text;
            public string ReadText(string path) => Files[path];
            public void Replace(string source, string destination) { Files[destination] = Files[source]; Files.Remove(source); }
        }
    }
}

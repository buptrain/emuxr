using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

namespace GbaXr
{
    [Serializable] internal sealed class GbaDeveloperCoreReleaseManifest { public string coreId; public string version; public string assetUrl; public string sha256; }
    [Serializable] internal sealed class GbaDeveloperCoreDescriptor { public string coreId; public string version; public string archiveRelativePath; public string sha256; public bool downloadedNotLoadable = true; }

    internal static class GbaDeveloperCoreReleasePolicy
    {
        internal const long MaximumArchiveBytes = 64L * 1024L * 1024L;
        private static readonly Regex SafeSegment = new("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant);
        internal static bool IsAllowed(bool enabled, bool developmentOrSideloadBuild) => enabled && developmentOrSideloadBuild;
        internal static bool TryValidate(GbaDeveloperCoreReleaseManifest manifest, out string error)
        {
            error = null;
            if (manifest == null || !SafeSegment.IsMatch(manifest.coreId ?? string.Empty) || !SafeSegment.IsMatch(manifest.version ?? string.Empty)) { error = "core id or version is unsafe"; return false; }
            if (!Uri.TryCreate(manifest.assetUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) { error = "artifact URL must use HTTPS"; return false; }
            if (!Regex.IsMatch(manifest.sha256 ?? string.Empty, "^[a-fA-F0-9]{64}$")) { error = "artifact SHA-256 is invalid"; return false; }
            return true;
        }
        internal static bool MatchesSha256(byte[] bytes, string expected)
        {
            if (bytes == null || !Regex.IsMatch(expected ?? string.Empty, "^[a-fA-F0-9]{64}$")) return false;
            using var sha = SHA256.Create();
            return string.Equals(BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty), expected, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal interface IGbaDeveloperCoreFileSystem
    {
        bool Exists(string path); void CreateDirectory(string path); void WriteBytes(string path, byte[] bytes); void WriteText(string path, string text); string ReadText(string path); void Replace(string source, string destination);
    }
    internal sealed class GbaDeveloperCorePhysicalFileSystem : IGbaDeveloperCoreFileSystem
    {
        public bool Exists(string path) => File.Exists(path); public void CreateDirectory(string path) => Directory.CreateDirectory(path); public void WriteBytes(string path, byte[] bytes) => File.WriteAllBytes(path, bytes); public void WriteText(string path, string text) => File.WriteAllText(path, text); public string ReadText(string path) => File.ReadAllText(path);
        public void Replace(string source, string destination)
        {
            // Replace is atomic when a prior file exists; first install is a
            // rename from an already-complete temporary file.
            if (File.Exists(destination)) File.Replace(source, destination, null);
            else File.Move(source, destination);
        }
    }

    internal static class GbaDeveloperCoreReleaseStore
    {
        internal static bool TryStageAndSelect(IGbaDeveloperCoreFileSystem fs, string persistentRoot, GbaDeveloperCoreReleaseManifest manifest, byte[] archive, out GbaDeveloperCoreDescriptor descriptor, out string error)
        {
            descriptor = null;
            if (!GbaDeveloperCoreReleasePolicy.TryValidate(manifest, out error) || archive == null || archive.LongLength > GbaDeveloperCoreReleasePolicy.MaximumArchiveBytes) { error ??= "artifact is missing or exceeds 64 MiB"; return false; }
            if (!GbaDeveloperCoreReleasePolicy.MatchesSha256(archive, manifest.sha256)) { error = "artifact SHA-256 mismatch"; return false; }
            try
            {
                var root = Path.Combine(persistentRoot, "developer-cores", manifest.coreId);
                var version = Path.Combine(root, manifest.version);
                var archivePath = Path.Combine(version, "artifact.zip");
                fs.CreateDirectory(version);
                fs.WriteBytes(archivePath + ".tmp", archive); fs.Replace(archivePath + ".tmp", archivePath);
                var active = Path.Combine(root, "active.json");
                if (fs.Exists(active)) fs.WriteText(Path.Combine(root, "previous.json"), fs.ReadText(active));
                descriptor = new GbaDeveloperCoreDescriptor { coreId = manifest.coreId, version = manifest.version, archiveRelativePath = Path.Combine(manifest.coreId, manifest.version, "artifact.zip").Replace('\\', '/'), sha256 = manifest.sha256.ToLowerInvariant() };
                fs.WriteText(active + ".tmp", JsonUtility.ToJson(descriptor)); fs.Replace(active + ".tmp", active);
                return true;
            }
            catch (Exception exception) { descriptor = null; error = "developer core staging failed: " + exception.Message; return false; }
        }

        internal static bool TryRollback(IGbaDeveloperCoreFileSystem fs, string persistentRoot, string coreId, out string error)
        {
            error = null;
            if (!GbaDeveloperCoreReleasePolicy.TryValidate(new GbaDeveloperCoreReleaseManifest
                { coreId = coreId, version = "rollback", assetUrl = "https://invalid.example/rollback.zip", sha256 = new string('0', 64) }, out error)) return false;
            try
            {
                var root = Path.Combine(persistentRoot, "developer-cores", coreId);
                var active = Path.Combine(root, "active.json");
                var previous = Path.Combine(root, "previous.json");
                if (!fs.Exists(previous)) { error = "no previous developer core descriptor is available"; return false; }
                if (fs.Exists(active)) fs.WriteText(Path.Combine(root, "rollback.json"), fs.ReadText(active));
                fs.WriteText(active + ".tmp", fs.ReadText(previous));
                fs.Replace(active + ".tmp", active);
                return true;
            }
            catch (Exception exception) { error = "developer core rollback failed: " + exception.Message; return false; }
        }
    }

    /// <summary>Developer/sideload archive registry only. Downloaded archives are never loaded by Unity.</summary>
    [DisallowMultipleComponent]
    public sealed class GbaDeveloperCoreReleaseProvider : MonoBehaviour
    {
        [SerializeField] private bool enableDeveloperSideload;
        [SerializeField] private string manifestUrl;
        public string Status { get; private set; } = "Developer core download disabled.";
        public bool IsAvailable => GbaDeveloperCoreReleasePolicy.IsAllowed(enableDeveloperSideload, IsDevelopmentOrSideloadBuild());

        [ContextMenu("Download Configured Developer Core")]
        public bool RequestConfiguredDownload()
        {
            if (!IsAvailable) { Status = "Developer core delivery is disabled outside explicit development/sideload builds."; Debug.LogWarning("GBA XR developer core: " + Status); return false; }
            if (!Uri.TryCreate(manifestUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) { Status = "Configure an HTTPS release manifest URL."; Debug.LogWarning("GBA XR developer core: " + Status); return false; }
            StartCoroutine(DownloadAndStage(uri.AbsoluteUri));
            return true;
        }

        public bool RequestRollback(string coreId)
        {
            if (!IsAvailable) { Status = "Developer core rollback is disabled outside explicit development/sideload builds."; return false; }
            if (!GbaDeveloperCoreReleaseStore.TryRollback(new GbaDeveloperCorePhysicalFileSystem(), Application.persistentDataPath, coreId, out var error)) { Status = error; Debug.LogError("GBA XR developer core: " + Status); return false; }
            Status = $"Rolled back {coreId} active descriptor. Downloaded artifacts remain not runtime-loadable.";
            Debug.Log("GBA XR developer core: " + Status);
            return true;
        }

        private IEnumerator DownloadAndStage(string url)
        {
            Status = "Downloading developer core manifest…";
            using (var manifestRequest = UnityWebRequest.Get(url))
            {
                yield return manifestRequest.SendWebRequest();
                if (manifestRequest.result != UnityWebRequest.Result.Success) { Status = "Manifest download failed: " + manifestRequest.error; Debug.LogError("GBA XR developer core: " + Status); yield break; }
                if (manifestRequest.downloadHandler == null) { Status = "Manifest response had no download handler."; Debug.LogError("GBA XR developer core: " + Status); yield break; }
                var manifest = JsonUtility.FromJson<GbaDeveloperCoreReleaseManifest>(manifestRequest.downloadHandler.text);
                if (!GbaDeveloperCoreReleasePolicy.TryValidate(manifest, out var validationError)) { Status = "Manifest rejected: " + validationError; Debug.LogError("GBA XR developer core: " + Status); yield break; }
                using (var artifactRequest = UnityWebRequest.Get(manifest.assetUrl))
                {
                    yield return artifactRequest.SendWebRequest();
                    var archive = artifactRequest.downloadHandler == null ? null : artifactRequest.downloadHandler.data;
                    if (artifactRequest.result != UnityWebRequest.Result.Success)
                    {
                        Status = "Artifact download failed: " + artifactRequest.error;
                        Debug.LogError("GBA XR developer core: " + Status);
                        yield break;
                    }

                    if (!GbaDeveloperCoreReleaseStore.TryStageAndSelect(
                            new GbaDeveloperCorePhysicalFileSystem(),
                            Application.persistentDataPath,
                            manifest,
                            archive,
                            out var descriptor,
                            out var stagingError))
                    {
                        Status = stagingError;
                        Debug.LogError("GBA XR developer core: " + Status);
                        yield break;
                    }

                    Status = $"DownloadedNotLoadable: staged {descriptor.coreId} {descriptor.version}; active descriptor selected with previous rollback retained. Unity will not load it.";
                    Debug.Log("GBA XR developer core: " + Status);
                }
            }
        }

        private static bool IsDevelopmentOrSideloadBuild()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || GBA_XR_DEVELOPER_SIDELOAD
            return true;
#else
            return false;
#endif
        }
    }
}

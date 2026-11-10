using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace GbaXr
{
    /// <summary>
    /// Receives an Android document-picker selection and validates its private
    /// copy. The caller decides whether to start or switch the mGBA worker.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GbaRomImportService : MonoBehaviour
    {
        private const string AndroidBridgeClass = "com.tiger.gbaixr.RomImportBridge";
        private const string CatalogFileName = "rom-library.json";
        private const string CatalogTempFileName = "rom-library.json.tmp";
        private const string CatalogBackupFileName = "rom-library.json.bak";

        private GbaAppPrivateStoragePaths _paths;
        private GbaRomLibraryCatalog _catalog;
        private bool _importInFlight;

        public event Action<GbaRomImportOutcome> ImportCompleted;

        public IReadOnlyList<GbaImportedRomMetadata> Entries => _catalog?.Entries;
        public GbaRomImportOutcome LastOutcome { get; private set; }
        public bool ImportInFlight => _importInFlight;

        private void Awake()
        {
            Initialize(Application.persistentDataPath);
        }

        /// <summary>Starts a one-time Android picker import; it retains no URI permission.</summary>
        public bool BeginImport()
        {
            if (_importInFlight)
            {
                return false;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                _importInFlight = true;
                using (var bridge = new AndroidJavaClass(AndroidBridgeClass))
                {
                    bridge.CallStatic("requestImport", gameObject.name, nameof(OnAndroidRomImportResult));
                }
                return true;
            }
            catch (Exception exception)
            {
                _importInFlight = false;
                Complete(new GbaRomImportOutcome(
                    GbaRomImportOutcomeKind.Error,
                    null,
                    GbaRomImportValidationError.None,
                    $"Could not launch the Android ROM picker: {exception.Message}"));
                return false;
            }
#else
            Complete(new GbaRomImportOutcome(
                GbaRomImportOutcomeKind.Error,
                null,
                GbaRomImportValidationError.None,
                "ROM import is available only in an Android player build."));
            return false;
#endif
        }

        /// <summary>
        /// Android's helper activity calls this exact method through
        /// UnitySendMessage after copying the provider stream into app-private
        /// storage. The payload has metadata only, never ROM bytes or a URI.
        /// </summary>
        public void OnAndroidRomImportResult(string payloadJson)
        {
            _importInFlight = false;
            AndroidImportResult payload;
            try
            {
                payload = JsonUtility.FromJson<AndroidImportResult>(payloadJson);
            }
            catch (Exception exception)
            {
                Complete(new GbaRomImportOutcome(
                    GbaRomImportOutcomeKind.Error,
                    null,
                    GbaRomImportValidationError.None,
                    $"Android import returned invalid metadata: {exception.Message}"));
                return;
            }

            if (payload == null || string.IsNullOrEmpty(payload.status))
            {
                Complete(new GbaRomImportOutcome(
                    GbaRomImportOutcomeKind.Error,
                    null,
                    GbaRomImportValidationError.None,
                    "Android import returned an empty result."));
                return;
            }
            if (string.Equals(payload.status, "cancelled", StringComparison.Ordinal))
            {
                Complete(new GbaRomImportOutcome(
                    GbaRomImportOutcomeKind.Cancelled,
                    null,
                    GbaRomImportValidationError.None,
                    "ROM import cancelled."));
                return;
            }
            if (!string.Equals(payload.status, "success", StringComparison.Ordinal))
            {
                Complete(new GbaRomImportOutcome(
                    GbaRomImportOutcomeKind.Error,
                    null,
                    GbaRomImportValidationError.None,
                    string.IsNullOrWhiteSpace(payload.error) ? "Android ROM import failed." : payload.error));
                return;
            }

            if (!GbaRomImportValidation.TryCreateMetadata(
                    payload.displayName,
                    payload.originalFileName,
                    payload.relativePath,
                    payload.contentHashSha256,
                    payload.sizeBytes,
                    out var metadata,
                    out var validationError))
            {
                Complete(new GbaRomImportOutcome(
                    GbaRomImportOutcomeKind.Error,
                    null,
                    validationError,
                    "Imported ROM metadata did not satisfy the local catalog policy."));
                return;
            }
            if (!_paths.TryResolveUserRomPath(metadata.RelativePath, out var absoluteRomPath) ||
                !File.Exists(absoluteRomPath))
            {
                Complete(new GbaRomImportOutcome(
                    GbaRomImportOutcomeKind.Error,
                    null,
                    GbaRomImportValidationError.InvalidRelativePath,
                    "Imported ROM file was not found in app-private storage."));
                return;
            }
            Complete(new GbaRomImportOutcome(
                GbaRomImportOutcomeKind.Success,
                metadata,
                GbaRomImportValidationError.None,
                "ROM selected and ready to load."));
        }

        private void Initialize(string appPrivateRoot)
        {
            _paths = GbaAppPrivateStoragePaths.Create(appPrivateRoot);
            _catalog = new GbaRomLibraryCatalog();
        }

        private void TryLoadCatalog()
        {
            var catalogPath = Path.Combine(_paths.RootDirectory, CatalogFileName);
            if (!File.Exists(catalogPath))
            {
                return;
            }

            try
            {
                var document = JsonUtility.FromJson<CatalogDocument>(File.ReadAllText(catalogPath, Encoding.UTF8));
                if (document?.entries == null)
                {
                    return;
                }
                foreach (var entry in document.entries)
                {
                    if (entry == null || !GbaRomImportValidation.TryCreateMetadata(
                            entry.displayName,
                            entry.originalFileName,
                            entry.relativePath,
                            entry.contentHashSha256,
                            entry.sizeBytes,
                            out var metadata,
                            out _) ||
                        !_paths.TryResolveUserRomPath(metadata.RelativePath, out var absoluteRomPath) ||
                        !File.Exists(absoluteRomPath))
                    {
                        Debug.LogWarning("GBA XR: ignored an invalid or missing app-private ROM library entry.");
                        continue;
                    }
                    _catalog.TryAdd(metadata, out _);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"GBA XR: could not read the local ROM catalog: {exception.Message}");
            }
        }

        private bool TrySaveCatalog(out string error)
        {
            var catalogPath = Path.Combine(_paths.RootDirectory, CatalogFileName);
            var tempPath = Path.Combine(_paths.RootDirectory, CatalogTempFileName);
            var backupPath = Path.Combine(_paths.RootDirectory, CatalogBackupFileName);
            try
            {
                Directory.CreateDirectory(_paths.RootDirectory);
                var document = new CatalogDocument();
                foreach (var entry in _catalog.Entries)
                {
                    document.entries.Add(new CatalogEntry
                    {
                        displayName = entry.DisplayName,
                        originalFileName = entry.OriginalFileName,
                        relativePath = entry.RelativePath,
                        contentHashSha256 = entry.ContentHashSha256,
                        sizeBytes = entry.SizeBytes
                    });
                }
                File.WriteAllText(tempPath, JsonUtility.ToJson(document, true), Encoding.UTF8);
                if (File.Exists(catalogPath))
                {
                    // Same-directory replacement preserves the existing catalog
                    // until the completed temp file can replace it atomically.
                    File.Replace(tempPath, catalogPath, backupPath, true);
                }
                else
                {
                    File.Move(tempPath, catalogPath);
                }
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                TryDeleteTempFile(tempPath);
                error = $"Could not atomically save the local ROM catalog: {exception.Message}";
                return false;
            }
        }

        private void Complete(GbaRomImportOutcome outcome)
        {
            LastOutcome = outcome;
            ImportCompleted?.Invoke(outcome);
            Debug.Log($"GBA XR ROM import: {outcome.Kind}. {outcome.Message}");
        }

        private static void TryDeleteTempFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // The intact catalog is more important than cleanup after a
                // failed write; a later save will replace the stale temp file.
            }
        }

        [Serializable]
        private sealed class AndroidImportResult
        {
            public string status;
            public string displayName;
            public string originalFileName;
            public string relativePath;
            public string contentHashSha256;
            public long sizeBytes;
            public string error;
        }

        [Serializable]
        private sealed class CatalogDocument
        {
            public List<CatalogEntry> entries = new();
        }

        [Serializable]
        private sealed class CatalogEntry
        {
            public string displayName;
            public string originalFileName;
            public string relativePath;
            public string contentHashSha256;
            public long sizeBytes;
        }
    }
}

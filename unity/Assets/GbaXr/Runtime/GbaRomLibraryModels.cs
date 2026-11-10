using System;
using System.Collections.Generic;
using System.IO;

namespace GbaXr
{
    public enum GbaRomImportValidationError
    {
        None = 0,
        MissingFileName = 1,
        InvalidExtension = 2,
        EmptyFile = 3,
        ExceedsMaximumSize = 4,
        InvalidDisplayName = 5,
        InvalidRelativePath = 6,
        InvalidContentHash = 7,
        InvalidStableId = 8,
        DuplicateStableId = 9,
        ConflictingRelativePath = 10
    }

    /// <summary>
    /// Metadata only: imported ROM bytes remain in app-private storage and are
    /// never copied into the Unity project, catalog, or diagnostic logs.
    /// </summary>
    public sealed class GbaImportedRomMetadata
    {
        internal GbaImportedRomMetadata(
            string stableId,
            string displayName,
            string originalFileName,
            string relativePath,
            string contentHashSha256,
            long sizeBytes)
        {
            StableId = stableId;
            DisplayName = displayName;
            OriginalFileName = originalFileName;
            RelativePath = relativePath;
            ContentHashSha256 = contentHashSha256;
            SizeBytes = sizeBytes;
        }

        /// <summary>Stable content identity in the form <c>sha256:&lt;hex&gt;</c>.</summary>
        public string StableId { get; }
        public string DisplayName { get; }
        public string OriginalFileName { get; }

        /// <summary>Path relative to <see cref="GbaAppPrivateStoragePaths.RomsDirectory"/>.</summary>
        public string RelativePath { get; }

        public string ContentHashSha256 { get; }
        public long SizeBytes { get; }
    }

    /// <summary>Pure validation for document-picker results before cataloging.</summary>
    public static class GbaRomImportValidation
    {
        // Retail GBA cartridges are at most 32 MiB. This also bounds accidental
        // provider selections before the Android bridge commits its temp file.
        public const long MaximumRomBytes = 32L * 1024L * 1024L;

        public static bool TryCreateMetadata(
            string displayName,
            string originalFileName,
            string relativePath,
            string contentHashSha256,
            long sizeBytes,
            out GbaImportedRomMetadata metadata,
            out GbaRomImportValidationError error)
        {
            metadata = null;
            error = ValidateFileNameAndSize(originalFileName, sizeBytes);
            if (error != GbaRomImportValidationError.None)
            {
                return false;
            }
            if (string.IsNullOrWhiteSpace(displayName) || ContainsDirectorySeparator(displayName))
            {
                error = GbaRomImportValidationError.InvalidDisplayName;
                return false;
            }
            if (!IsSafeRelativePath(relativePath))
            {
                error = GbaRomImportValidationError.InvalidRelativePath;
                return false;
            }

            var normalizedHash = contentHashSha256?.ToLowerInvariant();
            if (!IsLowercaseSha256(normalizedHash))
            {
                error = GbaRomImportValidationError.InvalidContentHash;
                return false;
            }

            var stableId = "sha256:" + normalizedHash;
            metadata = new GbaImportedRomMetadata(
                stableId,
                displayName.Trim(),
                originalFileName.Trim(),
                relativePath,
                normalizedHash,
                sizeBytes);
            error = GbaRomImportValidationError.None;
            return true;
        }

        public static GbaRomImportValidationError ValidateFileNameAndSize(string originalFileName, long sizeBytes)
        {
            if (string.IsNullOrWhiteSpace(originalFileName) || ContainsDirectorySeparator(originalFileName))
            {
                return GbaRomImportValidationError.MissingFileName;
            }
            if (!string.Equals(Path.GetExtension(originalFileName), ".gba", StringComparison.OrdinalIgnoreCase))
            {
                return GbaRomImportValidationError.InvalidExtension;
            }
            if (sizeBytes <= 0)
            {
                return GbaRomImportValidationError.EmptyFile;
            }
            if (sizeBytes > MaximumRomBytes)
            {
                return GbaRomImportValidationError.ExceedsMaximumSize;
            }
            return GbaRomImportValidationError.None;
        }

        private static bool IsLowercaseSha256(string value)
        {
            if (value == null || value.Length != 64)
            {
                return false;
            }
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f')))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool ContainsDirectorySeparator(string value)
        {
            return value.IndexOf('/') >= 0 || value.IndexOf('\\') >= 0;
        }

        private static bool IsSafeRelativePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value) ||
                value.IndexOf(':') >= 0 || value.EndsWith("/", StringComparison.Ordinal) ||
                value.EndsWith("\\", StringComparison.Ordinal))
            {
                return false;
            }
            foreach (var segment in value.Replace('\\', '/').Split('/'))
            {
                if (string.IsNullOrWhiteSpace(segment) || segment == "." || segment == "..")
                {
                    return false;
                }
            }
            return true;
        }
    }

    /// <summary>Small, deterministic in-memory catalog for direct mGBA imports.</summary>
    public sealed class GbaRomLibraryCatalog
    {
        private readonly List<GbaImportedRomMetadata> _entries = new();

        public IReadOnlyList<GbaImportedRomMetadata> Entries => _entries;

        public bool TryAdd(GbaImportedRomMetadata metadata, out GbaRomImportValidationError error)
        {
            if (metadata == null)
            {
                throw new ArgumentNullException(nameof(metadata));
            }

            for (var index = 0; index < _entries.Count; index++)
            {
                var existing = _entries[index];
                if (string.Equals(existing.StableId, metadata.StableId, StringComparison.Ordinal))
                {
                    error = GbaRomImportValidationError.DuplicateStableId;
                    return false;
                }
                if (string.Equals(existing.RelativePath, metadata.RelativePath, StringComparison.Ordinal))
                {
                    error = GbaRomImportValidationError.ConflictingRelativePath;
                    return false;
                }
            }

            _entries.Add(metadata);
            error = GbaRomImportValidationError.None;
            return true;
        }

        internal void Remove(string stableId)
        {
            for (var index = _entries.Count - 1; index >= 0; index--)
            {
                if (string.Equals(_entries[index].StableId, stableId, StringComparison.Ordinal))
                {
                    _entries.RemoveAt(index);
                    return;
                }
            }
        }
    }

    public enum GbaRomImportOutcomeKind
    {
        Success = 0,
        Cancelled = 1,
        Error = 2,
        Duplicate = 3
    }

    /// <summary>Result delivered by the managed import service; it never starts mGBA.</summary>
    public sealed class GbaRomImportOutcome
    {
        internal GbaRomImportOutcome(
            GbaRomImportOutcomeKind kind,
            GbaImportedRomMetadata metadata,
            GbaRomImportValidationError validationError,
            string message)
        {
            Kind = kind;
            Metadata = metadata;
            ValidationError = validationError;
            Message = message;
        }

        public GbaRomImportOutcomeKind Kind { get; }
        public GbaImportedRomMetadata Metadata { get; }
        public GbaRomImportValidationError ValidationError { get; }
        public string Message { get; }
    }
}

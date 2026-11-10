using NUnit.Framework;

namespace GbaXr.Tests
{
    public sealed class GbaRomLibraryModelsTests
    {
        private const string ValidHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        [Test]
        public void ImportedMetadata_UsesContentDerivedStableId()
        {
            var result = GbaRomImportValidation.TryCreateMetadata(
                "Example",
                "Example.GBA",
                "0123.gba",
                ValidHash,
                1024L,
                out var metadata,
                out var error);

            Assert.That(result, Is.True);
            Assert.That(error, Is.EqualTo(GbaRomImportValidationError.None));
            Assert.That(metadata.StableId, Is.EqualTo("sha256:" + ValidHash));
            Assert.That(metadata.RelativePath, Is.EqualTo("0123.gba"));
            Assert.That(metadata.OriginalFileName, Is.EqualTo("Example.GBA"));
        }

        [TestCase("Example.gbc", 1024L, GbaRomImportValidationError.InvalidExtension)]
        [TestCase("Example.gba", 0L, GbaRomImportValidationError.EmptyFile)]
        [TestCase("Example.gba", 33554433L, GbaRomImportValidationError.ExceedsMaximumSize)]
        [TestCase("folder/Example.gba", 1024L, GbaRomImportValidationError.MissingFileName)]
        public void Validation_RejectsUnsupportedOrUnsafeImports(
            string originalFileName,
            long sizeBytes,
            GbaRomImportValidationError expected)
        {
            var error = GbaRomImportValidation.ValidateFileNameAndSize(originalFileName, sizeBytes);

            Assert.That(error, Is.EqualTo(expected));
        }

        [Test]
        public void Metadata_RejectsInvalidHashAndAbsoluteLibraryPath()
        {
            var invalidHash = GbaRomImportValidation.TryCreateMetadata(
                "Example", "Example.gba", "entry.gba", "not-a-hash", 1L, out _, out var hashError);
            var absolutePath = GbaRomImportValidation.TryCreateMetadata(
                "Example", "Example.gba", "library/../entry.gba", ValidHash, 1L, out _, out var pathError);

            Assert.That(invalidHash, Is.False);
            Assert.That(hashError, Is.EqualTo(GbaRomImportValidationError.InvalidContentHash));
            Assert.That(absolutePath, Is.False);
            Assert.That(pathError, Is.EqualTo(GbaRomImportValidationError.InvalidRelativePath));
        }

        [Test]
        public void Metadata_RejectsPathNormalizationOrTraversalSegments()
        {
            var dotPath = GbaRomImportValidation.TryCreateMetadata(
                "Example", "Example.gba", "./entry.gba", ValidHash, 1L, out _, out var dotError);
            var traversalPath = GbaRomImportValidation.TryCreateMetadata(
                "Example", "Example.gba", "library/../entry.gba", ValidHash, 1L, out _, out var traversalError);

            Assert.That(dotPath, Is.False);
            Assert.That(dotError, Is.EqualTo(GbaRomImportValidationError.InvalidRelativePath));
            Assert.That(traversalPath, Is.False);
            Assert.That(traversalError, Is.EqualTo(GbaRomImportValidationError.InvalidRelativePath));
        }

        [Test]
        public void Catalog_RejectsDuplicateContentIdentity()
        {
            var catalog = new GbaRomLibraryCatalog();
            GbaRomImportValidation.TryCreateMetadata(
                "Example", "Example.gba", "entry.gba", ValidHash, 1L, out var first, out _);
            GbaRomImportValidation.TryCreateMetadata(
                "Copy", "Copy.gba", "copy.gba", ValidHash, 1L, out var duplicate, out _);

            Assert.That(catalog.TryAdd(first, out var firstError), Is.True);
            Assert.That(firstError, Is.EqualTo(GbaRomImportValidationError.None));
            Assert.That(catalog.TryAdd(duplicate, out var duplicateError), Is.False);
            Assert.That(duplicateError, Is.EqualTo(GbaRomImportValidationError.DuplicateStableId));
            Assert.That(catalog.Entries, Has.Count.EqualTo(1));
        }
    }
}

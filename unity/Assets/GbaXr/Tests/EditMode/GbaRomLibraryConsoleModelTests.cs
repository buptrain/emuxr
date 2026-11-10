using System.Collections.Generic;
using NUnit.Framework;

namespace GbaXr.Tests
{
    public sealed class GbaRomLibraryConsoleModelTests
    {
        private const string HashA = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        private const string HashB = "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210";

        private static GbaImportedRomMetadata CreateEntry(string displayName, string hash)
        {
            GbaRomImportValidation.TryCreateMetadata(
                displayName,
                displayName + ".gba",
                hash + ".gba",
                hash,
                1024L,
                out var entry,
                out _);
            return entry;
        }

        [Test]
        public void Open_ExposesImportAndLibraryRowsWithoutASelectedRom()
        {
            var model = new GbaRomLibraryConsoleModel();
            model.Open(new List<GbaImportedRomMetadata> { CreateEntry("First", HashA) });

            Assert.That(model.IsOpen, Is.True);
            Assert.That(model.SelectedRow, Is.EqualTo(model.ImportRow));
            Assert.That(model.RowCount, Is.EqualTo(4));
            Assert.That(model.SelectedEntry, Is.Null);
            Assert.That(model.ActivateSelection(), Is.EqualTo(GbaRomLibraryConsoleAction.Import));
        }

        [Test]
        public void LaunchRow_PreservesSelectedEntryAndRequiresExplicitLaunchAction()
        {
            var model = new GbaRomLibraryConsoleModel();
            model.Open(new List<GbaImportedRomMetadata>
            {
                CreateEntry("First", HashA),
                CreateEntry("Second", HashB)
            });

            model.MoveSelection(2); // second catalog entry
            var selected = model.SelectedEntry;
            model.MoveSelection(1); // launch row

            Assert.That(selected.DisplayName, Is.EqualTo("Second"));
            Assert.That(model.SelectedEntry, Is.SameAs(selected));
            Assert.That(model.SelectedRow, Is.EqualTo(model.LaunchRow));
            Assert.That(model.ActivateSelection(), Is.EqualTo(GbaRomLibraryConsoleAction.LaunchSelected));
        }

        [Test]
        public void BackRow_ClosesOnlyTheLibrarySubview()
        {
            var model = new GbaRomLibraryConsoleModel();
            model.Open(new List<GbaImportedRomMetadata>());
            model.MoveSelection(-1);

            Assert.That(model.SelectedRow, Is.EqualTo(model.BackRow));
            Assert.That(model.ActivateSelection(), Is.EqualTo(GbaRomLibraryConsoleAction.Back));
            model.Close();
            Assert.That(model.IsOpen, Is.False);
        }

        [Test]
        public void LaunchRow_RejectsActivationUntilAnEntryHasBeenSelected()
        {
            var model = new GbaRomLibraryConsoleModel();
            model.Open(new List<GbaImportedRomMetadata>());
            model.MoveSelection(1);

            Assert.That(model.SelectedRow, Is.EqualTo(model.LaunchRow));
            Assert.That(model.ActivateSelection(), Is.EqualTo(GbaRomLibraryConsoleAction.LaunchUnavailable));
        }

        [Test]
        public void ImportPresentation_PrioritizesInFlightThenGivesHonestOutcome()
        {
            Assert.That(
                GbaRomImportPresentationPolicy.Describe(true, GbaRomImportOutcomeKind.Error, "ignored"),
                Does.StartWith("Choose a .gba"));
            Assert.That(
                GbaRomImportPresentationPolicy.Describe(false, GbaRomImportOutcomeKind.Success, "stored locally"),
                Is.EqualTo("Selected: stored locally"));
            Assert.That(
                GbaRomImportPresentationPolicy.Describe(false, null, null),
                Does.Contain("Load ROM"));
        }
    }
}

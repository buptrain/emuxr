using NUnit.Framework;

namespace GbaXr.Tests
{
    public class GbaAdminConsoleModelTests
    {
        [TestCase(null, true)]
        [TestCase("", true)]
        [TestCase("  ", true)]
        [TestCase("gba-fixtures/frame_sequence.gba", false)]
        public void EmptyStartupRomOpensConsole(string path, bool expected)
        {
            Assert.That(GbaNativeFrameDriver.ShouldOpenConsoleAtStartup(path), Is.EqualTo(expected));
        }

        private static GbaAdminConsoleModel CreateConsole()
        {
            return new GbaAdminConsoleModel(new GbaAdminConsoleSettings
            {
                PresentationMode = GbaNativeFrameDriver.PresentationMode.Auto,
                PlacementMode = GbaScreenPlacementMode.VirtualField
            });
        }

        [Test]
        public void Open_CapturesFocusAndCopiesActiveSettingsToDraft()
        {
            var console = CreateConsole();

            console.Open();

            Assert.That(console.IsOpen, Is.True);
            Assert.That(console.ForwardsGameplayInput, Is.False);
            Assert.That(console.Draft.PresentationMode, Is.EqualTo(GbaNativeFrameDriver.PresentationMode.Auto));
        }

        [Test]
        public void DraftEdits_DoNotChangeActiveSettingsUntilApply()
        {
            var console = CreateConsole();
            console.Open();

            console.SetDraftPresentationMode(GbaNativeFrameDriver.PresentationMode.ForcePartition);
            console.SetDraftPlacementMode(GbaScreenPlacementMode.PlaneAnchoring);

            Assert.That(console.Active.PresentationMode, Is.EqualTo(GbaNativeFrameDriver.PresentationMode.Auto));
            Assert.That(console.Active.PlacementMode, Is.EqualTo(GbaScreenPlacementMode.VirtualField));

            var transaction = console.Apply();

            Assert.That(console.Active.PresentationMode, Is.EqualTo(GbaNativeFrameDriver.PresentationMode.ForcePartition));
            Assert.That(console.Active.PlacementMode, Is.EqualTo(GbaScreenPlacementMode.PlaneAnchoring));
        }

        [Test]
        public void Close_DiscardsDraftAndReturnsGameplayFocus()
        {
            var console = CreateConsole();
            console.Open();
            console.Request(GbaAdminOperation.ReloadCurrentRom);

            console.Close();

            Assert.That(console.IsOpen, Is.False);
            Assert.That(console.ForwardsGameplayInput, Is.True);
        }

        [Test]
        public void Apply_EmitsRequestedAdminOperationsAsOneTransaction()
        {
            var console = CreateConsole();
            console.Open();
            console.Request(GbaAdminOperation.ReloadCurrentRom);
            console.Request(GbaAdminOperation.SaveState);
            console.Request(GbaAdminOperation.ResetScreenTransform);

            var transaction = console.Apply();

            Assert.That(transaction.Includes(GbaAdminOperation.ReloadCurrentRom), Is.True);
            Assert.That(transaction.Includes(GbaAdminOperation.SaveState), Is.True);
            Assert.That(transaction.Includes(GbaAdminOperation.ResetScreenTransform), Is.True);
        }

        [Test]
        public void Catalog_ContainsEveryAdminOperationShownByTheConsole()
        {
            var console = CreateConsole();

            Assert.That(console.AvailableOperations, Does.Contain(GbaAdminOperation.ReloadCurrentRom));
            Assert.That(console.AvailableOperations, Does.Contain(GbaAdminOperation.LoadStagedRom));
            Assert.That(console.AvailableOperations, Does.Contain(GbaAdminOperation.ImportRom));
            Assert.That(console.AvailableOperations, Does.Contain(GbaAdminOperation.SaveState));
            Assert.That(console.AvailableOperations, Does.Contain(GbaAdminOperation.LoadState));
            Assert.That(console.AvailableOperations, Does.Contain(GbaAdminOperation.SetPresentationMode));
            Assert.That(console.AvailableOperations, Does.Contain(GbaAdminOperation.SetPlacementMode));
            Assert.That(console.AvailableOperations, Does.Contain(GbaAdminOperation.ResetScreenTransform));
        }

        [Test]
        public void FrameRateIsNotAnAdminOperation()
        {
            var console = CreateConsole();
            Assert.That(console.AvailableOperations, Has.Count.EqualTo(8));
            Assert.That(System.Enum.GetNames(typeof(GbaAdminOperation)), Does.Not.Contain("SetTargetFrameRate"));
        }
    }
}

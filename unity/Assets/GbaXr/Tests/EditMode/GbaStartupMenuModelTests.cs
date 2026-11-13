using NUnit.Framework;

namespace GbaXr.Tests
{
    public sealed class GbaStartupMenuModelTests
    {
        [TestCase(null, true)]
        [TestCase("", true)]
        [TestCase("gba-fixtures/frame_sequence.gba", false)]
        public void BlankRomStartup_UsesTheStartupMenuGate(string path, bool expected)
        {
            // Retains the existing pure startup predicate while the driver now
            // routes a true value to Parallax Emu rather than the admin panel.
            Assert.That(GbaNativeFrameDriver.ShouldOpenConsoleAtStartup(path), Is.EqualTo(expected));
        }

        [Test]
        public void Root_HasTheControllerFirstFunctionsInOrder()
        {
            CollectionAssert.AreEqual(
                new[] { "Core Selection", "Load ROM", "Save State", "Load State", "Settings" },
                GbaStartupMenuModel.RootItems);

            var model = Open();
            Assert.That(model.Page, Is.EqualTo(GbaStartupMenuPage.Root));
            Assert.That(model.ItemCount, Is.EqualTo(5));
        }

        [Test]
        public void RootLoadRom_RoutesToExplicitPickerAction()
        {
            var model = Open();
            model.Move(1);

            Assert.That(model.Activate(), Is.EqualTo(GbaStartupMenuAction.LoadRom));
            Assert.That(model.Page, Is.EqualTo(GbaStartupMenuPage.Root));
        }

        [Test]
        public void StateMenus_ExposeEightSlotsAndRouteActions()
        {
            var model = Open();
            model.Move(2);
            Assert.That(model.Activate(), Is.EqualTo(GbaStartupMenuAction.OpenStateSave));
            Assert.That(model.ItemCount, Is.EqualTo(8));
            model.Move(3);
            Assert.That(model.SelectedStateSlot, Is.EqualTo(3));
            Assert.That(model.Activate(), Is.EqualTo(GbaStartupMenuAction.SaveStateSlot));
            model.Back();
            model.Move(1);
            Assert.That(model.Activate(), Is.EqualTo(GbaStartupMenuAction.OpenStateLoad));
            Assert.That(model.ItemCount, Is.EqualTo(8));
            Assert.That(model.Activate(), Is.EqualTo(GbaStartupMenuAction.LoadStateSlot));
        }

        [Test]
        public void CoreSelection_PreservesRootSelectionAndRequiresExplicitConfirm()
        {
            var model = Open(libretroAvailable: true);
            Assert.That(model.Activate(), Is.EqualTo(GbaStartupMenuAction.None));
            Assert.That(model.Page, Is.EqualTo(GbaStartupMenuPage.Core));
            model.Move(1);

            Assert.That(model.Activate(), Is.EqualTo(GbaStartupMenuAction.CommitCore));
            Assert.That(model.SelectedCore, Is.EqualTo(GbaCoreKind.LibretroResolvedExperimental));
            Assert.That(model.Page, Is.EqualTo(GbaStartupMenuPage.Root));
            Assert.That(model.SelectedIndex, Is.EqualTo(0));
        }

        [Test]
        public void Settings_BackPreservesSelectionAndRoutesNestedActions()
        {
            var model = Open();
            model.Move(4);
            model.Activate();
            Assert.That(model.Page, Is.EqualTo(GbaStartupMenuPage.Settings));
            model.Move(2);
            model.Activate();
            Assert.That(model.Page, Is.EqualTo(GbaStartupMenuPage.ViewMode));
            model.Move(1);

            Assert.That(model.Activate(), Is.EqualTo(GbaStartupMenuAction.ApplyViewMode));
            Assert.That(model.SelectedViewMode, Is.EqualTo(GbaScreenPlacementMode.PlaneAnchoring));
            Assert.That(model.Page, Is.EqualTo(GbaStartupMenuPage.Settings));
            Assert.That(model.SelectedIndex, Is.EqualTo(2));
            model.Back();
            Assert.That(model.Page, Is.EqualTo(GbaStartupMenuPage.Root));
            Assert.That(model.SelectedIndex, Is.EqualTo(4));
        }

        [Test]
        public void Settings_ExposesAdvancedConsoleButNotAtRoot()
        {
            var model = Open();
            model.Move(4);
            model.Activate();
            model.Move(3);

            Assert.That(model.Activate(), Is.EqualTo(GbaStartupMenuAction.OpenAdvancedConsole));
            Assert.That(model.Page, Is.EqualTo(GbaStartupMenuPage.Settings));
        }

        [Test]
        public void PresentationMode_OffersOriginalAndExtendedParallaxOnly()
        {
            var model = Open();
            model.Move(GbaStartupMenuModel.RootItems.Length - 1);
            model.Activate();
            model.Move(1);

            Assert.That(model.Activate(), Is.EqualTo(GbaStartupMenuAction.None));
            Assert.That(model.Page, Is.EqualTo(GbaStartupMenuPage.PresentationMode));
            Assert.That(model.ItemCount, Is.EqualTo(2));
            Assert.That(model.SelectedPresentationMode, Is.EqualTo(GbaNativeFrameDriver.PresentationMode.Original));

            model.Move(1);
            Assert.That(model.Activate(), Is.EqualTo(GbaStartupMenuAction.ApplyPresentationMode));
            Assert.That(model.SelectedPresentationMode, Is.EqualTo(GbaNativeFrameDriver.PresentationMode.ForcePartitionExtendedParallax));
            Assert.That(model.Page, Is.EqualTo(GbaStartupMenuPage.Settings));
            Assert.That(model.SelectedIndex, Is.EqualTo(1));
        }

        [Test]
        public void PresentationMode_ReflectsTheActiveExtendedParallaxSelection()
        {
            var model = Open(presentationMode: GbaNativeFrameDriver.PresentationMode.ForcePartitionExtendedParallax);
            model.Move(GbaStartupMenuModel.RootItems.Length - 1);
            model.Activate();
            model.Move(1);
            model.Activate();

            Assert.That(model.Page, Is.EqualTo(GbaStartupMenuPage.PresentationMode));
            Assert.That(model.SelectedPresentationMode, Is.EqualTo(GbaNativeFrameDriver.PresentationMode.ForcePartitionExtendedParallax));
            Assert.That(model.SelectedIndex, Is.EqualTo(1));
        }

        [Test]
        public void BackAtRoot_ClosesMenu()
        {
            var model = Open();

            Assert.That(model.Back(), Is.EqualTo(GbaStartupMenuAction.Close));
            Assert.That(model.IsOpen, Is.False);
        }

        [Test]
        public void ControllerCalibration_IsSequentialAndApplyIsExplicit()
        {
            CollectionAssert.AreEqual(new[] { GbaMappedControl.Up, GbaMappedControl.Right,
                GbaMappedControl.Down, GbaMappedControl.Left, GbaMappedControl.Select,
                GbaMappedControl.Start, GbaMappedControl.A, GbaMappedControl.B,
                GbaMappedControl.L, GbaMappedControl.R }, GbaStartupMenuModel.CalibrationOrder);
            var model = Open();
            model.Move(GbaStartupMenuModel.RootItems.Length - 1);
            model.Activate();
            Assert.That(model.Page, Is.EqualTo(GbaStartupMenuPage.Settings));
            model.Activate();
            Assert.That(model.Page, Is.EqualTo(GbaStartupMenuPage.ControllerMapping));
            Assert.That(model.Activate(), Is.EqualTo(GbaStartupMenuAction.BeginControllerCapture));
            Assert.That(model.CaptureControl, Is.EqualTo(GbaMappedControl.Up));
            model.AdvanceControllerCapture();
            Assert.That(model.CaptureControl, Is.EqualTo(GbaMappedControl.Right));
            for (var index = 0; index < 9; index++) model.AdvanceControllerCapture();
            Assert.That(model.IsReviewingController, Is.True);
            Assert.That(model.Activate(), Is.EqualTo(GbaStartupMenuAction.ApplyControllerMapping));
        }

        [Test]
        public void ControllerMapping_BackCancelsDraftWorkflow()
        {
            var model = Open();
            model.Move(GbaStartupMenuModel.RootItems.Length - 1);
            model.Activate();
            model.Activate();
            model.Activate();
            Assert.That(model.IsCapturingController, Is.True);
            Assert.That(model.Back(), Is.EqualTo(GbaStartupMenuAction.CancelControllerMapping));
            Assert.That(model.IsCapturingController, Is.False);
            Assert.That(model.Page, Is.EqualTo(GbaStartupMenuPage.Settings));
        }

        private static GbaStartupMenuModel Open(
            bool libretroAvailable = false,
            GbaNativeFrameDriver.PresentationMode presentationMode = GbaNativeFrameDriver.PresentationMode.Original)
        {
            var model = new GbaStartupMenuModel();
            model.Open(
                GbaCoreKind.DirectMgba,
                presentationMode,
                GbaScreenPlacementMode.VirtualField,
                libretroAvailable);
            return model;
        }
    }
}

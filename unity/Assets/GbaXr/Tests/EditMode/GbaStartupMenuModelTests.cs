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
        public void Root_HasTheThreeControllerFirstFunctionsInOrder()
        {
            CollectionAssert.AreEqual(
                new[] { "Core Selection", "Load ROM", "Settings" },
                GbaStartupMenuModel.RootItems);

            var model = Open();
            Assert.That(model.Page, Is.EqualTo(GbaStartupMenuPage.Root));
            Assert.That(model.ItemCount, Is.EqualTo(3));
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
            model.Move(2);
            model.Activate();
            Assert.That(model.Page, Is.EqualTo(GbaStartupMenuPage.Settings));
            model.Move(1);
            model.Activate();
            Assert.That(model.Page, Is.EqualTo(GbaStartupMenuPage.ViewMode));
            model.Move(1);

            Assert.That(model.Activate(), Is.EqualTo(GbaStartupMenuAction.ApplyViewMode));
            Assert.That(model.SelectedViewMode, Is.EqualTo(GbaScreenPlacementMode.PlaneAnchoring));
            Assert.That(model.Page, Is.EqualTo(GbaStartupMenuPage.Settings));
            Assert.That(model.SelectedIndex, Is.EqualTo(1));
            model.Back();
            Assert.That(model.Page, Is.EqualTo(GbaStartupMenuPage.Root));
            Assert.That(model.SelectedIndex, Is.EqualTo(2));
        }

        [Test]
        public void Settings_ExposesAdvancedConsoleButNotAtRoot()
        {
            var model = Open();
            model.Move(2);
            model.Activate();
            model.Move(2);

            Assert.That(model.Activate(), Is.EqualTo(GbaStartupMenuAction.OpenAdvancedConsole));
            Assert.That(model.Page, Is.EqualTo(GbaStartupMenuPage.Settings));
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
            model.Move(2);
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
            model.Move(2);
            model.Activate();
            model.Activate();
            model.Activate();
            Assert.That(model.IsCapturingController, Is.True);
            Assert.That(model.Back(), Is.EqualTo(GbaStartupMenuAction.CancelControllerMapping));
            Assert.That(model.IsCapturingController, Is.False);
            Assert.That(model.Page, Is.EqualTo(GbaStartupMenuPage.Settings));
        }

        private static GbaStartupMenuModel Open(bool libretroAvailable = false)
        {
            var model = new GbaStartupMenuModel();
            model.Open(GbaCoreKind.DirectMgba, GbaScreenPlacementMode.VirtualField, libretroAvailable);
            return model;
        }
    }
}

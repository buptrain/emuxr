using System;
using System.Collections.Generic;

namespace GbaXr
{
    internal enum GbaStartupMenuPage { Root, Core, Settings, ControllerMapping, ViewMode }
    internal enum GbaStartupMenuAction { None, LoadRom, CommitCore, ApplyViewMode, OpenAdvancedConsole, BeginControllerCapture, ApplyControllerMapping, ResetControllerMapping, CancelControllerMapping, Close }

    /// <summary>Controller-only startup navigation. This model has no Unity UI or native-worker dependency.</summary>
    internal sealed class GbaStartupMenuModel
    {
        internal static readonly string[] RootItems = { "Core Selection", "Load ROM", "Settings" };
        internal static readonly string[] SettingsItems = { "Controller Mapping", "View Mode", "Advanced Console" };
        internal static readonly string[] MappingLines =
        {
            "A: GBA A     B: GBA B",
            "X / Left Shoulder: GBA L",
            "Y / Right Shoulder: GBA R",
            "D-pad: GBA D-pad     Menu: Start     View: Select",
            "R3: open Parallax Emu menu"
        };

        private readonly Dictionary<GbaStartupMenuPage, int> _selection = new();
        private readonly List<GbaCoreKind> _coreItems = new();

        internal bool IsOpen { get; private set; }
        internal GbaStartupMenuPage Page { get; private set; } = GbaStartupMenuPage.Root;
        internal GbaCoreKind SelectedCore { get; private set; } = GbaCoreKind.DirectMgba;
        internal GbaScreenPlacementMode SelectedViewMode { get; private set; } = GbaScreenPlacementMode.VirtualField;
        internal int SelectedIndex => _selection.TryGetValue(Page, out var value) ? value : 0;
        internal bool IsCapturingController { get; private set; }
        internal bool IsReviewingController { get; private set; }
        internal int CaptureIndex { get; private set; }
        internal static readonly GbaMappedControl[] CalibrationOrder = Array.ConvertAll(
            EmuControllerInputProfiles.Resolve(EmuCoreFamily.Gba).CalibrationOrder,
            ToGbaMappedControl);
        internal GbaMappedControl CaptureControl => CalibrationOrder[CaptureIndex];
        internal IReadOnlyList<GbaCoreKind> CoreItems => _coreItems;
        internal int ItemCount => Page switch
        {
            GbaStartupMenuPage.Root => RootItems.Length,
            GbaStartupMenuPage.Core => _coreItems.Count,
            GbaStartupMenuPage.Settings => SettingsItems.Length,
            GbaStartupMenuPage.ControllerMapping => IsCapturingController ? 0 : (IsReviewingController ? 2 : 1),
            GbaStartupMenuPage.ViewMode => 2,
            _ => 0
        };

        internal void Open(GbaCoreKind activeCore, GbaScreenPlacementMode activeViewMode, bool libretroAvailable)
        {
            Configure(activeCore, activeViewMode, libretroAvailable);
            IsOpen = true;
            Page = GbaStartupMenuPage.Root;
            ClampSelection();
        }

        internal void Configure(GbaCoreKind activeCore, GbaScreenPlacementMode activeViewMode, bool libretroAvailable)
        {
            SelectedCore = activeCore;
            SelectedViewMode = activeViewMode;
            _coreItems.Clear();
            _coreItems.Add(GbaCoreKind.DirectMgba);
            if (libretroAvailable) _coreItems.Add(GbaCoreKind.LibretroResolvedExperimental);
            ClampSelection();
        }

        internal void Close() => IsOpen = false;

        internal void Move(int delta)
        {
            var count = ItemCount;
            if (count <= 1) return;
            _selection[Page] = (SelectedIndex + delta % count + count) % count;
        }

        internal GbaStartupMenuAction Back()
        {
            switch (Page)
            {
                case GbaStartupMenuPage.Root:
                    Close();
                    return GbaStartupMenuAction.Close;
                case GbaStartupMenuPage.Core:
                case GbaStartupMenuPage.Settings:
                    Page = GbaStartupMenuPage.Root;
                    return GbaStartupMenuAction.None;
                case GbaStartupMenuPage.ControllerMapping:
                    IsCapturingController = false;
                    IsReviewingController = false;
                    CaptureIndex = 0;
                    Page = GbaStartupMenuPage.Settings;
                    return GbaStartupMenuAction.CancelControllerMapping;
                case GbaStartupMenuPage.ViewMode:
                    Page = GbaStartupMenuPage.Settings;
                    return GbaStartupMenuAction.None;
                default:
                    return GbaStartupMenuAction.None;
            }
        }

        internal GbaStartupMenuAction Activate()
        {
            if (!IsOpen) return GbaStartupMenuAction.None;
            switch (Page)
            {
                case GbaStartupMenuPage.Root:
                    return ActivateRoot();
                case GbaStartupMenuPage.Core:
                    if (_coreItems.Count == 0) return GbaStartupMenuAction.None;
                    SelectedCore = _coreItems[SelectedIndex];
                    Page = GbaStartupMenuPage.Root;
                    return GbaStartupMenuAction.CommitCore;
                case GbaStartupMenuPage.Settings:
                    return ActivateSettings();
                case GbaStartupMenuPage.ViewMode:
                    SelectedViewMode = SelectedIndex == 0
                        ? GbaScreenPlacementMode.VirtualField
                        : GbaScreenPlacementMode.PlaneAnchoring;
                    Page = GbaStartupMenuPage.Settings;
                    return GbaStartupMenuAction.ApplyViewMode;
                case GbaStartupMenuPage.ControllerMapping:
                    if (IsReviewingController)
                        return SelectedIndex == 0 ? GbaStartupMenuAction.ApplyControllerMapping : GbaStartupMenuAction.CancelControllerMapping;
                    IsCapturingController = true; CaptureIndex = 0; return GbaStartupMenuAction.BeginControllerCapture;
                default:
                    return GbaStartupMenuAction.None;
            }
        }

        internal void AdvanceControllerCapture()
        {
            CaptureIndex++;
            if (CaptureIndex >= 10) { IsCapturingController = false; IsReviewingController = true; _selection[Page] = 0; }
        }
        internal void CancelControllerCapture() { IsCapturingController = false; IsReviewingController = false; CaptureIndex = 0; }

        private static GbaMappedControl ToGbaMappedControl(EmuControllerAction action) => action switch
        {
            EmuControllerAction.Up => GbaMappedControl.Up,
            EmuControllerAction.Right => GbaMappedControl.Right,
            EmuControllerAction.Down => GbaMappedControl.Down,
            EmuControllerAction.Left => GbaMappedControl.Left,
            EmuControllerAction.Select => GbaMappedControl.Select,
            EmuControllerAction.Start => GbaMappedControl.Start,
            EmuControllerAction.A => GbaMappedControl.A,
            EmuControllerAction.B => GbaMappedControl.B,
            EmuControllerAction.L => GbaMappedControl.L,
            EmuControllerAction.R => GbaMappedControl.R,
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "GBA profile cannot map X or Y.")
        };

        private GbaStartupMenuAction ActivateRoot()
        {
            switch (SelectedIndex)
            {
                case 0: Page = GbaStartupMenuPage.Core; return GbaStartupMenuAction.None;
                case 1: return GbaStartupMenuAction.LoadRom;
                default: Page = GbaStartupMenuPage.Settings; return GbaStartupMenuAction.None;
            }
        }

        private GbaStartupMenuAction ActivateSettings()
        {
            switch (SelectedIndex)
            {
                case 0: Page = GbaStartupMenuPage.ControllerMapping; return GbaStartupMenuAction.None;
                case 1: Page = GbaStartupMenuPage.ViewMode; return GbaStartupMenuAction.None;
                default: return GbaStartupMenuAction.OpenAdvancedConsole;
            }
        }

        private void ClampSelection()
        {
            var count = ItemCount;
            if (count <= 0) { _selection[Page] = 0; return; }
            _selection[Page] = Math.Min(Math.Max(SelectedIndex, 0), count - 1);
        }
    }
}

using System;
namespace GbaXr
{
    internal enum EmuCoreFamily { Gba, Nds }
    internal enum EmuControllerAction { Up, Right, Down, Left, Select, Start, A, B, X, Y, L, R }
    internal sealed class EmuControllerInputProfile
    {
        private readonly EmuControllerAction[] _order;
        internal EmuControllerInputProfile(EmuCoreFamily family, EmuControllerAction[] order) { Family = family; _order = (EmuControllerAction[])order.Clone(); }
        internal EmuCoreFamily Family { get; }
        internal EmuControllerAction[] CalibrationOrder => (EmuControllerAction[])_order.Clone();
    }
    /// <summary>Layout-only seam; no NDS launcher/runtime path exists yet.</summary>
    internal static class EmuControllerInputProfiles
    {
        internal static readonly EmuControllerInputProfile Gba = new(EmuCoreFamily.Gba, new[] { EmuControllerAction.Up, EmuControllerAction.Right, EmuControllerAction.Down, EmuControllerAction.Left, EmuControllerAction.Select, EmuControllerAction.Start, EmuControllerAction.A, EmuControllerAction.B, EmuControllerAction.L, EmuControllerAction.R });
        internal static readonly EmuControllerInputProfile Nds = new(EmuCoreFamily.Nds, new[] { EmuControllerAction.Up, EmuControllerAction.Right, EmuControllerAction.Down, EmuControllerAction.Left, EmuControllerAction.Select, EmuControllerAction.Start, EmuControllerAction.A, EmuControllerAction.B, EmuControllerAction.X, EmuControllerAction.Y, EmuControllerAction.L, EmuControllerAction.R });
        internal static EmuControllerInputProfile Resolve(EmuCoreFamily family) => family == EmuCoreFamily.Nds ? Nds : Gba;
    }
}

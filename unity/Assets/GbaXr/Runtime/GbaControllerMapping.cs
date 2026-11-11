using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GbaXr
{
    internal enum GbaMappedControl { Up, Down, Left, Right, A, B, L, R, Start, Select }
    // Numeric order is persisted as JSON integers; do not reorder.
    internal enum EmuGamepadBinding { DpadUp, DpadDown, DpadLeft, DpadRight, South, East, West, North, LeftShoulder, RightShoulder, Start, Select }

    /// <summary>Direct-mGBA-only controller map. R3 and menu navigation are never bindable.</summary>
    internal sealed class GbaControllerMapping
    {
        private const string PreferenceKey = "GbaXr.DirectMgba.ControllerMap.v1";
        private readonly Dictionary<GbaMappedControl, EmuGamepadBinding> _bindings = Defaults();
        internal EmuGamepadBinding this[GbaMappedControl control] => _bindings[control];

        internal static GbaControllerMapping CreateDefaults() => new GbaControllerMapping();

        internal static GbaControllerMapping Load()
        {
            var mapping = new GbaControllerMapping();
            var encoded = PlayerPrefs.GetString(PreferenceKey, string.Empty);
            if (string.IsNullOrEmpty(encoded)) return mapping;
            try
            {
                var data = JsonUtility.FromJson<StoredMap>(encoded);
                if (data?.values == null || data.values.Length != 10) return mapping;
                var candidate = new Dictionary<GbaMappedControl, EmuGamepadBinding>();
                for (var index = 0; index < data.values.Length; index++)
                {
                    if (!Enum.IsDefined(typeof(EmuGamepadBinding), data.values[index])) return mapping;
                    candidate[(GbaMappedControl)index] = (EmuGamepadBinding)data.values[index];
                }
                if (!IsValid(candidate)) return mapping;
                mapping._bindings.Clear(); foreach (var pair in candidate) mapping._bindings[pair.Key] = pair.Value;
            }
            catch (Exception) { }
            return mapping;
        }

        internal GbaControllerMapping Clone()
        {
            var clone = new GbaControllerMapping(); clone._bindings.Clear();
            foreach (var pair in _bindings) clone._bindings[pair.Key] = pair.Value;
            return clone;
        }

        // Calibration permits intermediate duplicates so controls can swap.
        // Only Apply validates and persists the completed draft.
        internal void SetDraft(GbaMappedControl control, EmuGamepadBinding binding) => _bindings[control] = binding;
        internal bool IsValidForApply() => IsValid(_bindings);
        internal bool TryApply() { if (!IsValid(_bindings)) return false; Save(); return true; }
        internal void ResetDefaults() { _bindings.Clear(); foreach (var pair in Defaults()) _bindings[pair.Key] = pair.Value; }

        internal uint ReadKeys(Gamepad gamepad)
        {
            if (gamepad == null) return 0;
            uint keys = 0;
            foreach (GbaMappedControl control in Enum.GetValues(typeof(GbaMappedControl)))
            {
                if (Pressed(gamepad, _bindings[control])) keys |= 1U << Bit(control);
            }
            return keys;
        }

        internal static bool TryCapture(Gamepad gamepad, out EmuGamepadBinding binding)
        {
            binding = default;
            if (gamepad == null) return false;
            foreach (var value in CaptureOrder)
            {
                if (Pressed(gamepad, value) && !IsReserved(value)) { binding = value; return true; }
            }
            return false;
        }
        internal static bool AnyCapturePressed(Gamepad gamepad)
        {
            if (gamepad == null) return false;
            foreach (var value in CaptureOrder) if (Pressed(gamepad, value)) return true;
            return false;
        }

        internal static string Describe(EmuGamepadBinding binding) => binding switch
        {
            EmuGamepadBinding.South => "A", EmuGamepadBinding.East => "B", EmuGamepadBinding.West => "X",
            EmuGamepadBinding.North => "Y", EmuGamepadBinding.LeftShoulder => "Left Shoulder",
            EmuGamepadBinding.RightShoulder => "Right Shoulder", EmuGamepadBinding.Start => "Menu/Start",
            EmuGamepadBinding.Select => "View/Select", EmuGamepadBinding.DpadUp => "D-pad Up",
            EmuGamepadBinding.DpadDown => "D-pad Down", EmuGamepadBinding.DpadLeft => "D-pad Left", _ => "D-pad Right"
        };

        private void Save()
        {
            var values = new int[10]; for (var i = 0; i < values.Length; i++) values[i] = (int)_bindings[(GbaMappedControl)i];
            PlayerPrefs.SetString(PreferenceKey, JsonUtility.ToJson(new StoredMap { values = values })); PlayerPrefs.Save();
        }
        private static bool IsValid(Dictionary<GbaMappedControl, EmuGamepadBinding> values) => values.Count == 10 && new HashSet<EmuGamepadBinding>(values.Values).Count == 10;
        private static Dictionary<GbaMappedControl, EmuGamepadBinding> Defaults() => new()
        {
            [GbaMappedControl.Up] = EmuGamepadBinding.DpadUp, [GbaMappedControl.Down] = EmuGamepadBinding.DpadDown,
            [GbaMappedControl.Left] = EmuGamepadBinding.DpadLeft, [GbaMappedControl.Right] = EmuGamepadBinding.DpadRight,
            [GbaMappedControl.A] = EmuGamepadBinding.South, [GbaMappedControl.B] = EmuGamepadBinding.East,
            [GbaMappedControl.L] = EmuGamepadBinding.West, [GbaMappedControl.R] = EmuGamepadBinding.North,
            [GbaMappedControl.Start] = EmuGamepadBinding.Start, [GbaMappedControl.Select] = EmuGamepadBinding.Select
        };
        private static readonly EmuGamepadBinding[] CaptureOrder = { EmuGamepadBinding.DpadUp, EmuGamepadBinding.DpadDown, EmuGamepadBinding.DpadLeft, EmuGamepadBinding.DpadRight, EmuGamepadBinding.South, EmuGamepadBinding.East, EmuGamepadBinding.West, EmuGamepadBinding.North, EmuGamepadBinding.LeftShoulder, EmuGamepadBinding.RightShoulder, EmuGamepadBinding.Start, EmuGamepadBinding.Select };
        private static bool IsReserved(EmuGamepadBinding binding) => false;
        private static int Bit(GbaMappedControl c) => c switch { GbaMappedControl.A => 0, GbaMappedControl.B => 1, GbaMappedControl.Select => 2, GbaMappedControl.Start => 3, GbaMappedControl.Right => 4, GbaMappedControl.Left => 5, GbaMappedControl.Up => 6, GbaMappedControl.Down => 7, GbaMappedControl.R => 8, _ => 9 };
        private static bool Pressed(Gamepad p, EmuGamepadBinding b) => b switch { EmuGamepadBinding.DpadUp => p.dpad.up.isPressed, EmuGamepadBinding.DpadDown => p.dpad.down.isPressed, EmuGamepadBinding.DpadLeft => p.dpad.left.isPressed, EmuGamepadBinding.DpadRight => p.dpad.right.isPressed, EmuGamepadBinding.South => p.buttonSouth.isPressed, EmuGamepadBinding.East => p.buttonEast.isPressed, EmuGamepadBinding.West => p.buttonWest.isPressed, EmuGamepadBinding.North => p.buttonNorth.isPressed, EmuGamepadBinding.LeftShoulder => p.leftShoulder.isPressed, EmuGamepadBinding.RightShoulder => p.rightShoulder.isPressed, EmuGamepadBinding.Start => p.startButton.isPressed, _ => p.selectButton.isPressed };
        [Serializable] private sealed class StoredMap { public int[] values; }
    }
}

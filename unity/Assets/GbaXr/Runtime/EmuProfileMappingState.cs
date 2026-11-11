using System;
using System.Collections.Generic;
using UnityEngine;

namespace GbaXr
{
    internal interface IEmuControllerMappingStore { string Load(string key); void Save(string key, string value); }
    /// <summary>Future-core convenience store; deliberately unused by GBA v1.</summary>
    internal sealed class EmuPlayerPrefsMappingStore : IEmuControllerMappingStore
    {
        internal static readonly EmuPlayerPrefsMappingStore Instance = new EmuPlayerPrefsMappingStore();
        private EmuPlayerPrefsMappingStore() { }
        public string Load(string key) => string.IsNullOrEmpty(key) ? null : PlayerPrefs.GetString(key, string.Empty);
        public void Save(string key, string value)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value)) return;
            PlayerPrefs.SetString(key, value); PlayerPrefs.Save();
        }
    }
    /// <summary>
    /// Profile-owned persisted mapping contract for future cores. Current
    /// direct mGBA keeps its compatible v1 runtime map until it is deliberately
    /// migrated; NDS callers use Create/Deserialize with the NDS profile.
    /// </summary>
    internal sealed class EmuProfileMappingState
    {
        internal const string LegacyGbaPreferenceKey = "GbaXr.DirectMgba.ControllerMap.v1";
        private readonly EmuControllerInputProfile _profile;
        private readonly Dictionary<EmuControllerAction, EmuGamepadBinding> _bindings;

        private EmuProfileMappingState(EmuControllerInputProfile profile, Dictionary<EmuControllerAction, EmuGamepadBinding> bindings)
        { _profile = profile; _bindings = bindings; }
        internal EmuCoreFamily Family => _profile.Family;
        internal string PreferenceKey => $"GbaXr.ControllerMap.v2.{_profile.Family}";
        internal EmuGamepadBinding this[EmuControllerAction action] => _bindings[action];

        internal static EmuProfileMappingState Create(EmuControllerInputProfile profile) =>
            new(profile, Defaults(profile));
        internal static EmuProfileMappingState Load(EmuControllerInputProfile profile, IEmuControllerMappingStore store) =>
            DeserializeOrDefault(profile, store?.Load($"GbaXr.ControllerMap.v2.{profile.Family}"));
        internal EmuProfileMappingState Clone() => new(_profile, new Dictionary<EmuControllerAction, EmuGamepadBinding>(_bindings));
        internal void SetDraft(EmuControllerAction action, EmuGamepadBinding binding) => _bindings[action] = binding;
        internal bool IsValid => _bindings.Count == _profile.CalibrationOrder.Length && new HashSet<EmuGamepadBinding>(_bindings.Values).Count == _bindings.Count;
        internal void Save(IEmuControllerMappingStore store) { if (store != null && IsValid) store.Save(PreferenceKey, Serialize()); }
        internal string Serialize() { var values = new int[_profile.CalibrationOrder.Length]; for (var i = 0; i < values.Length; i++) values[i] = (int)_bindings[_profile.CalibrationOrder[i]]; return JsonUtility.ToJson(new Stored { schema = 2, family = (int)Family, values = values }); }
        internal static EmuProfileMappingState DeserializeOrDefault(EmuControllerInputProfile profile, string json)
        {
            var fallback = Create(profile);
            try
            {
                var stored = JsonUtility.FromJson<Stored>(json);
                if (stored?.values == null || stored.schema != 2 || stored.family != (int)profile.Family || stored.values.Length != profile.CalibrationOrder.Length) return fallback;
                var candidate = fallback.Clone();
                for (var i = 0; i < stored.values.Length; i++)
                {
                    if (!Enum.IsDefined(typeof(EmuGamepadBinding), stored.values[i])) return fallback;
                    candidate.SetDraft(profile.CalibrationOrder[i], (EmuGamepadBinding)stored.values[i]);
                }
                return candidate.IsValid ? candidate : fallback;
            }
            catch (Exception) { return fallback; }
        }
        // Future NDS worker integration point: resolve profile + this map,
        // then translate the profile action set to that core's native bits.
        internal static EmuProfileMappingState ForFutureCore(EmuCoreFamily family) => Create(EmuControllerInputProfiles.Resolve(family));

        private static Dictionary<EmuControllerAction, EmuGamepadBinding> Defaults(EmuControllerInputProfile profile)
        {
            var result = new Dictionary<EmuControllerAction, EmuGamepadBinding>();
            foreach (var action in profile.CalibrationOrder) result[action] = action switch
            {
                EmuControllerAction.Up => EmuGamepadBinding.DpadUp, EmuControllerAction.Right => EmuGamepadBinding.DpadRight,
                EmuControllerAction.Down => EmuGamepadBinding.DpadDown, EmuControllerAction.Left => EmuGamepadBinding.DpadLeft,
                EmuControllerAction.Select => EmuGamepadBinding.Select, EmuControllerAction.Start => EmuGamepadBinding.Start,
                EmuControllerAction.A => EmuGamepadBinding.South, EmuControllerAction.B => EmuGamepadBinding.East,
                EmuControllerAction.X => EmuGamepadBinding.West, EmuControllerAction.Y => EmuGamepadBinding.North,
                EmuControllerAction.L => profile.Family == EmuCoreFamily.Gba ? EmuGamepadBinding.West : EmuGamepadBinding.LeftShoulder,
                _ => profile.Family == EmuCoreFamily.Gba ? EmuGamepadBinding.North : EmuGamepadBinding.RightShoulder
            };
            return result;
        }
        [Serializable] private sealed class Stored { public int schema; public int family; public int[] values; }
    }
}

using System;
using UnityEngine;

namespace GbaXr
{
    [Flags]
    public enum GbaLibretroCertification
    {
        None = 0,
        FixtureFrameProbe = 1 << 0,
        ResolvedFrameParity = 1 << 1,
        InputParity = 1 << 2,
        AudioParity = 1 << 3,
        StateParity = 1 << 4,
        GalaxyXrDevice = 1 << 5,
        FullyCertified = FixtureFrameProbe | ResolvedFrameParity | InputParity | AudioParity | StateParity | GalaxyXrDevice
    }

    internal interface IGbaLibretroCertificationStorage { int GetInt(string key, int fallback); void SetInt(string key, int value); void Save(); }
    internal sealed class GbaPlayerPrefsCertificationStorage : IGbaLibretroCertificationStorage
    {
        public int GetInt(string key, int fallback) => PlayerPrefs.GetInt(key, fallback);
        public void SetInt(string key, int value) => PlayerPrefs.SetInt(key, value);
        public void Save() => PlayerPrefs.Save();
    }

    /// <summary>Persisted validation evidence; no console action can set these flags.</summary>
    public static class GbaLibretroReadinessStore
    {
        // v2 invalidates all evidence recorded under the former five-lease,
        // historical-hash frame contract. A new confirmed 30-lease canonical
        // frame result must accompany the other fresh batch evidence.
        internal const string Key = "GbaXr.LibretroCertification.v2";
        private static readonly IGbaLibretroCertificationStorage PlayerPrefsStorage = new GbaPlayerPrefsCertificationStorage();
        public static GbaLibretroReadiness Load() => ToReadiness(LoadFlags(PlayerPrefsStorage));
        public static void RecordApprovedEvidence(GbaLibretroCertification evidence)
        {
            var flags = LoadFlags(PlayerPrefsStorage) | evidence;
            PlayerPrefsStorage.SetInt(Key, (int)flags); PlayerPrefsStorage.Save();
        }
        internal static GbaLibretroCertification LoadFlags(IGbaLibretroCertificationStorage storage) => (GbaLibretroCertification)storage.GetInt(Key, 0);
        internal static GbaLibretroReadiness ToReadiness(GbaLibretroCertification flags) => new(
            GbaLibretroUnityPackaging.IsAdapterPackaged,
            flags.HasFlag(GbaLibretroCertification.ResolvedFrameParity),
            flags.HasFlag(GbaLibretroCertification.InputParity),
            flags.HasFlag(GbaLibretroCertification.AudioParity),
            flags.HasFlag(GbaLibretroCertification.StateParity),
            flags.HasFlag(GbaLibretroCertification.FixtureFrameProbe) && flags.HasFlag(GbaLibretroCertification.GalaxyXrDevice));
    }
}

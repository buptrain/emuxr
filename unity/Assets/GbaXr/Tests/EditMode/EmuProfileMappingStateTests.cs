using NUnit.Framework;
using System.Collections.Generic;
namespace GbaXr.Tests
{
    public sealed class EmuProfileMappingStateTests
    {
        [Test] public void Profiles_HaveIndependentNamespacesAndDefaults()
        { var gba = EmuProfileMappingState.ForFutureCore(EmuCoreFamily.Gba); var nds = EmuProfileMappingState.ForFutureCore(EmuCoreFamily.Nds); Assert.That(gba.PreferenceKey, Is.Not.EqualTo(nds.PreferenceKey)); Assert.That(nds[EmuControllerAction.X], Is.EqualTo(EmuGamepadBinding.West)); Assert.That(nds[EmuControllerAction.Y], Is.EqualTo(EmuGamepadBinding.North)); }
        [Test] public void NdsDeserialize_CannotMutateGbaMap()
        { var gba = EmuProfileMappingState.ForFutureCore(EmuCoreFamily.Gba); var original = gba[EmuControllerAction.A]; var nds = EmuProfileMappingState.ForFutureCore(EmuCoreFamily.Nds); nds.SetDraft(EmuControllerAction.A, EmuGamepadBinding.West); var reloaded = EmuProfileMappingState.DeserializeOrDefault(EmuControllerInputProfiles.Nds, nds.Serialize()); Assert.That(gba[EmuControllerAction.A], Is.EqualTo(original)); Assert.That(reloaded.Family, Is.EqualTo(EmuCoreFamily.Nds)); }
        [Test] public void Serialization_HonoursProfileOrderAndFallsBackOnCorruption()
        { var nds = EmuProfileMappingState.ForFutureCore(EmuCoreFamily.Nds); Assert.That(EmuProfileMappingState.DeserializeOrDefault(EmuControllerInputProfiles.Nds, nds.Serialize()).IsValid, Is.True); Assert.That(EmuProfileMappingState.DeserializeOrDefault(EmuControllerInputProfiles.Nds, "bad").IsValid, Is.True); }
        [Test] public void Store_IsProfileIsolatedAndWrongProfileFallsBack()
        {
            var store = new MemoryStore(); var nds = EmuProfileMappingState.ForFutureCore(EmuCoreFamily.Nds);
            nds.Save(store);
            Assert.That(EmuProfileMappingState.Load(EmuControllerInputProfiles.Nds, store)[EmuControllerAction.A], Is.EqualTo(EmuGamepadBinding.South));
            Assert.That(EmuProfileMappingState.Load(EmuControllerInputProfiles.Gba, store)[EmuControllerAction.A], Is.EqualTo(EmuGamepadBinding.South));
        }
        private sealed class MemoryStore : IEmuControllerMappingStore
        { private readonly Dictionary<string,string> _data = new(); public string Load(string key) => _data.TryGetValue(key, out var value) ? value : null; public void Save(string key, string value) => _data[key] = value; }
    }
}

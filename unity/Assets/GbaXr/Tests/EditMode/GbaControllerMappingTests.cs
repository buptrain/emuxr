using NUnit.Framework;

namespace GbaXr.Tests
{
    public sealed class GbaControllerMappingTests
    {
        [Test]
        public void Defaults_AreUniqueAndPreserveExpectedFaceButtons()
        {
            var mapping = GbaControllerMapping.CreateDefaults();
            Assert.That(mapping[GbaMappedControl.A], Is.EqualTo(EmuGamepadBinding.South));
            Assert.That(mapping[GbaMappedControl.B], Is.EqualTo(EmuGamepadBinding.East));
            Assert.That(mapping[GbaMappedControl.L], Is.EqualTo(EmuGamepadBinding.West));
            Assert.That(mapping[GbaMappedControl.R], Is.EqualTo(EmuGamepadBinding.North));
        }

        [Test]
        public void Draft_AllowsSwapUntilApplyThenRequiresUniqueBindings()
        {
            var mapping = GbaControllerMapping.CreateDefaults();
            var draft = mapping.Clone();
            draft.SetDraft(GbaMappedControl.A, mapping[GbaMappedControl.B]);
            draft.SetDraft(GbaMappedControl.B, mapping[GbaMappedControl.A]);
            Assert.That(draft.IsValidForApply(), Is.True);
            Assert.That(mapping[GbaMappedControl.A], Is.EqualTo(EmuGamepadBinding.South));
            Assert.That(draft[GbaMappedControl.A], Is.EqualTo(EmuGamepadBinding.East));
        }

        [Test]
        public void InvalidDuplicateDraft_FailsValidationWithoutChangingActiveMap()
        {
            var active = GbaControllerMapping.CreateDefaults();
            var draft = active.Clone();
            draft.SetDraft(GbaMappedControl.A, EmuGamepadBinding.East);
            Assert.That(draft.IsValidForApply(), Is.False);
            Assert.That(active[GbaMappedControl.A], Is.EqualTo(EmuGamepadBinding.South));
        }
    }
}

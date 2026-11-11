using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace GbaXr.Tests
{
    public sealed class GbaLibretroNativeBindingsTests
    {
        [Test]
        public void Binding_UsesOnlyTheIsolatedAdapterLibrary()
        {
            Assert.That(GbaLibretroNativeBindings.LibraryName, Is.EqualTo("emuxr_mgba_libretro"));

            var imports = typeof(GbaLibretroNativeBindings)
                .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                .Select(method => method.GetCustomAttribute<DllImportAttribute>())
                .Where(attribute => attribute != null)
                .ToArray();

            Assert.That(imports, Is.Not.Empty);
            Assert.That(imports.All(attribute => attribute.Value == GbaLibretroNativeBindings.LibraryName), Is.True);
            Assert.That(imports.All(attribute => attribute.EntryPoint.StartsWith("emuxr_mgba_libretro_adapter_")), Is.True);
            Assert.That(imports.Any(attribute => attribute.EntryPoint == "emuxr_mgba_libretro_adapter_acquire_latest_frame"), Is.True);
            Assert.That(imports.Any(attribute => attribute.EntryPoint == "emuxr_mgba_libretro_adapter_release_frame"), Is.True);
        }

        [Test]
        public void PackagedExperimentalBinding_IsNotDeviceCertifiedOrLaunchEligible()
        {
            Assert.That(GbaLibretroUnityPackaging.IsAdapterPackaged, Is.True);
            Assert.That(GbaLibretroUnityPackaging.IsDeviceCertified, Is.False);

            var plan = GbaCoreSelectionPolicy.Select(
                GbaCoreKind.LibretroResolvedExperimental,
                new GbaLibretroReadiness(
                    GbaLibretroUnityPackaging.IsAdapterPackaged,
                    true,
                    true,
                    true,
                    true,
                    GbaLibretroUnityPackaging.IsDeviceCertified));

            Assert.That(plan.Effective, Is.EqualTo(GbaCoreKind.DirectMgba));
        }
    }
}

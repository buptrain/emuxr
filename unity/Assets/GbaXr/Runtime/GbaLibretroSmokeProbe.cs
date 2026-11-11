using System;
using UnityEngine;

namespace GbaXr
{
    /// <summary>Outcome of the deliberately minimal packaged-adapter smoke probe.</summary>
    internal enum GbaLibretroSmokeProbeResult
    {
        Pending,
        Disabled,
        UnsupportedPlatform,
        AdapterNotPackaged,
        CreatedAndDestroyed,
        CreateReturnedNull,
        NativeLibraryMissing,
        AdapterEntryPointMissing,
        NativeCallFailed
    }

    /// <summary>Pure gate and result mapping for the isolated-adapter probe.</summary>
    internal static class GbaLibretroSmokeProbePolicy
    {
        internal static GbaLibretroSmokeProbeResult EvaluatePreflight(
            bool enabled,
            bool isAndroidPlayer,
            bool adapterPackaged)
        {
            if (!enabled)
            {
                return GbaLibretroSmokeProbeResult.Disabled;
            }

            if (!isAndroidPlayer)
            {
                return GbaLibretroSmokeProbeResult.UnsupportedPlatform;
            }

            return adapterPackaged
                ? GbaLibretroSmokeProbeResult.Pending
                : GbaLibretroSmokeProbeResult.AdapterNotPackaged;
        }

        internal static GbaLibretroSmokeProbeResult FromCreatedAdapter(IntPtr adapter) =>
            adapter == IntPtr.Zero
                ? GbaLibretroSmokeProbeResult.CreateReturnedNull
                : GbaLibretroSmokeProbeResult.CreatedAndDestroyed;

        internal static GbaLibretroSmokeProbeResult FromException(Exception exception)
        {
            return exception switch
            {
                DllNotFoundException _ => GbaLibretroSmokeProbeResult.NativeLibraryMissing,
                EntryPointNotFoundException _ => GbaLibretroSmokeProbeResult.AdapterEntryPointMissing,
                _ => GbaLibretroSmokeProbeResult.NativeCallFailed
            };
        }

        internal static bool ShouldAttempt(GbaLibretroSmokeProbeResult result) =>
            result == GbaLibretroSmokeProbeResult.Pending;

        internal static string Describe(GbaLibretroSmokeProbeResult result)
        {
            return result switch
            {
                GbaLibretroSmokeProbeResult.Disabled => "disabled by the serialized opt-in toggle",
                GbaLibretroSmokeProbeResult.UnsupportedPlatform => "not an Android player build",
                GbaLibretroSmokeProbeResult.AdapterNotPackaged => "the isolated adapter package is unavailable",
                GbaLibretroSmokeProbeResult.CreatedAndDestroyed => "adapter create/destroy completed without starting a ROM",
                GbaLibretroSmokeProbeResult.CreateReturnedNull => "adapter create returned null",
                GbaLibretroSmokeProbeResult.NativeLibraryMissing => "the adapter or its ELF dependency could not be loaded",
                GbaLibretroSmokeProbeResult.AdapterEntryPointMissing => "the adapter ABI entry point was unavailable",
                GbaLibretroSmokeProbeResult.NativeCallFailed => "an unexpected native interop exception occurred",
                _ => "probe has not attempted a native call"
            };
        }
    }

    /// <summary>
    /// Explicit, default-off Android packaging probe for the isolated mGBA
    /// libretro adapter. It creates and immediately destroys an adapter only:
    /// it never starts a ROM, acquires frames, reads audio, or touches state.
    /// This component is deliberately not added by <see cref="GbaNativeFrameDriver"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GbaLibretroSmokeProbe : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Experimental Android packaging smoke probe. Default off; only create/destroy the isolated adapter.")]
        private bool runOnAndroidAwake;

        private bool _attempted;

        private void Awake()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            RunIfExplicitlyEnabled(isAndroidPlayer: true);
#else
            RunIfExplicitlyEnabled(isAndroidPlayer: false);
#endif
        }

        private void RunIfExplicitlyEnabled(bool isAndroidPlayer)
        {
            if (_attempted)
            {
                return;
            }

            var preflight = GbaLibretroSmokeProbePolicy.EvaluatePreflight(
                runOnAndroidAwake,
                isAndroidPlayer,
                GbaLibretroUnityPackaging.IsAdapterPackaged);
            if (!GbaLibretroSmokeProbePolicy.ShouldAttempt(preflight))
            {
                if (runOnAndroidAwake)
                {
                    Debug.LogWarning($"GBA XR libretro probe: skipped; {GbaLibretroSmokeProbePolicy.Describe(preflight)}.");
                }
                return;
            }

            _attempted = true;
            IntPtr adapter = IntPtr.Zero;
            try
            {
                adapter = GbaLibretroNativeBindings.Create(GbaNativeBindings.AbiVersion);
                var result = GbaLibretroSmokeProbePolicy.FromCreatedAdapter(adapter);
                if (result != GbaLibretroSmokeProbeResult.CreatedAndDestroyed)
                {
                    Debug.LogError($"GBA XR libretro probe: failure; {GbaLibretroSmokeProbePolicy.Describe(result)}.");
                    return;
                }

                GbaLibretroNativeBindings.Destroy(adapter);
                adapter = IntPtr.Zero;
                Debug.Log($"GBA XR libretro probe: success; {GbaLibretroSmokeProbePolicy.Describe(result)}. Direct mGBA remains selected.");
            }
            catch (Exception exception)
            {
                var result = GbaLibretroSmokeProbePolicy.FromException(exception);
                Debug.LogError($"GBA XR libretro probe: failure; {GbaLibretroSmokeProbePolicy.Describe(result)} ({exception.GetType().Name}).");
            }
            finally
            {
                if (adapter != IntPtr.Zero)
                {
                    try
                    {
                        GbaLibretroNativeBindings.Destroy(adapter);
                    }
                    catch (Exception exception)
                    {
                        var result = GbaLibretroSmokeProbePolicy.FromException(exception);
                        Debug.LogError($"GBA XR libretro probe: failure during cleanup; {GbaLibretroSmokeProbePolicy.Describe(result)} ({exception.GetType().Name}).");
                    }
                }
            }
        }
    }
}

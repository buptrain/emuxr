using System;

namespace GbaXr
{
    /// <summary>
    /// A launch request is intentionally separate from the active native worker.
    /// The experimental libretro route cannot become active merely because a
    /// core was compiled; every transport parity gate and explicit Galaxy XR
    /// device certification must be recorded first.
    /// </summary>
    public enum GbaCoreKind
    {
        DirectMgba = 0,
        LibretroResolvedExperimental = 1
    }

    public readonly struct GbaLibretroReadiness
    {
        public GbaLibretroReadiness(
            bool nativeBindingPackaged,
            bool resolvedFrameParityVerified,
            bool inputParityVerified,
            bool audioParityVerified,
            bool stateParityVerified,
            bool deviceCertificationVerified)
        {
            NativeBindingPackaged = nativeBindingPackaged;
            ResolvedFrameParityVerified = resolvedFrameParityVerified;
            InputParityVerified = inputParityVerified;
            AudioParityVerified = audioParityVerified;
            StateParityVerified = stateParityVerified;
            DeviceCertificationVerified = deviceCertificationVerified;
        }

        public bool NativeBindingPackaged { get; }
        public bool ResolvedFrameParityVerified { get; }
        public bool InputParityVerified { get; }
        public bool AudioParityVerified { get; }
        public bool StateParityVerified { get; }
        public bool DeviceCertificationVerified { get; }

        public bool IsLaunchEligible => NativeBindingPackaged &&
                                        ResolvedFrameParityVerified &&
                                        InputParityVerified &&
                                        AudioParityVerified &&
                                        StateParityVerified &&
                                        DeviceCertificationVerified;

        public string FirstMissingRequirement()
        {
            if (!NativeBindingPackaged) return "native Unity binding is not packaged";
            if (!ResolvedFrameParityVerified) return "resolved-frame parity is not verified";
            if (!InputParityVerified) return "input parity is not verified";
            if (!AudioParityVerified) return "audio parity is not verified";
            if (!StateParityVerified) return "save-state parity is not verified";
            return !DeviceCertificationVerified ? "Galaxy XR device certification is not verified" : string.Empty;
        }
    }

    public readonly struct GbaCoreLaunchPlan
    {
        internal GbaCoreLaunchPlan(GbaCoreKind requested, GbaCoreKind effective, string fallbackReason)
        {
            Requested = requested;
            Effective = effective;
            FallbackReason = fallbackReason ?? string.Empty;
        }

        public GbaCoreKind Requested { get; }
        public GbaCoreKind Effective { get; }
        public string FallbackReason { get; }
        public bool FellBack => Requested != Effective;
    }

    public static class GbaCoreSelectionPolicy
    {
        public static GbaCoreLaunchPlan Select(
            GbaCoreKind requested,
            GbaLibretroReadiness libretroReadiness)
        {
            if (requested == GbaCoreKind.LibretroResolvedExperimental &&
                libretroReadiness.IsLaunchEligible)
            {
                return new GbaCoreLaunchPlan(requested, requested, string.Empty);
            }

            if (requested == GbaCoreKind.LibretroResolvedExperimental)
            {
                return new GbaCoreLaunchPlan(
                    requested,
                    GbaCoreKind.DirectMgba,
                    $"Libretro route disabled: {libretroReadiness.FirstMissingRequirement()}.");
            }

            return new GbaCoreLaunchPlan(
                GbaCoreKind.DirectMgba,
                GbaCoreKind.DirectMgba,
                string.Empty);
        }

        /// <summary>
        /// Deliberately narrow visual-diagnostic escape hatch. It never changes
        /// readiness evidence and cannot run without the packaged adapter.
        /// Normal callers must use <see cref="Select"/>.
        /// </summary>
        internal static GbaCoreLaunchPlan SelectWithDiagnosticPreview(
            GbaCoreKind requested,
            GbaLibretroReadiness readiness,
            bool explicitlyEnabled)
        {
            var plan = Select(requested, readiness);
            if (plan.FellBack && explicitlyEnabled && readiness.NativeBindingPackaged &&
                requested == GbaCoreKind.LibretroResolvedExperimental)
            {
                return new GbaCoreLaunchPlan(
                    requested,
                    requested,
                    "UNRESOLVED DIAGNOSTIC PREVIEW: parity/device certification is bypassed only for visual investigation.");
            }
            return plan;
        }

        internal static bool CanExposeCoreSelection(GbaLibretroReadiness readiness, bool explicitlyEnabled) =>
            readiness.IsLaunchEligible || (explicitlyEnabled && readiness.NativeBindingPackaged);
    }
}

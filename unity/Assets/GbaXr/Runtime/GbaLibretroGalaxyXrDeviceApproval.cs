using UnityEngine;

namespace GbaXr
{
    /// <summary>
    /// The final device flag may be recorded only after a reviewer has checked
    /// a successful fixture-only batch from this Galaxy XR build. This is not
    /// a substitute for the batch; it rejects incomplete persisted evidence.
    /// </summary>
    internal static class GbaLibretroGalaxyXrDeviceApprovalPolicy
    {
        internal static bool CanApprove(GbaLibretroReadiness readiness) =>
            readiness.NativeBindingPackaged &&
            readiness.ResolvedFrameParityVerified &&
            readiness.InputParityVerified &&
            readiness.AudioParityVerified &&
            readiness.StateParityVerified;
    }

    /// <summary>
    /// Explicit, one-purpose final approval action for an already reviewed
    /// Galaxy XR certification batch. It is default-off and should be removed
    /// or disabled immediately after the approval build has logged success.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GbaLibretroGalaxyXrDeviceApproval : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Manual post-review approval. Records Galaxy XR evidence only when all fixture batch evidence already exists.")]
        private bool approvePersistedEvidenceOnAndroidAwake;

        private void Awake()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!approvePersistedEvidenceOnAndroidAwake)
            {
                return;
            }

            var readiness = GbaLibretroReadinessStore.Load();
            if (!GbaLibretroGalaxyXrDeviceApprovalPolicy.CanApprove(readiness))
            {
                Debug.LogError("GBA XR libretro device certification: rejected; the reviewed fixture batch evidence is incomplete.");
                return;
            }

            GbaLibretroReadinessStore.RecordApprovedEvidence(
                GbaLibretroCertification.GalaxyXrDevice);
            Debug.Log("GBA XR libretro device certification: approved; Galaxy XR evidence recorded after reviewed fixture batch success.");
#endif
        }
    }
}

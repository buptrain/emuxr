using UnityEngine;

namespace GbaXr
{
    /// <summary>
    /// Explicit, default-off visual diagnostic override. Attach beside the
    /// frame driver only for controlled experimental builds. It writes no
    /// readiness evidence and never represents certification.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GbaLibretroDiagnosticPreview : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("UNRESOLVED DIAGNOSTIC ONLY: expose the libretro resolved-frame Core option without certification. Never use for normal play or approval.")]
        private bool enableUncertifiedAdapterPreview;

        internal bool IsEnabled => enableUncertifiedAdapterPreview;
    }
}

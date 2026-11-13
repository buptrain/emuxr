namespace GbaXr
{
    /// <summary>
    /// Experimental-only depth mapping for final visible PPU source buckets.
    /// The buckets do not contain occluded pixels, so this profile is never a
    /// faithful layer extraction and must be opt-in.
    /// </summary>
    public static class GbaForcePartitionProfile
    {
        /// <summary>
        /// Decides whether an explicitly selected ForcePartition presentation may use a
        /// visible-source lease. A visible partition is intrinsically an unsafe depth
        /// experiment: it is not an independent-PPU-layer extraction and will not
        /// restore pixels that were hidden by the original GBA composition.
        ///
        /// The caller must still reject malformed descriptors before passing
        /// <paramref name="hasStructurallyValidVisiblePartition"/>. The separate
        /// zero-parallax recomposition verifier remains valuable telemetry, but must
        /// not turn an explicit force selection into a silent resolved-frame fallback.
        /// </summary>
        internal static bool ShouldActivateExplicitForcePartition(
            bool forcePartitionRequested,
            bool hasStructurallyValidVisiblePartition) =>
            forcePartitionRequested && hasStructurallyValidVisiblePartition;

        public static bool TryGetDepth(int source, bool extended, out float localZ)
        {
            // The camera looks along +Z. Each GBA source receives a stable
            // authored band solely to make parallax visible during experiments.
            var backgroundSpacingMetres = extended ? 0.05f : 0.01f;
            localZ = source switch
            {
                5 => extended ? -0.05f : -0.02f, // OBJ
                1 => 0.000f,  // BG0
                2 => backgroundSpacingMetres, // BG1
                3 => backgroundSpacingMetres * 2f, // BG2
                4 => backgroundSpacingMetres * 3f, // BG3
                0 => backgroundSpacingMetres * 4f, // backdrop
                _ => 0f
            };
            return source >= 0 && source <= 5;
        }
    }
}

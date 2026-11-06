namespace GbaXr
{
    /// <summary>
    /// Experimental-only depth mapping for final visible PPU source buckets.
    /// The buckets do not contain occluded pixels, so this profile is never a
    /// faithful layer extraction and must be opt-in.
    /// </summary>
    public static class GbaUnsafeVisiblePartitionProfile
    {
        public static bool TryGetDepth(int source, out float localZ)
        {
            // The camera looks along +Z. Each GBA source receives a stable
            // authored band solely to make parallax visible during experiments.
            localZ = source switch
            {
                5 => -0.020f, // OBJ
                1 => 0.000f,  // BG0
                2 => 0.010f,  // BG1
                3 => 0.020f,  // BG2
                4 => 0.030f,  // BG3
                0 => 0.040f,  // backdrop
                _ => 0f
            };
            return source >= 0 && source <= 5;
        }
    }
}

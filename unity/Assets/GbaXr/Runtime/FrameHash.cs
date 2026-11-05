using System;

namespace GbaXr
{
    /// <summary>Stable hash for deterministic RGBA framebuffer tests.</summary>
    internal static class FrameHash
    {
        internal const uint Fnv1a32OffsetBasis = 2166136261u;
        internal const uint Fnv1a32Prime = 16777619u;

        internal static uint ComputeFnv1a32(byte[] bytes)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            var hash = Fnv1a32OffsetBasis;
            for (var index = 0; index < bytes.Length; index++)
            {
                hash ^= bytes[index];
                hash *= Fnv1a32Prime;
            }
            return hash;
        }
    }
}

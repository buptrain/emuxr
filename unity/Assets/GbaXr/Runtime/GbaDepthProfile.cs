using System.Collections.Generic;

namespace GbaXr
{
    /// <summary>
    /// The deliberately small, conservative depth model used by the first
    /// 2.5D profile. It accepts only raw independent candidates, never a
    /// final-visible partition.
    /// </summary>
    public static class GbaDepthProfile
    {
        private const float Bg0DepthMetres = 0.00f;
        private const float Bg1DepthMetres = 0.01f;
        private const float ObjDepthMetres = -0.02f;

        public static bool TryGetDepth(int source, uint priority, out float localZ)
        {
            localZ = 0f;
            if (priority > 3U)
            {
                return false;
            }

            // This first profile is intentionally limited to the raw
            // equal-priority candidate pairs proven by the native adapter.
            // The camera looks along +Z: positive is behind the display plane.
            localZ = source switch
            {
                1 => Bg0DepthMetres,
                2 => Bg1DepthMetres,
                5 => ObjDepthMetres,
                _ => float.NaN
            };
            if (float.IsNaN(localZ))
            {
                localZ = 0f;
                return false;
            }
            return true;
        }

        public static bool SupportsSourceSet(ISet<int> sources)
        {
            if (sources == null || sources.Count == 0 || !sources.Contains(1))
            {
                return false;
            }

            foreach (var source in sources)
            {
                if (!TryGetDepth(source, 0U, out _))
                {
                    return false;
                }
            }

            return true;
        }
    }
}

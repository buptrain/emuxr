using System;

namespace GbaXr
{
    /// <summary>
    /// Controls which ROMs may use an authored depth profile. Raw PPU
    /// candidates alone are not an authorization: a commercial ROM needs an
    /// explicit reviewed profile before it can leave resolved-frame mode.
    /// </summary>
    public static class GbaPresentationProfilePolicy
    {
        public static bool AllowsFirstDepthProfile(string romRelativePath)
        {
            return string.Equals(romRelativePath, "gba-fixtures/bg_tie.gba", StringComparison.Ordinal) ||
                   string.Equals(romRelativePath, "gba-fixtures/obj_priority_halfword.gba", StringComparison.Ordinal);
        }

        public static bool AllowsCandidateDepth(string romRelativePath, bool forceUnprofiledCandidates)
        {
            return forceUnprofiledCandidates || AllowsFirstDepthProfile(romRelativePath);
        }
    }
}

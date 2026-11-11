namespace GbaXr
{
    /// <summary>
    /// Pure, fixture-only policy for additional direct-mGBA frame evidence.
    /// It never participates in runtime certification or alters a golden.
    /// </summary>
    internal static class GbaDirectFrameSequenceSweepPolicy
    {
        internal static readonly int[] LeaseCheckpoints = { 30, 60, 120 };

        internal static bool ShouldStart(bool isSyntheticFixture, bool optIn, bool goldenMismatch) =>
            isSyntheticFixture && (optIn || goldenMismatch);

        internal static bool IsCheckpoint(int presentedLeaseCount)
        {
            foreach (var checkpoint in LeaseCheckpoints)
            {
                if (checkpoint == presentedLeaseCount) return true;
            }
            return false;
        }

        internal static string FormatCheckpoint(
            int presentedLeaseCount,
            uint hash,
            GbaLibretroFrameSequenceDiagnostic diagnostic)
        {
            return $"lease={presentedLeaseCount}/hash=0x{hash:X8}/mismatches={diagnostic.MismatchCount}/" +
                   $"first=({diagnostic.FirstX},{diagnostic.FirstY})/" +
                   $"expected={diagnostic.FirstExpectedRgba:X8}/actual={diagnostic.FirstActualRgba:X8}/" +
                   $"candidate={diagnostic.MatchingCandidate}";
        }
    }
}

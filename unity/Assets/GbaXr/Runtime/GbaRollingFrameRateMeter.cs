namespace GbaXr
{
    /// <summary>Main-thread rolling measurement of completed native frame leases.</summary>
    internal sealed class GbaRollingFrameRateMeter
    {
        internal const float RollingIntervalSeconds = 1f;

        private float _windowStartTime;
        private int _completedFrames;
        private bool _hasWindow;

        internal float FramesPerSecond { get; private set; }

        internal void Reset(float unscaledTime)
        {
            _windowStartTime = unscaledTime;
            _completedFrames = 0;
            _hasWindow = true;
            FramesPerSecond = 0f;
        }

        /// <summary>
        /// Records a completed lease and reports whether the displayed rolling
        /// rate changed. Callers can update diagnostic UI at this cadence rather
        /// than once per rendered frame.
        /// </summary>
        internal bool RecordCompletedLease(float unscaledTime)
        {
            if (!_hasWindow)
            {
                Reset(unscaledTime);
            }

            _completedFrames++;
            var elapsed = unscaledTime - _windowStartTime;
            if (elapsed < RollingIntervalSeconds)
            {
                return false;
            }

            FramesPerSecond = _completedFrames / elapsed;
            _completedFrames = 0;
            _windowStartTime = unscaledTime;
            return true;
        }
    }
}

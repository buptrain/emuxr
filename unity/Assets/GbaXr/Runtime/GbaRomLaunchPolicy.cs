using System;

namespace GbaXr
{
    public enum GbaRomLaunchState
    {
        Idle = 0,
        ValidatingSelection = 1,
        StoppingCurrentWorker = 2,
        StartingSelectedWorker = 3,
        Running = 4,
        FailedStopped = 5
    }

    /// <summary>Validated direct-mGBA launch target; no URI or ROM bytes cross this boundary.</summary>
    public readonly struct GbaRomLaunchRequest
    {
        internal GbaRomLaunchRequest(GbaImportedRomMetadata metadata, string absolutePath)
        {
            Metadata = metadata;
            AbsolutePath = absolutePath;
        }

        public GbaImportedRomMetadata Metadata { get; }
        public string AbsolutePath { get; }
    }

    /// <summary>Pure policy for validating and sequencing an explicit library launch.</summary>
    public static class GbaRomLaunchPolicy
    {
        public static bool TryCreateRequest(
            GbaAppPrivateStoragePaths paths,
            GbaImportedRomMetadata selected,
            Func<string, bool> fileExists,
            out GbaRomLaunchRequest request,
            out string error)
        {
            request = default;
            if (paths == null)
            {
                throw new ArgumentNullException(nameof(paths));
            }
            if (fileExists == null)
            {
                throw new ArgumentNullException(nameof(fileExists));
            }
            if (selected == null)
            {
                error = "Select a local ROM before launching.";
                return false;
            }
            if (!paths.TryResolveUserRomPath(selected.RelativePath, out var absolutePath))
            {
                error = "The selected library path is invalid.";
                return false;
            }
            if (!fileExists(absolutePath))
            {
                error = "The selected ROM file is missing from app-private storage.";
                return false;
            }

            request = new GbaRomLaunchRequest(selected, absolutePath);
            error = null;
            return true;
        }

        public static bool IsAllowedTransition(GbaRomLaunchState from, GbaRomLaunchState to)
        {
            return (from, to) switch
            {
                (GbaRomLaunchState.Idle, GbaRomLaunchState.ValidatingSelection) => true,
                (GbaRomLaunchState.Running, GbaRomLaunchState.ValidatingSelection) => true,
                (GbaRomLaunchState.FailedStopped, GbaRomLaunchState.ValidatingSelection) => true,
                (GbaRomLaunchState.ValidatingSelection, GbaRomLaunchState.StoppingCurrentWorker) => true,
                (GbaRomLaunchState.ValidatingSelection, GbaRomLaunchState.FailedStopped) => true,
                (GbaRomLaunchState.StoppingCurrentWorker, GbaRomLaunchState.StartingSelectedWorker) => true,
                (GbaRomLaunchState.StartingSelectedWorker, GbaRomLaunchState.Running) => true,
                (GbaRomLaunchState.StartingSelectedWorker, GbaRomLaunchState.FailedStopped) => true,
                _ => false
            };
        }
    }
}

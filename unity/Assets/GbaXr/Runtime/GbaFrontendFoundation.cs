using System;
using System.Collections.Generic;
using System.IO;

namespace GbaXr
{
    /// <summary>Observable state of the worker-to-Unity PCM handoff.</summary>
    public enum GbaFrontendAudioState
    {
        NotStarted = 0,
        Priming = 1,
        Active = 2,
        Underflowing = 3,
        Stopped = 4,
        Unavailable = 5
    }

    /// <summary>
    /// A transient audio snapshot. It is deliberately separate from core
    /// capabilities: an active core can still be priming or recovering audio.
    /// </summary>
    public readonly struct GbaFrontendAudioStatus
    {
        public GbaFrontendAudioStatus(
            GbaFrontendAudioState state,
            uint availableFrames,
            ulong nativeDroppedFrames)
        {
            State = state;
            AvailableFrames = availableFrames;
            NativeDroppedFrames = nativeDroppedFrames;
        }

        public GbaFrontendAudioState State { get; }
        public uint AvailableFrames { get; }
        public ulong NativeDroppedFrames { get; }
    }

    /// <summary>
    /// Immutable declaration of what the directly integrated mGBA frontend can
    /// currently do. It does not describe a libretro or generic-core loader.
    /// </summary>
    public sealed class GbaFrontendCapabilityManifest
    {
        internal const string DirectMgbaCoreId = "mgba-direct";
        internal const string DirectMgbaDisplayName = "mGBA (Direct)";

        private GbaFrontendCapabilityManifest()
        {
            CoreId = DirectMgbaCoreId;
            CoreDisplayName = DirectMgbaDisplayName;
            AbiVersion = GbaNativeBindings.AbiVersion;
            UsesDirectNativeMgbaWorker = true;
            SupportsResolvedFrameRenderer = true;
            SupportsFinalPixelPpuProvenance = true;
            SupportsVisibleSourcePartition = true;
            SupportsGeneralIndependentPpuCandidatePlanes = false;
            SupportsWorkerPcmPull = true;
            SupportsPersistentSaveRam = false;
            SupportsSaveStates = true;
        }

        public string CoreId { get; }
        public string CoreDisplayName { get; }
        public uint AbiVersion { get; }
        public bool UsesDirectNativeMgbaWorker { get; }
        public bool SupportsResolvedFrameRenderer { get; }
        public bool SupportsFinalPixelPpuProvenance { get; }
        public bool SupportsVisibleSourcePartition { get; }
        public bool SupportsGeneralIndependentPpuCandidatePlanes { get; }
        public bool SupportsWorkerPcmPull { get; }
        public bool SupportsPersistentSaveRam { get; }
        public bool SupportsSaveStates { get; }

        public static GbaFrontendCapabilityManifest CreateDirectMgba()
        {
            return new GbaFrontendCapabilityManifest();
        }
    }

    /// <summary>
    /// Runtime-facing direct-mGBA frontend state. Future document-picker,
    /// library, and save/state services can consume this model without changing
    /// the existing fixture staging or active ROM loading behavior.
    /// </summary>
    public sealed class GbaFrontendRuntimeModel
    {
        private GbaFrontendRuntimeModel(GbaFrontendCapabilityManifest capabilities)
        {
            Capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
            Audio = new GbaFrontendAudioStatus(GbaFrontendAudioState.NotStarted, 0U, 0U);
        }

        public GbaFrontendCapabilityManifest Capabilities { get; }
        public GbaFrontendAudioStatus Audio { get; private set; }

        public static GbaFrontendRuntimeModel CreateDirectMgba()
        {
            return new GbaFrontendRuntimeModel(GbaFrontendCapabilityManifest.CreateDirectMgba());
        }

        public void UpdateAudioStatus(GbaFrontendAudioStatus status)
        {
            Audio = status;
        }
    }

    /// <summary>
    /// Pure app-private layout for future user-ROM, save, state, firmware, and
    /// temporary-file services. Construction never creates or moves files.
    /// </summary>
    public sealed class GbaAppPrivateStoragePaths
    {
        private readonly string _romsPrefix;

        private GbaAppPrivateStoragePaths(string appPrivateRoot)
        {
            RootDirectory = Path.GetFullPath(appPrivateRoot);
            RomsDirectory = Path.Combine(RootDirectory, "roms");
            SavesDirectory = Path.Combine(RootDirectory, "saves");
            StatesDirectory = Path.Combine(RootDirectory, "states");
            SystemDirectory = Path.Combine(RootDirectory, "system");
            TempDirectory = Path.Combine(RootDirectory, "temp");
            _romsPrefix = EnsureTrailingDirectorySeparator(Path.GetFullPath(RomsDirectory));
        }

        public string RootDirectory { get; }
        public string RomsDirectory { get; }
        public string SavesDirectory { get; }
        public string StatesDirectory { get; }
        public string SystemDirectory { get; }
        public string TempDirectory { get; }

        public static GbaAppPrivateStoragePaths Create(string appPrivateRoot)
        {
            if (string.IsNullOrWhiteSpace(appPrivateRoot))
            {
                throw new ArgumentException("An app-private root directory is required.", nameof(appPrivateRoot));
            }

            return new GbaAppPrivateStoragePaths(appPrivateRoot);
        }

        /// <summary>
        /// Resolves a future user-ROM library relative path beneath <see
        /// cref="RomsDirectory"/>. Absolute paths, parent traversal, and
        /// platform-ambiguous names are rejected. Existing fixture and staged
        /// ROM paths intentionally do not use this policy yet.
        /// </summary>
        public bool TryResolveUserRomPath(string relativePath, out string absolutePath)
        {
            absolutePath = null;
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return false;
            }

            var normalizedInput = relativePath.Replace('\\', '/');
            if (normalizedInput.StartsWith("/", StringComparison.Ordinal) ||
                normalizedInput.EndsWith("/", StringComparison.Ordinal) ||
                Path.IsPathRooted(relativePath))
            {
                return false;
            }

            var safeSegments = new List<string>();
            foreach (var segment in normalizedInput.Split('/'))
            {
                if (segment.Length == 0 || segment == ".")
                {
                    continue;
                }
                if (segment == ".." || string.IsNullOrWhiteSpace(segment) ||
                    segment.IndexOf(':') >= 0 ||
                    segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    return false;
                }

                safeSegments.Add(segment);
            }

            if (safeSegments.Count == 0)
            {
                return false;
            }

            var normalizedRelativePath = string.Join(Path.DirectorySeparatorChar.ToString(), safeSegments);
            var candidate = Path.GetFullPath(Path.Combine(RomsDirectory, normalizedRelativePath));
            if (!candidate.StartsWith(_romsPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            absolutePath = candidate;
            return true;
        }

        private static string EnsureTrailingDirectorySeparator(string path)
        {
            return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? path
                : path + Path.DirectorySeparatorChar;
        }
    }
}

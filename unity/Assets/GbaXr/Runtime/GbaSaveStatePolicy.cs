using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace GbaXr
{
    /// <summary>Pure app-private save-state slot naming and validation.</summary>
    internal static class GbaSaveStatePolicy
    {
        internal const int QuickSlot = 0;
        private const int MaximumSlots = 9;

        internal static bool TryCreateSlotPath(
            GbaAppPrivateStoragePaths paths,
            string romIdentity,
            int slot,
            out string absolutePath,
            out string error)
        {
            absolutePath = null;
            error = null;
            if (paths == null || slot < 0 || slot > MaximumSlots)
            {
                error = "The requested state slot is not supported.";
                return false;
            }

            var stateKey = CreateStateKey(romIdentity);
            if (stateKey == null)
            {
                error = "The active ROM has no safe state identity.";
                return false;
            }

            absolutePath = Path.Combine(paths.StatesDirectory, stateKey, $"slot-{slot}.ss0");
            return true;
        }

        internal static string CreateStateKey(string romIdentity)
        {
            if (string.IsNullOrWhiteSpace(romIdentity))
            {
                return null;
            }

            // Imported ROMs use their content hash directly. Staged fixtures
            // derive a stable non-path key, so no user-provided filename is
            // ever interpreted as a state directory.
            const string contentPrefix = "sha256:";
            if (romIdentity.StartsWith(contentPrefix, StringComparison.Ordinal))
            {
                if (romIdentity.Length != contentPrefix.Length + 64)
                {
                    return null;
                }

                var hash = romIdentity.Substring(contentPrefix.Length);
                for (var index = 0; index < hash.Length; index++)
                {
                    var character = hash[index];
                    if (!((character >= '0' && character <= '9') ||
                          (character >= 'a' && character <= 'f')))
                    {
                        return null;
                    }
                }
                return "sha256-" + hash;
            }

            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes("staged-rom:" + romIdentity);
            var hashBytes = sha256.ComputeHash(bytes);
            return "staged-" + BitConverter.ToString(hashBytes).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}

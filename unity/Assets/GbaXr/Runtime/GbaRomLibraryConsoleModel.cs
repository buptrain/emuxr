using System;
using System.Collections.Generic;

namespace GbaXr
{
    public enum GbaRomLibraryConsoleAction
    {
        None = 0,
        Import = 1,
        LaunchSelected = 2,
        LaunchUnavailable = 3,
        Back = 4
    }

    /// <summary>
    /// Pure controller-navigation state for the local ROM-library console
    /// subview. Launch is available only for an explicit selected entry; the
    /// driver owns actual worker lifecycle and file revalidation.
    /// </summary>
    public sealed class GbaRomLibraryConsoleModel
    {
        private IReadOnlyList<GbaImportedRomMetadata> _entries = Array.Empty<GbaImportedRomMetadata>();
        private int _selectedRow;
        private int _selectedEntryIndex = -1;

        public bool IsOpen { get; private set; }
        public int SelectedRow => _selectedRow;
        public int EntryCount => _entries.Count;
        public int RowCount => EntryCount + 3;
        public GbaImportedRomMetadata SelectedEntry =>
            _selectedEntryIndex >= 0 && _selectedEntryIndex < EntryCount
                ? _entries[_selectedEntryIndex]
                : null;

        // Rows are fixed around the variable catalog entries.
        public int ImportRow => 0;
        public int FirstEntryRow => 1;
        public int LaunchRow => EntryCount + 1;
        public int BackRow => EntryCount + 2;

        public GbaImportedRomMetadata GetEntry(int index)
        {
            if (index < 0 || index >= EntryCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            return _entries[index];
        }

        public void Open(IReadOnlyList<GbaImportedRomMetadata> entries)
        {
            IsOpen = true;
            _selectedRow = ImportRow;
            _selectedEntryIndex = -1;
            UpdateEntries(entries);
        }

        public void Close()
        {
            IsOpen = false;
        }

        public void UpdateEntries(IReadOnlyList<GbaImportedRomMetadata> entries)
        {
            var previousStableId = SelectedEntry?.StableId;
            _entries = entries ?? Array.Empty<GbaImportedRomMetadata>();
            _selectedEntryIndex = -1;
            if (previousStableId != null)
            {
                for (var index = 0; index < _entries.Count; index++)
                {
                    if (string.Equals(_entries[index].StableId, previousStableId, StringComparison.Ordinal))
                    {
                        _selectedEntryIndex = index;
                        break;
                    }
                }
            }
            _selectedRow = Math.Min(_selectedRow, BackRow);
        }

        public void MoveSelection(int direction)
        {
            if (!IsOpen)
            {
                throw new InvalidOperationException("Open the ROM library before navigating it.");
            }
            if (direction == 0)
            {
                return;
            }

            _selectedRow = (_selectedRow + direction) % RowCount;
            if (_selectedRow < 0)
            {
                _selectedRow += RowCount;
            }
            if (_selectedRow >= FirstEntryRow && _selectedRow < LaunchRow)
            {
                _selectedEntryIndex = _selectedRow - FirstEntryRow;
            }
        }

        public GbaRomLibraryConsoleAction ActivateSelection()
        {
            if (!IsOpen)
            {
                throw new InvalidOperationException("Open the ROM library before activating it.");
            }
            if (_selectedRow == ImportRow)
            {
                return GbaRomLibraryConsoleAction.Import;
            }
            if (_selectedRow == LaunchRow)
            {
                return SelectedEntry == null
                    ? GbaRomLibraryConsoleAction.LaunchUnavailable
                    : GbaRomLibraryConsoleAction.LaunchSelected;
            }
            if (_selectedRow == BackRow)
            {
                return GbaRomLibraryConsoleAction.Back;
            }
            return GbaRomLibraryConsoleAction.None;
        }
    }

    /// <summary>Maps import state to compact, non-technical console feedback.</summary>
    public static class GbaRomImportPresentationPolicy
    {
        public static string Describe(
            bool importInFlight,
            GbaRomImportOutcomeKind? lastOutcomeKind,
            string lastMessage)
        {
            if (importInFlight)
            {
                return "Choose a .gba file in the Android picker.";
            }
            if (!lastOutcomeKind.HasValue)
            {
                return "Choose Load ROM to select a .gba file.";
            }

            var message = string.IsNullOrWhiteSpace(lastMessage) ? "No further details." : lastMessage;
            return lastOutcomeKind.Value switch
            {
                GbaRomImportOutcomeKind.Success => "Selected: " + message,
                GbaRomImportOutcomeKind.Duplicate => "Selected: " + message,
                GbaRomImportOutcomeKind.Cancelled => "Selection cancelled.",
                _ => "Selection failed: " + message
            };
        }
    }
}

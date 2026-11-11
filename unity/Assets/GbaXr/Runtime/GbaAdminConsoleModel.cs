using System;
using System.Collections.Generic;

namespace GbaXr
{
    public enum GbaAdminOperation
    {
        ReloadCurrentRom,
        LoadStagedRom,
        ImportRom,
        SaveState,
        LoadState,
        SetPresentationMode,
        SetPlacementMode,
        ResetScreenTransform
    }

    /// <summary>
    /// UI-independent console state. The future world-space Canvas edits this
    /// model, then passes a committed transaction to the Unity/native layers.
    /// </summary>
    public sealed class GbaAdminConsoleModel
    {
        private static readonly GbaAdminOperation[] Operations =
        {
            GbaAdminOperation.ReloadCurrentRom,
            GbaAdminOperation.LoadStagedRom,
            GbaAdminOperation.ImportRom,
            GbaAdminOperation.SaveState,
            GbaAdminOperation.LoadState,
            GbaAdminOperation.SetPresentationMode,
            GbaAdminOperation.SetPlacementMode,
            GbaAdminOperation.ResetScreenTransform
        };

        private readonly HashSet<GbaAdminOperation> _requestedOperations = new();
        private GbaAdminConsoleSettings _active;
        private GbaAdminConsoleSettings _draft;

        public GbaAdminConsoleModel(GbaAdminConsoleSettings initialSettings)
        {
            _active = (initialSettings ?? throw new ArgumentNullException(nameof(initialSettings))).Clone();
            _draft = _active.Clone();
        }

        public bool IsOpen { get; private set; }
        public bool ForwardsGameplayInput => !IsOpen;
        public IReadOnlyList<GbaAdminOperation> AvailableOperations => Operations;
        public GbaAdminConsoleSettings Active => _active.Clone();
        public GbaAdminConsoleSettings Draft => _draft.Clone();

        public void Open()
        {
            _draft = _active.Clone();
            _requestedOperations.Clear();
            IsOpen = true;
        }

        public void SetDraftPresentationMode(GbaNativeFrameDriver.PresentationMode presentationMode)
        {
            if (!IsOpen)
            {
                throw new InvalidOperationException("Open the admin console before editing its draft settings.");
            }
            _draft.PresentationMode = presentationMode;
        }

        public void SetDraftPlacementMode(GbaScreenPlacementMode placementMode)
        {
            if (!IsOpen)
            {
                throw new InvalidOperationException("Open the admin console before editing its draft settings.");
            }
            _draft.PlacementMode = placementMode;
        }

        public void SetDraftRequestedCore(GbaCoreKind core)
        {
            if (!IsOpen) throw new InvalidOperationException("Open the admin console before editing its draft settings.");
            _draft.RequestedCore = core;
        }

        public void Request(GbaAdminOperation operation)
        {
            if (!IsOpen)
            {
                throw new InvalidOperationException("Open the admin console before requesting an operation.");
            }
            _requestedOperations.Add(operation);
        }

        public GbaAdminConsoleTransaction Apply()
        {
            if (!IsOpen)
            {
                throw new InvalidOperationException("Open the admin console before applying its draft settings.");
            }

            var transaction = new GbaAdminConsoleTransaction(_draft, _requestedOperations);
            _active = _draft.Clone();
            _requestedOperations.Clear();
            return transaction;
        }

        public void Close()
        {
            _draft = _active.Clone();
            _requestedOperations.Clear();
            IsOpen = false;
        }
    }

    public sealed class GbaAdminConsoleSettings
    {
        public GbaNativeFrameDriver.PresentationMode PresentationMode;
        public GbaScreenPlacementMode PlacementMode;
        public GbaCoreKind RequestedCore;

        public GbaAdminConsoleSettings Clone()
        {
            return new GbaAdminConsoleSettings
            {
                PresentationMode = PresentationMode,
                PlacementMode = PlacementMode,
                RequestedCore = RequestedCore
            };
        }
    }

    public sealed class GbaAdminConsoleTransaction
    {
        private readonly HashSet<GbaAdminOperation> _operations;

        internal GbaAdminConsoleTransaction(GbaAdminConsoleSettings settings, IEnumerable<GbaAdminOperation> operations)
        {
            Settings = settings.Clone();
            _operations = new HashSet<GbaAdminOperation>(operations);
        }

        public GbaAdminConsoleSettings Settings { get; }
        public bool Includes(GbaAdminOperation operation) => _operations.Contains(operation);
    }

}

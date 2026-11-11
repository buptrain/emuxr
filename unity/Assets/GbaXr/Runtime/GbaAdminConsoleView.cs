using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GbaXr
{
    /// <summary>World-space, controller-driven admin panel for Android XR.</summary>
    public sealed class GbaAdminConsoleView : MonoBehaviour
    {
        private Canvas _canvas;
        private Text _body;
        private Button _applyButton;
        private Button _closeButton;
        private Action _apply;
        private Action _close;

        public void Initialize(Action apply, Action close)
        {
            _apply = apply ?? throw new ArgumentNullException(nameof(apply));
            _close = close ?? throw new ArgumentNullException(nameof(close));
            Build();
            Hide();
        }

        public void Show(
            GbaAdminConsoleSettings settings,
            int workerState,
            float measuredFramesPerSecond,
            int selectedRow,
            int libraryEntryCount,
            string importStatus,
            string stateStatus,
            bool coreSelectionAvailable = false,
            bool unresolvedDiagnosticPreview = false)
        {
            _canvas.worldCamera = Camera.main;
            gameObject.SetActive(true);
            Refresh(settings, workerState, measuredFramesPerSecond, selectedRow, libraryEntryCount, importStatus, stateStatus, coreSelectionAvailable, unresolvedDiagnosticPreview);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        public void Refresh(
            GbaAdminConsoleSettings settings,
            int workerState,
            float measuredFramesPerSecond,
            int selectedRow,
            int libraryEntryCount,
            string importStatus,
            string stateStatus,
            bool coreSelectionAvailable = false,
            bool unresolvedDiagnosticPreview = false)
        {
            SetMainControlsVisible(true);
            var rows = new List<string>
            {
                $"Measured emulation FPS: {measuredFramesPerSecond:F1} (real-time, read-only)",
                $"Presentation: {settings.PresentationMode}   ◀ / ▶",
                $"Screen placement: {settings.PlacementMode}   ◀ / ▶"
            };
            if (coreSelectionAvailable)
            {
                rows.Add(unresolvedDiagnosticPreview
                    ? $"Core: {settings.RequestedCore}   ◀ / ▶   [UNRESOLVED DIAGNOSTIC]"
                    : $"Core: {settings.RequestedCore}   ◀ / ▶");
            }
            rows.AddRange(new[]
            {
                "Load ROM…",
                "Apply changes",
                "Close"
            });
            for (var index = 0; index < rows.Count; index++)
            {
                rows[index] = (index == selectedRow ? "▶ " : "   ") + rows[index];
            }

            _body.text =
                $"Worker state: {workerState}\n\n" +
                string.Join("\n\n", rows) +
                $"\n\nROM selection: {importStatus}\n\n" +
                $"State status: {stateStatus}\n\n" +
                (unresolvedDiagnosticPreview
                    ? "WARNING: libretro Core preview is uncertified diagnostic output only. It does not establish parity or Galaxy XR approval.\n\n"
                    : string.Empty) +
                "D-pad: select/change presentation or placement   A: activate   B: close\n" +
                "Load ROM opens the Android file picker. Save/load slots are available from the Parallax Emu top menu.\n" +
                "Plane Anchoring: pinch the screen, drag it over a floor or table, then release to attach.\n" +
                "Hand: pinch with both hands to scale while dragging.\n" +
                "ForcePartition modes are unsafe visual experiments.\n" +
                "Open this Advanced Console from Settings in the Parallax Emu menu. Plane placement is hand-only.";
        }

        public void ShowLibrary(
            GbaRomLibraryConsoleModel library,
            int workerState,
            string importStatus,
            string currentRomLabel,
            string launchStatus)
        {
            _canvas.worldCamera = Camera.main;
            gameObject.SetActive(true);
            RefreshLibrary(library, workerState, importStatus, currentRomLabel, launchStatus);
        }

        public void RefreshLibrary(
            GbaRomLibraryConsoleModel library,
            int workerState,
            string importStatus,
            string currentRomLabel,
            string launchStatus)
        {
            SetMainControlsVisible(false);
            var rows = new List<string> { "Import ROM…" };
            for (var index = 0; index < library.EntryCount; index++)
            {
                var entry = library.GetEntry(index);
                rows.Add($"{index + 1}. {entry.DisplayName}  ({entry.SizeBytes / 1024L} KiB)");
            }
            rows.Add(library.SelectedEntry == null
                ? "Launch selected ROM  [select a ROM first]"
                : "Launch selected ROM  [A: explicitly switch worker]");
            rows.Add("Back to admin console");
            for (var index = 0; index < rows.Count; index++)
            {
                rows[index] = (index == library.SelectedRow ? "▶ " : "   ") + rows[index];
            }

            var selected = library.SelectedEntry;
            var detail = selected == null
                ? "Selected ROM: none"
                : $"Selected ROM: {selected.DisplayName}\nOriginal: {selected.OriginalFileName}\n" +
                  $"SHA-256: {ShortHash(selected.ContentHashSha256)}\nPath: roms/{selected.RelativePath}";
            _body.text =
                $"LOCAL ROM LIBRARY · Worker state: {workerState}\n\n" +
                string.Join("\n\n", rows) +
                $"\n\nActive ROM: {currentRomLabel}\nLaunch: {launchStatus}\n\n" +
                $"{detail}\n\nROM import: {importStatus}\n\n" +
                "D-pad: select   A: import/choose action   B: return to admin console\n" +
                "Launch runs only after explicit A on this row. Import never replaces the active worker automatically.";
        }

        private void Build()
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.sortingOrder = 500;
            gameObject.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 12f;
            gameObject.AddComponent<GraphicRaycaster>();

            var root = (RectTransform)transform;
            root.sizeDelta = new Vector2(980f, 700f);
            root.localPosition = new Vector3(0f, 0f, -0.04f);
            root.localScale = Vector3.one * 0.00125f;

            var panel = CreatePanel("Panel", transform, new Color(0.025f, 0.045f, 0.09f, 0.96f));
            Stretch(panel.rectTransform, 0f, 0f, 1f, 1f, 0f, 0f, 0f, 0f);

            var title = CreateText("Title", panel.transform, 30, TextAnchor.MiddleLeft);
            title.text = "GBA XR ADMIN CONSOLE";
            Stretch(title.rectTransform, 0f, 0.87f, 1f, 1f, 28f, -24f, -28f, 0f);

            _body = CreateText("Body", panel.transform, 19, TextAnchor.UpperLeft);
            Stretch(_body.rectTransform, 0f, 0.15f, 1f, 0.86f, 30f, 0f, -30f, 0f);

            _applyButton = CreateButton("Apply", panel.transform, "APPLY", new Color(0.08f, 0.36f, 0.26f, 1f));
            Stretch(_applyButton.GetComponent<RectTransform>(), 0.54f, 0.03f, 0.75f, 0.12f, 0f, 0f, 0f, 0f);
            _applyButton.onClick.AddListener(() => _apply());

            _closeButton = CreateButton("Close", panel.transform, "CLOSE", new Color(0.28f, 0.10f, 0.12f, 1f));
            Stretch(_closeButton.GetComponent<RectTransform>(), 0.77f, 0.03f, 0.97f, 0.12f, 0f, 0f, 0f, 0f);
            _closeButton.onClick.AddListener(() => _close());
        }

        private void SetMainControlsVisible(bool visible)
        {
            if (_applyButton != null)
            {
                _applyButton.gameObject.SetActive(visible);
            }
            if (_closeButton != null)
            {
                _closeButton.gameObject.SetActive(visible);
            }
        }

        private static string ShortHash(string hash)
        {
            return string.IsNullOrEmpty(hash) || hash.Length < 12 ? "unavailable" : hash.Substring(0, 12) + "…";
        }

        private static Image CreatePanel(string name, Transform parent, Color color)
        {
            var panel = new GameObject(name, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            var image = panel.GetComponent<Image>();
            image.color = color;
            return image;
        }

        private static Text CreateText(string name, Transform parent, int fontSize, TextAnchor alignment)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            var text = textObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.color = new Color(0.9f, 0.95f, 1f, 1f);
            return text;
        }

        private static Button CreateButton(string name, Transform parent, string label, Color color)
        {
            var buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            buttonObject.GetComponent<Image>().color = color;
            var button = buttonObject.GetComponent<Button>();
            var text = CreateText("Label", buttonObject.transform, 20, TextAnchor.MiddleCenter);
            text.text = label;
            Stretch(text.rectTransform, 0f, 0f, 1f, 1f, 0f, 0f, 0f, 0f);
            return button;
        }

        private static void Stretch(RectTransform transform, float minX, float minY, float maxX, float maxY,
                                    float left, float bottom, float right, float top)
        {
            transform.anchorMin = new Vector2(minX, minY);
            transform.anchorMax = new Vector2(maxX, maxY);
            transform.offsetMin = new Vector2(left, bottom);
            transform.offsetMax = new Vector2(right, top);
        }
    }
}

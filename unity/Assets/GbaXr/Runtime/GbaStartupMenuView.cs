using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GbaXr
{
    /// <summary>World-space, controller-rendered startup menu; no raycast interaction is required.</summary>
    public sealed class GbaStartupMenuView : MonoBehaviour
    {
        private Canvas _canvas;
        private Text _title;
        private Text _subtitle;
        private Text _hint;
        private Image _controllerReference;
        private Text _controllerReferenceLabels;
        private readonly List<Image> _cards = new();
        private readonly List<Text> _cardText = new();

        public void Initialize()
        {
            Build();
            Hide();
        }

        internal void Show(GbaStartupMenuModel model, bool libretroAvailable)
        {
            _canvas.worldCamera = Camera.main;
            gameObject.SetActive(true);
            Refresh(model, libretroAvailable);
        }

        public void Hide() => gameObject.SetActive(false);

        internal void Refresh(GbaStartupMenuModel model, bool libretroAvailable)
        {
            if (model == null) return;
            _canvas.worldCamera = Camera.main;
            var rows = BuildRows(model, libretroAvailable, out var detail);
            EnsureCards(rows.Count);
            _title.text = "Parallax Emu";
            _subtitle.text = model.Page == GbaStartupMenuPage.Root
                ? "Your GBA, in depth."
                : PageTitle(model.Page);
            _hint.text = detail + "\n\nD-pad: navigate   A: select   B: back   R3: menu";
            var mapping = model.Page == GbaStartupMenuPage.ControllerMapping;
            _controllerReference.gameObject.SetActive(mapping);
            _controllerReferenceLabels.gameObject.SetActive(mapping);
            var stateSlots = model.Page == GbaStartupMenuPage.StateSave || model.Page == GbaStartupMenuPage.StateLoad;
            if (stateSlots)
            {
                LayoutStateSlotCards();
            }
            else if (mapping)
            {
                _controllerReference.color = model.IsCapturingController
                    ? new Color(0.12f, 0.43f, 0.9f, 0.95f) : new Color(0.22f, 0.43f, 0.7f, 0.9f);
                _controllerReferenceLabels.text = model.IsCapturingController
                    ? $"L      [ D-PAD ]        [ A B X Y ]      R\n          Target {model.CaptureIndex + 1} / 10: {model.CaptureControl}"
                    : "L      [ D-PAD ]        [ A B X Y ]      R\n             Menu / View       R3 reserved";
                LayoutMappingCards();
            }
            else LayoutDefaultCards();
            for (var index = 0; index < _cards.Count; index++)
            {
                var visible = index < rows.Count;
                _cards[index].gameObject.SetActive(visible);
                if (!visible) continue;
                var selected = index == model.SelectedIndex;
                _cards[index].color = selected
                    ? new Color(0.12f, 0.43f, 0.9f, 1f)
                    : new Color(0.94f, 0.98f, 1f, 0.98f);
                _cardText[index].color = selected ? Color.white : new Color(0.07f, 0.19f, 0.36f, 1f);
                _cardText[index].text = (selected ? "▶  " : string.Empty) + rows[index];
            }
        }

        private static List<string> BuildRows(GbaStartupMenuModel model, bool libretroAvailable, out string detail)
        {
            detail = string.Empty;
            switch (model.Page)
            {
                case GbaStartupMenuPage.Root:
                    return new List<string>(GbaStartupMenuModel.RootItems);
                case GbaStartupMenuPage.Core:
                    detail = libretroAvailable
                        ? "Choose a core, then confirm with A. A running ROM switches only after this explicit confirmation."
                        : "Direct mGBA is available. The experimental core is locked pending certification.";
                    var cores = new List<string>();
                    foreach (var core in model.CoreItems)
                    {
                        cores.Add(core == GbaCoreKind.DirectMgba ? "Direct mGBA" : "mGBA libretro (experimental)");
                    }
                    return cores;
                case GbaStartupMenuPage.Settings:
                    return new List<string>(GbaStartupMenuModel.SettingsItems);
                case GbaStartupMenuPage.PresentationMode:
                    detail = "Original preserves the resolved GBA screen. Extended Parallax is an experimental 2.5D view with wider layer spacing and head-motion parallax.";
                    return new List<string> { "Original", "Extended Parallax" };
                case GbaStartupMenuPage.StateSave:
                    detail = "Choose one of eight ROM-specific save slots.";
                    return new List<string>(model.StateSlots);
                case GbaStartupMenuPage.StateLoad:
                    detail = "Choose an existing ROM-specific save slot to load.";
                    return new List<string>(model.StateSlots);
                case GbaStartupMenuPage.ControllerMapping:
                    detail = model.IsCapturingController
                        ? $"[  ◀  ●  ▶ ]\n[ D-pad ]  [ A B X Y ]\nTarget: {model.CaptureControl}\nRelease all buttons, then press one. R3 cancels."
                        : model.IsReviewingController
                            ? "[  ◀  ●  ▶ ]\n[ D-pad ]  [ A B X Y ]\nReview completed draft. Confirm or discard. R3 discards."
                            : "[  ◀  ●  ▶ ]\n[ D-pad ]  [ A B X Y ]\nOriginal controller reference. R3 is reserved.";
                    return model.IsCapturingController ? new List<string>() : model.IsReviewingController
                        ? new List<string> { "Confirm changes", "Discard changes" }
                        : new List<string> { "Start calibration" };
                case GbaStartupMenuPage.ViewMode:
                    detail = "Virtual Field keeps the screen in the virtual scene. Plane Anchoring enables hand placement on a detected floor or table.";
                    return new List<string> { "Virtual Field", "Plane Anchoring" };
                default:
                    return new List<string>();
            }
        }

        private static string PageTitle(GbaStartupMenuPage page) => page switch
        {
            GbaStartupMenuPage.Core => "Core Selection",
            GbaStartupMenuPage.Settings => "Settings",
            GbaStartupMenuPage.ControllerMapping => "Controller Mapping",
            GbaStartupMenuPage.PresentationMode => "Presentation Mode",
            GbaStartupMenuPage.ViewMode => "View Mode",
            GbaStartupMenuPage.StateSave => "Save State",
            GbaStartupMenuPage.StateLoad => "Load State",
            _ => "Parallax Emu"
        };

        private void Build()
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.sortingOrder = 510;
            gameObject.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 14f;
            var root = (RectTransform)transform;
            root.sizeDelta = new Vector2(920f, 620f);
            root.localPosition = new Vector3(0f, 0f, -0.05f);
            root.localScale = Vector3.one * 0.00135f;

            var panel = CreateImage("Panel", transform, new Color(0.84f, 0.93f, 1f, 0.98f));
            Stretch(panel.rectTransform, 0f, 0f, 1f, 1f, 0f, 0f, 0f, 0f);
            _title = CreateText("Title", panel.transform, 46, TextAnchor.MiddleLeft, new Color(0.05f, 0.2f, 0.43f));
            Stretch(_title.rectTransform, 0f, .84f, 1f, 1f, 46f, -18f, -46f, 0f);
            _subtitle = CreateText("Subtitle", panel.transform, 22, TextAnchor.MiddleLeft, new Color(0.19f, 0.39f, 0.63f));
            Stretch(_subtitle.rectTransform, 0f, .76f, 1f, .86f, 48f, 0f, -48f, 0f);
            _hint = CreateText("Hint", panel.transform, 17, TextAnchor.UpperLeft, new Color(0.12f, 0.25f, 0.43f));
            Stretch(_hint.rectTransform, 0f, .03f, 1f, .22f, 48f, 0f, -48f, 0f);
            _controllerReference = CreateImage("Original Controller Reference", panel.transform, new Color(0.22f, .43f, .7f, .9f));
            Stretch(_controllerReference.rectTransform, .13f, .25f, .87f, .48f, 0f, 0f, 0f, 0f);
            _controllerReferenceLabels = CreateText("Controller Labels", _controllerReference.transform, 22, TextAnchor.MiddleCenter, Color.white);
            Stretch(_controllerReferenceLabels.rectTransform, 0f, 0f, 1f, 1f, 8f, 0f, -8f, 0f);
        }

        private void EnsureCards(int count)
        {
            while (_cards.Count < count)
            {
                var card = CreateImage("Menu Card", transform, Color.white);
                var text = CreateText("Label", card.transform, 29, TextAnchor.MiddleLeft, Color.white);
                Stretch(text.rectTransform, 0f, 0f, 1f, 1f, 30f, 0f, -30f, 0f);
                _cards.Add(card);
                _cardText.Add(text);
            }
            LayoutDefaultCards();
        }

        private void LayoutDefaultCards()
        {
            for (var index = 0; index < _cards.Count; index++)
            { var top = .70f - index * .145f; Stretch(_cards[index].rectTransform, .055f, top - .115f, .945f, top, 0f, 0f, 0f, 0f); }
            if (_controllerReference != null) Stretch(_controllerReference.rectTransform, .13f, .25f, .87f, .48f, 0f, 0f, 0f, 0f);
        }

        private void LayoutMappingCards()
        {
            Stretch(_controllerReference.rectTransform, .13f, .49f, .87f, .70f, 0f, 0f, 0f, 0f);
            for (var index = 0; index < _cards.Count; index++)
            { var top = .45f - index * .075f; Stretch(_cards[index].rectTransform, .055f, top - .06f, .945f, top, 0f, 0f, 0f, 0f); }
        }

        private void LayoutStateSlotCards()
        {
            for (var index = 0; index < _cards.Count; index++)
            {
                var column = index % 2;
                var row = index / 2;
                var top = .70f - row * .105f;
                Stretch(_cards[index].rectTransform, column == 0 ? .055f : .51f, top - .08f,
                    column == 0 ? .49f : .945f, top, 0f, 0f, 0f, 0f);
            }
        }

        private static Image CreateImage(string name, Transform parent, Color color)
        {
            var value = new GameObject(name, typeof(RectTransform), typeof(Image));
            value.transform.SetParent(parent, false);
            var image = value.GetComponent<Image>();
            image.color = color;
            return image;
        }

        private static Text CreateText(string name, Transform parent, int size, TextAnchor anchor, Color color)
        {
            var value = new GameObject(name, typeof(RectTransform), typeof(Text));
            value.transform.SetParent(parent, false);
            var text = value.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.color = color;
            return text;
        }

        private static void Stretch(RectTransform target, float minX, float minY, float maxX, float maxY,
                                    float left, float bottom, float right, float top)
        {
            target.anchorMin = new Vector2(minX, minY);
            target.anchorMax = new Vector2(maxX, maxY);
            target.offsetMin = new Vector2(left, bottom);
            target.offsetMax = new Vector2(right, top);
        }
    }
}

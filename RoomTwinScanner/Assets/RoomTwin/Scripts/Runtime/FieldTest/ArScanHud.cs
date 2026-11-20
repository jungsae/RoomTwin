using RoomTwin.Geometry;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RoomTwin.AR
{
    /// <summary>
    /// Compact scan HUD. The center of the screen stays clear for the camera.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArScanHud : MonoBehaviour
    {
        const float BarHeight = 112f;
        const float ButtonSize = 144f;

        RectTransform safeRoot;
        RectTransform topBar;
        RectTransform bottomBar;
        RectTransform resetButton;
        RectTransform developerButton;
        RectTransform debugPanel;
        LabelSlot headline;
        LabelSlot guidance;
        LabelSlot progress;
        LabelSlot debug;
        bool developerVisible;
        ScanPhase shownPhase;
        int shownCorners = -1;
        string shownFailure;
        bool shownDeveloper;
        Rect lastSafeArea;

        public void EnsureBuilt()
        {
            if (safeRoot != null)
            {
                return;
            }

            var canvasObject = new GameObject("ScanHud");
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 2400f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();

            var rootObject = new GameObject("SafeArea");
            safeRoot = rootObject.AddComponent<RectTransform>();
            safeRoot.SetParent(canvasObject.transform, false);
            safeRoot.anchorMin = Vector2.zero;
            safeRoot.anchorMax = Vector2.one;
            safeRoot.offsetMin = Vector2.zero;
            safeRoot.offsetMax = Vector2.zero;

            topBar = CreateBar("Top", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), BarHeight);
            headline = CreateLabel(topBar, 36f, TextAlignmentOptions.Center, FontStyles.Bold);

            bottomBar = CreateBar("Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), 220f);
            guidance = CreateLabel(bottomBar, 32f, TextAlignmentOptions.Center, FontStyles.Normal);
            progress = CreateLabel(bottomBar, 28f, TextAlignmentOptions.Center, FontStyles.Bold);
            var guidanceRect = guidance.Rect;
            guidanceRect.anchorMin = new Vector2(0.04f, 0.42f);
            guidanceRect.anchorMax = new Vector2(0.72f, 0.92f);
            guidanceRect.offsetMin = Vector2.zero;
            guidanceRect.offsetMax = Vector2.zero;
            var progressRect = progress.Rect;
            progressRect.anchorMin = new Vector2(0.04f, 0.08f);
            progressRect.anchorMax = new Vector2(0.72f, 0.4f);
            progressRect.offsetMin = Vector2.zero;
            progressRect.offsetMax = Vector2.zero;

            resetButton = CreateButton(safeRoot, "Reset");
            developerButton = CreateButton(safeRoot, "개발");
            developerButton.anchorMin = new Vector2(0f, 0f);
            developerButton.anchorMax = new Vector2(0f, 0f);
            developerButton.pivot = new Vector2(0f, 0f);
            developerButton.sizeDelta = new Vector2(120f, 72f);
            developerButton.anchoredPosition = new Vector2(24f, 24f);
            developerButton.GetComponent<Button>().onClick.AddListener(ToggleDeveloper);

            debugPanel = CreateBar("Debug", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), 280f);
            debugPanel.anchoredPosition = new Vector2(0f, -BarHeight - 8f);
            debug = CreateLabel(debugPanel, 24f, TextAlignmentOptions.TopLeft, FontStyles.Normal);
            debugPanel.gameObject.SetActive(false);
            ApplySafeArea();
        }

        public void Apply(in ScanSnapshot snapshot, string failure, string debugText)
        {
            EnsureBuilt();
            ApplySafeArea();
            ScanPhase phase = ScanFeedback.Resolve(snapshot);
            if (phase == shownPhase && snapshot.CornerCount == shownCorners && failure == shownFailure && developerVisible == shownDeveloper)
            {
                return;
            }

            shownPhase = phase;
            shownCorners = snapshot.CornerCount;
            shownFailure = failure;
            shownDeveloper = developerVisible;
            headline.Text = ScanFeedback.Headline(phase);
            guidance.Text = ScanFeedback.Guidance(phase, snapshot.CornerCount, failure);
            progress.Text = ScanFeedback.ProgressLabel(snapshot.CornerCount);
            if (developerVisible)
            {
                debug.Text = debugText ?? string.Empty;
            }
        }

        public void ClearFailureDisplay()
        {
            shownFailure = null;
            shownCorners = -1;
        }

        public bool ContainsScreenPoint(Vector2 screenPoint)
        {
            EnsureBuilt();
            return Contains(topBar, screenPoint)
                || Contains(bottomBar, screenPoint)
                || Contains(resetButton, screenPoint)
                || Contains(developerButton, screenPoint)
                || (developerVisible && Contains(debugPanel, screenPoint));
        }

        public void BindReset(UnityEngine.Events.UnityAction action)
        {
            EnsureBuilt();
            resetButton.GetComponent<Button>().onClick.AddListener(action);
        }

        void ToggleDeveloper()
        {
            developerVisible = !developerVisible;
            debugPanel.gameObject.SetActive(developerVisible);
            shownDeveloper = !developerVisible;
        }

        void ApplySafeArea()
        {
            Rect safe = Screen.safeArea;
            if (safe == lastSafeArea || Screen.width <= 0 || Screen.height <= 0)
            {
                return;
            }

            lastSafeArea = safe;
            safeRoot.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
            safeRoot.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
            safeRoot.offsetMin = Vector2.zero;
            safeRoot.offsetMax = Vector2.zero;
        }

        RectTransform CreateBar(string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, float height)
        {
            var bar = CreateRect(name, safeRoot);
            bar.anchorMin = anchorMin;
            bar.anchorMax = anchorMax;
            bar.pivot = pivot;
            bar.sizeDelta = new Vector2(0f, height);
            bar.anchoredPosition = Vector2.zero;
            var image = bar.gameObject.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.46f);
            image.raycastTarget = true;
            return bar;
        }

        RectTransform CreateButton(RectTransform parent, string label)
        {
            var rect = CreateRect(label + "Button", parent);
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.sizeDelta = new Vector2(ButtonSize, ButtonSize);
            rect.anchoredPosition = new Vector2(-24f, 24f);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.12f, 0.12f, 0.12f, 0.82f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var text = CreateLabel(rect, label == "Reset" ? 32f : 26f, TextAlignmentOptions.Center, FontStyles.Bold);
            text.Text = label;
            return rect;
        }

        LabelSlot CreateLabel(RectTransform parent, float fontSize, TextAlignmentOptions alignment, FontStyles style)
        {
            var rect = CreateRect("Label", parent);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(16f, 8f);
            rect.offsetMax = new Vector2(-16f, -8f);
            var slot = new LabelSlot();
            TMP_FontAsset font = SharedFont();
            if (font != null)
            {
                var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
                text.font = font;
                text.fontSize = fontSize;
                text.alignment = alignment;
                text.fontStyle = style;
                text.color = Color.white;
                text.enableWordWrapping = true;
                text.raycastTarget = false;
                slot.Tmp = text;
            }
            else
            {
                var text = rect.gameObject.AddComponent<Text>();
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text.fontSize = Mathf.RoundToInt(fontSize);
                text.alignment = TextAnchor.MiddleCenter;
                text.color = Color.white;
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.verticalOverflow = VerticalWrapMode.Truncate;
                text.raycastTarget = false;
                slot.Legacy = text;
            }

            slot.Rect = rect;
            return slot;
        }

        static RectTransform CreateRect(string name, RectTransform parent)
        {
            var rect = new GameObject(name).AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        static bool Contains(RectTransform rect, Vector2 screenPoint)
        {
            return rect != null && rect.gameObject.activeInHierarchy
                && RectTransformUtility.RectangleContainsScreenPoint(rect, screenPoint, null);
        }

        static TMP_FontAsset sharedFont;

        static TMP_FontAsset SharedFont()
        {
            if (sharedFont != null)
            {
                return sharedFont;
            }

            Font source = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (source == null)
            {
                source = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            if (source == null)
            {
                return null;
            }

            sharedFont = TMP_FontAsset.CreateFontAsset(source);
            return sharedFont;
        }

        sealed class LabelSlot
        {
            public TMP_Text Tmp;
            public Text Legacy;
            public RectTransform Rect;

            public string Text
            {
                set
                {
                    if (Tmp != null)
                    {
                        Tmp.text = value;
                    }
                    else if (Legacy != null)
                    {
                        Legacy.text = value;
                    }
                }
            }
        }
    }
}

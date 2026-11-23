using System;
using RoomTwin.Geometry;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace RoomTwin.AR
{
    /// <summary>
    /// Compact scan HUD drawn with Unity UI Text. It does not use TextMeshPro.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArScanHud : MonoBehaviour
    {
        public const string FontResourcePath = "Fonts/NotoSansKR-Regular";

        const float BarHeight = 112f;
        const float ButtonSize = 144f;
        const string LogTag = "[RoomTwin][FieldTest]";

        RectTransform safeRoot;
        RectTransform topBar;
        RectTransform bottomBar;
        RectTransform resetButton;
        RectTransform developerButton;
        RectTransform debugPanel;
        Text headline;
        Text guidance;
        Text progress;
        Text debugTextLabel;
        GameObject canvasObject;
        GameObject createdEventSystem;
        bool developerVisible;
        ScanPhase shownPhase;
        int shownCorners = -1;
        string shownFailure;
        string shownDebug;
        bool shownDeveloper;
        Rect lastSafeArea;

        public static Func<Font> LoadFontOverride;

        public bool IsReady { get; private set; }
        public string Headline => headline != null ? headline.text : string.Empty;
        public string Guidance => guidance != null ? guidance.text : string.Empty;
        public string Progress => progress != null ? progress.text : string.Empty;
        public bool IsDeveloperPanelActive => debugPanel != null && debugPanel.gameObject.activeSelf;
        public Button ResetButtonComponent => resetButton != null ? resetButton.GetComponent<Button>() : null;

        public bool TryInitialize(out string error)
        {
            error = null;
            if (IsReady && headline != null && progress != null && ResetButtonComponent != null)
            {
                return true;
            }

            CleanupFailedBuild();
            Debug.Log(LogTag + " hud-init=start");
            try
            {
                Font font = LoadFont();
                if (font == null)
                {
                    error = "Korean font was not included in the player.";
                    Debug.LogError(LogTag + " font-load=failure path=" + FontResourcePath);
                    Debug.LogError(LogTag + " hud-init=failure " + error);
                    return false;
                }

                Debug.Log(LogTag + " font-load=success name=" + font.name);
                Build(font);
                if (headline == null || guidance == null || progress == null || ResetButtonComponent == null)
                {
                    error = "HUD widgets were not all created.";
                    CleanupFailedBuild();
                    Debug.LogError(LogTag + " hud-init=failure " + error);
                    return false;
                }

                IsReady = true;
                Debug.Log(LogTag + " hud-init=success");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                Debug.LogException(ex);
                CleanupFailedBuild();
                Debug.LogError(LogTag + " hud-init=failure " + error);
                return false;
            }
        }

        public void Apply(in ScanSnapshot snapshot, string failure, string debugText)
        {
            if (!IsReady && !TryInitialize(out _))
            {
                return;
            }

            ApplySafeArea();
            ScanPhase phase = ScanFeedback.Resolve(snapshot);
            bool debugChanged = developerVisible && debugText != shownDebug;
            if (phase == shownPhase
                && snapshot.CornerCount == shownCorners
                && failure == shownFailure
                && developerVisible == shownDeveloper
                && !debugChanged)
            {
                return;
            }

            shownPhase = phase;
            shownCorners = snapshot.CornerCount;
            shownFailure = failure;
            shownDeveloper = developerVisible;
            shownDebug = debugText;
            headline.text = ScanFeedback.Headline(phase);
            guidance.text = ScanFeedback.Guidance(phase, snapshot.CornerCount, failure);
            progress.text = ScanFeedback.ProgressLabel(snapshot.CornerCount);
            if (developerVisible && debugTextLabel != null)
            {
                debugTextLabel.text = debugText ?? string.Empty;
            }
        }

        public void BindReset(UnityAction action)
        {
            if (!IsReady && !TryInitialize(out _))
            {
                return;
            }

            Button button = ResetButtonComponent;
            if (button == null || action == null)
            {
                return;
            }

            button.onClick.AddListener(action);
        }

        public void SetDeveloperVisible(bool visible)
        {
            developerVisible = visible;
            if (debugPanel != null)
            {
                debugPanel.gameObject.SetActive(visible);
            }

            shownDeveloper = !visible;
        }

        public void ClearFailureDisplay()
        {
            shownFailure = null;
            shownCorners = -1;
        }

        public bool ContainsScreenPoint(Vector2 screenPoint)
        {
            if (!IsReady)
            {
                return false;
            }

            return Contains(topBar, screenPoint)
                || Contains(bottomBar, screenPoint)
                || Contains(resetButton, screenPoint)
                || Contains(developerButton, screenPoint)
                || (developerVisible && Contains(debugPanel, screenPoint));
        }

        void OnDestroy()
        {
            if (createdEventSystem != null)
            {
                Destroy(createdEventSystem);
                createdEventSystem = null;
            }
        }

        public void CleanupFailedBuild()
        {
            IsReady = false;
            headline = null;
            guidance = null;
            progress = null;
            debugTextLabel = null;
            safeRoot = null;
            topBar = null;
            bottomBar = null;
            resetButton = null;
            developerButton = null;
            debugPanel = null;
            if (canvasObject != null)
            {
                Destroy(canvasObject);
                canvasObject = null;
            }

            if (createdEventSystem != null)
            {
                Destroy(createdEventSystem);
                createdEventSystem = null;
            }
        }

        void Build(Font font)
        {
            EnsureEventSystem();
            canvasObject = new GameObject("ScanHud");
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 2400f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();

            safeRoot = CreateRect("SafeArea", canvasObject.transform);
            safeRoot.anchorMin = Vector2.zero;
            safeRoot.anchorMax = Vector2.one;
            safeRoot.offsetMin = Vector2.zero;
            safeRoot.offsetMax = Vector2.zero;

            topBar = CreateBar(safeRoot, "Top", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), BarHeight);
            headline = CreateLabel(topBar, font, 36, TextAnchor.MiddleCenter);

            bottomBar = CreateBar(safeRoot, "Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), 220f);
            guidance = CreateLabel(bottomBar, font, 32, TextAnchor.MiddleCenter);
            progress = CreateLabel(bottomBar, font, 28, TextAnchor.MiddleCenter);
            SetStretch(guidance.rectTransform, 0.04f, 0.42f, 0.72f, 0.92f);
            SetStretch(progress.rectTransform, 0.04f, 0.08f, 0.72f, 0.4f);

            resetButton = CreateButton(safeRoot, font, "Reset", 32);
            developerButton = CreateButton(safeRoot, font, "개발", 26);
            developerButton.anchorMin = new Vector2(0f, 0f);
            developerButton.anchorMax = new Vector2(0f, 0f);
            developerButton.pivot = new Vector2(0f, 0f);
            developerButton.sizeDelta = new Vector2(120f, 72f);
            developerButton.anchoredPosition = new Vector2(24f, 24f);
            developerButton.GetComponent<Button>().onClick.AddListener(() => SetDeveloperVisible(!developerVisible));

            debugPanel = CreateBar(safeRoot, "Debug", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), 280f);
            debugPanel.anchoredPosition = new Vector2(0f, -BarHeight - 8f);
            debugTextLabel = CreateLabel(debugPanel, font, 24, TextAnchor.UpperLeft);
            debugPanel.gameObject.SetActive(false);
            ApplySafeArea();
        }

        Font LoadFont()
        {
            if (LoadFontOverride != null)
            {
                return LoadFontOverride();
            }

            return Resources.Load<Font>(FontResourcePath);
        }

        void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null)
            {
                return;
            }

            createdEventSystem = new GameObject("EventSystem");
            createdEventSystem.AddComponent<EventSystem>();
            var module = createdEventSystem.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
        }

        void ApplySafeArea()
        {
            if (safeRoot == null || Screen.width <= 0 || Screen.height <= 0)
            {
                return;
            }

            Rect safe = Screen.safeArea;
            if (safe == lastSafeArea)
            {
                return;
            }

            lastSafeArea = safe;
            safeRoot.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
            safeRoot.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
            safeRoot.offsetMin = Vector2.zero;
            safeRoot.offsetMax = Vector2.zero;
        }

        static RectTransform CreateBar(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, float height)
        {
            RectTransform bar = CreateRect(name, parent);
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

        static RectTransform CreateButton(RectTransform parent, Font font, string label, int fontSize)
        {
            RectTransform rect = CreateRect(label + "Button", parent);
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.sizeDelta = new Vector2(ButtonSize, ButtonSize);
            rect.anchoredPosition = new Vector2(-24f, 24f);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.12f, 0.12f, 0.12f, 0.82f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            Text text = CreateLabel(rect, font, fontSize, TextAnchor.MiddleCenter);
            text.text = label;
            return rect;
        }

        static Text CreateLabel(RectTransform parent, Font font, int fontSize, TextAnchor alignment)
        {
            RectTransform rect = CreateRect("Label", parent);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(16f, 8f);
            rect.offsetMax = new Vector2(-16f, -8f);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        static void SetStretch(RectTransform rect, float minX, float minY, float maxX, float maxY)
        {
            rect.anchorMin = new Vector2(minX, minY);
            rect.anchorMax = new Vector2(maxX, maxY);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        static RectTransform CreateRect(string name, Transform parent)
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
    }
}

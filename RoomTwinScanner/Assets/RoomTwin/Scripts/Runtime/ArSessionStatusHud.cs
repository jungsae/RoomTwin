using System.Text;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace RoomTwin.AR
{
    /// <summary>
    /// On-screen AR session / plane / touch-marker diagnostics for Phase 0 device checks.
    /// Overlay: cyan cross = last touch, magenta square = hit reprojection at placement,
    /// yellow square = newest marker reprojected every frame.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArSessionStatusHud : MonoBehaviour
    {
        const float Pad = 12f;
        const float PanelWidth = 430f;
        const float LineHeight = 17f;
        const float ButtonHeight = 34f;
        const float CrossHalfPx = 40f;
        const float CrossThicknessPx = 4f;
        const float SquareHalfPx = 14f;

        static readonly Color TouchColor = Color.cyan;
        static readonly Color HitReprojColor = Color.magenta;
        static readonly Color LiveMarkerColor = Color.yellow;

        [SerializeField]
        ARSession arSession;

        [SerializeField]
        ARPlaneManager planeManager;

        [SerializeField]
        ArTouchMarkerPlacer markerPlacer;

        [SerializeField]
        bool drawGui = true;

        readonly StringBuilder text = new StringBuilder(1024);
        GUIStyle labelStyle;
        Rect lastPanelRect = new Rect(Pad, Pad, PanelWidth, 200f);

        void Awake()
        {
            if (arSession == null)
            {
                arSession = FindFirstObjectByType<ARSession>();
            }

            if (planeManager == null)
            {
                planeManager = FindFirstObjectByType<ARPlaneManager>();
            }

            if (markerPlacer == null)
            {
                markerPlacer = FindFirstObjectByType<ArTouchMarkerPlacer>();
            }
        }

        /// <summary>Screen position uses Input System convention (origin bottom-left, pixels).</summary>
        public bool IsScreenPointOverHud(Vector2 screenPosition)
        {
            if (!drawGui || !isActiveAndEnabled)
            {
                return false;
            }

            float scale = GuiScale();
            var guiPoint = new Vector2(screenPosition.x, Screen.height - screenPosition.y) / scale;
            return lastPanelRect.Contains(guiPoint);
        }

        static float GuiScale()
        {
            float dpiScale = Screen.dpi > 0f ? Screen.dpi / 160f : 1f;
            float maxScale = Screen.width * 0.95f / (PanelWidth + Pad);
            return Mathf.Clamp(dpiScale, 1f, Mathf.Max(1f, maxScale));
        }

        void OnGUI()
        {
            if (!drawGui)
            {
                return;
            }

            DrawScreenOverlay();

            labelStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                wordWrap = false,
                clipping = TextClipping.Clip
            };

            float scale = GuiScale();
            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            BuildText();
            int lineCount = CountLines(text);
            float textHeight = lineCount * LineHeight;
            lastPanelRect = new Rect(Pad, Pad, PanelWidth, textHeight + ButtonHeight + 26f);

            GUI.Box(lastPanelRect, GUIContent.none);
            GUI.Label(
                new Rect(lastPanelRect.x + 8f, lastPanelRect.y + 6f, lastPanelRect.width - 16f, textHeight),
                text.ToString(),
                labelStyle);

            if (markerPlacer != null)
            {
                string label = markerPlacer.FrontOfCameraTestMode
                    ? "Mode: TEST (1 m ahead, no raycast) - tap to switch"
                    : "Mode: RAYCAST - tap to switch to TEST";
                Rect buttonRect = new Rect(
                    lastPanelRect.x + 8f,
                    lastPanelRect.yMax - ButtonHeight - 8f,
                    lastPanelRect.width - 16f,
                    ButtonHeight);
                if (GUI.Button(buttonRect, label))
                {
                    markerPlacer.FrontOfCameraTestMode = !markerPlacer.FrontOfCameraTestMode;
                }
            }

            GUI.matrix = previousMatrix;
        }

        void BuildText()
        {
            text.Clear();

            int planeCount = planeManager != null ? planeManager.trackables.count : 0;
            string state = arSession != null ? ARSession.state.ToString() : "No ARSession";
            text.Append("AR Session: ").Append(state)
                .Append(" | Planes (candidates): ").Append(planeCount).Append('\n');

            if (markerPlacer == null)
            {
                text.Append("ArTouchMarkerPlacer: not found");
                return;
            }

            MarkerPlacementDiagnostics d = markerPlacer.Last;
            text.Append($"Touch Count: {markerPlacer.TouchCount} | Marker Count: {markerPlacer.MarkerCount}\n");
            text.Append($"Last Result: {d.Result} | {d.Reason}\n");
            text.Append($"Screen {Screen.width}x{Screen.height} {Screen.orientation} | ")
                .Append($"Cam {d.CameraPixelWidth}x{d.CameraPixelHeight} | in-screen: {Yes(d.TouchInsideScreen)}\n");
            text.Append($"Touch px: {d.TouchScreen:F0}");
            text.Append(d.TestMode
                ? " | Raycast: skipped (test)\n"
                : $" | Raycast in px: {d.RaycastScreen:F0}\n");

            if (!d.TestMode)
            {
                string any = d.AnyTypeHitCount >= 0 ? $" (any type: {d.AnyTypeHitCount})" : string.Empty;
                text.Append($"Raycast Hits: {(d.HitCount < 0 ? "-" : d.HitCount.ToString())}{any} ")
                    .Append($"[{markerPlacer.RaycastTypes}] hit={d.HitType}\n");
                text.Append($"Dist {d.HitDistance:0.00} m | Align {d.PlaneAlignment} | ")
                    .Append($"camY-hitY {d.CameraHeightAboveHit:0.00} m\n");
                text.Append($"Trackable: {d.TrackableId}\n");
                text.Append($"Hit pos {d.HitPose.position:F2} rot {d.HitPose.rotation.eulerAngles:F0}\n");
            }

            text.Append($"Marker world {d.MarkerWorld:F2} | renderer on: {Yes(d.MarkerRendererEnabled)}\n");
            text.Append(d.TestMode ? "Reproj vs screen center: " : "Hit reproj px: ")
                .Append($"{d.HitReprojected:F0} err {(d.ReprojErrorPx < 0f ? "-" : d.ReprojErrorPx.ToString("0.0"))} px\n");
            text.Append("Verdict: ").Append(d.Verdict).Append('\n');

            if (markerPlacer.TryGetLastMarkerLiveState(
                    out Vector3 live, out bool rendererOn, out bool visible))
            {
                text.Append($"Live marker px {live:F0} front: {Yes(live.z > 0f)} ")
                    .Append($"renderer: {Yes(rendererOn)} visible: {Yes(visible)}\n");
            }
            else
            {
                text.Append("Live marker: none\n");
            }

            text.Append("Overlay: cyan=touch  magenta=hit reproj  yellow=live marker");
        }

        void DrawScreenOverlay()
        {
            if (markerPlacer == null || markerPlacer.TouchCount == 0)
            {
                return;
            }

            MarkerPlacementDiagnostics d = markerPlacer.Last;
            DrawCross(d.TouchScreen, TouchColor);

            if (d.ReprojErrorPx >= 0f && d.HitReprojected.z > 0f)
            {
                DrawSquare(d.HitReprojected, HitReprojColor);
            }

            if (markerPlacer.TryGetLastMarkerLiveState(out Vector3 live, out _, out _) && live.z > 0f)
            {
                DrawSquare(live, LiveMarkerColor);
            }
        }

        static void DrawCross(Vector2 screenPoint, Color color)
        {
            Vector2 p = ToGui(screenPoint);
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(
                new Rect(p.x - CrossHalfPx, p.y - CrossThicknessPx * 0.5f, CrossHalfPx * 2f, CrossThicknessPx),
                Texture2D.whiteTexture);
            GUI.DrawTexture(
                new Rect(p.x - CrossThicknessPx * 0.5f, p.y - CrossHalfPx, CrossThicknessPx, CrossHalfPx * 2f),
                Texture2D.whiteTexture);
            GUI.color = previous;
        }

        static void DrawSquare(Vector2 screenPoint, Color color)
        {
            Vector2 p = ToGui(screenPoint);
            Color previous = GUI.color;
            GUI.color = color;
            float size = SquareHalfPx * 2f;
            float t = CrossThicknessPx;
            float x = p.x - SquareHalfPx;
            float y = p.y - SquareHalfPx;
            GUI.DrawTexture(new Rect(x, y, size, t), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x, y + size - t, size, t), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x, y, t, size), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x + size - t, y, t, size), Texture2D.whiteTexture);
            GUI.color = previous;
        }

        static Vector2 ToGui(Vector2 screenPoint)
        {
            return new Vector2(screenPoint.x, Screen.height - screenPoint.y);
        }

        static int CountLines(StringBuilder sb)
        {
            int lines = 1;
            for (int i = 0; i < sb.Length; i++)
            {
                if (sb[i] == '\n')
                {
                    lines++;
                }
            }

            return lines;
        }

        static string Yes(bool value) => value ? "yes" : "no";
    }
}

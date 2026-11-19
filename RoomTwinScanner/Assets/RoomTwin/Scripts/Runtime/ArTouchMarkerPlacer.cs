using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace RoomTwin.AR
{
    public enum MarkerPlacementResult
    {
        NoInput,
        RaycastFailed,
        Placed,
        Error
    }

    /// <summary>
    /// Snapshot of the last touch → raycast → marker pipeline. Screen values use
    /// Input System convention (pixels, origin bottom-left).
    /// </summary>
    public sealed class MarkerPlacementDiagnostics
    {
        public MarkerPlacementResult Result = MarkerPlacementResult.NoInput;
        public string Reason = "No touch yet";
        public bool TestMode;

        public Vector2 TouchScreen;
        public Vector2 RaycastScreen;

        public int HitCount = -1;
        public int AnyTypeHitCount = -1;
        public TrackableType HitType;
        public float HitDistance;
        public Pose HitPose;
        public string TrackableId = "-";
        public string PlaneAlignment = "-";
        public float CameraHeightAboveHit;

        public Vector3 MarkerWorld;
        public Vector3 CameraWorld;
        public Vector3 HitReprojected;
        public float ReprojErrorPx = -1f;
        public bool MarkerRendererEnabled;
        public string Verdict = "-";

        public int ScreenWidth;
        public int ScreenHeight;
        public int CameraPixelWidth;
        public int CameraPixelHeight;
        public ScreenOrientation Orientation;
        public bool TouchInsideScreen;
    }

    /// <summary>
    /// Places debug markers at AR raycast hits (Raycast mode) or 1 m in front of the
    /// camera (Test mode). Hits use session space and are for Phase 0 probing only.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArTouchMarkerPlacer : MonoBehaviour
    {
        static readonly List<ARRaycastHit> Hits = new List<ARRaycastHit>();
        static readonly List<ARRaycastHit> CompareScreenHits = new List<ARRaycastHit>();
        static readonly List<ARRaycastHit> CompareRayHits = new List<ARRaycastHit>();

        [SerializeField]
        ARRaycastManager raycastManager;

        [SerializeField]
        GameObject markerPrefab;

        [SerializeField]
        Transform markerParent;

        [SerializeField]
        Camera arCamera;

        [SerializeField]
        ArSessionStatusHud hud;

        [SerializeField]
        TrackableType raycastTypes = TrackableType.PlaneWithinPolygon;

        [SerializeField]
        float markerScaleMeters = 0.1f;

        [SerializeField]
        float surfaceOffsetMeters = 0.005f;

        [SerializeField]
        float testModeDistanceMeters = 1f;

        [SerializeField]
        float reprojectionTolerancePx = 20f;

        [SerializeField]
        bool frontOfCameraTestMode = false;

        [SerializeField]
        bool clearMarkersOnDisable = false;

        readonly List<GameObject> spawnedMarkers = new List<GameObject>();
        GameObject lastMarker;
        Renderer lastMarkerRenderer;

        public MarkerPlacementDiagnostics Last { get; } = new MarkerPlacementDiagnostics();
        public int TouchCount { get; private set; }
        public int MarkerCount => spawnedMarkers.Count;
        public TrackableType RaycastTypes => raycastTypes;
        public bool HasLastMarker => lastMarker != null;

        public bool FrontOfCameraTestMode
        {
            get => frontOfCameraTestMode;
            set => frontOfCameraTestMode = value;
        }

        void Awake()
        {
            if (raycastManager == null)
            {
                raycastManager = FindFirstObjectByType<ARRaycastManager>();
            }

            if (arCamera == null)
            {
                arCamera = Camera.main;
            }

            if (hud == null)
            {
                hud = FindFirstObjectByType<ArSessionStatusHud>();
            }

            if (markerParent == null)
            {
                markerParent = transform;
            }
        }

        void OnEnable()
        {
            EnhancedTouchSupport.Enable();
        }

        void Update()
        {
            if (!TryGetPressScreenPosition(out Vector2 touchScreen))
            {
                return;
            }

            if (hud != null && hud.IsScreenPointOverHud(touchScreen))
            {
                return;
            }

            TouchCount++;
            ResetDiagnostics(touchScreen);

            try
            {
                if (frontOfCameraTestMode)
                {
                    PlaceInFrontOfCamera();
                }
                else
                {
                    PlaceAtRaycastHit(touchScreen);
                }
            }
            catch (System.Exception ex)
            {
                Fail(ex.GetType().Name + ": " + ex.Message);
                Debug.LogException(ex);
            }

            LogDiagnostics();
        }

        void ResetDiagnostics(Vector2 touchScreen)
        {
            MarkerPlacementDiagnostics d = Last;
            d.Result = MarkerPlacementResult.NoInput;
            d.Reason = "-";
            d.Verdict = "-";
            d.TestMode = frontOfCameraTestMode;
            d.TouchScreen = touchScreen;
            d.RaycastScreen = touchScreen;
            d.HitCount = -1;
            d.AnyTypeHitCount = -1;
            d.HitType = TrackableType.None;
            d.HitDistance = 0f;
            d.HitPose = default;
            d.TrackableId = "-";
            d.PlaneAlignment = "-";
            d.CameraHeightAboveHit = 0f;
            d.MarkerWorld = Vector3.zero;
            d.HitReprojected = Vector3.zero;
            d.ReprojErrorPx = -1f;
            d.MarkerRendererEnabled = false;
            d.ScreenWidth = Screen.width;
            d.ScreenHeight = Screen.height;
            d.Orientation = Screen.orientation;
            d.CameraPixelWidth = arCamera != null ? arCamera.pixelWidth : 0;
            d.CameraPixelHeight = arCamera != null ? arCamera.pixelHeight : 0;
            d.CameraWorld = arCamera != null ? arCamera.transform.position : Vector3.zero;
            d.TouchInsideScreen =
                touchScreen.x >= 0f && touchScreen.x <= Screen.width &&
                touchScreen.y >= 0f && touchScreen.y <= Screen.height;
        }

        void PlaceInFrontOfCamera()
        {
            if (!CheckCommonRefs(requireRaycast: false))
            {
                return;
            }

            Transform cameraTransform = arCamera.transform;
            Vector3 position = cameraTransform.position +
                cameraTransform.forward * testModeDistanceMeters;
            SpawnMarker(position, Quaternion.identity);

            MarkerPlacementDiagnostics d = Last;
            d.Reason = $"Test mode: raycast skipped, {testModeDistanceMeters:0.0} m ahead";
            Vector3 reproj = arCamera.WorldToScreenPoint(position);
            d.HitReprojected = reproj;
            var screenCenter = new Vector2(arCamera.pixelWidth * 0.5f, arCamera.pixelHeight * 0.5f);
            d.ReprojErrorPx = Vector2.Distance(screenCenter, reproj);
            d.Verdict = d.ReprojErrorPx <= reprojectionTolerancePx
                ? "Test OK: marker projects to screen center"
                : "Test MISMATCH: marker not at screen center (camera/transform issue)";
        }

        void PlaceAtRaycastHit(Vector2 touchScreen)
        {
            if (!CheckCommonRefs(requireRaycast: true))
            {
                return;
            }

            LogRaycastPathComparison(touchScreen);

            MarkerPlacementDiagnostics d = Last;

            Hits.Clear();
            bool hit = raycastManager.Raycast(touchScreen, Hits, raycastTypes);
            d.HitCount = hit ? Hits.Count : 0;

            if (!hit)
            {
                Hits.Clear();
                raycastManager.Raycast(touchScreen, Hits, TrackableType.AllTypes);
                d.AnyTypeHitCount = Hits.Count;
                d.Result = MarkerPlacementResult.RaycastFailed;
                d.Reason = d.AnyTypeHitCount > 0
                    ? $"No {raycastTypes} hit; {d.AnyTypeHitCount} hit(s) of other types"
                    : "No hit of any type at touch point";
                return;
            }

            ARRaycastHit nearest = Hits[0];
            d.HitType = nearest.hitType;
            d.HitDistance = nearest.distance;
            d.HitPose = nearest.pose;
            d.TrackableId = nearest.trackableId.ToString();
            d.PlaneAlignment = nearest.trackable is ARPlane plane
                ? plane.alignment.ToString()
                : "not a plane";
            d.CameraHeightAboveHit = d.CameraWorld.y - nearest.pose.position.y;

            Vector3 hitReproj = arCamera.WorldToScreenPoint(nearest.pose.position);
            d.HitReprojected = hitReproj;
            d.ReprojErrorPx = Vector2.Distance(touchScreen, hitReproj);

            // Plane pose up is the surface normal; lift the marker so it is not half-buried.
            Vector3 offset = nearest.pose.up * (markerScaleMeters * 0.5f + surfaceOffsetMeters);
            SpawnMarker(nearest.pose.position + offset, nearest.pose.rotation);

            d.Reason = "Raycast hit";
            d.Verdict = BuildRaycastVerdict(d, hitReproj.z);
        }

        /// <summary>
        /// Diagnostic only: casts the same touch through the screen-point path (ARCore viewport
        /// hit test) and the world-ray path (Unity camera ray) in one frame and logs both.
        /// Does not affect marker placement.
        /// </summary>
        void LogRaycastPathComparison(Vector2 touchScreen)
        {
            Transform cameraTransform = arCamera.transform;
            Vector3 cameraPosition = cameraTransform.position;
            Quaternion cameraRotation = cameraTransform.rotation;
            Ray worldRay = arCamera.ScreenPointToRay(touchScreen);

            CompareScreenHits.Clear();
            CompareRayHits.Clear();
            bool screenHit = raycastManager.Raycast(touchScreen, CompareScreenHits, raycastTypes);
            bool rayHit = raycastManager.Raycast(worldRay, CompareRayHits, raycastTypes);

            string screenText = DescribeHit(screenHit, CompareScreenHits, touchScreen);
            string rayText = DescribeHit(rayHit, CompareRayHits, touchScreen);

            string delta = "n/a";
            if (screenHit && rayHit)
            {
                Vector3 screenPos = CompareScreenHits[0].pose.position;
                Vector3 rayPos = CompareRayHits[0].pose.position;
                float angle = Vector3.Angle(screenPos - cameraPosition, rayPos - cameraPosition);
                bool sameTrackable = CompareScreenHits[0].trackableId == CompareRayHits[0].trackableId;
                delta = $"worldDist={Vector3.Distance(screenPos, rayPos):0.000}m " +
                    $"rayAngle={angle:0.00}deg sameTrackable={sameTrackable}";
            }

            Debug.Log(
                "[RoomTwin][Phase0][RayCompare] " +
                $"frame={Time.frameCount} touch={touchScreen:F1} " +
                $"screen={Screen.width}x{Screen.height} orient={Screen.orientation} " +
                $"camPixelRect={arCamera.pixelRect} " +
                $"camPos={cameraPosition:F3} camRot={cameraRotation.eulerAngles:F2} " +
                $"worldRayOrigin={worldRay.origin:F3} worldRayDir={worldRay.direction:F4} " +
                $"types={raycastTypes} | SCREEN {screenText} | RAY {rayText} | DELTA {delta}");
        }

        string DescribeHit(bool hit, List<ARRaycastHit> hits, Vector2 touchScreen)
        {
            if (!hit || hits.Count == 0)
            {
                return "hit=False";
            }

            ARRaycastHit nearest = hits[0];
            Vector3 reproj = arCamera.WorldToScreenPoint(nearest.pose.position);
            float errorPx = Vector2.Distance(touchScreen, reproj);
            string alignment = nearest.trackable is ARPlane plane ? plane.alignment.ToString() : "-";
            return $"hit=True count={hits.Count} type={nearest.hitType} id={nearest.trackableId} " +
                $"align={alignment} dist={nearest.distance:0.000}m " +
                $"pos={nearest.pose.position:F3} rot={nearest.pose.rotation.eulerAngles:F1} " +
                $"sessionPos={nearest.sessionRelativePose.position:F3} " +
                $"reproj={reproj:F1} errPx={errorPx:0.0}";
        }

        string BuildRaycastVerdict(MarkerPlacementDiagnostics d, float hitDepth)
        {
            bool sizeMismatch =
                d.ScreenWidth != d.CameraPixelWidth || d.ScreenHeight != d.CameraPixelHeight;

            if (!d.TouchInsideScreen)
            {
                return "COORD ERROR: touch outside Screen bounds";
            }

            if (hitDepth <= 0f)
            {
                return "COORD ERROR: hit is behind camera";
            }

            if (d.ReprojErrorPx > reprojectionTolerancePx)
            {
                return sizeMismatch
                    ? "COORD MISMATCH: Screen vs camera pixel size differ"
                    : "COORD MISMATCH: hit not on touch ray (code/transform)";
            }

            string plausibility = d.PlaneAlignment switch
            {
                nameof(PlaneAlignment.HorizontalUp) when d.CameraHeightAboveHit < 0.2f ||
                    d.CameraHeightAboveHit > 2.5f =>
                    $" | floor height {d.CameraHeightAboveHit:0.00} m suspicious",
                _ when d.HitDistance > 6f => $" | far hit {d.HitDistance:0.0} m suspicious",
                _ => string.Empty
            };

            return "COORD OK: hit on touch ray; offsets = ARCore plane pose/depth" + plausibility;
        }

        bool CheckCommonRefs(bool requireRaycast)
        {
            if (markerPrefab == null)
            {
                Fail("markerPrefab is not assigned");
                return false;
            }

            if (arCamera == null)
            {
                Fail("AR camera not found");
                return false;
            }

            if (requireRaycast && (raycastManager == null || !raycastManager.isActiveAndEnabled))
            {
                Fail("ARRaycastManager missing or disabled");
                return false;
            }

            return true;
        }

        void SpawnMarker(Vector3 position, Quaternion rotation)
        {
            GameObject marker = Instantiate(markerPrefab, position, rotation, markerParent);
            marker.name = $"DebugMarker_{spawnedMarkers.Count:000}";
            marker.transform.localScale = Vector3.one * markerScaleMeters;
            spawnedMarkers.Add(marker);

            lastMarker = marker;
            lastMarkerRenderer = marker.GetComponentInChildren<Renderer>();

            MarkerPlacementDiagnostics d = Last;
            d.Result = MarkerPlacementResult.Placed;
            d.MarkerWorld = marker.transform.position;
            d.MarkerRendererEnabled = lastMarkerRenderer != null && lastMarkerRenderer.enabled;
        }

        /// <summary>Live state of the newest marker, re-evaluated every frame.</summary>
        public bool TryGetLastMarkerLiveState(
            out Vector3 screenPoint,
            out bool rendererEnabled,
            out bool isVisible)
        {
            screenPoint = default;
            rendererEnabled = false;
            isVisible = false;

            if (lastMarker == null || arCamera == null)
            {
                return false;
            }

            screenPoint = arCamera.WorldToScreenPoint(lastMarker.transform.position);
            rendererEnabled = lastMarkerRenderer != null && lastMarkerRenderer.enabled;
            isVisible = lastMarkerRenderer != null && lastMarkerRenderer.isVisible;
            return true;
        }

        void Fail(string message)
        {
            Last.Result = MarkerPlacementResult.Error;
            Last.Reason = message;
            Debug.LogError("[RoomTwin][Phase0] " + message);
        }

        void LogDiagnostics()
        {
            MarkerPlacementDiagnostics d = Last;
            Debug.Log(
                "[RoomTwin][Phase0][Touch] " +
                $"mode={(d.TestMode ? "test" : "raycast")} result={d.Result} reason=\"{d.Reason}\" " +
                $"touch={d.TouchScreen} raycastIn={d.RaycastScreen} " +
                $"screen={d.ScreenWidth}x{d.ScreenHeight} cam={d.CameraPixelWidth}x{d.CameraPixelHeight} " +
                $"orient={d.Orientation} inside={d.TouchInsideScreen} " +
                $"hits={d.HitCount} any={d.AnyTypeHitCount} type={d.HitType} dist={d.HitDistance:0.000} " +
                $"hitPos={d.HitPose.position:F3} hitRot={d.HitPose.rotation.eulerAngles:F1} " +
                $"id={d.TrackableId} align={d.PlaneAlignment} camHeight={d.CameraHeightAboveHit:0.000} " +
                $"cam={d.CameraWorld:F3} marker={d.MarkerWorld:F3} " +
                $"reproj={d.HitReprojected:F1} errPx={d.ReprojErrorPx:0.0} " +
                $"renderer={d.MarkerRendererEnabled} verdict=\"{d.Verdict}\"");
        }

        void OnDisable()
        {
            EnhancedTouchSupport.Disable();

            if (!clearMarkersOnDisable)
            {
                return;
            }

            for (int i = 0; i < spawnedMarkers.Count; i++)
            {
                if (spawnedMarkers[i] != null)
                {
                    Destroy(spawnedMarkers[i]);
                }
            }

            spawnedMarkers.Clear();
            lastMarker = null;
            lastMarkerRenderer = null;
        }

        static bool TryGetPressScreenPosition(out Vector2 screenPosition)
        {
            screenPosition = default;

            // EnhancedTouch keeps a Began phase even when the finger lifts within the same frame.
            var touches = ETouch.activeTouches;
            for (int i = 0; i < touches.Count; i++)
            {
                if (touches[i].phase == UnityEngine.InputSystem.TouchPhase.Began)
                {
                    screenPosition = touches[i].screenPosition;
                    return true;
                }
            }

            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                screenPosition = mouse.position.ReadValue();
                return true;
            }

            return false;
        }
    }
}

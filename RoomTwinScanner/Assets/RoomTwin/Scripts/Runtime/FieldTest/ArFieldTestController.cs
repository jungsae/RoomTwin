using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using RoomTwin.Geometry;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace RoomTwin.AR
{
    /// <summary>
    /// Field-test capture: four HorizontalUp hits become one room mesh parented to the XR Origin.
    /// The mesh is not parented to the camera.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArFieldTestController : MonoBehaviour
    {
        const string LogTag = "[RoomTwin][FieldTest]";
        const float MarkerSizeMeters = 0.08f;

        static readonly List<ARRaycastHit> Hits = new List<ARRaycastHit>();

        [SerializeField]
        ARRaycastManager raycastManager;

        [SerializeField]
        ARPlaneManager planeManager;

        [SerializeField]
        ARSession arSession;

        [SerializeField]
        Camera arCamera;

        [SerializeField]
        Material floorMaterial;

        [SerializeField]
        Material wallMaterial;

        [SerializeField]
        GameObject markerPrefab;

        [SerializeField]
        float wallHeightMeters = RoomGeometryData.DefaultWallHeightMeters;

        [SerializeField]
        float floorHeightToleranceMeters = FloorCornerCapture.DefaultHeightToleranceMeters;

        readonly FloorCornerCapture capture = new FloorCornerCapture();
        readonly StringBuilder logBuilder = new StringBuilder(512);
        readonly List<WallCornerSolver.FloorSample> floorSamples = new List<WallCornerSolver.FloorSample>(8);
        readonly List<DetectedWall> verticalWalls = new List<DetectedWall>(8);
        ArScanHud hud;
        ArScanWallOutlines outlines;
        readonly List<string> visibleWallIds = new List<string>(8);
        string highlightedSelected = string.Empty;
        string highlightedOther = string.Empty;
        ScanSessionLog sessionLog;
        bool waitingForRescan;
        float nextSnapshotTime;
        string logStatus = "starting";
        float pulseUntil;
        float presentationTimer;
        Transform pulseMarker;
        Transform sessionRoot;
        Transform markerRoot;
        Transform roomAnchor;
        string status = "Select a wall. The corner is the wall intersection, not the touch point.";
        string lastRejection = string.Empty;
        bool meshBuilt;
        ARSessionState lastSessionState;

        public int CornerCount => capture.Count;
        public bool MeshBuilt => meshBuilt;
        public string Status => status;
        public string LastRejection => lastRejection;
        public float WallHeightMeters => wallHeightMeters;
        public float FloorHeightToleranceMeters => floorHeightToleranceMeters;
        public bool HasReferenceFloorY => capture.HasReferenceFloorY;
        public float ReferenceFloorY => capture.ReferenceFloorY;
        public bool ControllerInitSucceeded { get; private set; }
        public bool HudReady => hud != null && hud.IsReady;
        public bool UsingEmergencyHud { get; private set; }

        void Awake()
        {
            try
            {
                if (raycastManager == null)
                {
                    raycastManager = FindFirstObjectByType<ARRaycastManager>();
                }

                if (planeManager == null)
                {
                    planeManager = FindFirstObjectByType<ARPlaneManager>();
                }

                if (arSession == null)
                {
                    arSession = FindFirstObjectByType<ARSession>();
                }

                if (arCamera == null)
                {
                    arCamera = Camera.main;
                }

                sessionRoot = raycastManager != null ? raycastManager.transform : transform;
                capture.HeightToleranceMeters = floorHeightToleranceMeters;
                outlines = GetComponent<ArScanWallOutlines>();
                if (outlines == null)
                {
                    outlines = gameObject.AddComponent<ArScanWallOutlines>();
                }

                ControllerInitSucceeded = true;
                BeginSessionLog();
                Debug.Log(LogTag + " controller-init=success");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                Debug.LogError(LogTag + " controller-init=failure " + ex.GetType().Name + ": " + ex.Message);
                return;
            }

            InitializeHud();
        }

        void InitializeHud()
        {
            try
            {
                hud = GetComponent<ArScanHud>();
                if (hud == null)
                {
                    hud = gameObject.AddComponent<ArScanHud>();
                }

                if (!hud.TryInitialize(out _))
                {
                    UsingEmergencyHud = true;
                    return;
                }

                hud.BindReset(StartNewScan);
                hud.BindUndo(UndoLastCorner);
                hud.BindLogActions(ExportLog, DeleteLogs);
                RefreshPresentation();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                Debug.LogError(LogTag + " hud-init=failure " + ex.GetType().Name + ": " + ex.Message);
                if (hud != null)
                {
                    hud.CleanupFailedBuild();
                }

                UsingEmergencyHud = true;
            }
        }

        void OnEnable()
        {
            EnhancedTouchSupport.Enable();
            lastSessionState = ARSession.state;
            ARSession.stateChanged += OnSessionStateChanged;
            Debug.Log($"{LogTag} tracking {lastSessionState}");
        }

        void OnDisable()
        {
            ARSession.stateChanged -= OnSessionStateChanged;
            EnhancedTouchSupport.Disable();
            sessionLog?.Write("session-end", "\"accepted\":" + (sessionLog?.AcceptedCount ?? 0).ToString(CultureInfo.InvariantCulture));
            sessionLog?.DisposeWriter();
            sessionLog = null;
        }

        void ExportLog()
        {
            string directory = Path.Combine(Application.persistentDataPath, "Logs");
            string exported = ScanSessionLog.ExportLatest(directory);
            status = exported == null ? "저장된 로그가 없습니다." : exported;
            Debug.Log(LogTag + " log-export=" + (exported ?? "none"));
        }

        void DeleteLogs()
        {
            string directory = Path.Combine(Application.persistentDataPath, "Logs");
            ScanSessionLog.DeleteAll(directory);
            logStatus = "deleted";
            status = "저장된 스캔 로그를 삭제했습니다.";
            Debug.Log(LogTag + " log-delete path=" + directory);
        }

        void BeginSessionLog()
        {
            sessionLog?.DisposeWriter();
            string directory = Path.Combine(Application.persistentDataPath, "Logs");
            sessionLog = ScanSessionLog.Start(directory, Application.version, "c9a5169+", SystemInfo.deviceModel);
            logStatus = sessionLog.IsRecording ? "recording" : "failed";
            if (!sessionLog.IsRecording)
            {
                Debug.LogError(LogTag + " log-open=failure path=" + sessionLog.FilePath);
            }
        }

        void WriteSnapshotIfDue()
        {
            if (sessionLog == null || !sessionLog.IsRecording || Time.unscaledTime < nextSnapshotTime)
            {
                return;
            }

            nextSnapshotTime = Time.unscaledTime + 2f;
            Vector3 position = arCamera != null ? arCamera.transform.position : Vector3.zero;
            Vector3 euler = arCamera != null ? arCamera.transform.eulerAngles : Vector3.zero;
            sessionLog.Write(
                "snapshot",
                "\"tracking\":" + ScanSessionLog.Quote(ARSession.state.ToString()) +
                ",\"cameraPos\":" + JsonVector(position) +
                ",\"cameraEuler\":" + JsonVector(euler) +
                ",\"verticalWalls\":" + CountVerticalWalls().ToString(CultureInfo.InvariantCulture) +
                ",\"corners\":" + capture.Count.ToString(CultureInfo.InvariantCulture) +
                ",\"planes\":" + PlaneSnapshotJson());
        }

        string PlaneSnapshotJson()
        {
            if (planeManager == null)
            {
                return "[]";
            }

            var builder = new StringBuilder(256);
            builder.Append('[');
            bool any = false;
            foreach (ARPlane plane in planeManager.trackables)
            {
                if (any)
                {
                    builder.Append(',');
                }

                any = true;
                builder.Append("{\"id\":").Append(ScanSessionLog.Quote(plane.trackableId.ToString()));
                builder.Append(",\"alignment\":").Append(ScanSessionLog.Quote(plane.alignment.ToString()));
                builder.Append(",\"center\":").Append(JsonVector(plane.center));
                builder.Append(",\"normal\":").Append(JsonVector(plane.normal));
                builder.Append(",\"area\":").Append((plane.size.x * plane.size.y).ToString("0.00", CultureInfo.InvariantCulture));
                builder.Append('}');
            }

            builder.Append(']');
            return builder.ToString();
        }

        static string JsonVector(Vector3 value)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "[{0:0.000},{1:0.000},{2:0.000}]",
                value.x,
                value.y,
                value.z);
        }

        void Update()
        {
            if (pulseMarker != null)
            {
                float t = Mathf.Clamp01((pulseUntil - Time.time) / 0.35f);
                pulseMarker.localScale = Vector3.one * Mathf.Lerp(MarkerSizeMeters, MarkerSizeMeters * 1.6f, t);
                if (t <= 0f)
                {
                    pulseMarker.localScale = Vector3.one * MarkerSizeMeters;
                    pulseMarker = null;
                }
            }

            presentationTimer -= Time.deltaTime;
            if (presentationTimer <= 0f)
            {
                presentationTimer = 0.25f;
                RefreshPresentation();
            }

            if (!TryGetPressScreenPosition(out Vector2 screen))
            {
                return;
            }

            if (hud != null && hud.ContainsScreenPoint(screen))
            {
                return;
            }

            TrySelectCorner(screen);
        }

        public int CountHorizontalFloors()
        {
            if (planeManager == null)
            {
                return 0;
            }

            int count = 0;
            foreach (ARPlane plane in planeManager.trackables)
            {
                if (plane.alignment == PlaneAlignment.HorizontalUp)
                {
                    count++;
                }
            }

            return count;
        }

        public void UndoLastCorner()
        {
            if (!capture.UndoLast())
            {
                status = "취소할 모서리가 없습니다.";
                return;
            }

            meshBuilt = false;
            DestroyChild(roomAnchor);
            roomAnchor = null;
            RemoveLastMarker();
            lastRejection = string.Empty;
            status = "마지막 모서리 추정을 취소했습니다. 스캔 데이터는 유지됩니다.";
            sessionLog?.Write("undo", "\"corners\":" + capture.Count.ToString(CultureInfo.InvariantCulture));
            Debug.Log(LogTag + " undo corners=" + capture.Count);
            RefreshPresentation();
        }

        public void StartNewScan()
        {
            ClearLocalScan();
            waitingForRescan = arSession != null;
            if (arSession != null)
            {
                arSession.Reset();
            }

            BeginSessionLog();
            status = waitingForRescan
                ? "새 스캔을 준비 중입니다. 추적이 다시 잡힐 때까지 모서리를 확정하지 않습니다."
                : "새 스캔을 시작했습니다. 벽을 선택하세요.";
            Debug.Log(LogTag + " new-scan session=" + (sessionLog != null ? sessionLog.SessionId : "none") + " waiting=" + waitingForRescan);
            RefreshPresentation();
        }

        public void ResetCapture()
        {
            StartNewScan();
        }

        void ClearLocalScan()
        {
            capture.Reset();
            meshBuilt = false;
            lastRejection = string.Empty;
            DestroyChild(markerRoot);
            DestroyChild(roomAnchor);
            markerRoot = null;
            roomAnchor = null;
            pulseMarker = null;
            highlightedSelected = string.Empty;
            highlightedOther = string.Empty;
            visibleWallIds.Clear();
            hud?.ClearFailureDisplay();
            Debug.Log(LogTag + " reset referenceFloorY=cleared");
        }

        void RemoveLastMarker()
        {
            if (markerRoot == null || markerRoot.childCount == 0)
            {
                return;
            }

            Destroy(markerRoot.GetChild(markerRoot.childCount - 1).gameObject);
        }

        void TrySelectCorner(Vector2 screen)
        {
            if (waitingForRescan || ARSession.state != ARSessionState.SessionTracking)
            {
                Reject("AR tracking is not ready. Wait before confirming a corner.");
                return;
            }

            if (arCamera == null || raycastManager == null)
            {
                Reject("AR camera or ARRaycastManager is missing.");
                return;
            }

            if (capture.IsComplete)
            {
                Reject("Four corners are already selected. Reset before measuring again.");
                return;
            }

            capture.HeightToleranceMeters = floorHeightToleranceMeters;
            Hits.Clear();
            if (!raycastManager.Raycast(screen, Hits, TrackableType.PlaneWithinPolygon))
            {
                Reject("No plane hit. Scan the wall and the floor. A touch outside a detected plane cannot estimate a corner.");
                Debug.LogWarning($"{LogTag} rejected hits=0");
                return;
            }

            if (!TryGetSelectedWall(out ARRaycastHit hit, out ARPlane selectedPlane))
            {
                Reject("Select a vertical wall. A floor or ceiling touch is not used as a corner.");
                Debug.LogWarning($"{LogTag} rejected hits={Hits.Count} candidates={DescribeHits()}");
                return;
            }

            if (!TryResolveFloorY(out float floorY, out string floorError))
            {
                Reject(floorError);
                Debug.LogWarning($"{LogTag} rejected floor \"{floorError}\" horizontalYs={DescribeHorizontalYs()}");
                return;
            }

            if (planeManager == null)
            {
                Reject("ARPlaneManager is missing.");
                return;
            }

            DetectedWall selected = ToWall(selectedPlane);
            var others = new List<DetectedWall>();
            foreach (ARPlane plane in planeManager.trackables)
            {
                if (plane.trackableId == selectedPlane.trackableId || plane.alignment != PlaneAlignment.Vertical)
                {
                    continue;
                }

                others.Add(ToWall(plane));
            }

            if (!WallCornerSolver.TryEstimate(selected, others, floorY, out WallCornerEstimate estimate))
            {
                Reject(estimate.Rejection);
                Debug.LogWarning(
                    $"{LogTag} corner-estimate selected={selected.Id} touch={Format(hit.pose.position)} " +
                    $"floorY={floorY.ToString("0.000", CultureInfo.InvariantCulture)} accepted=false reason=\"{estimate.Rejection}\"");
                return;
            }

            Vector3 corner = estimate.Corner;
            lastRejection = string.Empty;
            if (!capture.TryAdd(corner, out string rejection))
            {
                Reject(rejection);
                Debug.LogWarning($"{LogTag} rejected estimatedCorner={Format(corner)} reason=\"{rejection}\"");
                return;
            }

            EnsureMarkerRoot();
            CreateMarker(capture.Count, corner);
            sessionLog?.Accept();
            sessionLog?.Write(
                "corner",
                "\"index\":" + capture.Count.ToString(CultureInfo.InvariantCulture) +
                ",\"corner\":" + JsonVector(corner) +
                ",\"selected\":" + ScanSessionLog.Quote(estimate.SelectedWallId) +
                ",\"other\":" + ScanSessionLog.Quote(estimate.OtherWallId));
            highlightedSelected = estimate.SelectedWallId;
            highlightedOther = estimate.OtherWallId;
            pulseUntil = Time.time + 0.35f;
            LogEstimate(capture.Count, hit.pose.position, estimate);
            RefreshPresentation();

            if (!capture.IsComplete)
            {
                status = $"Estimated corner {capture.Count}/4. Select the next wall.";
                return;
            }

            BuildRoom();
        }

        bool TryGetSelectedWall(out ARRaycastHit chosen, out ARPlane plane)
        {
            for (int i = 0; i < Hits.Count; i++)
            {
                if (Hits[i].trackable is ARPlane wall && wall.alignment == PlaneAlignment.Vertical)
                {
                    chosen = Hits[i];
                    plane = wall;
                    return true;
                }
            }

            chosen = default;
            plane = null;
            return false;
        }

        bool TryResolveFloorY(out float floorY, out string error)
        {
            if (capture.HasReferenceFloorY)
            {
                floorY = capture.ReferenceFloorY;
                error = null;
                return true;
            }

            CollectFloorSamples();
            return WallCornerSolver.TryChooseFloorY(floorSamples, floorHeightToleranceMeters, out floorY, out error);
        }

        static DetectedWall ToWall(ARPlane plane)
        {
            Vector2[] boundary = null;
            if (plane.boundary.IsCreated && plane.boundary.Length >= 3)
            {
                boundary = new Vector2[plane.boundary.Length];
                for (int i = 0; i < plane.boundary.Length; i++)
                {
                    boundary[i] = plane.boundary[i];
                }
            }

            return new DetectedWall
            {
                Id = plane.trackableId.ToString(),
                Point = plane.transform.position,
                Normal = plane.normal,
                Center = plane.center,
                AxisU = plane.transform.right,
                AxisV = plane.transform.forward,
                HalfExtents = plane.extents,
                Boundary = boundary
            };
        }

        void LogEstimate(int index, Vector3 touch, WallCornerEstimate estimate)
        {
            Debug.Log(
                $"{LogTag} corner={index}/4 estimated=true not-a-survey " +
                $"selected={estimate.SelectedWallId} other={estimate.OtherWallId} " +
                $"touch={Format(touch)} corner={Format(estimate.Corner)} " +
                $"line={Format(estimate.IntersectionDirection)} " +
                $"floorY={estimate.FloorY.ToString("0.000", CultureInfo.InvariantCulture)} " +
                $"residualM={estimate.PlaneResidualMeters.ToString("0.000", CultureInfo.InvariantCulture)} " +
                $"boundaryOverflowM={estimate.BoundaryOverflowMeters.ToString("0.000", CultureInfo.InvariantCulture)} " +
                $"normalCross={estimate.NormalCrossMagnitude.ToString("0.000", CultureInfo.InvariantCulture)} " +
                $"touchToCornerM={Vector3.Distance(touch, estimate.Corner).ToString("0.000", CultureInfo.InvariantCulture)}");
        }

        string DescribeHorizontalYs()
        {
            logBuilder.Clear();
            if (planeManager == null)
            {
                return "none";
            }

            bool any = false;
            foreach (ARPlane plane in planeManager.trackables)
            {
                if (plane.alignment != PlaneAlignment.HorizontalUp)
                {
                    continue;
                }

                if (any)
                {
                    logBuilder.Append(',');
                }

                any = true;
                logBuilder.Append(plane.center.y.ToString("0.000", CultureInfo.InvariantCulture));
            }

            return any ? logBuilder.ToString() : "none";
        }

        void BuildRoom()
        {
            bool mapped = RoomLocalFrame.TryMap(
                capture.WorldCorners,
                wallHeightMeters,
                out RoomGeometryData data,
                out Vector3 anchor,
                out float floorHeight,
                out float[] edges,
                out string error);

            LogEdges(edges, floorHeight, mapped, error);
            if (!mapped)
            {
                meshBuilt = false;
                status = "Room rejected: " + error;
                Debug.LogWarning($"{LogTag} mesh=not-built reason=\"{error}\"");
                return;
            }

            EnsureRoomAnchor(anchor);
            ClearChildren(roomAnchor);
            CreateMeshObject(roomAnchor, "Floor", RoomMeshGenerator.BuildFloor(data.floorCornersXz), floorMaterial, new Color(0.62f, 0.48f, 0.32f));
            Mesh[] walls = RoomMeshGenerator.BuildWalls(data.floorCornersXz, data.wallHeightMeters);
            for (int i = 0; i < walls.Length; i++)
            {
                CreateMeshObject(roomAnchor, walls[i].name, walls[i], wallMaterial, new Color(0.62f, 0.74f, 0.82f));
            }

            meshBuilt = true;
            status = "Room built. Walk around to check that it stays in place.";
            Debug.Log(
                $"{LogTag} mesh=built anchor={Format(anchor)} wallHeight={wallHeightMeters.ToString("0.00", CultureInfo.InvariantCulture)} " +
                "parent=XROrigin rotation=identity");
        }

        void LogEdges(float[] edges, float floorHeight, bool polygonValid, string error)
        {
            logBuilder.Clear();
            logBuilder.Append(LogTag).Append(" edges-horizontal-m=");
            if (edges == null)
            {
                logBuilder.Append("none");
            }
            else
            {
                for (int i = 0; i < edges.Length; i++)
                {
                    if (i > 0)
                    {
                        logBuilder.Append(',');
                    }

                    logBuilder.Append((i + 1).ToString(CultureInfo.InvariantCulture));
                    logBuilder.Append('-');
                    logBuilder.Append(((i + 1) % edges.Length + 1).ToString(CultureInfo.InvariantCulture));
                    logBuilder.Append('=');
                    logBuilder.Append(edges[i].ToString("0.000", CultureInfo.InvariantCulture));
                }
            }

            logBuilder.Append(" floorY=").Append(floorHeight.ToString("0.000", CultureInfo.InvariantCulture));
            logBuilder.Append(" polygon=").Append(polygonValid ? "ok" : "fail");
            if (!polygonValid)
            {
                logBuilder.Append(" reason=\"").Append(error).Append('"');
            }

            logBuilder.Append(" estimated-edges=not a survey measurement");
            Debug.Log(logBuilder.ToString());

            IReadOnlyList<Vector3> corners = capture.WorldCorners;
            for (int i = 0; i < corners.Count; i++)
            {
                Vector3 corner = corners[i];
                Vector2 local = new Vector2(corner.x - corners[0].x, corner.z - corners[0].z);
                Debug.Log($"{LogTag} local corner={i + 1} xz={local.x.ToString("0.000", CultureInfo.InvariantCulture)},{local.y.ToString("0.000", CultureInfo.InvariantCulture)} worldY={corner.y.ToString("0.000", CultureInfo.InvariantCulture)}");
            }
        }

        void Reject(string reason)
        {
            lastRejection = reason;
            status = ScanFeedback.ToKorean(reason);
            sessionLog?.Reject();
            sessionLog?.Write("reject", "\"reason\":" + ScanSessionLog.Quote(reason));
            Debug.LogWarning($"{LogTag} rejected \"{reason}\"");
        }

        void OnSessionStateChanged(ARSessionStateChangedEventArgs args)
        {
            Debug.Log($"{LogTag} tracking {lastSessionState} -> {args.state}");
            sessionLog?.Write("tracking", "\"from\":" + ScanSessionLog.Quote(lastSessionState.ToString()) + ",\"to\":" + ScanSessionLog.Quote(args.state.ToString()));
            lastSessionState = args.state;
            if (waitingForRescan && args.state == ARSessionState.SessionTracking)
            {
                waitingForRescan = false;
                status = "추적이 다시 준비됐습니다. 벽을 선택하세요.";
                Debug.Log(LogTag + " rescan-ready=true");
            }
        }

        void EnsureMarkerRoot()
        {
            if (markerRoot != null)
            {
                return;
            }

            var rootObject = new GameObject("FieldTestCorners");
            rootObject.transform.SetParent(sessionRoot, false);
            markerRoot = rootObject.transform;
        }

        void EnsureRoomAnchor(Vector3 anchorWorld)
        {
            if (roomAnchor == null)
            {
                var anchorObject = new GameObject("FieldTestRoom");
                anchorObject.transform.SetParent(sessionRoot, false);
                roomAnchor = anchorObject.transform;
            }

            roomAnchor.SetPositionAndRotation(anchorWorld, Quaternion.identity);
        }

        void CreateMarker(int number, Vector3 world)
        {
            GameObject marker = markerPrefab != null
                ? Instantiate(markerPrefab, world, Quaternion.identity, markerRoot)
                : CreateMarkerObject(world);
            marker.name = $"Corner_{number}";
            marker.transform.localScale = Vector3.one * MarkerSizeMeters * 1.6f;
            pulseMarker = marker.transform;

            Renderer renderer = marker.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            if (renderer.sharedMaterial == null)
            {
                renderer.sharedMaterial = CreateUnlit("FieldTestCorner", Color.white);
            }

            var block = new MaterialPropertyBlock();
            Color color = NumberColor(number);
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);
            renderer.SetPropertyBlock(block);
        }

        GameObject CreateMarkerObject(Vector3 world)
        {
            var marker = new GameObject("Corner");
            marker.transform.SetParent(markerRoot, false);
            marker.transform.position = world;
            marker.AddComponent<MeshFilter>().sharedMesh = SharedSphereMesh();
            marker.AddComponent<MeshRenderer>();
            return marker;
        }

        static Mesh sharedSphereMesh;

        static Mesh SharedSphereMesh()
        {
            if (sharedSphereMesh != null)
            {
                return sharedSphereMesh;
            }

            sharedSphereMesh = new Mesh { name = "FieldTestCornerSphere" };
            const int segments = 8;
            const int rings = 6;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int ring = 0; ring <= rings; ring++)
            {
                float v = ring / (float)rings;
                float phi = Mathf.PI * v;
                for (int segment = 0; segment <= segments; segment++)
                {
                    float u = segment / (float)segments;
                    float theta = 2f * Mathf.PI * u;
                    vertices.Add(new Vector3(
                        Mathf.Sin(phi) * Mathf.Cos(theta),
                        Mathf.Cos(phi),
                        Mathf.Sin(phi) * Mathf.Sin(theta)) * 0.5f);
                }
            }

            int stride = segments + 1;
            for (int ring = 0; ring < rings; ring++)
            {
                for (int segment = 0; segment < segments; segment++)
                {
                    int a = ring * stride + segment;
                    int b = a + stride;
                    triangles.Add(a);
                    triangles.Add(a + 1);
                    triangles.Add(b);
                    triangles.Add(a + 1);
                    triangles.Add(b + 1);
                    triangles.Add(b);
                }
            }

            sharedSphereMesh.SetVertices(vertices);
            sharedSphereMesh.SetTriangles(triangles, 0);
            sharedSphereMesh.RecalculateNormals();
            return sharedSphereMesh;
        }

        string DescribeHits()
        {
            logBuilder.Clear();
            for (int i = 0; i < Hits.Count; i++)
            {
                if (i > 0)
                {
                    logBuilder.Append(';');
                }

                ARRaycastHit hit = Hits[i];
                string alignment = hit.trackable is ARPlane plane ? plane.alignment.ToString() : hit.hitType.ToString();
                logBuilder.Append(alignment);
                logBuilder.Append(" id=");
                logBuilder.Append(hit.trackableId);
                logBuilder.Append(" y=");
                logBuilder.Append(hit.pose.position.y.ToString("0.000", CultureInfo.InvariantCulture));
            }

            return logBuilder.ToString();
        }

        void CreateMeshObject(Transform parent, string objectName, Mesh mesh, Material material, Color fallback)
        {
            var meshObject = new GameObject(objectName);
            meshObject.transform.SetParent(parent, false);
            meshObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = meshObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = CreateScanMaterial(objectName == "Floor");
        }

        static Material CreateScanMaterial(bool floor)
        {
            Shader shader = Shader.Find("RoomTwin/ArPlaneTransparent");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            var material = new Material(shader);
            Color color = floor
                ? new Color(0.55f, 0.62f, 0.4f, 0.16f)
                : new Color(0.55f, 0.75f, 0.9f, 0.22f);
            material.color = color;
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            return material;
        }

        static Material CreateUnlit(string materialName, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            var material = new Material(shader) { name = materialName, color = color };
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            return material;
        }

        static Color NumberColor(int number)
        {
            return number switch
            {
                1 => new Color(0.95f, 0.25f, 0.2f),
                2 => new Color(0.95f, 0.75f, 0.15f),
                3 => new Color(0.2f, 0.75f, 0.35f),
                _ => new Color(0.25f, 0.55f, 0.95f)
            };
        }

        static void ClearChildren(Transform parent)
        {
            if (parent == null)
            {
                return;
            }

            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Destroy(parent.GetChild(i).gameObject);
            }
        }

        static void DestroyChild(Transform child)
        {
            if (child != null)
            {
                Destroy(child.gameObject);
            }
        }

        void OnGUI()
        {
            if (!UsingEmergencyHud)
            {
                return;
            }

            GUI.Label(new Rect(24f, 24f, Screen.width - 48f, 120f), "HUD failed. " + status);
            if (GUI.Button(new Rect(24f, 160f, 220f, 72f), "Reset"))
            {
                ResetCapture();
            }
        }

        void RefreshPresentation()
        {
            CollectFloorSamples();
            bool floorChosen = WallCornerSolver.TryChooseFloorY(
                floorSamples, floorHeightToleranceMeters, out float candidateFloorY, out _);
            bool floorReady = capture.HasReferenceFloorY || floorChosen;
            var snapshot = new ScanSnapshot
            {
                IsTracking = ARSession.state == ARSessionState.SessionTracking,
                FloorLocked = floorReady,
                FloorUncertain = !capture.HasReferenceFloorY && !floorChosen && floorSamples.Count > 0,
                VerticalWallCount = CountVerticalWalls(),
                HasIntersectablePair = HasSolverValidPair(),
                CornerCount = capture.Count,
                RoomBuilt = meshBuilt
            };
            string failure = string.IsNullOrEmpty(lastRejection) ? null : ScanFeedback.ToKorean(lastRejection);
            if (hud != null && hud.IsReady)
            {
                hud.Apply(snapshot, failure, BuildDebugText(candidateFloorY, floorChosen));
            }
            CollectVisibleWalls();
            outlines?.Sync(planeManager, highlightedSelected, highlightedOther, Time.time < pulseUntil, meshBuilt, visibleWallIds);
            WriteSnapshotIfDue();
        }

        void CollectFloorSamples()
        {
            floorSamples.Clear();
            if (planeManager == null)
            {
                return;
            }

            foreach (ARPlane plane in planeManager.trackables)
            {
                if (plane.alignment != PlaneAlignment.HorizontalUp)
                {
                    continue;
                }

                floorSamples.Add(new WallCornerSolver.FloorSample
                {
                    Height = plane.center.y,
                    AreaSquareMeters = Mathf.Max(0f, plane.size.x * plane.size.y)
                });
            }
        }

        int CountVerticalWalls()
        {
            if (planeManager == null)
            {
                return 0;
            }

            int count = 0;
            foreach (ARPlane plane in planeManager.trackables)
            {
                if (plane.alignment == PlaneAlignment.Vertical)
                {
                    count++;
                }
            }

            return count;
        }

        bool HasSolverValidPair()
        {
            if (planeManager == null || !TryResolveFloorY(out float floorY, out _))
            {
                return false;
            }

            verticalWalls.Clear();
            foreach (ARPlane plane in planeManager.trackables)
            {
                if (plane.alignment == PlaneAlignment.Vertical)
                {
                    verticalWalls.Add(ToWall(plane));
                }
            }

            for (int i = 0; i < verticalWalls.Count; i++)
            {
                for (int j = i + 1; j < verticalWalls.Count; j++)
                {
                    if (WallCornerSolver.TryPair(verticalWalls[i], verticalWalls[j], floorY, out WallCornerEstimate estimate)
                        && estimate.Accepted)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        string BuildDebugText(float candidateFloorY, bool floorChosen)
        {
            logBuilder.Clear();
            logBuilder.Append("Tracking: ").Append(ARSession.state);
            logBuilder.Append("\nFloor locked Y: ");
            if (capture.HasReferenceFloorY)
            {
                logBuilder.Append(capture.ReferenceFloorY.ToString("0.000", CultureInfo.InvariantCulture));
            }
            else
            {
                logBuilder.Append("no");
            }

            logBuilder.Append("\nFloor candidate Y: ");
            logBuilder.Append(floorChosen
                ? candidateFloorY.ToString("0.000", CultureInfo.InvariantCulture)
                : "not chosen");
            logBuilder.Append("\nFloor samples:");
            for (int i = 0; i < floorSamples.Count; i++)
            {
                logBuilder.Append("\n  y=");
                logBuilder.Append(floorSamples[i].Height.ToString("0.000", CultureInfo.InvariantCulture));
                logBuilder.Append(" area=");
                logBuilder.Append(floorSamples[i].AreaSquareMeters.ToString("0.00", CultureInfo.InvariantCulture));
            }

            logBuilder.Append("\nVertical walls: ").Append(CountVerticalWalls());
            logBuilder.Append("\nCorners: ").Append(capture.Count).Append("/4");
            logBuilder.Append("\nLast failure: ");
            logBuilder.Append(string.IsNullOrEmpty(lastRejection) ? "none" : lastRejection);
            logBuilder.Append("\nLog: ").Append(logStatus);
            if (sessionLog != null)
            {
                logBuilder.Append("\n").Append(sessionLog.FilePath);
                logBuilder.Append("\naccepted=").Append(sessionLog.AcceptedCount.ToString(CultureInfo.InvariantCulture));
                logBuilder.Append(" rejected=").Append(sessionLog.RejectedCount.ToString(CultureInfo.InvariantCulture));
            }

            return logBuilder.ToString();
        }

        void CollectVisibleWalls()
        {
            visibleWallIds.Clear();
            if (meshBuilt || planeManager == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(highlightedSelected))
            {
                visibleWallIds.Add(highlightedSelected);
            }

            if (!string.IsNullOrEmpty(highlightedOther))
            {
                visibleWallIds.Add(highlightedOther);
            }

            string largestId = null;
            float largestArea = 0f;
            int shown = visibleWallIds.Count;
            foreach (ARPlane plane in planeManager.trackables)
            {
                if (plane.alignment != PlaneAlignment.Vertical || shown >= 4)
                {
                    continue;
                }

                float area = plane.size.x * plane.size.y;
                string id = plane.trackableId.ToString();
                if (visibleWallIds.Contains(id))
                {
                    continue;
                }

                if (area > largestArea)
                {
                    largestArea = area;
                    largestId = id;
                }
            }

            if (largestId != null && shown < 4)
            {
                visibleWallIds.Add(largestId);
            }
        }

        static bool TryGetPressScreenPosition(out Vector2 screenPosition)
        {
            screenPosition = default;
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

        static string Format(Vector3 value)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "({0:0.000},{1:0.000},{2:0.000})",
                value.x,
                value.y,
                value.z);
        }
    }
}

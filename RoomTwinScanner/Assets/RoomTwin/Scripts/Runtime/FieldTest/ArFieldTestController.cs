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
        readonly List<FloorObservation> floorObservations = new List<FloorObservation>(8);
        readonly List<float> wallBottoms = new List<float>(8);
        readonly List<WallObservation> wallObservations = new List<WallObservation>(16);
        readonly List<string> intendedPlaneIds = new List<string>(8);
        readonly List<string> requiredClusterIds = new List<string>(4);
        readonly Dictionary<string, float> firstSeenAt = new Dictionary<string, float>(32);
        readonly WallClusterBuilder wallClusters = new WallClusterBuilder();
        readonly FloorHeightEstimator floorEstimator = new FloorHeightEstimator();
        readonly RoomReconstructionSolver reconstructionSolver = new RoomReconstructionSolver();
        ArScanHud hud;
        ArScanWallOutlines outlines;
        readonly List<string> visibleWallIds = new List<string>(8);
        string highlightedSelected = string.Empty;
        string highlightedOther = string.Empty;
        ScanSessionLog sessionLog;
        bool waitingForRescan;
        float nextSnapshotTime;
        string logStatus = "starting";
        WallClusterSettings clusterSettings = WallClusterSettings.CreateDefault();
        Vector3[] previewCorners;
        string[] previewWallIds;
        bool previewReady;
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
                hud.BindConfirm(ConfirmPreview);
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
                ",\"planes\":" + PlaneSnapshotJson() +
                ",\"clusters\":" + ClusterSnapshotJson());
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
                builder.Append(",\"position\":").Append(JsonVector(plane.transform.position));
                builder.Append(",\"axisU\":").Append(JsonVector(plane.transform.right));
                builder.Append(",\"axisV\":").Append(JsonVector(plane.transform.forward));
                builder.Append(",\"extents\":").Append(string.Format(CultureInfo.InvariantCulture, "[{0:0.000},{1:0.000}]", plane.extents.x, plane.extents.y));
                builder.Append(",\"area\":").Append((plane.size.x * plane.size.y).ToString("0.00", CultureInfo.InvariantCulture));
                builder.Append(",\"tracking\":").Append(ScanSessionLog.Quote(plane.trackingState.ToString()));
                builder.Append(",\"subsumed\":").Append(plane.subsumedBy != null ? "true" : "false");
                builder.Append(",\"boundary\":[");
                if (plane.boundary.IsCreated)
                {
                    int limit = plane.boundary.Length < 12 ? plane.boundary.Length : 12;
                    for (int i = 0; i < limit; i++)
                    {
                        if (i > 0)
                        {
                            builder.Append(',');
                        }

                        builder.Append(string.Format(CultureInfo.InvariantCulture, "[{0:0.000},{1:0.000}]", plane.boundary[i].x, plane.boundary[i].y));
                    }
                }

                builder.Append("]}");
            }

            builder.Append(']');
            return builder.ToString();
        }

        string ClusterSnapshotJson()
        {
            var builder = new StringBuilder(256);
            builder.Append("{\"kept\":[");
            AppendClusters(builder, wallClusters.Clusters, false);
            builder.Append("],\"discarded\":[");
            AppendClusters(builder, wallClusters.Discarded, true);
            builder.Append("]}");
            return builder.ToString();
        }

        static void AppendClusters(StringBuilder builder, List<WallCluster> clusters, bool includeReason)
        {
            for (int i = 0; i < clusters.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                WallCluster cluster = clusters[i];
                builder.Append("{\"id\":").Append(ScanSessionLog.Quote(cluster.Id));
                builder.Append(",\"confidence\":").Append(cluster.Confidence.ToString("0.00", CultureInfo.InvariantCulture));
                builder.Append(",\"area\":").Append(cluster.AreaSquareMeters.ToString("0.00", CultureInfo.InvariantCulture));
                builder.Append(",\"members\":[");
                for (int m = 0; m < cluster.MemberIds.Count; m++)
                {
                    if (m > 0)
                    {
                        builder.Append(',');
                    }

                    builder.Append(ScanSessionLog.Quote(cluster.MemberIds[m]));
                }

                builder.Append(']');
                if (includeReason)
                {
                    builder.Append(",\"reason\":").Append(ScanSessionLog.Quote(cluster.DiscardReason));
                }

                builder.Append('}');
            }
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
            if (meshBuilt)
            {
                meshBuilt = false;
                capture.Reset();
                DestroyChild(roomAnchor);
                roomAnchor = null;
                previewReady = false;
                status = "만든 방 메시를 취소했습니다. 스캔 데이터는 유지됩니다.";
                sessionLog?.Write("undo", "\"mesh\":false");
                RefreshPresentation();
                return;
            }

            if (intendedPlaneIds.Count == 0)
            {
                status = "취소할 벽 선택이 없습니다.";
                return;
            }

            intendedPlaneIds.RemoveAt(intendedPlaneIds.Count - 1);
            capture.Reset();
            previewReady = false;
            previewCorners = null;
            ClearChildren(markerRoot);
            lastRejection = string.Empty;
            status = "마지막 벽 선택을 취소했습니다. 스캔 데이터는 유지됩니다.";
            sessionLog?.Write("undo", "\"intended\":" + intendedPlaneIds.Count.ToString(CultureInfo.InvariantCulture));
            Debug.Log(LogTag + " undo intended=" + intendedPlaneIds.Count);
            RefreshPresentation();
        }

        public void ConfirmPreview()
        {
            if (!previewReady || previewCorners == null || previewCorners.Length != 4)
            {
                status = "확정할 방 윤곽이 없습니다. 벽을 더 비춰 주세요.";
                return;
            }

            if (waitingForRescan || ARSession.state != ARSessionState.SessionTracking)
            {
                Reject("AR tracking is not ready. Wait before confirming a corner.");
                return;
            }

            capture.Reset();
            for (int i = 0; i < previewCorners.Length; i++)
            {
                if (!capture.TryAdd(previewCorners[i], out string rejection))
                {
                    capture.Reset();
                    Reject(rejection);
                    return;
                }
            }

            ClearChildren(markerRoot);
            EnsureMarkerRoot();
            for (int i = 0; i < previewCorners.Length; i++)
            {
                CreateMarker(i + 1, previewCorners[i], false);
            }

            sessionLog?.Write("confirm", "\"corners\":4");
            BuildRoom();
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
            intendedPlaneIds.Clear();
            requiredClusterIds.Clear();
            firstSeenAt.Clear();
            previewCorners = null;
            previewWallIds = null;
            previewReady = false;
            wallObservations.Clear();
            hud?.ClearFailureDisplay();
            Debug.Log(LogTag + " reset referenceFloorY=cleared");
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

            if (meshBuilt)
            {
                Reject("The room is already built. Start a new scan to measure again.");
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

            RememberSeen(selectedPlane.trackableId.ToString());
            UpdateClusters(floorY, true);
            WallCluster selectedCluster = wallClusters.FindByMember(selectedPlane.trackableId.ToString());
            if (selectedCluster == null || !string.IsNullOrEmpty(selectedCluster.DiscardReason))
            {
                Reject("The selected surface is not a stable wall candidate.");
                return;
            }

            string planeId = selectedPlane.trackableId.ToString();
            bool addedIntent = false;
            if (!intendedPlaneIds.Contains(planeId))
            {
                if (intendedPlaneIds.Count >= 12)
                {
                    intendedPlaneIds.RemoveAt(0);
                }

                intendedPlaneIds.Add(planeId);
                addedIntent = true;
            }

            highlightedSelected = planeId;
            highlightedOther = string.Empty;
            sessionLog?.Write(
                "intent",
                "\"plane\":" + ScanSessionLog.Quote(planeId) +
                ",\"cluster\":" + ScanSessionLog.Quote(selectedCluster.Id) +
                ",\"touch\":" + JsonVector(hit.pose.position) +
                ",\"floorY\":" + floorY.ToString("0.000", CultureInfo.InvariantCulture));
            Debug.Log(LogTag + " intent plane=" + planeId + " cluster=" + selectedCluster.Id + " touch=" + Format(hit.pose.position));
            if (!TryUpdatePreview(floorY))
            {
                Reject(string.IsNullOrEmpty(lastRejection) ? "Not enough wall information to close a room." : lastRejection);
                return;
            }

            if (addedIntent)
            {
                sessionLog?.Accept();
            }

            lastRejection = string.Empty;
            status = "방 윤곽 미리보기입니다. 맞으면 확정하세요. 추정 길이는 실측이 아닙니다.";
            RefreshPresentation();
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
            UpdateClusters(float.NaN, false);
            return floorEstimator.TryChoose(
                floorObservations,
                arCamera != null ? arCamera.transform.position.y : float.NaN,
                floorHeightToleranceMeters,
                wallBottoms,
                out floorY,
                out error);
        }

        static DetectedWall ToWall(ARPlane plane)
        {
            return new DetectedWall
            {
                Id = plane.trackableId.ToString(),
                Point = plane.transform.position,
                Normal = plane.normal,
                Center = plane.center,
                AxisU = plane.transform.right,
                AxisV = plane.transform.forward,
                HalfExtents = plane.extents
            };
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
            sessionLog?.Write(
                "geometry",
                "\"built\":" + (polygonValid ? "true" : "false") +
                ",\"reason\":" + ScanSessionLog.Quote(error) +
                ",\"edges\":" + ScanSessionLog.Quote(logBuilder.ToString()));

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

        void CreateMarker(int number, Vector3 world, bool candidate)
        {
            GameObject marker = markerPrefab != null
                ? Instantiate(markerPrefab, world, Quaternion.identity, markerRoot)
                : CreateMarkerObject(world);
            marker.name = (candidate ? "Candidate_" : "Corner_") + number;
            float scale = candidate ? MarkerSizeMeters * 0.7f : MarkerSizeMeters * 1.6f;
            marker.transform.localScale = Vector3.one * scale;
            if (!candidate)
            {
                pulseMarker = marker.transform;
            }

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
            Color color = candidate ? new Color(0.75f, 0.86f, 1f) : NumberColor(number);
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);
            renderer.SetPropertyBlock(block);
            var labelObject = new GameObject("Number");
            labelObject.transform.SetParent(marker.transform, false);
            labelObject.transform.localPosition = new Vector3(0f, 1.4f, 0f);
            var label = labelObject.AddComponent<TextMesh>();
            label.text = number.ToString(CultureInfo.InvariantCulture);
            label.characterSize = candidate ? 0.08f : 0.12f;
            label.anchor = TextAnchor.MiddleCenter;
            label.color = color;
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
            bool floorChosen = TryResolveFloorY(out float candidateFloorY, out _);
            if (!meshBuilt && intendedPlaneIds.Count > 0 && floorChosen)
            {
                TryUpdatePreview(candidateFloorY);
            }

            bool floorReady = capture.HasReferenceFloorY || floorChosen;
            var snapshot = new ScanSnapshot
            {
                IsTracking = ARSession.state == ARSessionState.SessionTracking,
                FloorLocked = floorReady,
                FloorUncertain = !capture.HasReferenceFloorY && !floorChosen && floorObservations.Count > 0,
                VerticalWallCount = wallClusters.Clusters.Count,
                HasIntersectablePair = floorChosen && HasClosedRoom(candidateFloorY),
                CornerCount = meshBuilt ? capture.Count : previewReady ? 4 : 0,
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
            floorObservations.Clear();
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

                string id = plane.trackableId.ToString();
                RememberSeen(id);
                float area = Mathf.Max(0f, plane.size.x * plane.size.y);
                float seconds = ObservedSeconds(id);
                floorSamples.Add(new WallCornerSolver.FloorSample
                {
                    Height = plane.center.y,
                    AreaSquareMeters = area
                });
                floorObservations.Add(new FloorObservation
                {
                    Id = id,
                    Height = plane.center.y,
                    AreaSquareMeters = area,
                    ObservedSeconds = seconds
                });
            }
        }

        void UpdateClusters(float floorY, bool hasFloor)
        {
            wallObservations.Clear();
            if (planeManager != null)
            {
                foreach (ARPlane plane in planeManager.trackables)
                {
                    if (plane.alignment != PlaneAlignment.Vertical)
                    {
                        continue;
                    }

                    string id = plane.trackableId.ToString();
                    RememberSeen(id);
                    wallObservations.Add(ToObservation(plane));
                }
            }

            wallClusters.Rebuild(wallObservations, ActiveSettings(), floorY, hasFloor);
            wallBottoms.Clear();
            for (int i = 0; i < wallClusters.Clusters.Count; i++)
            {
                WallCluster cluster = wallClusters.Clusters[i];
                if (string.IsNullOrEmpty(cluster.DiscardReason))
                {
                    wallBottoms.Add(cluster.VerticalMin);
                }
            }
        }

        bool TryUpdatePreview(float floorY)
        {
            UpdateClusters(floorY, true);
            CollectRequiredClusters();
            if (!reconstructionSolver.TryBuild(
                    wallClusters.Clusters,
                    floorY,
                    requiredClusterIds,
                    ActiveSettings(),
                    out RoomReconstruction result))
            {
                previewReady = false;
                previewCorners = null;
                previewWallIds = null;
                lastRejection = result.Rejection;
                pulseMarker = null;
                ClearChildren(markerRoot);
                return false;
            }

            previewReady = true;
            previewWallIds = result.WallIds;
            lastRejection = string.Empty;
            if (!SamePreview(result.Corners))
            {
                previewCorners = result.Corners;
                pulseMarker = null;
                ClearChildren(markerRoot);
                EnsureMarkerRoot();
                for (int i = 0; i < previewCorners.Length; i++)
                {
                    CreateMarker(i + 1, previewCorners[i], true);
                }

                sessionLog?.Write("preview", PreviewJson(result));
            }

            return true;
        }

        bool HasClosedRoom(float floorY)
        {
            UpdateClusters(floorY, true);
            return reconstructionSolver.TryBuild(
                wallClusters.Clusters,
                floorY,
                null,
                ActiveSettings(),
                out _);
        }

        void CollectRequiredClusters()
        {
            requiredClusterIds.Clear();
            for (int i = 0; i < intendedPlaneIds.Count; i++)
            {
                WallCluster cluster = wallClusters.FindByMember(intendedPlaneIds[i]);
                if (cluster == null || requiredClusterIds.Contains(cluster.Id))
                {
                    continue;
                }

                requiredClusterIds.Add(cluster.Id);
            }
        }

        WallClusterSettings ActiveSettings()
        {
            return WallClusterSettings.Resolve(clusterSettings);
        }

        void RememberSeen(string id)
        {
            if (!firstSeenAt.ContainsKey(id))
            {
                firstSeenAt.Add(id, Time.time);
            }
        }

        float ObservedSeconds(string id)
        {
            return firstSeenAt.TryGetValue(id, out float seenAt) ? Mathf.Max(0f, Time.time - seenAt) : 0f;
        }

        WallObservation ToObservation(ARPlane plane)
        {
            string id = plane.trackableId.ToString();
            WallTrackingQuality tracking = WallTrackingQuality.None;
            if (plane.trackingState == TrackingState.Tracking)
            {
                tracking = WallTrackingQuality.Tracking;
            }
            else if (plane.trackingState == TrackingState.Limited)
            {
                tracking = WallTrackingQuality.Limited;
            }

            return new WallObservation
            {
                Wall = ToWall(plane),
                AreaSquareMeters = Mathf.Max(0f, plane.size.x * plane.size.y),
                ObservedSeconds = ObservedSeconds(id),
                Subsumed = plane.subsumedBy != null,
                Tracking = tracking
            };
        }

        bool SamePreview(Vector3[] next)
        {
            if (previewCorners == null || next == null || previewCorners.Length != next.Length)
            {
                return false;
            }

            for (int i = 0; i < next.Length; i++)
            {
                if ((previewCorners[i] - next[i]).sqrMagnitude > 0.0004f)
                {
                    return false;
                }
            }

            return true;
        }

        static string PreviewJson(RoomReconstruction result)
        {
            var builder = new StringBuilder(128);
            builder.Append("\"confidence\":").Append(result.Confidence.ToString("0.00", CultureInfo.InvariantCulture));
            builder.Append(",\"corners\":[");
            for (int i = 0; i < result.Corners.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                builder.Append(JsonVector(result.Corners[i]));
            }

            builder.Append("],\"edges\":[");
            for (int i = 0; i < result.EdgeLengths.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                builder.Append(result.EdgeLengths[i].ToString("0.000", CultureInfo.InvariantCulture));
            }

            builder.Append(']');
            return builder.ToString();
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

            logBuilder.Append("\nWall clusters: ").Append(wallClusters.Clusters.Count.ToString(CultureInfo.InvariantCulture));
            logBuilder.Append("\nDiscarded: ").Append(wallClusters.Discarded.Count.ToString(CultureInfo.InvariantCulture));
            logBuilder.Append("\nPreview: ").Append(previewReady ? "yes" : "no");
            logBuilder.Append("\nCorners: ").Append(capture.Count.ToString(CultureInfo.InvariantCulture)).Append("/4");
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

            if (previewWallIds != null)
            {
                for (int i = 0; i < previewWallIds.Length; i++)
                {
                    AddClusterMembers(previewWallIds[i]);
                }
            }

            for (int i = 0; i < intendedPlaneIds.Count; i++)
            {
                AddVisible(intendedPlaneIds[i]);
            }

            if (visibleWallIds.Count > 0)
            {
                return;
            }

            WallCluster largest = null;
            for (int i = 0; i < wallClusters.Clusters.Count; i++)
            {
                WallCluster cluster = wallClusters.Clusters[i];
                if (largest == null || cluster.AreaSquareMeters > largest.AreaSquareMeters)
                {
                    largest = cluster;
                }
            }

            if (largest != null)
            {
                AddClusterMembers(largest.Id);
            }
        }

        void AddClusterMembers(string clusterId)
        {
            for (int i = 0; i < wallClusters.Clusters.Count; i++)
            {
                WallCluster cluster = wallClusters.Clusters[i];
                if (cluster.Id != clusterId)
                {
                    continue;
                }

                for (int m = 0; m < cluster.MemberIds.Count; m++)
                {
                    AddVisible(cluster.MemberIds[m]);
                }
            }
        }

        void AddVisible(string id)
        {
            if (string.IsNullOrEmpty(id) || visibleWallIds.Count >= 8 || visibleWallIds.Contains(id))
            {
                return;
            }

            visibleWallIds.Add(id);
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

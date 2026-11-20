using System.Collections.Generic;
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
        Transform sessionRoot;
        Transform markerRoot;
        Transform roomAnchor;
        string status = "Select floor corner 1.";
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

        void Awake()
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
        }

        void Update()
        {
            if (!TryGetPressScreenPosition(out Vector2 screen))
            {
                return;
            }

            if (IsScreenPointOverReset(screen))
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

        public void ResetCapture()
        {
            capture.Reset();
            meshBuilt = false;
            lastRejection = string.Empty;
            status = "Select floor corner 1.";
            DestroyChild(markerRoot);
            DestroyChild(roomAnchor);
            markerRoot = null;
            roomAnchor = null;
            Debug.Log($"{LogTag} reset referenceFloorY=cleared");
        }

        void TrySelectCorner(Vector2 screen)
        {
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
                Reject("No plane hit. Point the camera at the floor. The touch may also be outside a detected plane boundary.");
                Debug.LogWarning($"{LogTag} rejected hits=0 alignment=none");
                return;
            }

            if (!TryGetHorizontalFloorHit(out ARRaycastHit hit, out ARPlane plane))
            {
                Reject("The hit is not a HorizontalUp surface. Point the camera at the floor, not a wall.");
                Debug.LogWarning($"{LogTag} rejected hits={Hits.Count} candidates={DescribeHits()}");
                return;
            }

            Vector3 world = hit.pose.position;
            bool hadReference = capture.HasReferenceFloorY;
            float referenceY = hadReference ? capture.ReferenceFloorY : world.y;
            float deltaY = world.y - referenceY;
            if (!capture.TryAdd(world, out string rejection))
            {
                Reject(rejection);
                Debug.LogWarning(
                    $"{LogTag} rejected hits={Hits.Count} alignment={plane.alignment} trackable={hit.trackableId} " +
                    $"planeY={plane.center.y.ToString("0.000", CultureInfo.InvariantCulture)} " +
                    $"hitY={world.y.ToString("0.000", CultureInfo.InvariantCulture)} " +
                    $"referenceFloorY={(hadReference ? referenceY.ToString("0.000", CultureInfo.InvariantCulture) : "none")} " +
                    $"deltaY={deltaY.ToString("0.000", CultureInfo.InvariantCulture)} " +
                    $"reason=\"{rejection}\" candidates={DescribeHits()}");
                return;
            }

            EnsureMarkerRoot();
            CreateMarker(capture.Count, world);
            LogCorner(capture.Count, world, screen, hit, plane);

            if (!capture.IsComplete)
            {
                status = $"Select floor corner {capture.Count + 1}.";
                return;
            }

            BuildRoom();
        }

        bool TryGetHorizontalFloorHit(out ARRaycastHit chosen, out ARPlane plane)
        {
            for (int i = 0; i < Hits.Count; i++)
            {
                ARRaycastHit candidate = Hits[i];
                if (candidate.trackable is ARPlane floor && floor.alignment == PlaneAlignment.HorizontalUp)
                {
                    chosen = candidate;
                    plane = floor;
                    return true;
                }
            }

            chosen = default;
            plane = null;
            return false;
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

        void LogCorner(int index, Vector3 world, Vector2 screen, ARRaycastHit hit, ARPlane plane)
        {
            Vector3 reprojection = arCamera.WorldToScreenPoint(world);
            float errorPx = Vector2.Distance(screen, reprojection);
            Vector3 cameraPosition = arCamera.transform.position;
            Debug.Log(
                $"{LogTag} corner={index}/4 world={Format(world)} " +
                $"screen={screen.x.ToString("0", CultureInfo.InvariantCulture)},{screen.y.ToString("0", CultureInfo.InvariantCulture)} " +
                $"reprojErrPx={errorPx.ToString("0.0", CultureInfo.InvariantCulture)} " +
                $"alignment={plane.alignment} trackable={hit.trackableId} " +
                $"planeY={plane.center.y.ToString("0.000", CultureInfo.InvariantCulture)} " +
                $"hitY={world.y.ToString("0.000", CultureInfo.InvariantCulture)} " +
                $"referenceFloorY={capture.ReferenceFloorY.ToString("0.000", CultureInfo.InvariantCulture)} " +
                $"deltaY={(world.y - capture.ReferenceFloorY).ToString("0.000", CultureInfo.InvariantCulture)} " +
                $"hits={Hits.Count} distance={hit.distance.ToString("0.000", CultureInfo.InvariantCulture)} " +
                $"camera={Format(cameraPosition)}");
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

            logBuilder.Append(" tape=compare each edge with a tape along the floor, in tap order");
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
            status = reason;
            Debug.LogWarning($"{LogTag} rejected \"{reason}\"");
        }

        void OnSessionStateChanged(ARSessionStateChangedEventArgs args)
        {
            Debug.Log($"{LogTag} tracking {lastSessionState} -> {args.state}");
            lastSessionState = args.state;
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
            marker.transform.localScale = Vector3.one * MarkerSizeMeters;

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
            renderer.sharedMaterial = material != null ? material : CreateUnlit(objectName, fallback);
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

        bool IsScreenPointOverReset(Vector2 screenPosition)
        {
            float scale = GuiScale();
            var guiPoint = new Vector2(screenPosition.x, Screen.height - screenPosition.y) / scale;
            return ResetButtonRect().Contains(guiPoint);
        }

        static float GuiScale()
        {
            float dpiScale = Screen.dpi > 0f ? Screen.dpi / 160f : 1f;
            return Mathf.Clamp(dpiScale, 1f, 3f);
        }

        static Rect ResetButtonRect()
        {
            return new Rect(16f, 220f, 280f, 52f);
        }

        void OnGUI()
        {
            float scale = GuiScale();
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            GUI.Box(new Rect(8f, 8f, 640f, 300f), GUIContent.none);
            string tracking = arSession != null ? ARSession.state.ToString() : "No ARSession";
            string reference = capture.HasReferenceFloorY
                ? capture.ReferenceFloorY.ToString("0.000", CultureInfo.InvariantCulture) + " m"
                : "not set";
            GUI.Label(new Rect(20f, 16f, 600f, 24f), "Tracking: " + tracking);
            GUI.Label(new Rect(20f, 40f, 600f, 24f), "HorizontalUp planes: " + CountHorizontalFloors());
            GUI.Label(new Rect(20f, 64f, 600f, 24f), $"Corners: {capture.Count}/4  floor Y: {reference}");
            GUI.Label(new Rect(20f, 88f, 600f, 48f), "Aim at the room floor. The first point sets the height and can be a table.");
            GUI.Label(new Rect(20f, 136f, 600f, 72f), "Room: " + (meshBuilt ? "built" : status));
            if (GUI.Button(ResetButtonRect(), "Reset"))
            {
                ResetCapture();
            }

            GUI.matrix = previous;
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

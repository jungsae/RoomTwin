using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;
using Unity.XR.CoreUtils;
using RoomTwin.AR;

namespace RoomTwin.EditorTools
{
    /// <summary>
    /// One-shot Phase 0 setup: XR ARCore loader, assets, and AR probe scene.
    /// </summary>
    public static class Phase0ArBootstrap
    {
        const string Root = "Assets/RoomTwin";
        const string MaterialsPath = Root + "/Materials";
        const string PrefabsPath = Root + "/Prefabs";
        const string ScenesPath = Root + "/Scenes";
        const string PlaneMaterialPath = MaterialsPath + "/ArPlanePreview.mat";
        const string PlanePrefabPath = PrefabsPath + "/ArPlaneVisual.prefab";
        const string MarkerPrefabPath = PrefabsPath + "/DebugMarker.prefab";
        const string ScenePath = ScenesPath + "/ARPlaneProbe.unity";

        [MenuItem("RoomTwin/Phase0/Bootstrap AR Scene And XR")]
        public static void BootstrapFromMenu()
        {
            Run(exitOnBatchMode: false);
        }

        /// <summary>Batch entry: -executeMethod RoomTwin.EditorTools.Phase0ArBootstrap.RunBatch</summary>
        public static void RunBatch()
        {
            Run(exitOnBatchMode: true);
        }

        public static void Run()
        {
            Run(exitOnBatchMode: Application.isBatchMode);
        }

        public static void Run(bool exitOnBatchMode)
        {
            try
            {
                EnsureFolders();
                ConfigurePlayerSettings();
                ConfigureAndroidXr();
                Material planeMaterial = CreateOrUpdatePlaneMaterial();
                GameObject planePrefab = CreateOrUpdatePlanePrefab(planeMaterial);
                GameObject markerPrefab = CreateOrUpdateMarkerPrefab();
                CreateOrUpdateProbeScene(planePrefab, markerPrefab);
                ConfigureBuildSettings();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("[RoomTwin][Phase0] Bootstrap completed: " + ScenePath);
                if (exitOnBatchMode && Application.isBatchMode)
                {
                    EditorApplication.Exit(0);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[RoomTwin][Phase0] Bootstrap failed: " + ex);
                if (exitOnBatchMode && Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                }

                throw;
            }
        }

        static void EnsureFolders()
        {
            EnsureFolder("Assets", "RoomTwin");
            EnsureFolder(Root, "Materials");
            EnsureFolder(Root, "Prefabs");
            EnsureFolder(Root, "Scenes");
            EnsureFolder(Root, "Shaders");
            EnsureFolder(Root, "Scripts");
            EnsureFolder(Root + "/Scripts", "Runtime");
            EnsureFolder(Root + "/Scripts", "Editor");
        }

        static void EnsureFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }

        static void ConfigurePlayerSettings()
        {
            PlayerSettings.companyName = "RoomTwin";
            PlayerSettings.productName = "RoomTwinScanner";
            PlayerSettings.SetApplicationIdentifier(
                NamedBuildTarget.Android,
                "com.roomtwin.scanner");
            // ARCore Required + Vulkan needs API 29. Keep this equal to ProjectSettings.
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(
                NamedBuildTarget.Android,
                ScriptingImplementation.IL2CPP);
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetGraphicsAPIs(
                BuildTarget.Android,
                new[] { GraphicsDeviceType.OpenGLES3, GraphicsDeviceType.Vulkan });
        }

        static void ConfigureAndroidXr()
        {
            BuildTargetGroup androidGroup = BuildTargetGroup.Android;
            EnsureFolder("Assets", "XR");

            if (!EditorBuildSettings.TryGetConfigObject(
                    XRGeneralSettings.k_SettingsKey,
                    out XRGeneralSettingsPerBuildTarget perBuildTarget) ||
                perBuildTarget == null)
            {
                perBuildTarget =
                    ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(
                    perBuildTarget,
                    "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                EditorBuildSettings.AddConfigObject(
                    XRGeneralSettings.k_SettingsKey,
                    perBuildTarget,
                    true);
            }

            XRGeneralSettings settings =
                XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(androidGroup);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<XRGeneralSettings>();
                settings.name = "Android XR Settings";
                AssetDatabase.AddObjectToAsset(settings, perBuildTarget);
                perBuildTarget.SetSettingsForBuildTarget(androidGroup, settings);
            }

            if (settings.AssignedSettings == null)
            {
                XRManagerSettings manager =
                    ScriptableObject.CreateInstance<XRManagerSettings>();
                manager.name = "Android XR Manager";
                AssetDatabase.AddObjectToAsset(manager, perBuildTarget);
                settings.AssignedSettings = manager;
            }

            settings.InitManagerOnStart = true;

            bool assigned = XRPackageMetadataStore.AssignLoader(
                settings.Manager,
                "UnityEngine.XR.ARCore.ARCoreLoader",
                androidGroup);

            if (!assigned)
            {
                throw new System.InvalidOperationException(
                    "Failed to assign ARCoreLoader. Is com.unity.xr.arcore installed?");
            }

            EditorUtility.SetDirty(perBuildTarget);
            EditorUtility.SetDirty(settings);
            EditorUtility.SetDirty(settings.Manager);
        }

        static Material CreateOrUpdatePlaneMaterial()
        {
            Shader shader = Shader.Find("RoomTwin/ArPlaneTransparent");
            if (shader == null)
            {
                throw new System.InvalidOperationException(
                    "Missing shader RoomTwin/ArPlaneTransparent");
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(PlaneMaterialPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, PlaneMaterialPath);
            }
            else
            {
                material.shader = shader;
            }

            material.SetColor("_BaseColor", new Color(0.15f, 0.75f, 1f, 0.35f));
            EditorUtility.SetDirty(material);
            return material;
        }

        static GameObject CreateOrUpdatePlanePrefab(Material planeMaterial)
        {
            GameObject root = new GameObject("ArPlaneVisual");
            ARPlane plane = root.AddComponent<ARPlane>();
            root.AddComponent<ARPlaneMeshVisualizer>();
            MeshRenderer meshRenderer = root.AddComponent<MeshRenderer>();
            root.AddComponent<MeshFilter>();
            meshRenderer.sharedMaterial = planeMaterial;

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PlanePrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static GameObject CreateOrUpdateMarkerPrefab()
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "DebugMarker";
            Object.DestroyImmediate(marker.GetComponent<Collider>());

            MeshRenderer renderer = marker.GetComponent<MeshRenderer>();
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            Material markerMaterial = new Material(shader);
            if (markerMaterial.HasProperty("_BaseColor"))
            {
                markerMaterial.SetColor("_BaseColor", new Color(1f, 0.35f, 0.1f, 1f));
            }
            else if (markerMaterial.HasProperty("_Color"))
            {
                markerMaterial.SetColor("_Color", new Color(1f, 0.35f, 0.1f, 1f));
            }

            string markerMatPath = MaterialsPath + "/DebugMarker.mat";
            AssetDatabase.CreateAsset(markerMaterial, markerMatPath);
            renderer.sharedMaterial = markerMaterial;

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(marker, MarkerPrefabPath);
            Object.DestroyImmediate(marker);
            return prefab;
        }

        static void CreateOrUpdateProbeScene(GameObject planePrefab, GameObject markerPrefab)
        {
            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);

            GameObject lighting = new GameObject("Directional Light");
            Light light = lighting.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            lighting.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            GameObject sessionGo = new GameObject("AR Session");
            ARSession arSession = sessionGo.AddComponent<ARSession>();
            // Starts the XRInputSubsystem that feeds HandheldARInputDevice pose.
            if (sessionGo.GetComponent<ARInputManager>() == null)
            {
                sessionGo.AddComponent<ARInputManager>();
            }

            GameObject originGo = new GameObject("XR Origin");
            XROrigin xrOrigin = originGo.AddComponent<XROrigin>();
            ARPlaneManager planeManager = originGo.AddComponent<ARPlaneManager>();
            ARRaycastManager raycastManager = originGo.AddComponent<ARRaycastManager>();
            originGo.AddComponent<ArPlaneDetectionController>();

            planeManager.planePrefab = planePrefab;

            GameObject cameraOffset = new GameObject("Camera Offset");
            cameraOffset.transform.SetParent(originGo.transform, false);

            GameObject cameraGo = new GameObject("Main Camera");
            cameraGo.transform.SetParent(cameraOffset.transform, false);
            cameraGo.tag = "MainCamera";
            Camera camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
            cameraGo.AddComponent<AudioListener>();
            cameraGo.AddComponent<ARCameraManager>();
            cameraGo.AddComponent<ARCameraBackground>();

            TrackedPoseDriver poseDriver = cameraGo.AddComponent<TrackedPoseDriver>();
            poseDriver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            poseDriver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            // AddComponent leaves the actions unbound, which freezes the camera at the origin.
            poseDriver.positionInput = new InputActionProperty(new InputAction(
                "Position",
                binding: "<HandheldARInputDevice>/devicePosition",
                expectedControlType: "Vector3"));
            poseDriver.rotationInput = new InputActionProperty(new InputAction(
                "Rotation",
                binding: "<HandheldARInputDevice>/deviceRotation",
                expectedControlType: "Quaternion"));

            xrOrigin.Camera = camera;
            xrOrigin.CameraFloorOffsetObject = cameraOffset;
            xrOrigin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;
            xrOrigin.CameraYOffset = 0f;

            GameObject systems = new GameObject("RoomTwin Systems");
            ArTouchMarkerPlacer placer = systems.AddComponent<ArTouchMarkerPlacer>();
            SerializedObject placerSo = new SerializedObject(placer);
            placerSo.FindProperty("raycastManager").objectReferenceValue = raycastManager;
            placerSo.FindProperty("markerPrefab").objectReferenceValue = markerPrefab;
            placerSo.FindProperty("markerParent").objectReferenceValue = systems.transform;
            placerSo.ApplyModifiedPropertiesWithoutUndo();

            ArSessionStatusHud hud = systems.AddComponent<ArSessionStatusHud>();
            SerializedObject hudSo = new SerializedObject(hud);
            hudSo.FindProperty("arSession").objectReferenceValue = arSession;
            hudSo.FindProperty("planeManager").objectReferenceValue = planeManager;
            hudSo.ApplyModifiedPropertiesWithoutUndo();

            Directory.CreateDirectory(
                Path.Combine(Application.dataPath, "RoomTwin/Scenes"));
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        static void ConfigureBuildSettings()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true)
            };
        }
    }
}

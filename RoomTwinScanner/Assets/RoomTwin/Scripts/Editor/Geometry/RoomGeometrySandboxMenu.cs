using RoomTwin.Geometry;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RoomTwin.Geometry.EditorTools
{
    public static class RoomGeometrySandboxMenu
    {
        const string ScenePath = "Assets/RoomTwin/Scenes/GeometrySandbox.unity";

        [MenuItem("RoomTwin/Phase1/Create Geometry Sandbox Scene")]
        public static void CreateScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            {
                Debug.Log("[RoomTwin][Geometry] GeometrySandbox already exists at " + ScenePath + ". This menu does not overwrite it or change build settings.");
                return;
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var lightObject = new GameObject("Directional Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.transform.position = new Vector3(2f, 4f, -5f);
            camera.transform.rotation = Quaternion.Euler(25f, 0f, 0f);
            cameraObject.AddComponent<AudioListener>();

            var roomObject = new GameObject("Room");
            roomObject.AddComponent<RoomGeometryController>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[RoomTwin][Geometry] Created " + ScenePath + ". Build settings were not changed.");
        }
    }
}

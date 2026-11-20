using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RoomTwin.EditorTools
{
    /// <summary>
    /// Builds only the field-test scene. Does not change XR preload assets.
    /// </summary>
    public static class FieldTestAndroidBuild
    {
        const string ScenePath = "Assets/RoomTwin/Scenes/ARFieldTest.unity";
        const string ApkPath = "Builds/Android/RoomTwinScanner-FieldTest.apk";

        [MenuItem("RoomTwin/FieldTest/Build Android APK")]
        public static void BuildFromMenu()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string apkPath = Path.Combine(projectRoot, ApkPath);
            Directory.CreateDirectory(Path.GetDirectoryName(apkPath));

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = apkPath,
                target = BuildTarget.Android,
                options = BuildOptions.Development
            };

            Debug.Log("[RoomTwin][FieldTest] Building APK -> " + apkPath);
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log("[RoomTwin][FieldTest] APK build succeeded: " + apkPath);
            }
            else
            {
                Debug.LogError("[RoomTwin][FieldTest] APK build failed: " + report.summary.result);
            }
        }
    }
}

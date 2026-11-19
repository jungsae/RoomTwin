using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RoomTwin.EditorTools
{
    /// <summary>
    /// Batch-mode Android APK build entry for Phase 0 verification.
    /// </summary>
    public static class Phase0AndroidBuild
    {
        const string DefaultApkPath = "Builds/Android/RoomTwinScanner-Phase0.apk";

        [MenuItem("RoomTwin/Phase0/Build Android APK")]
        public static void BuildFromMenu()
        {
            Build(DefaultApkPath);
        }

        public static void Build()
        {
            Build(DefaultApkPath);
            if (Application.isBatchMode)
            {
                // Exit code is set inside Build().
            }
        }

        public static void Build(string apkRelativePath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            string apkPath = Path.Combine(projectRoot, apkRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(apkPath)!);

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes
                    .Where(scene => scene.enabled)
                    .Select(scene => scene.path)
                    .ToArray(),
                locationPathName = apkPath,
                target = BuildTarget.Android,
                options = BuildOptions.Development
            };

            if (options.scenes == null || options.scenes.Length == 0)
            {
                Debug.LogError("[RoomTwin][Phase0] No enabled scenes in Build Settings.");
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(2);
                }

                return;
            }

            Debug.Log("[RoomTwin][Phase0] Building APK -> " + apkPath);
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log(
                    $"[RoomTwin][Phase0] APK build succeeded: {apkPath} " +
                    $"size={summary.totalSize} bytes time={summary.totalTime}");
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(0);
                }
            }
            else
            {
                Debug.LogError(
                    $"[RoomTwin][Phase0] APK build failed: result={summary.result} " +
                    $"errors={summary.totalErrors}");
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                }
            }
        }
    }
}

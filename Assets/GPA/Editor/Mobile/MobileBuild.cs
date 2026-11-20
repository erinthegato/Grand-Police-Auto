using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GPA.EditorTools
{
    /// <summary>
    /// Build entry points for the mobile targets. Each one applies the mobile configuration, pins
    /// the URP "Mobile" quality level, and builds <see cref="MobileBuildSetup.MainScenePath"/>.
    ///
    /// Building requires the matching platform module in the editor:
    /// Android Build Support (with its SDK/NDK/JDK) for the APK/AAB, and iOS Build Support to
    /// export the Xcode project (the final iOS build itself needs Xcode on macOS).
    ///
    /// Headless:
    /// <c>Unity.exe -batchmode -quit -projectPath . -executeMethod GPA.EditorTools.MobileBuild.BuildAndroidApk</c>
    /// </summary>
    public static class MobileBuild
    {
        private const string OutputRoot = "Builds";

        [MenuItem("GPA/Mobile/Build Android APK", false, 1)]
        public static void BuildAndroidApk() => BuildAndroid(appBundle: false);

        [MenuItem("GPA/Mobile/Build Android AAB (Play Store)", false, 2)]
        public static void BuildAndroidBundle() => BuildAndroid(appBundle: true);

        [MenuItem("GPA/Mobile/Build iOS Xcode Project", false, 3)]
        public static void BuildIosProject() => BuildIos();

        private static void BuildAndroid(bool appBundle)
        {
            RequireSupport(BuildTarget.Android, BuildTargetGroup.Android, "Android Build Support (SDK/NDK/JDK)");

            MobileBuildSetup.Apply();
            SelectQualityLevel(MobileBuildSetup.MobileQualityLevel);
            EditorUserBuildSettings.buildAppBundle = appBundle;

            string extension = appBundle ? "aab" : "apk";
            string directory = Path.Combine(OutputRoot, "Android");
            Directory.CreateDirectory(directory);

            string fileName = $"LightsAndSirensHalden-{PlayerSettings.bundleVersion}.{extension}";
            string path = Path.Combine(directory, fileName).Replace('\\', '/');

            Build(BuildTarget.Android, BuildTargetGroup.Android, path,
                $"Android {(appBundle ? "app bundle" : "APK")}");
        }

        private static void BuildIos()
        {
            RequireSupport(BuildTarget.iOS, BuildTargetGroup.iOS, "iOS Build Support");

            MobileBuildSetup.Apply();
            SelectQualityLevel(MobileBuildSetup.MobileQualityLevel);
            EditorUserBuildSettings.iOSXcodeBuildConfig = XcodeBuildConfig.Release;

            string directory = Path.Combine(OutputRoot, "iOS");
            Directory.CreateDirectory(directory);

            Build(BuildTarget.iOS, BuildTargetGroup.iOS, directory.Replace('\\', '/'), "iOS Xcode project");
        }

        private static void Build(BuildTarget target, BuildTargetGroup group, string locationPathName, string label)
        {
            var options = new BuildPlayerOptions
            {
                scenes = new[] { MobileBuildSetup.MainScenePath },
                locationPathName = locationPathName,
                target = target,
                targetGroup = group,
                options = BuildOptions.CompressWithLz4HC,   // smaller game data on the device; loads just as fast
            };

            Debug.Log($"[Mobile] Building {label} → {locationPathName}");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException(
                    $"[Mobile] {label} failed: {summary.result}, {summary.totalErrors} error(s). See the Editor log.");
            }

            double megabytes = summary.totalSize / (1024.0 * 1024.0);
            Debug.Log($"[Mobile] {label} succeeded: {locationPathName} ({megabytes:0.0} MB, {summary.totalTime}).");
        }

        /// <summary>Fails loudly instead of silently building nothing when the module is absent.</summary>
        private static void RequireSupport(BuildTarget target, BuildTargetGroup group, string moduleName)
        {
            if (BuildPipeline.IsBuildTargetSupported(group, target)) return;

            throw new BuildFailedException(
                $"[Mobile] {moduleName} is not installed for this editor, so the build cannot run.\n" +
                "Unity Hub → Installs → 6000.6.9f1 → Add modules → install " + moduleName + ".\n" +
                "Then re-run GPA/Mobile/Build. (iOS additionally needs Xcode on macOS to produce the .ipa.)");
        }

        private static void SelectQualityLevel(string levelName)
        {
            int index = Array.IndexOf(QualitySettings.names, levelName);
            if (index < 0)
            {
                Debug.LogWarning($"[Mobile] Quality level '{levelName}' not found; keeping the current level. " +
                                 "Expected the URP template's Mobile level with Assets/Settings/Mobile_RPAsset.");
                return;
            }

            QualitySettings.SetQualityLevel(index, applyExpensiveChanges: true);
        }
    }
}

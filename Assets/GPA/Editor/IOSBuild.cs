using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GPA.EditorTools
{
    /// <summary>
    /// iPhone build. The PC build is untouched: these settings only apply to the iOS target, and the
    /// phone UI scaling switches itself on at runtime on mobile only (see GPA.Core.UiScale).
    /// Cloud builds (Unity Build Automation / GitHub Actions) can call <see cref="CloudExport"/>.
    /// </summary>
    public sealed class IOSBuild : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        /// <summary>iOS-only player settings, applied right before every iOS build.</summary>
        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.iOS) return;
            Apply();
        }

        public static void Apply()
        {
            var ios = NamedBuildTarget.iOS;
            PlayerSettings.SetScriptingBackend(ios, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetArchitecture(ios, 1);                         // ARM64
            PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneAndiPad;
            PlayerSettings.iOS.targetOSVersionString = "15.0";
            PlayerSettings.iOS.requiresFullScreen = true;
            PlayerSettings.iOS.appleEnableAutomaticSigning = false;           // Sideloadly re-signs the .ipa
            // Landscape only (the game is played sideways with a controller).
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            // Many scanner radio streams are plain http://.
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
        }

        [MenuItem("Tools/GPA/Export iPhone (Xcode project)")]
        private static void ExportMenu()
        {
            string path = EditorUtility.SaveFolderPanel("Export iPhone Xcode project", "Builds", "iOS");
            if (!string.IsNullOrEmpty(path)) Export(path);
        }

        /// <summary>Command-line / cloud entry: -executeMethod GPA.EditorTools.IOSBuild.CloudExport</summary>
        public static void CloudExport() => Export(Path.GetFullPath("Builds/iOS"));

        private static void Export(string path)
        {
            Apply();
            var scenes = System.Array.ConvertAll(System.Array.FindAll(EditorBuildSettings.scenes, s => s.enabled), s => s.path);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes, locationPathName = path, target = BuildTarget.iOS, targetGroup = BuildTargetGroup.iOS,
                options = BuildOptions.CompressWithLz4HC,   // smaller game data on the phone; loads just as fast
            });
            Debug.Log($"[IOSBuild] {report.summary.result}: {path} ({report.summary.totalErrors} errors)");
            if (Application.isBatchMode && report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}

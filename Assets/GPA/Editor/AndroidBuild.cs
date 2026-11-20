using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GPA.EditorTools
{
    /// <summary>
    /// Android builds. The PC and iPhone builds are untouched: these settings only apply to the Android
    /// target, and the phone UI switches itself on at runtime on mobile (see GPA.Core.UiScale).
    ///  • "Build Android APK (test)": an .apk you can copy to a phone and install (signed with Unity's debug key)
    ///  • "Build Android App Bundle (Play Store)": an .aab for Google Play; needs your upload keystore set in Player Settings
    /// Output: Builds/Android-yyyy-MM-dd/LightsAndSirensHalden.apk (or .aab)
    /// Command line: -executeMethod GPA.EditorTools.AndroidBuild.BuildApk (or .BuildBundle)
    /// </summary>
    public sealed class AndroidBuild : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform == BuildTarget.Android) Apply();
        }

        [MenuItem("Tools/GPA/Build Android APK (test)")]
        public static void BuildApk() => Build(false);

        [MenuItem("Tools/GPA/Build Android App Bundle (Play Store)")]
        public static void BuildBundle() => Build(true);

        /// <summary>Android-only player settings. Safe to call any time.</summary>
        public static void Apply()
        {
            var android = NamedBuildTarget.Android;
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;   // every phone from the last ~8 years; required by Google Play
            if (PlayerSettings.Android.minSdkVersion < AndroidSdkVersions.AndroidApiLevel26)
                PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.SetManagedStrippingLevel(android, ManagedStrippingLevel.Medium);
            // Landscape only, like the iPhone build.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            // The police scanner streams radio over the internet; many of those streams are plain http://.
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            PlayerSettings.Android.renderOutsideSafeArea = false;
            WindowsBuild.ApplySplash();
        }

        private static void Build(bool bundle)
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
            {
                const string msg = "Android Build Support isn't installed for this Unity version.\n\n" +
                                   "Open Unity Hub → Installs → (this Unity version) ⚙ → Add modules → tick \"Android Build Support\" " +
                                   "(with OpenJDK and Android SDK & NDK Tools), install, then restart Unity and build again.";
                Debug.LogError("[AndroidBuild] " + msg);
                if (!Application.isBatchMode) EditorUtility.DisplayDialog("Android build", msg, "OK");
                else EditorApplication.Exit(1);
                return;
            }
            if (bundle && !PlayerSettings.Android.useCustomKeystore)
            {
                const string msg = "Google Play needs the app bundle signed with your own upload key.\n\n" +
                                   "Project Settings → Player → Android → Publishing Settings → Keystore Manager: create a keystore, then build again.";
                Debug.LogError("[AndroidBuild] " + msg);
                if (!Application.isBatchMode) EditorUtility.DisplayDialog("Android build", msg, "OK");
                return;
            }

            Apply();
            EditorUserBuildSettings.buildAppBundle = bundle;
            string folder = Path.GetFullPath($"Builds/Android-{DateTime.Now:yyyy-MM-dd}");
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, "LightsAndSirensHalden" + (bundle ? ".aab" : ".apk"));
            if (File.Exists(file)) File.Delete(file);

            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = file,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.CompressWithLz4HC,
            });
            long bytes = File.Exists(file) ? new FileInfo(file).Length : 0;
            string result = $"{report.summary.result} | errors={report.summary.totalErrors} size={bytes / 1048576.0:0}MB time={report.summary.totalTime}";
            Directory.CreateDirectory(Path.GetFullPath("Builds"));
            File.WriteAllText(Path.Combine(Path.GetFullPath("Builds"), "last_build_result.txt"), $"{file}\n{result}\n");
            Debug.Log($"[AndroidBuild] {result}: {file}");
            if (Application.isBatchMode && report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}

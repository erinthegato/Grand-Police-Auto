using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;

namespace GPA.EditorTools
{
    /// <summary>
    /// One place that puts the project into a shippable mobile configuration: landscape-only,
    /// IL2CPP + ARM64, ASTC textures, a real bundle id, and the URP "Mobile" quality level the
    /// project already ships. Shared by the build entry points in <see cref="MobileBuild"/> and
    /// exposed as a menu item so the settings can be applied without building.
    /// </summary>
    public static class MobileBuildSetup
    {
        public const string AndroidPackageId = "com.grandpoliceauto.downtownpatrol";
        public const string IosBundleId = "com.grandpoliceauto.downtownpatrol";
        public const string MainScenePath = "Assets/GPA/Scenes/City_Midtown.unity";
        public const string MobileQualityLevel = "Mobile";

        [MenuItem("GPA/Mobile/Apply Mobile Player Settings", false, 20)]
        public static void ApplyFromMenu()
        {
            Apply();
            Verify();
            Debug.Log("[Mobile] Player settings applied for Android and iOS.");
        }

        /// <summary>Idempotent: safe to run before every build.</summary>
        public static void Apply()
        {
            PlayerSettings.companyName = "Grand Police Auto";
            PlayerSettings.productName = "Grand Police Auto";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, AndroidPackageId);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, IosBundleId);
            PlayerSettings.colorSpace = ColorSpace.Linear;

            // Landscape only: the patrol HUD, minimap and driving cameras are built for 16:9+.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            ApplyAndroid();
            ApplyIos();
            EnsureMainSceneInBuild();
        }

        private static void ApplyAndroid()
        {
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.Android, Il2CppCompilerConfiguration.Release);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.bundleVersionCode = 1;
            PlayerSettings.Android.startInFullscreen = true;
            // Keep the HUD inside the safe area on notched phones.
            PlayerSettings.Android.renderOutsideSafeArea = false;

            // URP on Android: Vulkan first, GLES3 fallback for older drivers.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
                new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });

            EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
        }

        private static void ApplyIos()
        {
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.iOS, Il2CppCompilerConfiguration.Release);
            PlayerSettings.iOS.targetOSVersionString = "13.0";
            PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneAndiPad;
            // Full-screen (no iPad multitasking): the HUD owns the screen.
            PlayerSettings.iOS.requiresFullScreen = true;
            // Metal is the only iOS graphics API; MFi controllers arrive through the GameController
            // framework and surface as standard Input System gamepads, so no extra setting is needed.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.iOS, true);
        }

        private static void EnsureMainSceneInBuild()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MainScenePath) == null)
            {
                Debug.LogWarning($"[Mobile] {MainScenePath} is missing; the build would ship with no city scene.");
                return;
            }

            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            int index = scenes.FindIndex(s => s.path == MainScenePath);
            if (index < 0)
            {
                scenes.Insert(0, new EditorBuildSettingsScene(MainScenePath, true));
            }
            else
            {
                scenes[index].enabled = true;
                if (index != 0)
                {
                    var scene = scenes[index];
                    scenes.RemoveAt(index);
                    scenes.Insert(0, scene);
                }
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }

        /// <summary>Logs the configuration and whether each mobile target can actually be built here.</summary>
        [MenuItem("GPA/Mobile/Verify Mobile Configuration", false, 21)]
        public static void Verify()
        {
            bool android = BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android);
            bool ios = BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.iOS, BuildTarget.iOS);

            string quality = QualitySettings.names.Length > 0 ? QualitySettings.names[QualitySettings.GetQualityLevel()] : "(none)";
            Debug.Log(
                "[Mobile] Configuration\n" +
                $"  Bundle: {AndroidPackageId} (Android) / {IosBundleId} (iOS)\n" +
                $"  Orientation: {PlayerSettings.defaultInterfaceOrientation}, portrait autorotate off\n" +
                $"  Android: {PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android)}, {PlayerSettings.Android.targetArchitectures}, " +
                $"min API {(int)PlayerSettings.Android.minSdkVersion}, textures {EditorUserBuildSettings.androidBuildSubtarget}\n" +
                $"  iOS: {PlayerSettings.GetScriptingBackend(NamedBuildTarget.iOS)}, target {PlayerSettings.iOS.targetOSVersionString}\n" +
                $"  Quality level: {quality} (Mobile level uses the URP Mobile asset)\n" +
                $"  Scene: {MainScenePath}\n" +
                $"  Android build support installed: {android}\n" +
                $"  iOS build support installed: {ios}");
        }
    }
}

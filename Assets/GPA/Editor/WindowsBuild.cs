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
    /// Release build for Windows, set up for a small download:
    ///  • LZ4HC-compressed game data (smaller on disk, loads just as fast)
    ///  • Direct3D 11 only (a 2D game doesn't need D3D12, whose DLLs add ~9 MB)
    ///  • managed code stripping (link.xml keeps every GPA assembly whole)
    ///  • debug-only folders that Unity writes next to the game are moved out of the shipped folder
    /// Output: Builds/Windows-yyyy-MM-dd/GrandPoliceAuto.exe
    /// Command line: -executeMethod GPA.EditorTools.WindowsBuild.Build
    /// </summary>
    public static class WindowsBuild
    {
        [MenuItem("Tools/GPA/Build Windows (release)")]
        public static void Build()
        {
            ApplySettings();
            string folder = Path.GetFullPath($"Builds/Windows-{DateTime.Now:yyyy-MM-dd}");
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(folder, "GrandPoliceAuto.exe"),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.CompressWithLz4HC,
            });
            MoveDebugFolders(folder);
            long bytes = Directory.Exists(folder) ? Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) : 0;
            Debug.Log($"[WindowsBuild] {report.summary.result}: {folder} ({bytes / 1048576.0:0.0} MB, {report.summary.totalTime})");
            if (Application.isBatchMode && report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }

        /// <summary>PC player settings for a small build. Safe to call any time.</summary>
        public static void ApplySettings()
        {
            var pc = NamedBuildTarget.Standalone;
            PlayerSettings.SetManagedStrippingLevel(pc, ManagedStrippingLevel.Medium);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 });
            PlayerSettings.stripEngineCode = true;
        }

        /// <summary>Unity writes crash-symbol / burst-debug folders beside the game; keep them, but not in the folder you ship.</summary>
        private static void MoveDebugFolders(string folder)
        {
            if (!Directory.Exists(folder)) return;
            string keep = Path.GetFullPath("Builds/_symbols/" + Path.GetFileName(folder));
            foreach (string d in Directory.GetDirectories(folder))
            {
                string name = Path.GetFileName(d);
                if (!name.Contains("DoNotShip") && !name.Contains("ButDontShipItWithYourGame")) continue;
                Directory.CreateDirectory(keep);
                string dest = Path.Combine(keep, name);
                if (Directory.Exists(dest)) Directory.Delete(dest, true);
                Directory.Move(d, dest);
            }
        }
    }
}

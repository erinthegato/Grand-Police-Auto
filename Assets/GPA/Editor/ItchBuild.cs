using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GPA.EditorTools
{
    /// <summary>
    /// itch.io release: the same small Windows build as <see cref="WindowsBuild"/>, with Steam
    /// turned off (DISABLESTEAMWORKS, for this build only; the project's own settings don't change)
    /// and the Steam DLL left out, then zipped ready to upload.
    /// Output: Builds/LightsAndSirensHalden-&lt;version&gt;-windows.zip
    /// (unzipped copy in Builds/Itch-yyyy-MM-dd/LightsAndSirensHalden).
    /// Result line: Builds/last_itch_result.txt
    /// Command line: -executeMethod GPA.EditorTools.ItchBuild.Build
    /// </summary>
    public static class ItchBuild
    {
        [MenuItem("Tools/GPA/Build for itch.io (Windows zip)")]
        public static void Build()
        {
            string resultFile = Path.GetFullPath("Builds/last_itch_result.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(resultFile));
            File.WriteAllText(resultFile, "RUNNING\n");

            WindowsBuild.ApplySettings();
            string version = PlayerSettings.bundleVersion;
            string folder = Path.GetFullPath($"Builds/Itch-{DateTime.Now:yyyy-MM-dd}/LightsAndSirensHalden");
            if (Directory.Exists(folder)) Directory.Delete(folder, true);

            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(folder, "LightsAndSirensHalden.exe"),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.CompressWithLz4HC,
                extraScriptingDefines = new[] { "DISABLESTEAMWORKS" },
            });

            if (report.summary.result != BuildResult.Succeeded)
            {
                File.WriteAllText(resultFile, $"FAILED {report.summary.result} errors={report.summary.totalErrors}\n");
                Debug.LogError($"[ItchBuild] {report.summary.result}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            // Debug-symbol folders: kept, but outside the shipped folder.
            string keep = Path.GetFullPath("Builds/_symbols/Itch-" + DateTime.Now.ToString("yyyy-MM-dd"));
            foreach (string d in Directory.GetDirectories(folder))
            {
                string name = Path.GetFileName(d);
                if (!name.Contains("DoNotShip") && !name.Contains("ButDontShipItWithYourGame")) continue;
                Directory.CreateDirectory(keep);
                string dest = Path.Combine(keep, name);
                if (Directory.Exists(dest)) Directory.Delete(dest, true);
                Directory.Move(d, dest);
            }

            // No Steam on itch: nothing calls these, so don't ship them.
            foreach (string f in Directory.GetFiles(folder, "steam_api*.dll", SearchOption.AllDirectories)) File.Delete(f);
            foreach (string f in Directory.GetFiles(folder, "steam_appid.txt", SearchOption.AllDirectories)) File.Delete(f);

            string zip = Path.GetFullPath($"Builds/LightsAndSirensHalden-{version}-windows.zip");
            if (File.Exists(zip)) File.Delete(zip);
            ZipFile.CreateFromDirectory(folder, zip, System.IO.Compression.CompressionLevel.Optimal, false);

            long zipBytes = new FileInfo(zip).Length;
            File.WriteAllText(resultFile, $"OK {zip}\nsize={zipBytes / 1048576.0:0.0}MB time={report.summary.totalTime}\n");
            Debug.Log($"[ItchBuild] Ready to upload: {zip} ({zipBytes / 1048576.0:0.0} MB)");
        }
    }
}

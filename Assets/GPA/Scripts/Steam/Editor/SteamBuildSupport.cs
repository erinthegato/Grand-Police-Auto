using System.IO;
using GPA.Steam;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GPA.Steam.EditorTools
{
    /// <summary>
    /// Keeps steam_appid.txt in step with <see cref="SteamManager.AppId"/>.
    ///  • Project folder: the Editor needs it to talk to Steam while you press Play.
    ///  • Test (development) builds: copied next to the .exe so you can run the build
    ///    straight from the folder. Release builds leave it out: Steam supplies the ID.
    /// </summary>
    public sealed class SteamBuildSupport : IPostprocessBuildWithReport
    {
        public int callbackOrder => 100;

        [InitializeOnLoadMethod]
        private static void WriteProjectAppIdFile() => WriteAppId(Directory.GetParent(Application.dataPath).FullName);

        public void OnPostprocessBuild(BuildReport report)
        {
            var target = report.summary.platform;
            bool pc = target == BuildTarget.StandaloneWindows64 || target == BuildTarget.StandaloneWindows
                   || target == BuildTarget.StandaloneLinux64 || target == BuildTarget.StandaloneOSX;
            if (!pc) return;
            if ((report.summary.options & BuildOptions.Development) == 0) return;
            WriteAppId(Path.GetDirectoryName(report.summary.outputPath));
        }

        private static void WriteAppId(string folder)
        {
            string path = Path.Combine(folder, "steam_appid.txt");
            string id = SteamManager.AppId.ToString();
            if (File.Exists(path) && File.ReadAllText(path).Trim() == id) return;
            File.WriteAllText(path, id);
        }
    }
}

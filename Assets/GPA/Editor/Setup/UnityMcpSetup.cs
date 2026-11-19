using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace GPA.EditorTools
{
    /// <summary>
    /// Setup helper for the Unity MCP connection. Watches Assets/McpSetup~ for request flags
    /// (setup.flag → setup.ps1, diag.flag → diag.ps1, legacy run.flag → setup.ps1) and runs the matching
    /// PowerShell script in the background, logging next to it. Polls every few seconds, so a request
    /// doesn't need the Editor to recompile. Delete the McpSetup~ folder and this file when done.
    /// </summary>
    [InitializeOnLoad]
    internal static class UnityMcpSetup
    {
        private static Process _process;
        private static string _running;
        private static double _nextPoll;

        private static string Dir => Path.Combine(Application.dataPath, "McpSetup~");

        static UnityMcpSetup() => EditorApplication.update += Poll;

        private static void Poll()
        {
            if (EditorApplication.timeSinceStartup < _nextPoll) return;
            _nextPoll = EditorApplication.timeSinceStartup + 2.0;

            if (_process != null)
            {
                if (!_process.HasExited) return;
                Debug.Log($"[MCP Setup] {_running} finished (exit {_process.ExitCode}).");
                if (_running == "setup.ps1") { Client.Resolve(); AssetDatabase.Refresh(); }
                _process = null;
                return;
            }
            if (!Directory.Exists(Dir)) return;
            if (TryStart("setup.flag", "setup.ps1") || TryStart("run.flag", "setup.ps1")) return;
            TryStart("diag.flag", "diag.ps1");
        }

        private static bool TryStart(string flag, string script)
        {
            string flagPath = Path.Combine(Dir, flag), scriptPath = Path.Combine(Dir, script);
            if (!File.Exists(flagPath) || !File.Exists(scriptPath)) return false;
            File.Delete(flagPath);
            var info = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\"")
            { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Dir };
            try
            {
                _process = Process.Start(info);
                _running = script;
                Debug.Log($"[MCP Setup] Running {script} in the background.");
            }
            catch (System.Exception e) { Debug.LogError($"[MCP Setup] Could not start {script}: {e.Message}"); }
            return true;
        }
    }
}

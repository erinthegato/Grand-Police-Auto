using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace GPA.CityTools
{
    /// <summary>
    /// Milestone preview: opens the patrol city, enters Play mode, captures gameplay screenshots plus
    /// a bird's-eye shot of the generated city, logs what the player camera can actually see, and
    /// leaves the log for inspection. Run from the menu or the command line (the Editor needs a real
    /// graphics device — <c>-batchmode</c> renders nothing).
    ///
    /// Headless capture:
    /// <c>Unity.exe -projectPath . -executeMethod GPA.CityTools.PlayModePreview.CapturePreview</c>
    /// </summary>
    public static class PlayModePreview
    {
        public const string ScenePath = "Assets/GPA/Scenes/City_Midtown.unity";
        public const string OutputDirectory = "Previews";

        private const string ActiveKey = "GPA.Preview.Active";
        private const string StartKey = "GPA.Preview.Start";

        private const double GameplayShotOne = 3.0;
        private const double GameplayShotTwo = 7.0;
        private const double OverviewShot = 11.0;
        private const double FinishAt = 15.0;

        private const string StartedKey = "GPA.Preview.Started";

        private static bool _subscribed;
        private static int _captured;
        private static bool _diagnosed;
        private static Camera _overviewCamera;

        [MenuItem("GPA/Preview/Play Midtown", false, 1)]
        public static void PlayFromMenu()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        [MenuItem("GPA/Preview/Capture Preview Screenshots", false, 2)]
        public static void CaptureFromMenu() => BeginCapture();

        /// <summary>Command-line entry point: capture, then close the Editor.</summary>
        public static void CapturePreview() => BeginCapture();

        private static void BeginCapture()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                Debug.LogError($"[Preview] {ScenePath} is missing from the project.");
                if (LaunchedFromCommandLine()) EditorApplication.Exit(1);
                return;
            }

            Directory.CreateDirectory(OutputDirectory);
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            _captured = 0;
            _diagnosed = false;
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetBool(StartedKey, false);
            SessionState.SetFloat(StartKey, (float)EditorApplication.timeSinceStartup);

            Debug.Log($"[Preview] Opening {ScenePath} and entering Play mode.");
            EditorApplication.EnterPlaymode();
            Resume();
        }

        /// <summary>
        /// SessionState survives the domain reload that entering Play mode triggers, so this is what
        /// re-subscribes the capture loop after that reload.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void Resume()
        {
            if (!SessionState.GetBool(ActiveKey, false)) return;
            if (_subscribed) return;

            EditorApplication.update += Tick;
            _subscribed = true;
        }

        private static void Tick()
        {
            if (!SessionState.GetBool(ActiveKey, false)) return;
            if (!EditorApplication.isPlaying) return;

            // The domain reload that follows EnterPlaymode eats seconds, so the clock starts on the
            // first Play frame rather than when the capture was requested.
            if (!SessionState.GetBool(StartedKey, false))
            {
                SessionState.SetBool(StartedKey, true);
                SessionState.SetFloat(StartKey, (float)EditorApplication.timeSinceStartup);
            }

            double elapsed = EditorApplication.timeSinceStartup - SessionState.GetFloat(StartKey, 0f);

            if (!_diagnosed && elapsed >= 2.0)
            {
                _diagnosed = true;
                LogDiagnostics();
            }

            if (_captured == 0 && elapsed >= GameplayShotOne) Capture("preview_01.png", elapsed);
            else if (_captured == 1 && elapsed >= GameplayShotTwo) Capture("preview_02.png", elapsed);
            else if (_captured == 2 && elapsed >= OverviewShot)
            {
                CreateOverviewCamera();
                Capture("preview_03_overview.png", elapsed);
            }

            if (elapsed < FinishAt) return;

            if (_overviewCamera != null)
            {
                UnityEngine.Object.Destroy(_overviewCamera.gameObject);
                _overviewCamera = null;
            }

            SessionState.SetBool(ActiveKey, false);
            EditorApplication.update -= Tick;
            _subscribed = false;

            Debug.Log($"[Preview] Finished: {_captured} screenshot(s) in {Path.GetFullPath(OutputDirectory)}. " +
                      "Check the Console above for runtime errors.");
            EditorApplication.ExitPlaymode();

            if (LaunchedFromCommandLine()) EditorApplication.Exit(0);
        }

        /// <summary>
        /// ScreenCapture reads the Game view's own render target, so with no Game view shown (a
        /// scripted Editor session opened straight into Play mode) it returns an uninitialised
        /// buffer — flat garbage colour. Focusing a Game view first makes the capture real.
        /// </summary>
        private static void EnsureGameView()
        {
            Type gameViewType = Type.GetType("UnityEditor.GameView,UnityEditor");
            if (gameViewType == null) return;

            EditorWindow window = EditorWindow.GetWindow(gameViewType);
            if (window != null) window.Focus();
        }

        private static void Capture(string fileName, double elapsed)
        {
            EnsureGameView();

            string file = Path.GetFullPath(Path.Combine(OutputDirectory, fileName));
            ScreenCapture.CaptureScreenshot(file);
            Debug.Log($"[Preview] Captured {file} at {elapsed:0.0}s of Play.");
            _captured++;
        }

        /// <summary>What the player camera is looking at, in numbers, so a bad frame is diagnosable.</summary>
        private static void LogDiagnostics()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                Debug.LogWarning("[Preview] No Camera.main in the scene while playing.");
                return;
            }

            int renderers = 0;
            int visible = 0;
            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
            foreach (Renderer renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                renderers++;
                if (renderer.enabled && GeometryUtility.TestPlanesAABB(planes, renderer.bounds)) visible++;
            }

            Physics.Raycast(camera.transform.position, camera.transform.forward, out RaycastHit hit, 20f, ~0, QueryTriggerInteraction.Ignore);
            string obstacle = hit.collider != null
                ? $"{hit.collider.name} ({hit.collider.GetType().Name}) at {hit.distance:0.00} m"
                : "nothing within 20 m";

            GameObject player = GameObject.Find("Player");
            string playerInfo = player != null ? player.transform.position.ToString() : "missing";

            Debug.Log(
                $"[Preview] Camera '{camera.name}' pos={camera.transform.position} euler={camera.transform.eulerAngles} " +
                $"fov={camera.fieldOfView} clear={camera.clearFlags} mask=0x{camera.cullingMask:X} " +
                $"screen={Screen.width}x{Screen.height} player={playerInfo}\n" +
                $"[Preview] Renderers in scene: {renderers}, in frustum: {visible}. Forward obstacle: {obstacle}.");

            LogRendering();
        }

        /// <summary>Which pipeline, skybox and material set the scene is actually drawing with.</summary>
        private static void LogRendering()
        {
            int level = QualitySettings.GetQualityLevel();
            string qualityName = level >= 0 && level < QualitySettings.names.Length ? QualitySettings.names[level] : "?";
            RenderPipelineAsset current = GraphicsSettings.currentRenderPipeline;
            RenderPipelineAsset quality = QualitySettings.GetRenderPipelineAssetAt(level);
            Material skybox = RenderSettings.skybox;

            Debug.Log($"[Preview] Pipeline current={current?.name ?? "Built-in"} quality={quality?.name ?? "None"} " +
                      $"level={level} ({qualityName}) skybox={(skybox != null ? skybox.shader.name : "none")} " +
                      $"fog={RenderSettings.fog}");

            var seen = new System.Collections.Generic.HashSet<string>();
            int logged = 0;
            foreach (Renderer renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    string key = material == null
                        ? "NULL material"
                        : $"{material.name} shader={material.shader.name} " +
                          $"base={(material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor").ToString() : "n/a")}";

                    if (logged >= 10 || !seen.Add(key)) continue;
                    logged++;
                    Debug.Log($"[Preview] Draw material: {key}");
                }
            }
        }

        private static void CreateOverviewCamera()
        {
            Bounds bounds = new Bounds(Vector3.zero, Vector3.one);
            GameObject city = GameObject.Find("City");
            if (city != null)
            {
                Renderer[] renderers = city.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length > 0)
                {
                    bounds = renderers[0].bounds;
                    foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
                }
            }

            var go = new GameObject("PreviewOverviewCamera");
            var camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.cullingMask = ~0;
            camera.depth = 100f;
            camera.fieldOfView = 55f;

            float span = bounds.size.magnitude;
            go.transform.position = bounds.center + new Vector3(0f, span * 0.45f, -span * 0.45f);
            go.transform.LookAt(bounds.center);

            _overviewCamera = camera;
            Debug.Log($"[Preview] Overview camera at {go.transform.position} looking at {bounds.center} (city bounds {bounds.size}).");
        }

        private static bool LaunchedFromCommandLine()
        {
            foreach (string arg in Environment.GetCommandLineArgs())
                if (arg.StartsWith("-executeMethod", StringComparison.Ordinal)) return true;
            return false;
        }
    }
}

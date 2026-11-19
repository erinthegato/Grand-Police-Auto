using System;
using System.Collections;
using GPA.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GPA.HUD
{
    /// <summary>
    /// Registers <see cref="ISceneLoadingService"/> for the scene it lives in. The actual work happens
    /// in a runner object moved to <c>DontDestroyOnLoad</c>, so the loading screen survives the scene
    /// swap it is loading.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class SceneLoadingService : MonoBehaviour, ISceneLoadingService
    {
        public bool IsLoading => LoadingScreenRunner.Exists;

        private void Awake() => Services.Register<ISceneLoadingService>(this);

        private void OnDestroy() => Services.Unregister<ISceneLoadingService>(this);

        public void LoadScene(string sceneName, string spawnPointName = null)
        {
            if (string.IsNullOrEmpty(sceneName)) return;
            if (LoadingScreenRunner.Exists) return;
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
                Debug.LogWarning($"[SceneLoad] '{sceneName}' is not an enabled scene in Build Settings; loading may fail.", this);

            var host = new GameObject("LoadingScreen");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.AddComponent<LoadingScreenRunner>().Begin(sceneName, spawnPointName);
        }
    }

    /// <summary>
    /// Full-screen loading screen built at runtime (no UI assets needed): dark backdrop, location
    /// name, progress bar and percentage, then a short fade before the new scene is handed over.
    /// </summary>
    public sealed class LoadingScreenRunner : MonoBehaviour
    {
        public static bool Exists { get; private set; }

        private Canvas _canvas;
        private CanvasGroup _group;
        private Image _barFill;
        private Text _percent;

        public void Begin(string sceneName, string spawnPointName) => StartCoroutine(Run(sceneName, spawnPointName));

        private IEnumerator Run(string sceneName, string spawnPointName)
        {
            Exists = true;
            Build(sceneName);

            // Fade in before the old scene goes away, so the swap is never visible.
            float t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / 0.18f;
                _group.alpha = Mathf.Clamp01(t);
                yield return null;
            }

            AsyncOperation load = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            if (load == null)
            {
                Debug.LogError($"[SceneLoad] Could not load '{sceneName}'. Is it in Build Settings?");
                Finish();
                yield break;
            }

            // Hold activation until the bar is full so the screen never pops.
            load.allowSceneActivation = false;
            while (load.progress < 0.9f)
            {
                SetProgress(Mathf.Clamp01(load.progress / 0.9f) * 0.9f);
                yield return null;
            }

            SetProgress(0.95f);

            // Keep the screen up briefly even on an instant load, so it reads as a load and not a flicker.
            float shownAt = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - shownAt < 0.85f) yield return null;

            load.allowSceneActivation = true;
            while (!load.isDone) yield return null;

            if (!string.IsNullOrEmpty(spawnPointName)) PlacePlayer(spawnPointName);
            SetProgress(1f);

            // Fade out over the freshly loaded scene.
            t = 1f;
            while (t > 0f)
            {
                t -= Time.unscaledDeltaTime / 0.25f;
                _group.alpha = Mathf.Clamp01(t);
                yield return null;
            }

            Finish();
        }

        private void PlacePlayer(string spawnPointName)
        {
            var spawn = GameObject.Find(spawnPointName);
            var player = GameObject.Find("Player");
            if (spawn == null || player == null)
            {
                Debug.LogWarning($"[SceneLoad] Spawn point '{spawnPointName}' or Player missing in the loaded scene.");
                return;
            }

            // CharacterController fights a direct transform write, so disable it for the move.
            var controller = player.GetComponent<CharacterController>();
            bool wasEnabled = controller != null && controller.enabled;
            if (controller != null) controller.enabled = false;
            player.transform.SetPositionAndRotation(spawn.transform.position, spawn.transform.rotation);
            if (controller != null) controller.enabled = wasEnabled;

            Physics.SyncTransforms();
        }

        private void SetProgress(float value)
        {
            if (_barFill != null) _barFill.fillAmount = Mathf.Clamp01(value);
            if (_percent != null) _percent.text = Mathf.RoundToInt(Mathf.Clamp01(value) * 100f) + "%";
        }

        private void Finish()
        {
            Exists = false;
            if (_canvas != null) Destroy(_canvas.gameObject);
            Destroy(gameObject);
        }

        private void Build(string sceneName)
        {
            var canvasGo = new GameObject("LoadingCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 5000; // above every HUD element
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _group = canvasGo.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = true;

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            RectTransform Backdrop(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot)
            {
                var go = new GameObject(name, typeof(RectTransform));
                go.transform.SetParent(parent, false);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = anchorMin;
                rt.anchorMax = anchorMax;
                rt.pivot = pivot;
                rt.anchoredPosition = Vector2.zero;
                return rt;
            }

            var bg = Backdrop("Backdrop", canvasGo.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
            var bgImage = bg.gameObject.AddComponent<Image>();
            bgImage.color = new Color(0.03f, 0.04f, 0.06f, 1f);
            bgImage.raycastTarget = true;

            var titleRt = Backdrop("Title", bg, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            titleRt.sizeDelta = new Vector2(1200f, 90f);
            titleRt.anchoredPosition = new Vector2(0f, 80f);
            var title = titleRt.gameObject.AddComponent<Text>();
            title.font = font;
            title.fontSize = 54;
            title.alignment = TextAnchor.MiddleCenter;
            title.color = new Color(0.92f, 0.95f, 1f);
            title.text = FriendlyName(sceneName);

            var subRt = Backdrop("Subtitle", bg, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            subRt.sizeDelta = new Vector2(1200f, 60f);
            subRt.anchoredPosition = new Vector2(0f, 20f);
            var sub = subRt.gameObject.AddComponent<Text>();
            sub.font = font;
            sub.fontSize = 28;
            sub.alignment = TextAnchor.MiddleCenter;
            sub.color = new Color(0.55f, 0.62f, 0.72f);
            sub.text = "Loading…";

            var barBgRt = Backdrop("BarBackground", bg, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            barBgRt.sizeDelta = new Vector2(640f, 14f);
            barBgRt.anchoredPosition = new Vector2(0f, -70f);
            var barBg = barBgRt.gameObject.AddComponent<Image>();
            barBg.color = new Color(1f, 1f, 1f, 0.12f);

            var barRt = Backdrop("BarFill", barBgRt, Vector2.zero, Vector2.one, new Vector2(0f, 0.5f));
            barRt.offsetMin = new Vector2(0f, 0f);
            barRt.offsetMax = new Vector2(0f, 0f);
            _barFill = barRt.gameObject.AddComponent<Image>();
            _barFill.color = new Color(0.35f, 0.62f, 1f);
            _barFill.type = Image.Type.Filled;
            _barFill.fillMethod = Image.FillMethod.Horizontal;
            _barFill.fillAmount = 0f;

            var pctRt = Backdrop("Percent", bg, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            pctRt.sizeDelta = new Vector2(400f, 50f);
            pctRt.anchoredPosition = new Vector2(0f, -120f);
            _percent = pctRt.gameObject.AddComponent<Text>();
            _percent.font = font;
            _percent.fontSize = 26;
            _percent.alignment = TextAnchor.MiddleCenter;
            _percent.color = new Color(0.75f, 0.8f, 0.88f);
            _percent.text = "0%";
        }

        private static string FriendlyName(string sceneName)
        {
            if (sceneName == GameScenes.PrecinctInterior) return "Midtown South Precinct";
            if (sceneName == GameScenes.City) return "Midtown";
            return sceneName;
        }
    }
}

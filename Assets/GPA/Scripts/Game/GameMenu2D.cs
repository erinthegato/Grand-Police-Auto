using System.Collections.Generic;
using GPA.Core;
using GPA.TopDown2D;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GPA.Game
{
    /// <summary>
    /// Title screen (shown when the game starts) and pause menu (Esc / gamepad Start). Works with
    /// keyboard, mouse, controller and touch. While it's open the world is frozen and gameplay keys
    /// are blocked (GameState.Menu / Paused).
    /// </summary>
    public sealed class GameMenu2D : MonoBehaviour
    {
        private enum Page { Title, Pause, Settings, ConfirmNew }

        [SerializeField] private float _autosaveSeconds = 120f;

        public const string EditorTitlePref = "GPA.ShowTitleInEditor";

        /// <summary>Fired once when play begins (true = continued from a saved spot).</summary>
        public static event System.Action<bool> GameStarted;
        /// <summary>True once the player has started (or the title screen was skipped).</summary>
        public static bool HasStarted { get; private set; }
        /// <summary>True while the title screen is up (the radio plays the main theme).</summary>
        public static bool TitleShowing { get; private set; }

        private static void BeginGame(bool continued)
        {
            if (HasStarted) return;
            HasStarted = true;
            GameStarted?.Invoke(continued);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { HasStarted = false; GameStarted = null; TitleShowing = false; }

        private bool _open;
        private Page _page;
        private Page _settingsReturn;
        private int _row;
        private readonly List<(string label, string value, System.Action use, System.Action<int> adjust)> _rows = new List<(string, string, System.Action, System.Action<int>)>();
        private string _toast;
        private float _toastUntil;
        private float _autosaveTimer;
        private float _navRepeat;
        private Officer2D _officer;
        private GUIStyle _title, _subtitle, _rowStyle, _rowSelected, _small;
        private Texture2D _dim, _panel, _accent, _highlight, _leftShade, _bottomShade;
        private MenuSlideshow _slides;
        private GUIStyle _logo, _logoSub, _titleRow, _titleRowSel, _titleValue, _titleHelp;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAnyObjectByType<GameMenu2D>() != null || FindAnyObjectByType<Officer2D>() == null) return;
            new GameObject("Game Menu").AddComponent<GameMenu2D>();
        }

        private void Start()
        {
            _officer = FindAnyObjectByType<Officer2D>();
            float vol = Services.TryGet(out ISettings s) ? s.GetFloat(SettingKeys.MasterVolume, 1f) : 1f;
            AudioListener.volume = vol;
#if UNITY_EDITOR
            // Editor play testing drops you straight into the game. Tools > GPA > Show title screen in editor turns it back on.
            if (!UnityEditor.EditorPrefs.GetBool(EditorTitlePref, false)) { BeginGame(false); return; }
#endif
            Open(Page.Title);
        }

        // ---- open / close --------------------------------------------------------------------
        private void Open(Page page)
        {
            _page = page; _row = 0; _open = true;
            TitleShowing = page == Page.Title;
            Time.timeScale = 0f;
            AudioListener.pause = true;
            if (Services.TryGet(out ITimeOfDay clock)) clock.Paused = true;
            if (Services.TryGet(out IGameStateService state))
            {
                state.Pop(GameState.Paused); state.Pop(GameState.Menu);
                state.Push(page == Page.Title ? GameState.Menu : GameState.Paused);
            }
            if (_officer != null) _officer.SetControlLocked(this, true);
        }

        private void Close()
        {
            _open = false;
            TitleShowing = false;
            Time.timeScale = 1f;
            AudioListener.pause = false;
            if (Services.TryGet(out ITimeOfDay clock)) clock.Paused = false;
            if (Services.TryGet(out IGameStateService state)) { state.Pop(GameState.Paused); state.Pop(GameState.Menu); }
            if (_officer != null) _officer.SetControlLocked(this, false);
            GameplayHotkeys.SuppressThisFrame();
        }

        private void Toast(string text) { _toast = text; _toastUntil = Time.unscaledTime + 2.5f; }

        // ---- per frame ----------------------------------------------------------------------
        private void Update()
        {
            if (!_open)
            {
                SaveGame.PlaySeconds += Time.unscaledDeltaTime;
                _autosaveTimer += Time.unscaledDeltaTime;
                if (_autosaveTimer >= _autosaveSeconds && Playing()) { _autosaveTimer = 0f; if (SaveGame.Save()) Toast("Autosaved"); }
                if (PausePressed() && Playing() && !GameplayHotkeys.Blocked) Open(Page.Pause);
                return;
            }

            if (BootSplash2D.Showing) return;   // the boot intro is still on screen
            BuildRows();
            var kb = Keyboard.current; var pad = Gamepad.current;
            int dir = 0;
            if (kb != null && (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame)) dir = -1;
            if (kb != null && (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame)) dir = 1;
            int side = 0;
            if (kb != null && (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame)) side = -1;
            if (kb != null && (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame)) side = 1;
            if (pad != null)
            {
                Vector2 v = pad.dpad.ReadValue() + pad.leftStick.ReadValue();
                if (v.magnitude < 0.5f) _navRepeat = 0f;
                else if (_navRepeat <= 0f)
                {
                    if (Mathf.Abs(v.y) > Mathf.Abs(v.x)) dir = v.y > 0 ? -1 : 1; else side = v.x > 0 ? 1 : -1;
                    _navRepeat = 0.22f;
                }
                _navRepeat -= Time.unscaledDeltaTime;
            }
            if (dir != 0 && _rows.Count > 0) _row = (_row + dir + _rows.Count) % _rows.Count;
            _row = Mathf.Clamp(_row, 0, Mathf.Max(0, _rows.Count - 1));
            if (side != 0 && _rows.Count > 0) _rows[_row].adjust?.Invoke(side);

            bool confirm = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)) || (pad != null && pad.buttonSouth.wasPressedThisFrame);
            bool back = (kb != null && kb.escapeKey.wasPressedThisFrame) || (pad != null && (pad.buttonEast.wasPressedThisFrame || pad.startButton.wasPressedThisFrame));
            if (confirm && _rows.Count > 0) _rows[_row].use?.Invoke();
            else if (back) Back();
        }

        private bool Playing() =>
            Services.TryGet(out IGameStateService s) ? s.Current == GameState.OnFoot || s.Current == GameState.InVehicle : true;

        private static bool PausePressed() =>
            (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) ||
            (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame);

        private void Back()
        {
            switch (_page)
            {
                case Page.Pause: Close(); break;
                case Page.Settings: _page = _settingsReturn; _row = 0; break;
                case Page.ConfirmNew: _page = Page.Title; _row = 0; break;
            }
        }

        // ---- menu content -------------------------------------------------------------------
        private void BuildRows()
        {
            _rows.Clear();
            switch (_page)
            {
                case Page.Title:
                    var save = SaveGame.Peek();
                    _rows.Add(("Start game", save != null ? $"continue · {save.place}" : "begin your career", () =>
                    {
                        bool loaded = save != null && SaveGame.Load();
                        if (!loaded) SaveGame.PlaySeconds = 0f;
                        Close();
                        BeginGame(loaded);
                    }, null));
                    if (save != null || PlayerLife.Instance != null && (PlayerLife.Instance.Money > 0 || PlayerLife.Instance.Data.properties.Count > 0))
                        _rows.Add(("New game", "start over from nothing", () => { _page = Page.ConfirmNew; _row = 1; }, null));
                    _rows.Add(("Settings", "", () => { _settingsReturn = Page.Title; _page = Page.Settings; _row = 0; }, null));
                    if (!Application.isMobilePlatform) _rows.Add(("Quit", "", Quit, null));
                    break;

                case Page.ConfirmNew:
                    _rows.Add(("Yes, start over", "money, homes, cars and position reset · rank kept", () => { SaveGame.Delete(); PlayerLife.Instance?.ResetAll(); SaveGame.PlaySeconds = 0f; Close(); BeginGame(false); }, null));
                    _rows.Add(("No, go back", "", () => { _page = Page.Title; _row = 0; }, null));
                    break;

                case Page.Pause:
                    _rows.Add(("Resume", "", Close, null));
                    _rows.Add(("Save game", "", () => Toast(SaveGame.Save() ? "Game saved" : "Couldn't save"), null));
                    _rows.Add(("Settings", "", () => { _settingsReturn = Page.Pause; _page = Page.Settings; _row = 0; }, null));
                    _rows.Add(("Save and quit to title", "", () => { SaveGame.Save(); Open(Page.Title); }, null));
                    break;

                case Page.Settings:
                    float vol = AudioListener.volume;
                    _rows.Add(("Master volume", $"◀ {Mathf.RoundToInt(vol * 100)}% ▶", () => SetVolume(vol >= 1f ? 0f : vol + 0.1f), d => SetVolume(vol + d * 0.1f)));
                    if (!Application.isMobilePlatform)
                        _rows.Add(("Fullscreen", Screen.fullScreen ? "◀ On ▶" : "◀ Off ▶", () => Screen.fullScreen = !Screen.fullScreen, d => Screen.fullScreen = !Screen.fullScreen));
                    _rows.Add(("Radio stations & volumes", "G / d-pad down in game", () => Toast("Open the radio menu in game with G or d-pad down"), null));
                    _rows.Add(("Back", "", Back, null));
                    break;
            }
        }

        private void SetVolume(float v)
        {
            v = Mathf.Clamp01(Mathf.Round(v * 10f) / 10f);
            AudioListener.volume = v;
            if (Services.TryGet(out ISettings s)) { s.Set(SettingKeys.MasterVolume, v); s.Save(); }
        }

        private void Quit()
        {
            SaveGame.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // Phones and alt-tab: save when the app goes to the background.
        private void OnApplicationPause(bool paused) { if (paused && !_open) SaveGame.Save(); }
        private void OnApplicationQuit() { if (!_open || _page == Page.Pause) SaveGame.Save(); }

        // ---- drawing ------------------------------------------------------------------------
        private void OnGUI()
        {
            if (!_open)
            {
                if (_toast != null && Time.unscaledTime < _toastUntil) DrawToast();
                return;
            }
            EnsureStyles();
            GUI.depth = -200;
            bool titleScreen = _page == Page.Title || _page == Page.ConfirmNew || (_page == Page.Settings && _settingsReturn == Page.Title);
            if (titleScreen) { DrawTitleScreen(); return; }
            Matrix4x4 old = UiScale.Begin(out Vector2 canvas);
            float w = canvas.x, h = canvas.y;
            GUI.DrawTexture(new Rect(0, 0, w, h), _dim);

            bool title = _page == Page.Title || _page == Page.ConfirmNew;
            float panelW = 640f, rowH = 54f;
            float panelH = (title ? 190f : 110f) + _rows.Count * rowH + 60f;
            var panel = new Rect(w * 0.5f - panelW * 0.5f, h * 0.5f - panelH * 0.5f, panelW, panelH);
            GUI.DrawTexture(panel, _panel);
            GUI.DrawTexture(new Rect(panel.x, panel.y, panel.width, 5f), _accent);

            float y = panel.y + 24f;
            if (title)
            {
                GUI.Label(new Rect(panel.x, y, panelW, 70), "GRAND POLICE AUTO", _title);
                GUI.Label(new Rect(panel.x, y + 70, panelW, 30), "New Halden PD · Patrol Division", _subtitle);
                y += 130f;
                if (_page == Page.ConfirmNew) { GUI.Label(new Rect(panel.x, y - 24, panelW, 26), "Start over? Your money, homes, cars and saved position will be lost.", _subtitle); }
            }
            else
            {
                GUI.Label(new Rect(panel.x, y, panelW, 50), _page == Page.Settings ? "SETTINGS" : "PAUSED", _title);
                y += 70f;
            }

            for (int i = 0; i < _rows.Count; i++)
            {
                var r = new Rect(panel.x + 30, y + i * rowH, panelW - 60, rowH - 8);
                bool sel = i == _row;
                if (sel) GUI.DrawTexture(r, _highlight);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { _row = i; _rows[i].use?.Invoke(); }
                GUI.Label(new Rect(r.x + 18, r.y, r.width * 0.5f, r.height), _rows[i].label, sel ? _rowSelected : _rowStyle);
                if (!string.IsNullOrEmpty(_rows[i].value))
                    GUI.Label(new Rect(r.x + r.width * 0.42f, r.y, r.width * 0.56f, r.height), _rows[i].value, _small);
            }

            string help = Gamepad.current != null ? "A select   B back   left / right change" : "↑ ↓ choose   Enter select   Esc back   ← → change";
            GUI.Label(new Rect(panel.x, panel.yMax - 40, panelW, 30), help, _subtitle);
            if (_toast != null && Time.unscaledTime < _toastUntil) GUI.Label(new Rect(panel.x, panel.yMax + 10, panelW, 30), _toast, _subtitle);
            GUI.matrix = old;
        }

        /// <summary>
        /// GTA V-style title: full-screen slideshow of police action, darkened on the left, with the
        /// game's name and a left-aligned menu list over it.
        /// </summary>
        private void DrawTitleScreen()
        {
            if (_slides == null) _slides = new MenuSlideshow();
            _slides.Draw(new Rect(0, 0, Screen.width, Screen.height));

            Matrix4x4 old = UiScale.Begin(out Vector2 canvas);
            float w = canvas.x, h = canvas.y;
            GUI.DrawTexture(new Rect(0, 0, Mathf.Max(760f, w * 0.52f), h), _leftShade);   // readable column
            GUI.DrawTexture(new Rect(0, h - 140f, w, 140f), _bottomShade);

            float x = 96f, y = h * 0.2f;
            // Logo block: thin accent bar, two-tone name, unit line.
            GUI.DrawTexture(new Rect(x, y, 6f, 150f), _accent);
            _logo.normal.textColor = Color.white;
            GUI.Label(new Rect(x + 28f, y - 6f, 900f, 80f), "GRAND POLICE", _logo);
            _logo.normal.textColor = new Color(0.45f, 0.68f, 1f);
            GUI.Label(new Rect(x + 28f, y + 62f, 900f, 80f), "AUTO", _logo);
            GUI.Label(new Rect(x + 30f, y + 134f, 900f, 30f), "NEW HALDEN POLICE DEPARTMENT  ·  PATROL DIVISION", _logoSub);

            y += 230f;
            if (_page == Page.ConfirmNew)
            {
                GUI.Label(new Rect(x, y, 700f, 30f), "Start over? Your money, homes, cars and saved position will be lost.", _titleValue);
                y += 44f;
            }
            else if (_page == Page.Settings)
            {
                GUI.Label(new Rect(x, y, 700f, 30f), "SETTINGS", _titleValue);
                y += 44f;
            }
            float rowH = 58f, rowW = 600f;
            for (int i = 0; i < _rows.Count; i++)
            {
                var r = new Rect(x, y + i * rowH, rowW, rowH - 8f);
                bool sel = i == _row;
                if (sel)
                {
                    GUI.DrawTexture(r, _highlight);
                    GUI.DrawTexture(new Rect(r.x, r.y, 5f, r.height), _accent);
                }
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { _row = i; _rows[i].use?.Invoke(); }
                GUI.Label(new Rect(r.x + 24f, r.y, rowW * 0.55f, r.height), _rows[i].label.ToUpperInvariant(), sel ? _titleRowSel : _titleRow);
                if (!string.IsNullOrEmpty(_rows[i].value))
                    GUI.Label(new Rect(r.x + rowW * 0.4f, r.y, rowW * 0.58f, r.height), _rows[i].value, _titleValue);
            }

            string help = Gamepad.current != null ? "Ⓐ  SELECT     Ⓑ  BACK" : "ENTER  SELECT     ESC  BACK     ↑ ↓  CHOOSE";
            GUI.Label(new Rect(x, h - 80f, 900f, 30f), help, _titleHelp);
            var right = new GUIStyle(_titleHelp) { alignment = TextAnchor.MiddleRight };
            GUI.Label(new Rect(w - 700f, h - 80f, 640f, 30f), "CODE 4 STUDIOS", right);
            if (_toast != null && Time.unscaledTime < _toastUntil) GUI.Label(new Rect(x, y + _rows.Count * rowH + 10f, 700f, 30f), _toast, _titleValue);
            GUI.matrix = old;
        }

        private void DrawToast()
        {
            EnsureStyles();
            Matrix4x4 old = UiScale.Begin(out Vector2 canvas);
            var r = new Rect(canvas.x * 0.5f - 120, 70, 240, 36);
            GUI.DrawTexture(r, _panel);
            GUI.Label(r, _toast, _subtitle);
            GUI.matrix = old;
        }

        private void EnsureStyles()
        {
            if (_title != null) return;
            _dim = Tex(new Color(0.02f, 0.03f, 0.06f, 0.78f));
            _panel = Tex(new Color(0.07f, 0.08f, 0.11f, 0.96f));
            _accent = Tex(new Color(0.2f, 0.45f, 0.95f));
            _highlight = Tex(new Color(0.2f, 0.45f, 0.95f, 0.35f));
            _leftShade = GradientTex(new Color(0f, 0.01f, 0.03f, 0.9f), new Color(0f, 0.01f, 0.03f, 0f), true);
            _bottomShade = GradientTex(new Color(0f, 0f, 0f, 0f), new Color(0f, 0f, 0f, 0.7f), false);
            _logo = new GUIStyle(GUI.skin.label) { fontSize = 78, fontStyle = FontStyle.BoldAndItalic, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Overflow, normal = { textColor = Color.white } };
            _logoSub = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(0.78f, 0.82f, 0.9f) } };
            _titleRow = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(0.82f, 0.85f, 0.9f, 0.85f) } };
            _titleRowSel = new GUIStyle(_titleRow) { normal = { textColor = Color.white } };
            _titleValue = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleRight, wordWrap = true, normal = { textColor = new Color(0.75f, 0.8f, 0.88f) } };
            _titleHelp = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(1f, 1f, 1f, 0.7f) } };
            _title = new GUIStyle(GUI.skin.label) { fontSize = 46, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            _subtitle = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.7f, 0.74f, 0.8f) } };
            _rowStyle = new GUIStyle(GUI.skin.label) { fontSize = 24, alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(0.85f, 0.87f, 0.9f) } };
            _rowSelected = new GUIStyle(_rowStyle) { fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            _small = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleRight, wordWrap = true, normal = { textColor = new Color(0.7f, 0.74f, 0.8f) } };
        }

        /// <summary>A 64-px gradient, left→right (horizontal) or top→bottom.</summary>
        private static Texture2D GradientTex(Color from, Color to, bool horizontal)
        {
            var t = new Texture2D(horizontal ? 64 : 1, horizontal ? 1 : 64) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int i = 0; i < 64; i++)
            {
                float k = i / 63f;
                Color c = Color.Lerp(from, to, horizontal ? Mathf.SmoothStep(0f, 1f, k) : k);
                if (horizontal) t.SetPixel(i, 0, c); else t.SetPixel(0, 63 - i, c);   // texture y is up; GUI draws row 0 at the bottom
            }
            t.Apply();
            return t;
        }

        private static Texture2D Tex(Color c)
        {
            var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t;
        }
    }
}

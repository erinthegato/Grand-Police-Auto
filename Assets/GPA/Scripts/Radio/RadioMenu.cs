using GPA.Core;
using GPA.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GPA.Radio
{
    /// <summary>
    /// The police radio menu. Opened with PoliceRadio/OpenRadio (G, or a quick tap of d-pad down),
    /// or the on-screen RADIO button on touch screens. Rows: dispatch volume, mute dispatch, request
    /// backup / Code 4, officer needs assistance, close. Up/down picks, left/right changes,
    /// Enter / A activates, Esc / B closes; mouse and touch work too.
    /// </summary>
    public sealed class RadioMenu : MonoBehaviour
    {
        private enum Row { DispatchVolume, DispatchMute, Backup, Emergency, Close }
        private const int RowCount = 5;

        private IInputService _input;
        private IGameStateService _state;
        private IPlayerAvatar _locked;
        private InputAction _open, _navigate, _submit, _close;
        private int _row;
        private float _navRepeat;
        private Vector2Int _lastNav;
        private bool _waitForRelease;
        private string _hint;
        private Texture2D _panel, _rowOn, _rowOff, _accent, _barBg, _barFill;
        private GUIStyle _title, _label, _value, _small, _touchButton;

        public static RadioMenu Instance { get; private set; }
        public bool IsOpen { get; private set; }

        private static bool TouchUi => Application.isMobilePlatform || Touchscreen.current != null;
        private static IRadioAudioManager Radio => Services.TryGet(out IRadioAudioManager r) ? r : null;

        private void Awake()
        {
            Instance = this;
            _panel = Solid(new Color(0.06f, 0.07f, 0.09f, 0.94f));
            _rowOn = Solid(new Color(0.16f, 0.36f, 0.85f, 0.9f));
            _rowOff = Solid(new Color(1f, 1f, 1f, 0.05f));
            _accent = Solid(new Color(0.16f, 0.36f, 0.85f, 1f));
            _barBg = Solid(new Color(1f, 1f, 1f, 0.12f));
            _barFill = Solid(new Color(0.37f, 0.82f, 0.42f, 1f));
        }

        private void OnDestroy() { if (Instance == this) Instance = null; if (IsOpen) Close(); }

        private bool Bind()
        {
            if (_input == null && !Services.TryGet(out _input)) return false;
            if (_state == null && !Services.TryGet(out _state)) return false;
            if (_open == null)
            {
                try
                {
                    _open = _input.Get(ActionMaps.PoliceRadio, "OpenRadio");
                    _navigate = _input.Get(ActionMaps.PoliceRadio, "Navigate");
                    _submit = _input.Get(ActionMaps.PoliceRadio, "Transmit");
                    _close = _input.Get(ActionMaps.PoliceRadio, "Close");
                }
                catch { return false; }
            }
            return true;
        }

        private void Update()
        {
            if (!Bind()) return;
            if (!IsOpen)
            {
                bool blocked = _state.Current != GameState.OnFoot && _state.Current != GameState.InVehicle;
                if (!blocked && _open.WasPerformedThisFrame()) Open();
                return;
            }
            if (_state.Current != GameState.Radio) { Close(); return; }
            if (_waitForRelease) { if (!_submit.IsPressed() && !_open.IsPressed()) _waitForRelease = false; return; }

            bool keyboardOpen = _open.WasPerformedThisFrame() && _open.activeControl != null && _open.activeControl.device is Keyboard;
            if (_close.WasPerformedThisFrame() || keyboardOpen) { Close(); return; }

            Vector2 v = _navigate.ReadValue<Vector2>();
            var dir = new Vector2Int(Mathf.Abs(v.x) > 0.5f ? (int)Mathf.Sign(v.x) : 0, Mathf.Abs(v.y) > 0.5f && Mathf.Abs(v.x) <= 0.5f ? (int)Mathf.Sign(v.y) : 0);
            _navRepeat -= Time.unscaledDeltaTime;
            if (dir != Vector2Int.zero && (dir != _lastNav || _navRepeat <= 0f))
            {
                if (dir.y != 0) _row = (_row - dir.y + RowCount) % RowCount;
                else Adjust((Row)_row, dir.x);
                _navRepeat = dir != _lastNav ? 0.4f : 0.11f;
            }
            _lastNav = dir;
            if (_submit.WasPerformedThisFrame()) Activate((Row)_row);
        }

        public void Open()
        {
            if (IsOpen || !Bind()) return;
            IsOpen = true;
            _waitForRelease = true;
            _hint = null;
            _state.Push(GameState.Radio);
            if (Services.TryGet(out IPlayerAvatar avatar)) { _locked = avatar; avatar.SetControlLocked(this, true); }
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            GameplayHotkeys.SuppressThisFrame();
            _state?.Pop(GameState.Radio);
            _locked?.SetControlLocked(this, false);
            _locked = null;
        }

        private void Adjust(Row row, int step)
        {
            var r = Radio;
            if (r == null) return;
            if (row == Row.DispatchVolume) r.DispatchVolume = Mathf.Clamp01(Mathf.Round((r.DispatchVolume + step * 0.1f) * 10f) / 10f);
            else if (row == Row.DispatchMute) r.DispatchMuted = !r.DispatchMuted;
        }

        private void Activate(Row row)
        {
            switch (row)
            {
                case Row.DispatchVolume: Adjust(row, Radio != null && Radio.DispatchVolume >= 1f ? -10 : 1); break;
                case Row.DispatchMute: Adjust(row, 1); break;
                case Row.Backup:
                    if (Services.TryGet(out IBackupService backup)) { if (backup.UnitsAssigned > 0) backup.CancelBackup(); else backup.RequestBackup(false); Close(); }
                    else _hint = "Backup isn't available in this scene.";
                    break;
                case Row.Emergency:
                    if (Services.TryGet(out IBackupService help)) { help.RequestBackup(true); Close(); }
                    else _hint = "Backup isn't available in this scene.";
                    break;
                case Row.Close: Close(); break;
            }
        }

        // ---- drawing ------------------------------------------------------------------------
        private void OnGUI()
        {
            if (!IsOpen) { DrawTouchOpenButton(); return; }
            EnsureStyles();
            GUI.depth = -90;
            Matrix4x4 old = UiScale.Begin(out Vector2 canvas);
            float w = canvas.x, h = canvas.y;
            var panel = new Rect(w * 0.5f - 360f, h * 0.5f - 200f, 720f, 400f);
            GUI.DrawTexture(panel, _panel);
            GUI.DrawTexture(new Rect(panel.x, panel.y, panel.width, 4f), _accent);
            GUI.Label(new Rect(panel.x + 28, panel.y + 18, 500, 36), "POLICE RADIO", _title);

            var r = Radio;
            const float rowH = 46f, gap = 4f;
            for (int i = 0; i < RowCount; i++)
            {
                var row = (Row)i;
                var rect = new Rect(panel.x + 28, panel.y + 70 + i * (rowH + gap), panel.width - 56, rowH);
                GUI.DrawTexture(rect, i == _row ? _rowOn : _rowOff);
                if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
                {
                    if (_row == i || TouchUi) { _row = i; Activate(row); } else _row = i;
                }
                string label = "", value = "";
                float bar = -1f;
                bool haveUnits = Services.TryGet(out IBackupService bs) && bs.UnitsAssigned > 0;
                switch (row)
                {
                    case Row.DispatchVolume: label = "Dispatch volume"; bar = r != null ? r.DispatchVolume : 1f; value = $"{Mathf.RoundToInt(bar * 100)}%"; break;
                    case Row.DispatchMute: label = "Mute dispatch"; value = r != null && r.DispatchMuted ? "On" : "Off"; break;
                    case Row.Backup: label = haveUnits ? "Cancel backup (Code 4)" : "Request backup"; value = haveUnits ? $"{bs.UnitsAssigned} unit(s) assigned" : RadioCodes.Get("backup", "10-78") + " · also B key"; break;
                    case Row.Emergency: label = "Officer needs assistance"; value = RadioCodes.Get("emergency", "10-33") + " · emergency, all units"; break;
                    case Row.Close: label = "Close"; break;
                }
                GUI.Label(new Rect(rect.x + 16, rect.y, 320, rowH), label, _label);
                if (bar >= 0f)
                {
                    var b = new Rect(rect.x + 330, rect.y + rowH * 0.5f - 6f, 240, 12);
                    GUI.DrawTexture(b, _barBg);
                    GUI.DrawTexture(new Rect(b.x, b.y, b.width * bar, b.height), _barFill);
                    GUI.Label(new Rect(rect.x + 585, rect.y, rect.width - 600, rowH), value, _value);
                }
                else GUI.Label(new Rect(rect.x + 330, rect.y, rect.width - 346, rowH), value, _value);
            }

            string help = _hint ?? (TouchUi && !(_input != null && _input.IsGamepadActive)
                ? "Tap a row to use it"
                : (_input != null && _input.IsGamepadActive ? "D-pad choose / change   A select   B close" : "↑ ↓ choose   ← → change   Enter select   Esc / G close"));
            GUI.Label(new Rect(panel.x + 28, panel.yMax - 40, panel.width - 56, 28), help, _small);
            GUI.matrix = old;
        }

        private void DrawTouchOpenButton()
        {
            if (!TouchUi || !Bind()) return;
            if (_state.Current != GameState.OnFoot && _state.Current != GameState.InVehicle) return;
            if (_touchButton == null) _touchButton = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            float scale = UiScale.Factor();
            Rect safe = UiScale.Handheld ? UiScale.Safe : new Rect(0, 0, Screen.width, Screen.height);
            _touchButton.fontSize = Mathf.RoundToInt(22 * scale);
            var rect = new Rect(safe.xMax - 150 * scale, safe.center.y - 40 * scale, 130 * scale, 80 * scale);
            GUI.depth = -90;
            if (GUI.Button(rect, "RADIO", _touchButton)) Open();
        }

        private void EnsureStyles()
        {
            if (_title != null) return;
            _title = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            _label = new GUIStyle(GUI.skin.label) { fontSize = 21, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } };
            _value = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(0.8f, 0.84f, 0.9f) } };
            _small = new GUIStyle(GUI.skin.label) { fontSize = 16, normal = { textColor = new Color(0.65f, 0.7f, 0.76f) } };
        }

        private static Texture2D Solid(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }
    }
}

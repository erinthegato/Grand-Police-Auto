using System;
using System.Collections.Generic;
using GPA.Core;
using GPA.TopDown2D;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GPA.Game
{
    /// <summary>
    /// One shared list menu for the off-duty places (property sales, home options, dealership,
    /// garage, casino lobby). Keyboard, mouse, controller and touch; locks the officer while open.
    /// The page is rebuilt each frame by the caller's builder, so prices and money stay current.
    /// </summary>
    public sealed class LifeMenu : MonoBehaviour
    {
        public sealed class Row
        {
            public string Label, Value;
            public Action Use;
            public bool Enabled = true;
            public Row(string label, string value, Action use, bool enabled = true) { Label = label; Value = value; Use = use; Enabled = enabled; }
        }

        public static LifeMenu Instance { get; private set; }
        public static bool AnyOpen => Instance != null && Instance._open;

        private bool _open;
        private string _title, _subtitle;
        private Func<List<Row>> _builder;
        private List<Row> _rows = new List<Row>();
        private int _row;
        private float _navRepeat;
        private string _message;
        private float _messageUntil;
        private Officer2D _officer;
        private GUIStyle _titleStyle, _subStyle, _rowStyle, _rowSel, _valueStyle, _off, _offValue;
        private int _openedFrame;

        public static LifeMenu Get()
        {
            if (Instance == null) Instance = new GameObject("Life Menu").AddComponent<LifeMenu>();
            return Instance;
        }

        public void Show(string title, string subtitle, Func<List<Row>> builder)
        {
            _title = title; _subtitle = subtitle; _builder = builder; _row = 0; _message = null;
            _openedFrame = Time.frameCount;   // the button that opened the menu must not also press a row
            if (!_open)
            {
                _open = true;
                if (_officer == null) _officer = FindAnyObjectByType<Officer2D>();
                if (_officer != null) _officer.SetControlLocked(this, true);
                if (Services.TryGet(out IGameStateService st)) st.Push(GameState.Menu);
            }
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            if (_officer != null) _officer.SetControlLocked(this, false);
            if (Services.TryGet(out IGameStateService st)) st.Pop(GameState.Menu);
            GameplayHotkeys.SuppressThisFrame();
        }

        public void Message(string text) { _message = text; _messageUntil = Time.unscaledTime + 2.5f; }

        private void Update()
        {
            if (!_open) return;
            _rows = _builder != null ? _builder() : new List<Row>();
            var kb = Keyboard.current; var pad = Gamepad.current;
            int dir = 0;
            if (kb != null && (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame)) dir = -1;
            if (kb != null && (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame)) dir = 1;
            if (pad != null)
            {
                float y = pad.dpad.ReadValue().y + pad.leftStick.ReadValue().y;
                if (Mathf.Abs(y) < 0.5f) _navRepeat = 0f;
                else if (_navRepeat <= 0f) { dir = y > 0 ? -1 : 1; _navRepeat = 0.22f; }
                _navRepeat -= Time.unscaledDeltaTime;
            }
            if (Time.frameCount <= _openedFrame + 1) return;
            if (_rows.Count > 0 && dir != 0) _row = (_row + dir + _rows.Count) % _rows.Count;
            _row = Mathf.Clamp(_row, 0, Mathf.Max(0, _rows.Count - 1));
            bool confirm = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame)) || (pad != null && pad.buttonSouth.wasPressedThisFrame);
            bool back = (kb != null && kb.escapeKey.wasPressedThisFrame) || (pad != null && pad.buttonEast.wasPressedThisFrame);
            if (confirm && _rows.Count > 0) Activate(_row);
            else if (back) Close();
        }

        private void Activate(int i)
        {
            if (i < 0 || i >= _rows.Count) return;
            if (!_rows[i].Enabled) { GameAudio.UiClick(); return; }
            _rows[i].Use?.Invoke();
            GameAudio.UiClick();
        }

        private void OnGUI()
        {
            if (!_open) return;
            Styles();
            GUI.depth = -100;
            Matrix4x4 old = UiScale.Begin(out Vector2 canvas);
            float w = 680f, rowH = 50f;
            float h = 120f + _rows.Count * rowH + 50f;
            var panel = new Rect(canvas.x * 0.5f - w * 0.5f, Mathf.Max(20f, canvas.y * 0.5f - h * 0.5f), w, h);
            GUI.color = new Color(0.06f, 0.07f, 0.1f, 0.95f); GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = new Color(0.3f, 0.85f, 0.45f); GUI.DrawTexture(new Rect(panel.x, panel.y, w, 5f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(panel.x + 24, panel.y + 16, w - 48, 40), _title, _titleStyle);
            string money = PlayerLife.Instance != null ? $"${PlayerLife.Instance.Money:N0}" : "";
            GUI.Label(new Rect(panel.x + 24, panel.y + 16, w - 48, 40), money, _valueStyle);
            GUI.Label(new Rect(panel.x + 24, panel.y + 58, w - 48, 30), _subtitle, _subStyle);
            float y = panel.y + 100f;
            for (int i = 0; i < _rows.Count; i++)
            {
                var r = new Rect(panel.x + 18, y + i * rowH, w - 36, rowH - 6);
                if (i == _row) { GUI.color = new Color(0.3f, 0.85f, 0.45f, 0.28f); GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = Color.white; }
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { _row = i; Activate(i); }
                var style = !_rows[i].Enabled ? _off : i == _row ? _rowSel : _rowStyle;
                GUI.Label(new Rect(r.x + 14, r.y, r.width * 0.6f, r.height), _rows[i].Label, style);
                GUI.Label(new Rect(r.x + r.width * 0.4f, r.y, r.width * 0.6f - 14, r.height), _rows[i].Value, _rows[i].Enabled ? _valueStyle : _offValue);
            }
            string help = _message != null && Time.unscaledTime < _messageUntil ? _message
                : Gamepad.current != null ? "A select   B back" : "↑ ↓ choose   Enter / E select   Esc back";
            GUI.Label(new Rect(panel.x, panel.yMax - 40, w, 30), help, _subStyle);
            GUI.matrix = old;
        }

        private void Styles()
        {
            if (_titleStyle != null) return;
            _titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            _subStyle = new GUIStyle(GUI.skin.label) { fontSize = 17, alignment = TextAnchor.MiddleCenter, wordWrap = true, normal = { textColor = new Color(0.72f, 0.76f, 0.82f) } };
            _rowStyle = new GUIStyle(GUI.skin.label) { fontSize = 21, alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(0.86f, 0.88f, 0.92f) } };
            _rowSel = new GUIStyle(_rowStyle) { fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            _valueStyle = new GUIStyle(GUI.skin.label) { fontSize = 19, alignment = TextAnchor.MiddleRight, normal = { textColor = new Color(0.45f, 0.95f, 0.5f) } };
            _off = new GUIStyle(_rowStyle) { normal = { textColor = new Color(0.5f, 0.52f, 0.56f) } };
            _offValue = new GUIStyle(_valueStyle) { normal = { textColor = new Color(0.5f, 0.52f, 0.56f) } };
        }
    }
}

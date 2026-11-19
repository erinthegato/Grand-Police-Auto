using System;
using System.Collections.Generic;
using GPA.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GPA.Police
{
    /// <summary>
    /// Contextual police interaction menu (spec §9/§10): a title, a running conversation/records log,
    /// and a list of options. Works with keyboard (W/S or arrows, E/Enter, Esc/Backspace) and
    /// controller (D-pad / left stick, A, B). The officer's movement is locked while it's open.
    /// Gameplay systems supply the options; this class only presents them.
    /// </summary>
    public sealed class InteractionMenu2D : MonoBehaviour
    {
        public struct Option
        {
            public string Label;
            public Action Run;
            public bool Enabled;
            public Option(string label, Action run, bool enabled = true) { Label = label; Run = run; Enabled = enabled; }
        }

        public static InteractionMenu2D Instance { get; private set; }

        private readonly List<Option> _options = new List<Option>();
        private readonly List<string> _log = new List<string>();
        private InputAction _up, _down, _confirm, _back, _stick;
        private Func<List<Option>> _builder;
        private Action _onClose;
        private string _title, _subtitle;
        private int _selected;
        private float _stickRepeat;
        private IPlayerAvatar _lockedAvatar;
        private Texture2D _panel, _highlight;
        private GUIStyle _titleStyle, _body, _option, _small;

        public bool IsOpen { get; private set; }

        private void Awake()
        {
            Instance = this;
            _up = new InputAction("MenuUp", InputActionType.Button);
            _up.AddBinding("<Keyboard>/w"); _up.AddBinding("<Keyboard>/upArrow"); _up.AddBinding("<Gamepad>/dpad/up");
            _down = new InputAction("MenuDown", InputActionType.Button);
            _down.AddBinding("<Keyboard>/s"); _down.AddBinding("<Keyboard>/downArrow"); _down.AddBinding("<Gamepad>/dpad/down");
            _confirm = new InputAction("MenuConfirm", InputActionType.Button);
            _confirm.AddBinding("<Keyboard>/e"); _confirm.AddBinding("<Keyboard>/enter"); _confirm.AddBinding("<Gamepad>/buttonSouth");
            _back = new InputAction("MenuBack", InputActionType.Button);
            _back.AddBinding("<Keyboard>/escape"); _back.AddBinding("<Keyboard>/backspace"); _back.AddBinding("<Gamepad>/buttonEast");
            _stick = new InputAction("MenuStick", InputActionType.Value);
            _stick.AddBinding("<Gamepad>/leftStick/y");
            _panel = Solid(new Color(0.05f, 0.06f, 0.08f, 0.9f));
            _highlight = Solid(new Color(0.16f, 0.36f, 0.85f, 0.95f));
        }

        private void OnDestroy() { if (Instance == this) Instance = null; Unlock(); }

        /// <summary>Opens the menu. <paramref name="buildOptions"/> is re-run after every choice so options can change.</summary>
        public void Open(string title, string subtitle, Func<List<Option>> buildOptions, Action onClose = null)
        {
            _title = title; _subtitle = subtitle; _builder = buildOptions; _onClose = onClose;
            _log.Clear();
            _selected = 0;
            Rebuild();
            IsOpen = true;
            _up.Enable(); _down.Enable(); _confirm.Enable(); _back.Enable(); _stick.Enable();
            if (Services.TryGet(out _lockedAvatar)) _lockedAvatar.SetControlLocked(this, true);
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            _up.Disable(); _down.Disable(); _confirm.Disable(); _back.Disable(); _stick.Disable();
            Unlock();
            var cb = _onClose; _onClose = null;
            cb?.Invoke();
        }

        public void SetSubtitle(string subtitle) => _subtitle = subtitle;

        /// <summary>Adds a line to the conversation/records log shown above the options.</summary>
        public void Say(string line)
        {
            _log.Add(line);
            while (_log.Count > 12) _log.RemoveAt(0);
        }

        private void Unlock()
        {
            if (_lockedAvatar != null) _lockedAvatar.SetControlLocked(this, false);
            _lockedAvatar = null;
        }

        private void Rebuild()
        {
            _options.Clear();
            if (_builder != null) _options.AddRange(_builder());
            _selected = Mathf.Clamp(_selected, 0, Mathf.Max(0, _options.Count - 1));
        }

        private void Update()
        {
            if (!IsOpen || GameplayHotkeys.Blocked) return;
            float stick = _stick.ReadValue<float>();
            _stickRepeat -= Time.unscaledDeltaTime;
            int move = 0;
            if (_up.WasPerformedThisFrame()) move = -1;
            if (_down.WasPerformedThisFrame()) move = 1;
            if (Mathf.Abs(stick) > 0.6f && _stickRepeat <= 0f) { move = stick > 0 ? -1 : 1; _stickRepeat = 0.22f; }
            if (Mathf.Abs(stick) < 0.3f) _stickRepeat = 0f;
            if (move != 0 && _options.Count > 0) _selected = (_selected + move + _options.Count) % _options.Count;

            if (_back.WasPerformedThisFrame()) { Close(); return; }
            if (_confirm.WasPerformedThisFrame() && _options.Count > 0)
            {
                Option o = _options[_selected];
                if (!o.Enabled) return;
                o.Run?.Invoke();
                if (IsOpen) Rebuild();
            }
        }

        private void OnGUI()
        {
            if (!IsOpen) return;
            EnsureStyles();
            Matrix4x4 old = UiScale.Begin(out Vector2 canvas);
            float w = canvas.x;

            float width = 620f;
            float logH = _log.Count * 24f + 8f;
            float height = 96f + logH + _options.Count * 34f + 34f;
            var r = new Rect(w - width - 30f, 140f, width, height);
            GUI.DrawTexture(r, _panel);
            GUI.DrawTexture(new Rect(r.x, r.y, 6, r.height), _highlight);
            GUI.Label(new Rect(r.x + 20, r.y + 10, width - 40, 32), _title, _titleStyle);
            GUI.Label(new Rect(r.x + 20, r.y + 44, width - 40, 26), _subtitle, _small);

            float y = r.y + 80f;
            foreach (string line in _log) { GUI.Label(new Rect(r.x + 20, y, width - 40, 24), line, _body); y += 24f; }
            y += 8f;
            for (int i = 0; i < _options.Count; i++)
            {
                var row = new Rect(r.x + 14, y, width - 28, 32);
                if (i == _selected) GUI.DrawTexture(row, _highlight);
                string label = _options[i].Enabled ? _options[i].Label : $"<color=#777777>{_options[i].Label}</color>";
                GUI.Label(new Rect(row.x + 10, row.y + 3, row.width - 20, 28), label, _option);
                y += 34f;
            }
            GUI.Label(new Rect(r.x + 20, y + 4, width - 40, 24), "[W/S · D-pad] choose   [E · A] select   [Esc · B] close", _small);
            GUI.matrix = old;
        }

        private void EnsureStyles()
        {
            if (_titleStyle != null) return;
            _titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, richText = true, normal = { textColor = Color.white } };
            _body = new GUIStyle(GUI.skin.label) { fontSize = 18, richText = true, normal = { textColor = new Color(0.85f, 0.88f, 0.92f) } };
            _option = new GUIStyle(GUI.skin.label) { fontSize = 21, richText = true, normal = { textColor = Color.white } };
            _small = new GUIStyle(GUI.skin.label) { fontSize = 16, richText = true, normal = { textColor = new Color(0.65f, 0.7f, 0.76f) } };
        }

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c); t.Apply();
            return t;
        }
    }
}

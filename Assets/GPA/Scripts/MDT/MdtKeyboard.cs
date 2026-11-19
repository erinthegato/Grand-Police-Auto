using GPA.Core;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace GPA.Mdt
{
    /// <summary>
    /// The MDT's own on-screen keyboard, so text entry never raises the phone's system keyboard and
    /// works with a controller (d-pad + A, X backspace, Y space, B done) or by tapping. A physical
    /// keyboard still types straight into it. Edits are applied live to the field being edited.
    /// </summary>
    internal sealed class MdtKeyboard
    {
        public delegate bool ButtonFn(Rect r, string text, GUIStyle style, string id);

        public struct Suggestion
        {
            public string Label;
            public Action Pick;
            public Suggestion(string label, Action pick) { Label = label; Pick = pick; }
        }

        private const string Digits = "1234567890-", DigitsShifted = "!?#$%&*():;";
        private const string Row1 = "qwertyuiop'", Row2 = "asdfghjkl.,", Row3 = "zxcvbnm/";

        private Func<string> _get;
        private Action<string> _set;
        private Action _onClose;
        private Func<string, List<Suggestion>> _suggest;
        private List<Suggestion> _suggestions = new List<Suggestion>();
        private string _suggestedFor;
        private int _maxLength;
        private bool _multiline, _autoCap, _shift, _caps;

        public bool Open { get; private set; }
        public string FieldId { get; private set; }
        public string Label { get; private set; }
        public bool Multiline => _multiline;
        private string Text => _get() ?? "";

        /// <param name="caps">Start in caps lock (plates, IDs).</param>
        /// <param name="autoCap">Capitalise the first letter of each sentence (titles, narratives).</param>
        public void Begin(string fieldId, string label, Func<string> get, Action<string> set, int maxLength, bool multiline,
                          bool caps = false, bool autoCap = false, Func<string, List<Suggestion>> suggest = null, Action onClose = null)
        {
            if (Open && FieldId != fieldId) Close();
            FieldId = fieldId; Label = label;
            _get = get; _set = set; _maxLength = maxLength; _multiline = multiline;
            _caps = caps; _autoCap = autoCap; _shift = false;
            _suggest = suggest; _onClose = onClose;
            _suggestedFor = null;
            Open = true;
        }

        public void Close()
        {
            if (!Open) return;
            Open = false;
            FieldId = null;
            var cb = _onClose; _onClose = null;
            cb?.Invoke();
        }

        // ------------------------------------------------------------------ editing

        private void Set(string value)
        {
            if (value.Length > _maxLength) value = value.Substring(0, _maxLength);
            _set(value);
        }

        private bool SentenceStart
        {
            get
            {
                string t = Text.TrimEnd(' ');
                return t.Length == 0 || t.EndsWith(".") || t.EndsWith("!") || t.EndsWith("?") || t.EndsWith("\n");
            }
        }

        /// <summary>A key on the on-screen keyboard: applies shift, caps lock and sentence capitals.</summary>
        public void PressChar(char c)
        {
            if (char.IsLetter(c))
            {
                bool upper = _caps ^ _shift || (_autoCap && !_caps && SentenceStart);
                c = upper ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c);
            }
            Set(Text + c);
            _shift = false;
        }

        /// <summary>A character from a physical keyboard: typed exactly as given.</summary>
        public void TypeRaw(char c) => Set(Text + c);

        public void Backspace()
        {
            string t = Text;
            if (t.Length > 0) Set(t.Substring(0, t.Length - 1));
        }

        public void Space()
        {
            if (!Text.EndsWith(" ")) Set(Text + " ");
        }

        public void Newline()
        {
            if (_multiline) Set(Text.TrimEnd(' ') + "\n");
            else Close();
        }

        public void DeleteWord()
        {
            string t = Text.TrimEnd(' ', '\n');
            int cut = Mathf.Max(t.LastIndexOf(' '), t.LastIndexOf('\n'));
            Set(cut < 0 ? "" : t.Substring(0, cut + 1));
        }

        public void Clear() => Set("");

        // ------------------------------------------------------------------ drawing

        public float Height => (_multiline ? 96 : 64) + (_suggest != null ? 56 : 0) + 5 * 62 + 30;

        public void Draw(Rect area, MdtStyles s, ButtonFn button, bool gamepad)
        {
            GUI.DrawTexture(area, s.KeyBg);
            GUI.DrawTexture(new Rect(area.x, area.y, area.width, 3), s.Accent);
            float pad = 20f, y = area.y + 14f;

            // What is being typed, so it stays visible even when the field is under the keyboard.
            float previewH = _multiline ? 84 : 52;
            GUI.Label(new Rect(area.x + pad, y, area.width - 2 * pad - 660, 22), Label.ToUpperInvariant(), s.H2);
            string hint = gamepad ? "A  key     X  delete     Y  space     B  done" : "Type or tap keys     Enter  " + (_multiline ? "new line" : "done") + "     Esc  done";
            GUI.Label(new Rect(area.xMax - pad - 640, y, 640, 22), hint, GuiStyleCache.With(s.Small, TextAnchor.UpperRight));
            y += 26;
            var preview = new Rect(area.x + pad, y, area.width - 2 * pad, previewH);
            GUI.DrawTexture(preview, s.FieldActive);
            string text = Text;
            if (_multiline && text.Length > 300) text = "…" + text.Substring(text.Length - 300);
            string caret = Mathf.Repeat(Time.unscaledTime, 1f) < 0.55f ? "<color=#ffd040>▌</color>" : " ";
            var style = _multiline ? s.Wrapped : s.Body;
            string shown = Escape(text) + caret;
            if (_multiline)
            {
                // Keep the end of the text (where typing happens) in view.
                float h = style.CalcHeight(new GUIContent(shown), preview.width - 24);
                GUI.BeginGroup(preview);
                GUI.Label(new Rect(12, Mathf.Min(6, previewH - h - 6), preview.width - 24, Mathf.Max(h, previewH)), shown, style);
                GUI.EndGroup();
            }
            else GUI.Label(new Rect(preview.x + 12, preview.y + 12, preview.width - 24, 30), shown, style);
            y += previewH + 8;

            if (_suggest != null)
            {
                if (_suggestedFor != Text) { _suggestedFor = Text; _suggestions = _suggest(Text) ?? new List<Suggestion>(); }
                float x = area.x + pad;
                if (_suggestions.Count == 0) GUI.Label(new Rect(x, y + 12, 600, 26), "<color=#6c7480>Keep typing for suggestions…</color>", s.Body);
                for (int i = 0; i < _suggestions.Count && i < 4; i++)
                {
                    float w = Mathf.Min(420f, (area.width - 2 * pad - 30) / 4f);
                    var r = new Rect(x, y, w, 44);
                    var sug = _suggestions[i];
                    if (button(r, sug.Label, s.ChipStyle, "osk:sug:" + i)) { sug.Pick?.Invoke(); Close(); return; }
                    x += w + 10;
                }
                y += 56;
            }

            // Keys: 11 units wide.
            float gap = 8f;
            float unit = Mathf.Min(118f, (area.width - 2 * pad - 10 * gap) / 11f);
            float left = area.x + (area.width - (unit * 11 + gap * 10)) / 2f;
            float kh = 54f;
            bool upper = _caps ^ _shift || (_autoCap && !_caps && SentenceStart);

            KeyRow("r0", _shift ? DigitsShifted : Digits, left, y, unit, kh, gap, s, button, false); y += kh + gap;
            KeyRow("r1", Row1, left, y, unit, kh, gap, s, button, upper); y += kh + gap;
            KeyRow("r2", Row2, left, y, unit, kh, gap, s, button, upper); y += kh + gap;

            float x3 = left;
            if (button(new Rect(x3, y, unit * 1.5f + gap * 0.5f, kh), _shift ? "<color=#ffd040>⇧ Shift</color>" : "⇧ Shift", s.Key, "osk:shift")) _shift = !_shift;
            x3 += unit * 1.5f + gap * 1.5f;
            KeyRow("r3", Row3, x3, y, unit, kh, gap, s, button, upper);
            x3 += Row3.Length * (unit + gap);
            if (button(new Rect(x3, y, unit * 1.5f + gap * 0.5f, kh), "← Delete", s.ButtonStyle, "osk:back")) Backspace();
            y += kh + gap;

            float x4 = left;
            float wide = unit * 1.5f + gap * 0.5f;
            if (button(new Rect(x4, y, wide, kh), _caps ? "<color=#ffd040>CAPS</color>" : "Caps", s.Key, "osk:caps")) { _caps = !_caps; _shift = false; }
            x4 += wide + gap;
            float spaceW = (_multiline ? 4f : 5f) * unit + (_multiline ? 3f : 4f) * gap;
            if (button(new Rect(x4, y, spaceW, kh), "space", s.Key, "osk:space")) Space();
            x4 += spaceW + gap;
            if (_multiline)
            {
                if (button(new Rect(x4, y, unit, kh), "New line", s.ButtonStyle, "osk:newline")) Newline();
                x4 += unit + gap;
            }
            if (button(new Rect(x4, y, wide, kh), "Del word", s.ButtonStyle, "osk:delword")) DeleteWord();
            x4 += wide + gap;
            if (button(new Rect(x4, y, wide, kh), "Clear", s.ButtonStyle, "osk:clear")) Clear();
            x4 += wide + gap;
            if (button(new Rect(x4, y, wide, kh), "Done", s.Primary, "osk:done")) Close();
        }

        private void KeyRow(string rowId, string keys, float x, float y, float unit, float kh, float gap, MdtStyles s, ButtonFn button, bool upper)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                char c = keys[i];
                string label = char.IsLetter(c) ? (upper ? char.ToUpperInvariant(c) : c).ToString() : c.ToString();
                // Ids follow the key's position so focus stays put when Shift swaps the symbols.
                if (button(new Rect(x + i * (unit + gap), y, unit, kh), label, s.Key, $"osk:{rowId}:{i}")) PressChar(c);
            }
        }

        private static string Escape(string text) => text.Replace("<", "‹").Replace(">", "›");
    }
}

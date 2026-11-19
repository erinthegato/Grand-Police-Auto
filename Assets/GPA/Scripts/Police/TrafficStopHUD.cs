using GPA.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GPA.Police
{
    /// <summary>
    /// Traffic-stop HUD (presentation only): target brackets and the pull-over prompt while your
    /// lights are on, then the stop's status line. Reads <see cref="TrafficStopManager"/>.
    /// </summary>
    public sealed class TrafficStopHUD : MonoBehaviour
    {
        private Texture2D _panel, _bracket;
        private GUIStyle _text;
        private bool _gamepad;

        private void Awake()
        {
            _panel = Solid(new Color(0.05f, 0.06f, 0.08f, 0.85f));
            _bracket = Solid(new Color(1f, 0.85f, 0.25f, 0.95f));
        }

        private void Update()
        {
            if (Gamepad.current != null && Gamepad.current.wasUpdatedThisFrame) _gamepad = true;
            if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) _gamepad = false;
        }

        private void OnGUI()
        {
            var m = TrafficStopManager.Instance;
            Camera cam = Camera.main;
            if (m == null || cam == null) return;
            if (_text == null) _text = new GUIStyle(GUI.skin.label) { fontSize = 20, richText = true, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };

            Matrix4x4 old = UiScale.Begin(out Vector2 canvas);
            float w = canvas.x, h = canvas.y;

            Transform target = m.Candidate != null ? m.Candidate.transform : m.Target != null ? m.Target.transform : null;
            if (target != null)
            {
                Vector3 s = cam.WorldToScreenPoint(target.position);
                if (s.z > 0)
                {
                    Vector2 c = UiScale.ScreenToCanvas(s);
                    float size = 70f;
                    DrawBrackets(new Rect(c.x - size * 0.5f, c.y - size * 0.5f, size, size));
                }
            }

            string line = null;
            if (m.Candidate != null)
                line = $"[{(_gamepad ? "D-pad →" : "Shift")}] Pull over: {m.Candidate.Description} · {m.Candidate.Plate}";
            else if (m.Active)
                line = $"{m.Status}   <color=#9aa3ad>{m.TargetLabel}</color>" + (m.Current == TrafficStopManager.State.Signalling ? $"   [{(_gamepad ? "D-pad →" : "Shift")}] cancel" : "");
            else if (!string.IsNullOrEmpty(m.Status))
                line = m.Status;

            if (line != null)
            {
                var r = new Rect(w * 0.5f - 380f, h - 190f, 760f, 44f);
                GUI.DrawTexture(r, _panel);
                GUI.Label(r, line, _text);
            }
            GUI.matrix = old;
        }

        private void DrawBrackets(Rect r)
        {
            const float t = 4f, l = 18f;
            GUI.DrawTexture(new Rect(r.xMin, r.yMin, l, t), _bracket); GUI.DrawTexture(new Rect(r.xMin, r.yMin, t, l), _bracket);
            GUI.DrawTexture(new Rect(r.xMax - l, r.yMin, l, t), _bracket); GUI.DrawTexture(new Rect(r.xMax - t, r.yMin, t, l), _bracket);
            GUI.DrawTexture(new Rect(r.xMin, r.yMax - t, l, t), _bracket); GUI.DrawTexture(new Rect(r.xMin, r.yMax - l, t, l), _bracket);
            GUI.DrawTexture(new Rect(r.xMax - l, r.yMax - t, l, t), _bracket); GUI.DrawTexture(new Rect(r.xMax - t, r.yMax - l, t, l), _bracket);
        }

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c); t.Apply();
            return t;
        }
    }
}

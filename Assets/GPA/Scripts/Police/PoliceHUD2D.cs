using GPA.Core;
using UnityEngine;

namespace GPA.Police
{
    /// <summary>
    /// Presentation for verbal commands (speech bubbles over the officer and the person addressed)
    /// and the custody pipeline (escort status, booking prompt at the precinct).
    /// </summary>
    public sealed class PoliceHUD2D : MonoBehaviour
    {
        private GUIStyle _bubbleOfficer, _bubbleCivilian;   // cached: no per-frame allocation

        private Texture2D _panel, _bubble, _officerBubble;
        private GUIStyle _text, _bubbleText;

        private void Awake()
        {
            _panel = Solid(new Color(0.05f, 0.06f, 0.08f, 0.85f));
            _bubble = Solid(new Color(0.96f, 0.96f, 0.94f, 0.95f));
            _officerBubble = Solid(new Color(0.16f, 0.24f, 0.45f, 0.95f));
        }

        private void OnGUI()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            if (_text == null)
            {
                _text = new GUIStyle(GUI.skin.label) { fontSize = 19, richText = true, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
                _bubbleText = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            }
            Matrix4x4 old = UiScale.Begin(out Vector2 canvas);
            float w = canvas.x, h = canvas.y;

            var commands = OfficerCommands2D.Instance;
            if (commands != null)
                foreach (var line in commands.Lines)
                {
                    Vector3 s = cam.WorldToScreenPoint(line.Who.position + Vector3.up * 1.2f);
                    if (s.z < 0f) continue;
                    if (_bubbleOfficer == null) { _bubbleOfficer = new GUIStyle(_bubbleText) { normal = { textColor = Color.white } }; _bubbleCivilian = new GUIStyle(_bubbleText) { normal = { textColor = new Color(0.1f, 0.1f, 0.12f) } }; }
                    var style = line.Officer ? _bubbleOfficer : _bubbleCivilian;
                    Vector2 size = style.CalcSize(new GUIContent(line.Text)) + new Vector2(20f, 10f);
                    Vector2 sc = UiScale.ScreenToCanvas(s);
                    var r = new Rect(sc.x - size.x * 0.5f, sc.y - size.y, size.x, size.y);
                    GUI.DrawTexture(r, line.Officer ? _officerBubble : _bubble);
                    GUI.Label(r, line.Text, style);
                }

            var pipeline = ArrestPipeline2D.Instance;
            string status = null;
            if (pipeline != null && pipeline.BookingPrompt != null) status = $"[E / A] {pipeline.BookingPrompt}";
            else if (pipeline != null && pipeline.Escorting != null) status = "Escorting suspect — walk to your car's rear passenger door.   [Backspace / B] let go";
            if (status != null)
            {
                var r = new Rect(w * 0.5f - 400f, h - 300f, 800f, 40f);
                GUI.DrawTexture(r, _panel);
                GUI.Label(r, status, _text);
            }
            GUI.matrix = old;
        }

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c); t.Apply();
            return t;
        }
    }
}

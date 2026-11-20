using System.Collections.Generic;
using GPA.Core;
using GPA.TopDown2D;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GPA.Callouts
{
    /// <summary>
    /// Patrol HUD (immediate-mode for now; a styled uGUI/UI Toolkit HUD replaces it with the MDT):
    /// duty and shift stats, incoming-call card, current objective with distance, a waypoint arrow
    /// around the officer, the dispatch radio feed, interaction prompt, speed, and the result banner.
    /// Button hints follow the last-used device.
    /// </summary>
    public sealed class CalloutHUD : MonoBehaviour
    {
        private GUIStyle _wrapCached;   // cached: no per-frame allocation

        [SerializeField] private float _referenceHeight = 1080f;

        private Texture2D _panel, _arrow, _accent;
        private GUIStyle _title, _body, _small, _banner;
        private bool _gamepad;

        private void Awake()
        {
            _panel = Solid(new Color(0.06f, 0.07f, 0.09f, 0.82f));
            _accent = Solid(new Color(0.16f, 0.36f, 0.85f, 1f));
            _arrow = Triangle(64, new Color(1f, 0.85f, 0.25f, 0.95f));
        }

        private void Update()
        {
            if (Gamepad.current != null && Gamepad.current.wasUpdatedThisFrame) _gamepad = true;
            if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) _gamepad = false;
        }

        private void OnGUI()
        {
            CalloutManager m = CalloutManager.Instance;
            if (m == null) return;
            EnsureStyles();

            Matrix4x4 old = UiScale.Begin(out Vector2 canvas);
            float w = canvas.x, h = canvas.y;

            Services.TryGet(out IPlayerAvatar player);

            // Duty / shift stats (top-left).
            bool rep = Services.TryGet(out IReputationService career);
            Box(new Rect(20, 20, 330, rep ? 100 : 74));
            GUI.Label(new Rect(34, 26, 310, 30), m.OnDuty ? "<color=#5fd16b>ON DUTY</color>  Unit 4127" : "<color=#d16b5f>OFF DUTY</color>  Unit 4127", _title);
            GUI.Label(new Rect(34, 58, 310, 26), $"Calls {m.Completed}   Failed {m.Failed}   Arrests {m.Arrests}   [{Key("O", "—")}] duty", _small);
            if (rep)
                GUI.Label(new Rect(34, 82, 310, 22), $"<color=#ffd75a>{career.Rank}</color> · {career.Points}" + (career.NextLevelAt > 0 ? $"/{career.NextLevelAt}" : "") + " rep", _small);

            // Incoming call card (top-centre).
            if (m.Phase == CalloutPhase.Offered && m.CurrentDefinition != null)
            {
                CalloutDefinition d = m.CurrentDefinition;
                Rect r = new Rect(w * 0.5f - 300, 20, 600, 150);
                Box(r);
                GUI.DrawTexture(new Rect(r.x, r.y, 6, r.height), _accent);
                GUI.Label(new Rect(r.x + 20, r.y + 10, 560, 30), $"INCOMING CALL   <color=#ffcc44>{d.code}</color>   {d.ResponseCode}", _small);
                GUI.Label(new Rect(r.x + 20, r.y + 34, 560, 36), d.title, _title);
                GUI.Label(new Rect(r.x + 20, r.y + 70, 560, 30), $"{m.CurrentSector} — {d.description}", _body);
                GUI.Label(new Rect(r.x + 20, r.y + 108, 560, 30),
                    $"[{Key("Y", "D-pad ↑")}] Accept    [{Key("N", "ignore")}] Decline    {Mathf.CeilToInt(m.OfferTimeLeft)}s", _body);
            }

            // Active objective (top-centre) + arrow.
            if ((m.Phase == CalloutPhase.EnRoute || m.Phase == CalloutPhase.OnScene) && m.Current != null && player != null)
            {
                Vector2 target = m.Current.Location;
                float dist = Vector2.Distance(player.Transform.position, target);
                Rect r = new Rect(w * 0.5f - 300, 20, 600, 92);
                Box(r);
                GUI.DrawTexture(new Rect(r.x, r.y, 6, r.height), _accent);
                GUI.Label(new Rect(r.x + 20, r.y + 8, 560, 28), $"{m.CurrentDefinition.title}  ·  {m.CurrentSector}  ·  {dist:0} m", _small);
                GUI.Label(new Rect(r.x + 20, r.y + 34, 560, 30), m.Current.Objective, _title);
                GUI.Label(new Rect(r.x + 20, r.y + 64, 560, 24), $"[{Key("End", "View")}] clear call", _small);

                if (dist > 12f) DrawArrow(player.Transform.position, target, w, h);
            }

            // Result banner (centre).
            if (m.ResultTimeLeft > 0f && !string.IsNullOrEmpty(m.LastResult))
            {
                Rect r = new Rect(w * 0.5f - 320, h * 0.30f, 640, 84);
                Box(r);
                GUI.Label(new Rect(r.x, r.y + 8, r.width, 36), m.LastResultSucceeded ? "<color=#5fd16b>CODE 4</color>" : "<color=#e0a040>CALL CLEARED</color>", _banner);
                GUI.Label(new Rect(r.x, r.y + 46, r.width, 30), m.LastResult, Centered(_body));
            }

            // Radio chatter: a speech bubble from the officer's radio while on foot, showing the line
            // being transmitted, held briefly after it ends. In a car there is no bubble (audio only).
            DispatchScanner s = m.Scanner;
            if (s != null)
            {
                if (s.Transmitting && !string.IsNullOrEmpty(s.CurrentLine)) { _radioLine = s.CurrentLine; _radioUntil = Time.unscaledTime + 2.5f; }
                if (_radioLine != null && Time.unscaledTime < _radioUntil && player != null && player.CurrentVehicle == null) DrawRadioBubble(player, _radioLine, s.Transmitting && s.CurrentLine == _radioLine, w, h);
            }

            // Interaction prompt (bottom-centre) and speed (bottom-right).
            if (player is Officer2D officer && !string.IsNullOrEmpty(officer.InteractPrompt))
            {
                // Phones: just under the officer, clear of the radio log in the bottom-left.
                // The box grows to fit long prompts ("Enter 1st Precinct — Harbor Point (380 Exchange Pl)").
                string prompt = $"[{Key("E", "A")}] {officer.InteractPrompt}";
                GUIStyle style = Centered(_title);
                var content = new GUIContent(prompt);
                float bw = UiScale.Handheld ? Mathf.Min(900, w - 40) : Mathf.Clamp(style.CalcSize(content).x + 48f, 400f, w - 40f);
                float bh = Mathf.Max(46f, style.CalcHeight(content, bw - 24f) + 12f);
                Rect r = UiScale.Handheld ? new Rect(w * 0.5f - bw * 0.5f, h * 0.5f + 50, bw, bh) : new Rect(w * 0.5f - bw * 0.5f, h - 74f - bh, bw, bh);
                Box(r);
                GUI.Label(new Rect(r.x + 12f, r.y, r.width - 24f, r.height), prompt, style);
            }
            if (player?.CurrentVehicle is Car2D car)
            {
                Rect r = new Rect(w - 220, h - 96, 200, 76);
                Box(r);
                GUI.Label(new Rect(r.x, r.y + 4, r.width, 44), $"{car.SpeedKmh:0}", _banner);
                GUI.Label(new Rect(r.x, r.y + 46, r.width, 24), $"km/h   [{Key("F", "Y")}] exit", Centered(_small));
            }

            GUI.matrix = old;
        }

        private string _radioLine;
        private float _radioUntil;
        private GUIStyle _radioText, _radioTag;

        private void DrawRadioBubble(IPlayerAvatar player, string line, bool live, float w, float h)
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            bool driving = player.CurrentVehicle != null;
            Transform anchor = driving ? player.CurrentVehicle.Transform : player.Transform;
            Vector3 screen = cam.WorldToScreenPoint(anchor.position + Vector3.up * (driving ? 3.2f : 1.6f));
            if (screen.z < 0f) return;
            if (_radioText == null)
            {
                _radioText = new GUIStyle(_small) { wordWrap = true, fontSize = 18, normal = { textColor = Color.white } };
                _radioTag = new GUIStyle(_small) { fontSize = 14, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.55f, 0.75f, 1f) } };
            }
            const float maxW = 520f, pad = 12f;
            var content = new GUIContent(line);
            float textW = Mathf.Min(maxW - pad * 2, _radioText.CalcSize(content).x + 4f);
            float textH = _radioText.CalcHeight(content, textW);
            float bw = textW + pad * 2, bh = textH + 30f;
            Vector2 tip = UiScale.ScreenToCanvas(screen);
            var r = new Rect(Mathf.Clamp(tip.x - bw * 0.5f, 10f, w - bw - 10f), Mathf.Clamp(tip.y - bh - 14f, 10f, h - bh - 10f), bw, bh);

            Color old = GUI.color;
            GUI.color = new Color(0.06f, 0.09f, 0.16f, live ? 0.92f : 0.75f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            // Tail pointing down at the radio.
            float tx = Mathf.Clamp(tip.x, r.x + 14f, r.xMax - 14f);
            for (int i = 0; i < 7; i++) GUI.DrawTexture(new Rect(tx - 7 + i, r.yMax + i, 14 - i * 2, 1.5f), Texture2D.whiteTexture);
            GUI.color = live ? new Color(1f, 0.32f, 0.32f) : new Color(0.5f, 0.55f, 0.62f);
            GUI.DrawTexture(new Rect(r.x, r.y, 4f, r.height), Texture2D.whiteTexture);
            GUI.color = old;
            GUI.Label(new Rect(r.x + pad, r.y + 4f, textW, 20f), live ? "● RADIO" : "RADIO", _radioTag);
            GUI.Label(new Rect(r.x + pad, r.y + 24f, textW, textH), line, _radioText);
        }

        private void DrawArrow(Vector2 from, Vector2 to, float w, float h)
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            Vector2 dir = (to - from).normalized;
            Vector3 screen = cam.WorldToScreenPoint(from);
            Vector2 centre = UiScale.ScreenToCanvas(screen) + new Vector2(dir.x, -dir.y) * 70f;
            float angle = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg; // 0 = up the screen
            Matrix4x4 m = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, UiScale.CanvasToGui(centre));
            GUI.DrawTexture(new Rect(centre.x - 18, centre.y - 18, 36, 36), _arrow);
            GUI.matrix = m;
        }

        private string Key(string keyboard, string gamepad) => _gamepad ? gamepad : keyboard;

        private void Box(Rect r) => GUI.DrawTexture(r, _panel);

        private void EnsureStyles()
        {
            if (_title != null) return;
            _title = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, richText = true, normal = { textColor = Color.white } };
            _body = new GUIStyle(GUI.skin.label) { fontSize = 20, richText = true, wordWrap = false, normal = { textColor = new Color(0.88f, 0.9f, 0.93f) } };
            _small = new GUIStyle(GUI.skin.label) { fontSize = 17, richText = true, normal = { textColor = new Color(0.75f, 0.79f, 0.84f) } };
            _banner = new GUIStyle(_title) { fontSize = 32, alignment = TextAnchor.MiddleCenter };
        }

        private static GUIStyle Centered(GUIStyle s) => GuiStyleCache.With(s, TextAnchor.MiddleCenter);

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        private static Texture2D Triangle(int size, Color c)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            var clear = new Color(0, 0, 0, 0);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // Point at the top (y = size-1 in texture space is the top of the drawn rect).
                float v = y / (float)(size - 1);
                float half = (1f - v) * 0.5f;
                float u = x / (float)(size - 1) - 0.5f;
                t.SetPixel(x, y, Mathf.Abs(u) <= half ? c : clear);
            }
            t.Apply();
            return t;
        }
    }
}

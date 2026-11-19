using GPA.Core;
using System.Collections.Generic;
using UnityEngine;

namespace GPA.TopDown2D
{
    /// <summary>
    /// Equipment HUD (presentation only): what's in hand with its ammo, reload bar, short messages,
    /// and the radial equipment wheel while it's held open. Reads <see cref="OfficerEquipment2D"/>.
    /// </summary>
    public sealed class EquipmentHUD2D : MonoBehaviour
    {
        private GUIStyle _messageStyle;   // cached: no per-frame allocation

        private Texture2D _panel, _accent, _slot, _slotHot;
        private GUIStyle _big, _small, _wheelLabel;

        private void Awake()
        {
            _panel = Solid(new Color(0.05f, 0.06f, 0.08f, 0.85f));
            _accent = Solid(new Color(1f, 0.82f, 0.25f, 1f));
            _slot = Solid(new Color(0.08f, 0.09f, 0.12f, 0.88f));
            _slotHot = Solid(new Color(0.22f, 0.3f, 0.5f, 0.95f));
        }

        private void OnGUI()
        {
            var eq = OfficerEquipment2D.Instance;
            if (eq == null || !eq.isActiveAndEnabled) return;
            var officer = eq.GetComponent<Officer2D>();
            if (officer != null && officer.CurrentCar != null) return;
            if (_big == null)
            {
                _big = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
                _small = new GUIStyle(GUI.skin.label) { fontSize = 16, normal = { textColor = new Color(0.8f, 0.83f, 0.88f) } };
                _wheelLabel = new GUIStyle(GUI.skin.label) { fontSize = 17, alignment = TextAnchor.MiddleCenter, wordWrap = true, normal = { textColor = Color.white } };
            }

            Matrix4x4 old = UiScale.Begin(out Vector2 canvas);
            float w = canvas.x, h = canvas.y;

            // Current item, bottom-right above the speedo area.
            var box = new Rect(w - 330f, h - 250f, 300f, 74f);
            GUI.DrawTexture(box, _panel);
            GUI.DrawTexture(new Rect(box.x, box.y, 4f, box.height), _accent);
            GUI.Label(new Rect(box.x + 16f, box.y + 8f, 280f, 30f), GearInfo.Name(eq.Equipped), _big);
            GUI.Label(new Rect(box.x + 16f, box.y + 40f, 280f, 24f), eq.Reloading ? "Reloading…" : eq.AmmoText(), _small);
            if (eq.Reloading) GUI.DrawTexture(new Rect(box.x + 4f, box.yMax - 4f, (box.width - 4f) * eq.ReloadProgress, 4f), _accent);

            // Stamina: thin bar above the item panel; shown while it's below full.
            if (officer != null && officer.Stamina < 0.999f)
            {
                var bar = new Rect(box.x, box.y - 16f, box.width, 9f);
                GUI.DrawTexture(bar, _panel);
                Color fill = officer.Exhausted ? new Color(0.9f, 0.3f, 0.25f) : new Color(0.35f, 0.85f, 0.45f);
                var old2 = GUI.color; GUI.color = fill;
                GUI.DrawTexture(new Rect(bar.x + 1f, bar.y + 1f, (bar.width - 2f) * officer.Stamina, bar.height - 2f), Texture2D.whiteTexture);
                GUI.color = old2;
            }

            if (!string.IsNullOrEmpty(eq.Message))
            {
                var m = new Rect(w * 0.5f - 300f, h - 245f, 600f, 36f);
                GUI.DrawTexture(m, _panel);
                if (_messageStyle == null) _messageStyle = new GUIStyle(_small) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
                GUI.Label(m, eq.Message, _messageStyle);
            }

            if (eq.SlowMoAmount > 0.01f) DrawSlowMoTint(eq.SlowMoAmount, w, h);
            if (eq.WheelOpen) DrawWheel(eq, new Vector2(w * 0.5f, h * 0.5f));
            GUI.matrix = old;
        }

        private Texture2D _vignette;

        /// <summary>Slow-motion look: the world dims to a cool grey with dark edges while the wheel is up.</summary>
        private void DrawSlowMoTint(float k, float w, float h)
        {
            if (_vignette == null)
            {
                const int n = 64;
                _vignette = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = new Vector2(x / (n - 1f) - 0.5f, y / (n - 1f) - 0.5f).magnitude * 2f;
                    _vignette.SetPixel(x, y, new Color(0f, 0f, 0f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 1.15f, d))));
                }
                _vignette.Apply();
            }
            Color old = GUI.color;
            GUI.color = new Color(0.12f, 0.15f, 0.2f, 0.38f * k);
            GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, 0.85f * k);
            GUI.DrawTexture(new Rect(0, 0, w, h), _vignette);
            GUI.color = old;
        }

        private void DrawWheel(OfficerEquipment2D eq, Vector2 centre)
        {
            var items = new List<Gear>(eq.Available());
            const float radius = 190f, size = 128f;
            for (int i = 0; i < items.Count; i++)
            {
                float a = (i / (float)items.Count) * 360f * Mathf.Deg2Rad;   // 0 = top, clockwise
                Vector2 p = centre + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * radius;
                var r = new Rect(p.x - size * 0.5f, p.y - size * 0.32f, size, size * 0.64f);
                GUI.DrawTexture(r, items[i] == eq.WheelHighlight ? _slotHot : _slot);
                if (items[i] == eq.Equipped) GUI.DrawTexture(new Rect(r.x, r.yMax - 4f, r.width, 4f), _accent);
                GUI.Label(r, GearInfo.Name(items[i]), _wheelLabel);
            }
            var hint = new Rect(centre.x - 160f, centre.y - 18f, 320f, 36f);
            GUI.Label(hint, "Release to equip", GuiStyleCache.With(_small, TextAnchor.MiddleCenter));
        }

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c); t.Apply();
            return t;
        }
    }
}

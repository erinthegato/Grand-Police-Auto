using UnityEngine;

namespace GPA.Mdt
{
    /// <summary>Textures and GUI styles shared by the MDT screen and its on-screen keyboard.</summary>
    internal sealed class MdtStyles
    {
        public readonly Texture2D Green, Grey, Dim, Panel, HeaderBg, TabOn, TabOff, RowSel, Divider, Red, Amber, FieldBg, FieldActive, Button, ButtonHover, Accent, Outline, KeyBg, Chip;
        public readonly GUIStyle H1, H2, Body, Wrapped, Small, Label, Row, Tab, FieldText, ButtonStyle, Primary, BannerText, Key, ChipStyle;

        public MdtStyles()
        {
            Green = Solid(new Color(0.16f, 0.48f, 0.24f, 1f));
            Grey = Solid(new Color(0.30f, 0.33f, 0.38f, 1f));
            Dim = Solid(new Color(0f, 0f, 0f, 0.6f));
            Panel = Solid(new Color(0.07f, 0.08f, 0.10f, 0.98f));
            HeaderBg = Solid(new Color(0.09f, 0.14f, 0.26f, 1f));
            Accent = Solid(new Color(0.16f, 0.36f, 0.85f, 1f));
            TabOn = Accent;
            TabOff = Solid(new Color(0.12f, 0.14f, 0.18f, 1f));
            RowSel = Solid(new Color(0.16f, 0.36f, 0.85f, 0.85f));
            Divider = Solid(new Color(0.24f, 0.27f, 0.32f, 1f));
            Red = Solid(new Color(0.62f, 0.12f, 0.12f, 1f));
            Amber = Solid(new Color(0.62f, 0.38f, 0.04f, 1f));
            FieldBg = Solid(new Color(0.04f, 0.05f, 0.06f, 1f));
            FieldActive = Solid(new Color(0.07f, 0.11f, 0.19f, 1f));
            Button = Solid(new Color(0.18f, 0.21f, 0.26f, 1f));
            ButtonHover = Solid(new Color(0.24f, 0.28f, 0.35f, 1f));
            Outline = Solid(new Color(1f, 0.82f, 0.25f, 1f));
            KeyBg = Solid(new Color(0.10f, 0.12f, 0.15f, 0.99f));
            Chip = Solid(new Color(0.13f, 0.19f, 0.30f, 1f));
            var text = new Color(0.88f, 0.9f, 0.93f);

            H1 = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, richText = true, wordWrap = false, clipping = TextClipping.Clip, normal = { textColor = Color.white } };
            H2 = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, richText = true, wordWrap = false, normal = { textColor = new Color(0.5f, 0.7f, 1f) } };
            Body = new GUIStyle(GUI.skin.label) { fontSize = 19, richText = true, wordWrap = false, clipping = TextClipping.Clip, normal = { textColor = text } };
            Wrapped = new GUIStyle(Body) { wordWrap = true };
            Small = new GUIStyle(GUI.skin.label) { fontSize = 16, richText = true, wordWrap = false, clipping = TextClipping.Clip, normal = { textColor = new Color(0.68f, 0.72f, 0.78f) } };
            Label = new GUIStyle(Small) { fontSize = 17 };
            BannerText = new GUIStyle(Body) { fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            Row = new GUIStyle(GUI.skin.label) { fontSize = 18, richText = true, wordWrap = false, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(14, 10, 2, 2), clipping = TextClipping.Clip, normal = { textColor = text } };
            Tab = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            FieldText = new GUIStyle(Body) { padding = new RectOffset(12, 12, 9, 9), alignment = TextAnchor.MiddleLeft };

            ButtonStyle = new GUIStyle(GUI.skin.button) { fontSize = 17, richText = true, wordWrap = false, clipping = TextClipping.Clip, border = new RectOffset(0, 0, 0, 0) };
            ButtonStyle.normal.background = Button; ButtonStyle.hover.background = ButtonHover; ButtonStyle.active.background = Accent;
            ButtonStyle.normal.textColor = ButtonStyle.hover.textColor = ButtonStyle.active.textColor = Color.white;
            Primary = new GUIStyle(ButtonStyle) { fontStyle = FontStyle.Bold };
            Primary.normal.background = Accent; Primary.hover.background = Solid(new Color(0.24f, 0.45f, 0.95f, 1f));
            Key = new GUIStyle(ButtonStyle) { fontSize = 22, fontStyle = FontStyle.Bold };
            ChipStyle = new GUIStyle(ButtonStyle) { fontSize = 16, wordWrap = true, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(12, 12, 4, 4) };
            ChipStyle.normal.background = Chip;
        }

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }
    }
}

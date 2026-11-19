using UnityEngine;

namespace GPA.TopDown2D
{
    /// <summary>
    /// Brake lights and turn signals for any <see cref="Car2D"/>. Brake lights follow the driver's
    /// brake (and the held stop at lights); indicators come from drivers that implement
    /// <see cref="ITurnSignalSource"/>. Small coloured quads drawn just above the car body.
    /// </summary>
    [RequireComponent(typeof(Car2D))]
    public sealed class CarLights2D : MonoBehaviour
    {
        private static Sprite _dot;
        private static readonly Color BrakeOff = new Color(0.45f, 0.08f, 0.08f, 0.9f);
        private static readonly Color BrakeOn = new Color(1f, 0.15f, 0.12f, 1f);
        private static readonly Color Amber = new Color(1f, 0.68f, 0.12f, 1f);

        private Car2D _car;
        private SpriteRenderer _brakeL, _brakeR, _sigFL, _sigFR, _sigRL, _sigRR;

        private void Awake()
        {
            _car = GetComponent<Car2D>();
            if (_dot == null)
            {
                var tex = new Texture2D(4, 4) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
                var px = new Color[16];
                for (int i = 0; i < 16; i++) px[i] = Color.white;
                tex.SetPixels(px); tex.Apply();
                _dot = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
                _dot.hideFlags = HideFlags.HideAndDontSave;
            }
            _brakeL = Lamp(new Vector2(-0.72f, -2.2f), new Vector2(0.42f, 0.16f));
            _brakeR = Lamp(new Vector2(0.72f, -2.2f), new Vector2(0.42f, 0.16f));
            _sigRL = Lamp(new Vector2(-1.0f, -2.15f), new Vector2(0.16f, 0.16f));
            _sigRR = Lamp(new Vector2(1.0f, -2.15f), new Vector2(0.16f, 0.16f));
            _sigFL = Lamp(new Vector2(-0.95f, 2.2f), new Vector2(0.18f, 0.16f));
            _sigFR = Lamp(new Vector2(0.95f, 2.2f), new Vector2(0.18f, 0.16f));
        }

        private SpriteRenderer Lamp(Vector2 pos, Vector2 size)
        {
            var go = new GameObject("Lamp");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _dot;
            Sorting2D.Apply(sr, Sorting2D.LayerForLegacyOrder(WorldSprites2D.CarOrder), WorldSprites2D.CarOrder + 1);
            return sr;
        }

        private void LateUpdate()
        {
            var input = _car.Input;
            bool driven = _car.IsPlayerDriven || _car.IsAIDriven;
            bool braking = driven && input != null && (input.Brake > 0.05f && _car.ForwardSpeed > 0.2f
                           || input.Handbrake && Mathf.Abs(_car.ForwardSpeed) < 1f && !_car.IsPlayerDriven);
            _brakeL.color = _brakeR.color = braking ? BrakeOn : BrakeOff;

            int signal = driven && input is ITurnSignalSource s ? s.TurnSignal : 0;
            bool blink = Mathf.Repeat(Time.time, 0.7f) < 0.38f;
            Color off = new Color(0, 0, 0, 0);
            _sigFL.color = _sigRL.color = signal < 0 && blink ? Amber : off;
            _sigFR.color = _sigRR.color = signal > 0 && blink ? Amber : off;
        }
    }
}

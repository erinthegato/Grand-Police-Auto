using UnityEngine;

namespace GPA.TopDown2D
{
    /// <summary>
    /// Makes a Car2D fly: spinning rotor, drawn above roofs, shadow dropped on the ground below,
    /// passes over buildings and traffic, and stays inside the harbour around the island.
    /// </summary>
    [RequireComponent(typeof(Car2D))]
    public sealed class HelicopterFx2D : MonoBehaviour
    {
        public static readonly Rect FlyArea = new Rect(-528f, -240f, 1536f, 1368f);

        private Car2D _car;
        private Transform _rotor;
        private Transform _shadow;
        private float _spin;

        public void Setup(Sprite rotorSprite, Transform shadow)
        {
            _car = GetComponent<Car2D>();
            _shadow = shadow;
            if (rotorSprite != null)
            {
                var go = new GameObject("Rotor");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(0f, 2.95f, 0f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = rotorSprite;
                Sorting2D.Apply(sr, Sorting2D.Effects, 60);
                _rotor = go.transform;
            }
            // Above everything on the ground, including roofs.
            foreach (var sr in GetComponentsInChildren<SpriteRenderer>(true))
                if (sr.transform != _rotor && sr.transform != _shadow && !sr.name.StartsWith("glow")) Sorting2D.Apply(sr, Sorting2D.Effects, 50);
            foreach (var c in GetComponents<Collider2D>()) c.isTrigger = true;
        }

        private void LateUpdate()
        {
            if (_car == null) return;
            bool engine = _car.IsPlayerDriven;
            _spin = Mathf.MoveTowards(_spin, engine ? 1f : 0.15f, Time.deltaTime * 0.6f);
            if (_rotor != null) _rotor.Rotate(0f, 0f, -1600f * _spin * Time.deltaTime, Space.Self);
            // The shadow falls further away the faster the rotor turns (we're higher up).
            if (_shadow != null) _shadow.localPosition = new Vector3(2.5f * _spin, -3.5f * _spin, 0f);

            Vector3 p = transform.position;
            if (!FlyArea.Contains(p))
            {
                p.x = Mathf.Clamp(p.x, FlyArea.xMin, FlyArea.xMax);
                p.y = Mathf.Clamp(p.y, FlyArea.yMin, FlyArea.yMax);
                transform.position = p;
                var rb = GetComponent<Rigidbody2D>();
                if (rb != null) { rb.position = p; rb.linearVelocity *= 0.5f; }
            }
        }
    }
}

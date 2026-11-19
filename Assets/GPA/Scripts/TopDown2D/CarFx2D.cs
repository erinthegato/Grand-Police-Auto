using UnityEngine;

namespace GPA.TopDown2D
{
    /// <summary>
    /// Visual car effects: black skid marks laid on the road by the rear tyres while the car slides
    /// (they fade after a few seconds), and a body jolt on hard impacts. Added automatically by
    /// <see cref="Car2D"/>.
    /// </summary>
    [RequireComponent(typeof(Car2D))]
    public sealed class CarFx2D : MonoBehaviour
    {
        private static Material _material;

        private Car2D _car;
        private Transform _body;
        private Vector3 _bodyBase;
        private TrailRenderer _left, _right;
        private float _shake, _shakeAmount;

        private void Start()
        {
            _car = GetComponent<Car2D>();
            SpriteRenderer body = null;
            foreach (var sr in GetComponentsInChildren<SpriteRenderer>())
                if (sr.name == "Body") { body = sr; break; }
            _body = body != null ? body.transform : null;
            if (_body != null) _bodyBase = _body.localPosition;

            Vector2 size = body != null && body.sprite != null ? (Vector2)body.sprite.bounds.size : new Vector2(2.5f, 4.7f);
            if (_material == null) _material = new Material(Shader.Find("Sprites/Default")) { hideFlags = HideFlags.HideAndDontSave };
            _left = MakeTrail(new Vector2(-size.x * 0.36f, -size.y * 0.32f));
            _right = MakeTrail(new Vector2(size.x * 0.36f, -size.y * 0.32f));
        }

        private TrailRenderer MakeTrail(Vector2 local)
        {
            var go = new GameObject("Skid Mark");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = local;
            var trail = go.AddComponent<TrailRenderer>();
            trail.time = 8f;
            trail.minVertexDistance = 0.15f;
            trail.widthMultiplier = 0.22f;
            trail.material = _material;
            trail.startColor = new Color(0.04f, 0.04f, 0.04f, 0.5f);
            trail.endColor = new Color(0.04f, 0.04f, 0.04f, 0f);
            trail.numCapVertices = 0;
            trail.emitting = false;
            Sorting2D.Apply(trail, Sorting2D.Ground, 450);   // on the asphalt, under the cars
            return trail;
        }

        private void Update()
        {
            bool skidding = _car.RearSlide > 0.35f && Mathf.Abs(_car.ForwardSpeed) > 4f;
            if (_left != null) _left.emitting = skidding;
            if (_right != null) _right.emitting = skidding;

            if (_body == null) return;
            if (_shake > 0f)
            {
                _shake -= Time.deltaTime / 0.3f;
                Vector2 jolt = Random.insideUnitCircle * _shakeAmount * Mathf.Max(0f, _shake);
                _body.localPosition = _bodyBase + (Vector3)jolt;
                _body.localRotation = Quaternion.Euler(0f, 0f, jolt.x * 20f);
            }
            else if (_body.localPosition != _bodyBase)
            {
                _body.localPosition = _bodyBase;
                _body.localRotation = Quaternion.identity;
            }
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            float impact = collision.relativeVelocity.magnitude;
            if (collision.collider.GetComponentInParent<Ped2D>() != null) impact *= 0.3f;
            if (impact < 3f) return;
            _shake = 1f;
            _shakeAmount = Mathf.Clamp(impact * 0.012f, 0.03f, 0.18f);
        }
    }
}

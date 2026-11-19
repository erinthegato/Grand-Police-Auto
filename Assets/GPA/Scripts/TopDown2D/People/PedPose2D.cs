using UnityEngine;

namespace GPA.TopDown2D
{
    /// <summary>
    /// Procedural body poses for a pedestrian, layered on the walk cycle: knocked back and squashed
    /// when struck, hunched and swaying while stunned, slowly bobbing while kneeling, and lying flat on the
    /// ground when killed or tased (twitching while the taser cycles). Drives the body
    /// sprite's local transform only, never the physics body. Added automatically by <see cref="Ped2D"/>.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(Ped2D))]
    public sealed class PedPose2D : MonoBehaviour
    {
        private Ped2D _ped;
        private Transform _body;
        private Vector3 _basePos;
        private Vector2 _flinchDir;
        private float _flinch, _shock, _down, _t, _knocked;
        private SpriteRenderer _sprite;
        private Sprite _lying;
        private float _lyingAngle;
        private bool _lyingFlip;

        private void Awake() => _ped = GetComponent<Ped2D>();

        private void Start()
        {
            var anim = GetComponentInChildren<SpriteAnimator2D>();
            _body = anim != null ? anim.transform : null;
            if (_body != null) { _basePos = _body.localPosition; _sprite = _body.GetComponent<SpriteRenderer>(); }
            _t = Random.value * 10f;
        }

        /// <summary>Struck from <paramref name="worldDirection"/> (the direction of the blow).</summary>
        public void Flinch(Vector2 worldDirection)
        {
            _flinch = 1f;
            _flinchDir = ((Vector2)transform.InverseTransformDirection(worldDirection)).normalized;
        }

        /// <summary>Taser hit: convulse for a few seconds.</summary>
        public void Shock(float seconds) => _shock = Mathf.Max(_shock, seconds);

        /// <summary>Knocked off their feet (hit by a car): lie on the ground for a while.</summary>
        public void KnockDown(float seconds) => _knocked = Mathf.Max(_knocked, seconds);

        private void LateUpdate()
        {
            if (_body == null) return;
            float dt = Time.deltaTime;
            _t += dt;
            _flinch = Mathf.MoveTowards(_flinch, 0f, dt / 0.25f);
            _shock = Mathf.Max(0f, _shock - dt);
            _knocked = Mathf.Max(0f, _knocked - dt);
            bool tased = _ped.Current == Ped2D.Mode.Stunned && _shock > 0f;
            bool flat = _ped.Current == Ped2D.Mode.Downed || tased || _knocked > 0f;   // killed, tased or knocked over: lying on the ground
            _down = Mathf.MoveTowards(_down, flat ? 1f : 0f, dt / (flat ? 0.3f : 0.6f));

            Vector3 pos = _basePos;
            Vector3 scale = Vector3.one;
            float rot = 0f;

            // Struck: knocked along the blow and squashed for a moment.
            float f = _flinch * _flinch;
            pos += (Vector3)(_flinchDir * 0.18f * f);
            scale = new Vector3(1f + 0.12f * f, 1f - 0.12f * f, 1f);

            switch (_ped.Current)
            {
                case Ped2D.Mode.Stunned when _shock > 0f:      // tased: handled below (flat, twitching)
                    break;
                case Ped2D.Mode.Stunned:                        // sprayed / beaten: hunched, swaying
                    rot += Mathf.Sin(_t * 3f) * 6f;
                    scale *= 0.88f;
                    break;
                case Ped2D.Mode.Surrendered:                    // kneeling, hands up: a slow breathing bob
                    scale *= 0.86f + Mathf.Sin(_t * 2.5f) * 0.012f;
                    break;
            }

            // Killed, tased or knocked over: the body is drawn lying splayed out on the ground
            // (its own sprite: arms and legs spread), at a random angle so no two look alike.
            if (flat && _sprite != null && _lying == null && WorldSprites2D.Instance != null)
            {
                _lying = WorldSprites2D.Instance.DownedFor(_sprite.sprite);
                _lyingAngle = Random.Range(-160f, 160f);
                _lyingFlip = Random.value < 0.5f;
            }
            if (!flat && _down <= 0f) _lying = null;
            if (_down > 0f && _lying != null)
            {
                float e = 1f - (1f - _down) * (1f - _down);
                if (_down > 0.35f) { _sprite.sprite = _lying; _sprite.flipX = _lyingFlip; }   // after the animator has set the walk frame
                rot = Mathf.Lerp(rot, _lyingAngle, e);
                scale = Vector3.Lerp(scale, new Vector3(1.08f, 1.08f, 1f), e);
                if (tased)
                {
                    rot += Mathf.Sin(_t * 48f) * 4f;
                    pos += (Vector3)(Random.insideUnitCircle * 0.025f);
                }
            }
            else if (_down > 0f)   // no lying sprite for this look: tip the standing sprite over
            {
                float e = 1f - (1f - _down) * (1f - _down);
                rot = Mathf.Lerp(rot, 90f, e);
                scale = Vector3.Lerp(scale, new Vector3(0.72f, 1.55f, 1f), e);
                pos += new Vector3(0f, -0.3f * e, 0f);
                if (tased)   // convulsing on the ground while the taser cycles
                {
                    rot += Mathf.Sin(_t * 48f) * 4f;
                    pos += (Vector3)(Random.insideUnitCircle * 0.025f);
                }
            }

            _body.localPosition = pos;
            _body.localRotation = Quaternion.Euler(0f, 0f, rot);
            _body.localScale = scale;
        }
    }
}

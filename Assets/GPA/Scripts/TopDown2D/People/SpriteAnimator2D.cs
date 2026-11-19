using UnityEngine;

namespace GPA.TopDown2D
{
    /// <summary>
    /// Walk cycle for top-down characters: idle → step A → idle → step B, played faster the faster
    /// the parent rigidbody moves, idle frame when standing still.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class SpriteAnimator2D : MonoBehaviour
    {
        public Sprite Idle;
        public Sprite StepA;
        public Sprite StepB;
        [Tooltip("Steps per second per m/s of speed.")]
        public float StepsPerMetre = 1.6f;

        private SpriteRenderer _renderer;
        private Rigidbody2D _body;
        private float _phase;
        private bool _frozen;

        /// <summary>Hold the current frame (downed, cuffed).</summary>
        public void SetFrozen(bool frozen) => _frozen = frozen;

        private void Awake()
        {
            _renderer = GetComponent<SpriteRenderer>();
            _body = GetComponentInParent<Rigidbody2D>();
            if (Idle == null) Idle = _renderer.sprite;
        }

        private void LateUpdate()
        {
            if (_frozen) return;
            float speed = _body != null && _body.simulated ? _body.linearVelocity.magnitude : 0f;
            if (speed < 0.2f || StepA == null || StepB == null) { _renderer.sprite = Idle; _phase = 0f; return; }
            _phase = Mathf.Repeat(_phase + Time.deltaTime * speed * StepsPerMetre, 4f);
            int frame = (int)_phase;
            _renderer.sprite = frame == 1 ? StepA : frame == 3 ? StepB : Idle;
        }
    }
}


using GPA.Core;
using UnityEngine;

namespace GPA.TopDown2D
{
    /// <summary>
    /// Pedestrian body and movement. Ambient people follow the sidewalks cell by cell (using the
    /// <see cref="WalkMap2D"/>), sometimes crossing at crosswalks; they can flee from a threat,
    /// panic after being hit by a car, and be detained. Each has a personality profile (spec §5)
    /// that callouts, interactions and traffic stops read.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class Ped2D : MonoBehaviour
    {
        public enum Mode { Idle, Wander, Flee, Detained, Stunned, Downed, Surrendered, Escorted }
        public enum Personality { Cooperative, Nervous, Confused, Uncooperative, Aggressive, Suspicious, Fleeing }

        [SerializeField] private float _walkSpeed = 1.3f;
        [SerializeField] private float _runSpeed = 5.2f;
        [Range(0f, 1f)] [SerializeField] private float _crossChance = 0.3f;

        private Rigidbody2D _rb;
        private Transform _threat;
        private float _panicTimer;
        private Vector2Int _cell, _target, _dir;
        private bool _hasTarget, _crossing;
        private Vector2 _freeDir;
        private float _freeTimer;

        public Mode Current { get; private set; } = Mode.Idle;
        public Personality Profile { get; set; } = Personality.Cooperative;
        /// <summary>Spawned by the ambient population (can be despawned when far away).</summary>
        public bool Ambient { get; set; }
        /// <summary>Persistent citizen record this person is (filled once the citizen database exists).</summary>
        public string CitizenId { get; set; }
        /// <summary>Tased, sprayed or beaten into compliance — can be handcuffed without a fight.</summary>
        public bool Subdued { get; private set; }
        public bool Cuffed { get; private set; }
        public bool Incapacitated => Current == Mode.Stunned || Current == Mode.Downed;

        /// <summary>Obeyed a "stop" or "get down" command (can be cuffed without a struggle).</summary>
        public bool Compliant { get; private set; }
        private Transform _escort;
        private float _stunTimer;
        private int _strikes;
        private System.IDisposable _gunshotSub;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _rb.gravityScale = 0f;
            // Rotation is driven by code (face the walking direction); collisions mustn't spin people.
            _rb.freezeRotation = false;
            _rb.angularDamping = 50f;
            _rb.mass = 75f;
            _rb.linearDamping = 4f;
            _walkSpeed *= Random.Range(0.85f, 1.2f);
            if (GetComponent<PedPose2D>() == null) gameObject.AddComponent<PedPose2D>();
        }

        public void Idle() { Current = Mode.Idle; if (_rb != null) _rb.linearVelocity = Vector2.zero; }
        public void Wander() { Current = Mode.Wander; _hasTarget = false; }
        public void Flee(Transform threat) { _threat = threat; Current = Mode.Flee; }
        public void Detain() { if (Current == Mode.Downed) return; Current = Mode.Detained; _rb.angularVelocity = 0f; _rb.linearVelocity = Vector2.zero; _rb.bodyType = RigidbodyType2D.Kinematic; }

        // ---------------------------------------------------------------- less-lethal / lethal hits

        /// <summary>Taser, pepper spray, enough baton strikes: drops to the ground for a while, then complies.</summary>
        public void Stun(float seconds)
        {
            if (Current == Mode.Downed || Cuffed) return;
            if (Current != Mode.Stunned) GameAudio.PlayAt(GameAudio.Library?.PedPain, transform.position, 0.6f, 0.12f);
            Current = Mode.Stunned;
            _stunTimer = Mathf.Max(_stunTimer, seconds);
            Subdued = true;
            _rb.linearVelocity = Vector2.zero;
        }

        /// <summary>Baton strike from <paramref name="direction"/>. Compliant people give up at once; others take a few.</summary>
        public void Strike(Vector2 direction)
        {
            if (Current == Mode.Downed || Cuffed) return;
            _rb.AddForce(direction.normalized * 260f, ForceMode2D.Impulse);
            _strikes++;
            GetComponent<PedPose2D>()?.Flinch(direction);
            bool tough = Profile == Personality.Aggressive || Profile == Personality.Fleeing || Profile == Personality.Uncooperative;
            Stun(_strikes >= (tough ? 3 : 1) ? 4f : 0.6f);
        }

        /// <summary>Shot: down and out of the scene's action (no ragdoll, no gore).</summary>
        public void Down()
        {
            if (Current == Mode.Downed) return;
            GameAudio.PlayAt(GameAudio.Library?.PedPain, transform.position, 0.7f, 0.12f);
            Current = Mode.Downed;
            Subdued = true;
            _rb.linearVelocity = Vector2.zero;
            _rb.bodyType = RigidbodyType2D.Kinematic;
            // Cars can drive over a body, people step around nothing (no collision).
            foreach (var c in GetComponents<Collider2D>()) c.isTrigger = true;
            foreach (var sr in GetComponentsInChildren<SpriteRenderer>()) sr.color = new Color(0.78f, 0.74f, 0.74f, 1f);
            GetComponentInChildren<SpriteAnimator2D>()?.SetFrozen(true);
        }

        /// <summary>Handcuff attempt. Subdued, detained or compliant people are cuffed; others may resist or run.</summary>
        public bool TryCuff(Transform officer, out string result)
        {
            if (Cuffed) { result = "Already in handcuffs."; return false; }
            if (Current == Mode.Downed) { result = "Requires medical attention, not cuffs."; return false; }
            bool compliant = Subdued || Compliant || Current == Mode.Surrendered || Current == Mode.Detained || Current == Mode.Stunned ||
                             Profile == Personality.Cooperative || Profile == Personality.Nervous || Profile == Personality.Confused ||
                             (Profile == Personality.Suspicious && UnityEngine.Random.value < 0.7f) ||
                             (Profile == Personality.Uncooperative && UnityEngine.Random.value < 0.5f);
            if (!compliant)
            {
                if (Profile == Personality.Aggressive || Profile == Personality.Fleeing) { Flee(officer); _panicTimer = 0f; result = "They pulled away and ran!"; }
                else result = "They're resisting — subdue them first.";
                return false;
            }
            _stunTimer = 0f;
            Cuffed = true;
            Current = Mode.Idle;
            SetKneeling(false);
            Detain();
            result = "Handcuffed.";
            return true;
        }

        // ---------------------------------------------------------------- commands and custody

        /// <summary>"Police, stop!" obeyed: stands still and waits.</summary>
        public void Halt()
        {
            if (Cuffed || Incapacitated) return;
            Compliant = true;
            Current = Mode.Idle;
            _panicTimer = 0f;
            _rb.linearVelocity = Vector2.zero;
        }

        /// <summary>"Get on the ground!" obeyed: kneels with hands visible until cuffed.</summary>
        public void Surrender()
        {
            if (Cuffed || Current == Mode.Downed) return;
            Compliant = true;
            Subdued = true;
            Current = Mode.Surrendered;
            _rb.linearVelocity = Vector2.zero;
            SetKneeling(true);
        }

        /// <summary>Put in cuffs without a check (arrest from a traffic stop or callout).</summary>
        public void ForceCuff()
        {
            if (Cuffed || Current == Mode.Downed) return;
            _stunTimer = 0f;
            Cuffed = true;
            Subdued = true;
            Current = Mode.Idle;
            Detain();
        }

        /// <summary>Cuffed person walks along behind the officer.</summary>
        public void Escort(Transform officer)
        {
            if (!Cuffed) return;
            SetKneeling(false);
            _escort = officer;
            Current = Mode.Escorted;
            _rb.bodyType = RigidbodyType2D.Dynamic;
        }

        public void StopEscort()
        {
            _escort = null;
            if (Cuffed) Detain();
        }

        /// <summary>Uncuffed and let go: back to being an ordinary pedestrian.</summary>
        public void Release()
        {
            Cuffed = false; Subdued = false; Compliant = false;
            _escort = null;
            SetKneeling(false);
            _rb.bodyType = RigidbodyType2D.Dynamic;
            Ambient = true;
            Wander();
        }

        private void SetKneeling(bool kneel)
        {
            var anim = GetComponentInChildren<SpriteAnimator2D>();
            if (anim != null) { anim.transform.localScale = Vector3.one * (kneel ? 0.86f : 1f); anim.SetFrozen(kneel); }
        }

        private void OnEnable() => _gunshotSub = GameEvents.Subscribe<GunshotEvent>(OnGunshot);
        private void OnDisable() => _gunshotSub?.Dispose();

        private void OnGunshot(GunshotEvent e)
        {
            if (this == null || Cuffed || Current == Mode.Detained || Current == Mode.Surrendered || Incapacitated) return;
            if (e.Shooter == gameObject || Vector2.Distance(e.Position, transform.position) > e.AudibleRadius) return;
            if (e.Shooter != null) { Flee(e.Shooter.transform); _panicTimer = Ambient ? 10f : 0f; }
        }

        private void FixedUpdate()
        {
            if (Current == Mode.Detained || Current == Mode.Downed) return;
            if (Current == Mode.Surrendered) { _rb.linearVelocity = Vector2.zero; return; }
            if (Current == Mode.Escorted)
            {
                if (_escort == null) { StopEscort(); return; }
                Vector2 spot = (Vector2)_escort.position - (Vector2)_escort.up * 1.1f;
                Vector2 to = spot - (Vector2)transform.position;
                Vector2 want = to.magnitude < 0.3f ? Vector2.zero : to.normalized * Mathf.Min(6f, to.magnitude * 3f);
                _rb.linearVelocity = Vector2.MoveTowards(_rb.linearVelocity, want, 30f * Time.fixedDeltaTime);
                if (want.sqrMagnitude > 0.25f)
                    _rb.MoveRotation(Mathf.MoveTowardsAngle(_rb.rotation, Mathf.Atan2(want.y, want.x) * Mathf.Rad2Deg - 90f, 480f * Time.fixedDeltaTime));
                _rb.angularVelocity = 0f;
                if (to.magnitude > 15f) transform.position = spot;   // got snagged: catch up
                return;
            }
            if (Current == Mode.Stunned)
            {
                _rb.linearVelocity = Vector2.MoveTowards(_rb.linearVelocity, Vector2.zero, 30f * Time.fixedDeltaTime);
                _stunTimer -= Time.fixedDeltaTime;
                if (_stunTimer <= 0f) Current = Mode.Idle;   // gets up and complies
                return;
            }
            float dt = Time.fixedDeltaTime;
            Vector2 desired = Vector2.zero;

            if (_panicTimer > 0f)
            {
                _panicTimer -= dt;
                if (_panicTimer <= 0f && Current == Mode.Flee && Ambient) Wander();
            }

            switch (Current)
            {
                case Mode.Wander:
                    desired = WalkMap2D.Instance != null ? SidewalkStep() : FreeWander(dt);
                    break;
                case Mode.Flee when _threat != null:
                    Vector2 away = (Vector2)(transform.position - _threat.position);
                    Vector2 weave = new Vector2(-away.y, away.x).normalized * Mathf.Sin(Time.time * 1.7f) * 0.4f;
                    desired = (away.normalized + weave).normalized * _runSpeed;
                    break;
            }

            _rb.linearVelocity = Vector2.MoveTowards(_rb.linearVelocity, desired, 18f * dt);
            // Face the way we're actually moving, and only when properly moving (no twitching in place).
            Vector2 v = _rb.linearVelocity;
            if (desired.sqrMagnitude > 0.25f && v.sqrMagnitude > 0.25f)
            {
                float angle = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg - 90f;
                _rb.MoveRotation(Mathf.MoveTowardsAngle(_rb.rotation, angle, 480f * dt));
            }
            _rb.angularVelocity = 0f;
        }

        /// <summary>Walk to the next cell along the pavement; at each cell centre decide where next.</summary>
        private Vector2 SidewalkStep()
        {
            WalkMap2D map = WalkMap2D.Instance;
            Vector2 pos = transform.position;
            if (!_hasTarget)
            {
                _cell = map.CellOf(pos);
                if (!map.Walkable(_cell)) return FreeWander(Time.fixedDeltaTime);
                _target = _cell;
                _hasTarget = true;
            }

            // Keep to the right-hand side of the pavement, relative to the walking direction (not the
            // body's rotation — that fed back into the target and made people spin on the spot).
            Vector2 side = _dir == Vector2Int.zero ? Vector2.zero : new Vector2(_dir.y, -_dir.x) * 0.6f;
            if (Vector2.Distance(pos, map.Centre(_target) + side) < 0.6f || _target == _cell)
            {
                _cell = _target;
                _target = ChooseNext(map);
                side = _dir == Vector2Int.zero ? Vector2.zero : new Vector2(_dir.y, -_dir.x) * 0.6f;
            }
            Vector2 to = map.Centre(_target) + side - pos;
            return to.sqrMagnitude < 0.04f ? Vector2.zero : to.normalized * _walkSpeed;
        }

        private Vector2Int ChooseNext(WalkMap2D map)
        {
            Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
            bool onCrosswalk = map.At(_cell) == WalkMap2D.Kind.Crosswalk;
            if (onCrosswalk && map.Walkable(_cell + _dir)) return _cell + _dir;   // finish crossing

            float total = 0f;
            var weights = new float[4];
            for (int i = 0; i < 4; i++)
            {
                Vector2Int n = _cell + dirs[i];
                WalkMap2D.Kind k = map.At(n);
                if (k == WalkMap2D.Kind.Blocked) continue;
                if (k == WalkMap2D.Kind.Crosswalk && Random.value > _crossChance) continue;
                weights[i] = dirs[i] == _dir ? 6f : dirs[i] == -_dir ? 0.15f : 1f;
                total += weights[i];
            }
            if (total <= 0f) { _dir = -_dir; return map.Walkable(_cell + _dir) ? _cell + _dir : _cell; }
            float roll = Random.value * total;
            for (int i = 0; i < 4; i++)
            {
                roll -= weights[i];
                if (roll <= 0f && weights[i] > 0f) { _dir = dirs[i]; return _cell + dirs[i]; }
            }
            return _cell;
        }

        private Vector2 FreeWander(float dt)
        {
            _freeTimer -= dt;
            if (_freeTimer <= 0f) { _freeDir = Random.insideUnitCircle.normalized; _freeTimer = Random.Range(2f, 5f); }
            return _freeDir * _walkSpeed;
        }

        /// <summary>Impact speed (m/s) at which a car knocks someone down (~25 km/h); above <see cref="FatalSpeed"/> it's fatal.</summary>
        public const float RunOverSpeed = 7f, FatalSpeed = 12.5f;

        /// <summary>Hit by a car: thrown along the car's path and left lying in the road.</summary>
        private void RunOver(Car2D car, float impact)
        {
            bool fatal = impact >= FatalSpeed;
            Vector2 throwDir = car.Velocity.sqrMagnitude > 0.1f ? (Vector2)car.Velocity.normalized : (Vector2)(transform.position - car.transform.position).normalized;
            var pose = GetComponent<PedPose2D>();
            GameAudio.PlayAt(GameAudio.Library?.PedPain, transform.position, 0.9f, 0.1f);
            GameAudio.PlayAt(GameAudio.Library?.Crash, transform.position, 0.35f, 0.15f);
            if (fatal) Down();
            else
            {
                Current = Mode.Stunned;
                _rb.linearVelocity = Vector2.zero;
                pose?.KnockDown(8f);
                Stun(8f);
            }
            pose?.Flinch(throwDir);
            StartCoroutine(Thrown(throwDir * Mathf.Min(impact * 0.35f, 7f)));
            if (car.IsPlayerDriven) GameEvents.Publish(new PedestrianStruckEvent(transform.position, fatal));
        }

        private System.Collections.IEnumerator Thrown(Vector2 offset)
        {
            Vector2 start = transform.position, end = start + offset;
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.35f)
            {
                float e = 1f - (1f - t) * (1f - t);
                Vector2 p = Vector2.Lerp(start, end, e);
                if (_rb.bodyType == RigidbodyType2D.Kinematic) _rb.position = p; else transform.position = p;
                yield return null;
            }
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (Current == Mode.Detained || collision.rigidbody == null) return;
            var car = collision.rigidbody.GetComponent<Car2D>();
            float impact = collision.relativeVelocity.magnitude;
            if (car != null && !car.Hover && Current != Mode.Downed && impact > RunOverSpeed)
            {
                RunOver(car, impact);
                return;
            }
            if (car != null && impact > 3f)
            {
                // Hit by a car: run from it for a few seconds.
                _threat = collision.transform;
                Current = Mode.Flee;
                _panicTimer = 6f;
            }
        }
    }
}

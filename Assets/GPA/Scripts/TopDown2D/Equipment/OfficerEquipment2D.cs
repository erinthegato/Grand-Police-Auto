using System.Collections.Generic;
using GPA.Core;
using GPA.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GPA.TopDown2D
{
    /// <summary>
    /// The officer's duty belt and whatever's in hand. Starts unarmed. Belt: taser, baton, pepper spray,
    /// handcuffs and a holstered pistol; a patrol rifle or shotgun can be taken from the cruiser trunk.
    ///
    /// Controls (on foot): hold Q / LB for the equipment wheel (aim with mouse / right stick, release to
    /// pick); 1 / Y draws the last item or holsters; quick keys 2–7; LMB / RT uses the item; hold RMB / LT
    /// to show the aim line; R / X reloads.
    /// </summary>
    [RequireComponent(typeof(Officer2D))]
    public sealed class OfficerEquipment2D : MonoBehaviour
    {
        [Header("Officer sprites")]
        public SpriteAnimator2D Animator;
        public Sprite UnarmedIdle, UnarmedStepA, UnarmedStepB;
        public Sprite ArmedIdle, ArmedStepA, ArmedStepB;
        [Tooltip("Rifle / shotgun stance: braced against the right shoulder, support hand on the fore-end.")]
        public Sprite LongGunIdle, LongGunStepA, LongGunStepB;

        public static OfficerEquipment2D Instance { get; private set; }

        public Gear Equipped { get; private set; } = Gear.Unarmed;
        /// <summary>Rifle or shotgun taken from the trunk; Unarmed when none.</summary>
        public Gear LongGun { get; private set; } = Gear.Unarmed;
        public bool WheelOpen { get; private set; }
        public Gear WheelHighlight { get; private set; }
        public string Message { get; private set; }
        public float ReloadProgress => _reloadTotal > 0f ? 1f - _reloadTimer / _reloadTotal : 1f;
        public bool Reloading => _reloadTimer > 0f;
        public bool Aiming { get; private set; }

        // Ammunition and consumables.
        public int TaserShots = 2, TaserCartridges = 3;
        public float SprayLeft = 1f;
        public int PistolMag = 15, PistolReserve = 45;
        public int RifleMag = 30, RifleReserve = 90;
        public int ShotgunMag = 6, ShotgunReserve = 24;

        private const int PistolCap = 15, RifleCap = 30, ShotgunCap = 6, TaserCap = 2;

        private Officer2D _officer;
        private IInputService _input;
        private InputAction _fire, _aim, _reload, _draw, _wheel;
        private Gear _lastDrawn = Gear.Taser;
        private float _cooldown, _reloadTimer, _reloadTotal, _messageTimer, _sprayTick;
        private readonly Dictionary<Ped2D, float> _sprayExposure = new Dictionary<Ped2D, float>();
        private SpriteRenderer _held;
        private float _swingTime = -1f;   // seconds into the current baton swing, -1 when idle
        private float _kick, _kickSide = 1f, _drawT = 1f, _lunge, _flashTime = -1f;   // recoil, draw, cuff lunge, muzzle flash
        private Color _flashColor;
        private SpriteRenderer _flashA, _flashB;
        private Vector3 _bodyBase;
        private LineRenderer _aimLine, _sprayCloud;
        private readonly List<(LineRenderer line, float life)> _tracers = new List<(LineRenderer, float)>();
        private static Sprite _pixel;
        private static Material _lineMaterial;

        public IEnumerable<Gear> Available()
        {
            yield return Gear.Unarmed;
            yield return Gear.Taser;
            yield return Gear.Baton;
            yield return Gear.PepperSpray;
            yield return Gear.Handcuffs;
            yield return Gear.Pistol;
            if (LongGun != Gear.Unarmed) yield return LongGun;
        }

        // ------------------------------------------------------------------ setup

        private void Awake()
        {
            Instance = this;
            _officer = GetComponent<Officer2D>();
            if (_pixel == null)
            {
                var tex = new Texture2D(2, 2) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
                tex.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white }); tex.Apply();
                _pixel = Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), 2f);
                _pixel.hideFlags = HideFlags.HideAndDontSave;
            }
            if (_lineMaterial == null) _lineMaterial = new Material(Shader.Find("Sprites/Default")) { hideFlags = HideFlags.HideAndDontSave };

            Transform body = Animator != null ? Animator.transform : transform;
            var heldGo = new GameObject("Held Item");
            heldGo.transform.SetParent(body, false);
            _held = heldGo.AddComponent<SpriteRenderer>();
            _held.sprite = _pixel;
            Sorting2D.Apply(_held, Sorting2D.People, 31);

            _aimLine = NewLine("Aim Line", 0.04f, new Color(1f, 0.25f, 0.2f, 0.55f));
            _sprayCloud = NewLine("Spray", 0.1f, new Color(1f, 0.55f, 0.15f, 0.35f));
            _sprayCloud.widthCurve = new AnimationCurve(new Keyframe(0f, 0.1f), new Keyframe(1f, 1.6f));
            _flashA = NewFlash("Muzzle Flash A");
            _flashB = NewFlash("Muzzle Flash B");
            if (Animator != null) _bodyBase = Animator.transform.localPosition;
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Start()
        {
            if (!Services.TryGet(out _input)) { enabled = false; return; }
            _fire = _input.Get(ActionMaps.Firearms, "Fire");
            _aim = _input.Get(ActionMaps.Firearms, "Aim");
            _reload = _input.Get(ActionMaps.Firearms, "Reload");
            _draw = _input.Get(ActionMaps.Firearms, "DrawHolster");
            _wheel = _input.Get(ActionMaps.WeaponWheel, "OpenWheel");
            Equip(Gear.Unarmed, quiet: true);
        }

        private LineRenderer NewLine(string name, float width, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.widthMultiplier = width;
            lr.material = _lineMaterial;
            lr.startColor = lr.endColor = color;
            lr.numCapVertices = 2;
            Sorting2D.Apply(lr, Sorting2D.Effects, 5);
            lr.enabled = false;
            return lr;
        }

        // ------------------------------------------------------------------ public API (trunk, HUD)

        public void Equip(Gear gear, bool quiet = false)
        {
            if (GearInfo.IsLongGun(gear) && gear != LongGun) return;
            Equipped = gear;
            if (gear != Gear.Unarmed) _lastDrawn = gear;
            _reloadTimer = 0f;
            bool twoHanded = GearInfo.TwoHanded(gear);
            if (Animator != null)
            {
                bool shouldered = GearInfo.IsLongGun(gear) && LongGunIdle != null;
                Animator.Idle = shouldered ? LongGunIdle : twoHanded ? ArmedIdle : UnarmedIdle;
                Animator.StepA = shouldered ? LongGunStepA : twoHanded ? ArmedStepA : UnarmedStepA;
                Animator.StepB = shouldered ? LongGunStepB : twoHanded ? ArmedStepB : UnarmedStepB;
                if (Animator.TryGetComponent(out SpriteRenderer sr)) sr.sprite = Animator.Idle;
            }
            PlaceHeldItem();
            if (!quiet && gear != Gear.Unarmed) GameAudio.PlayAt(GameAudio.Library?.Draw, transform.position, 0.45f);
            if (!quiet && gear != Gear.Unarmed) _drawT = 0f;   // item comes up into the hand
            if (!quiet) Say(gear == Gear.Unarmed ? "Holstered." : GearInfo.Name(gear));
        }

        /// <summary>Takes a rifle or shotgun from the trunk (one long gun at a time) and readies it.</summary>
        public void TakeLongGun(Gear gun)
        {
            if (!GearInfo.IsLongGun(gun)) return;
            LongGun = gun;
            Equip(gun);
        }

        public void StowLongGun()
        {
            if (Equipped == LongGun) Equip(Gear.Unarmed, quiet: true);
            Say($"{GearInfo.Name(LongGun)} stowed in the trunk.");
            if (_lastDrawn == LongGun) _lastDrawn = Gear.Taser;
            LongGun = Gear.Unarmed;
        }

        public void Restock()
        {
            TaserShots = TaserCap; TaserCartridges = 3; SprayLeft = 1f;
            PistolMag = PistolCap; PistolReserve = 45;
            RifleMag = RifleCap; RifleReserve = 90;
            ShotgunMag = ShotgunCap; ShotgunReserve = 24;
            Say("Restocked from the trunk.");
        }

        public string AmmoText()
        {
            switch (Equipped)
            {
                case Gear.Taser: return $"{TaserShots}/{TaserCap}  +{TaserCartridges} cartridges";
                case Gear.PepperSpray: return $"{Mathf.RoundToInt(SprayLeft * 100f)}%";
                case Gear.Pistol: return $"{PistolMag} / {PistolReserve}";
                case Gear.Rifle: return $"{RifleMag} / {RifleReserve}";
                case Gear.Shotgun: return $"{ShotgunMag} / {ShotgunReserve}";
                default: return "";
            }
        }

        public void Say(string text) { Message = text; _messageTimer = 2.5f; }

        // ------------------------------------------------------------------ frame loop

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (_messageTimer > 0f && (_messageTimer -= dt) <= 0f) Message = null;
            TickTracers(Time.deltaTime);
            AnimateSwing(Time.deltaTime);
            if (_input == null) return;

            bool inCar = _officer.CurrentCar != null;
            bool blocked = inCar || _officer.ControlLocked;
            if (_held != null) _held.enabled = !inCar && Equipped != Gear.Unarmed;
            if (blocked)
            {
                CloseWheel(false);
                _aimLine.enabled = _sprayCloud.enabled = false;
                return;
            }

            UpdateWheel();
            if (WheelOpen) { _aimLine.enabled = _sprayCloud.enabled = false; return; }

            if (_draw.WasPerformedThisFrame()) Equip(Equipped == Gear.Unarmed ? _lastDrawn : Gear.Unarmed);
            QuickKeys();

            _cooldown -= Time.deltaTime;
            if (_reloadTimer > 0f && (_reloadTimer -= Time.deltaTime) <= 0f) FinishReload();
            if (_reload.WasPerformedThisFrame()) StartReload();

            Aiming = _aim.IsPressed();
            Vector2 origin = Muzzle(), dir = _officer.Facing;
            bool ranged = Equipped == Gear.Taser || GearInfo.IsFirearm(Equipped);
            _aimLine.enabled = Aiming && ranged;
            if (_aimLine.enabled)
            {
                float range = Range(Equipped);
                RaycastHit2D hit = FirstHit(origin, dir, range);
                _aimLine.SetPosition(0, origin);
                _aimLine.SetPosition(1, hit.collider != null ? hit.point : origin + dir * range);
            }

            bool spraying = Equipped == Gear.PepperSpray && _fire.IsPressed() && SprayLeft > 0f;
            _sprayCloud.enabled = spraying;
            if (!spraying) _spraySoundTimer = 0f;
            else if ((_spraySoundTimer -= Time.deltaTime) <= 0f) { _spraySoundTimer = 1.0f; GameAudio.PlayAt(GameAudio.Library?.PepperSpray, origin, 0.16f); }
            if (spraying) Spray(origin, dir);
            else _sprayExposure.Clear();

            if (_cooldown > 0f || Reloading) return;
            bool pressed = _fire.WasPressedThisFrame();
            switch (Equipped)
            {
                case Gear.Taser when pressed: FireTaser(origin, dir); break;
                case Gear.Baton when pressed: SwingBaton(dir); break;
                case Gear.Handcuffs when pressed: Cuff(dir); break;
                case Gear.Pistol when pressed: FireGun(origin, dir, ref PistolMag, 1, 1.2f, 45f, 0.22f); break;
                case Gear.Rifle when _fire.IsPressed(): FireGun(origin, dir, ref RifleMag, 1, 0.8f, 70f, 0.14f); break;
                case Gear.Shotgun when pressed: FireGun(origin, dir, ref ShotgunMag, 7, 7f, 24f, 0.85f); break;
            }
        }

        private float _spraySoundTimer;

        private void QuickKeys()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.digit2Key.wasPressedThisFrame) Equip(Gear.Taser);
            else if (kb.digit3Key.wasPressedThisFrame) Equip(Gear.Baton);
            else if (kb.digit4Key.wasPressedThisFrame) Equip(Gear.PepperSpray);
            else if (kb.digit5Key.wasPressedThisFrame) Equip(Gear.Handcuffs);
            else if (kb.digit6Key.wasPressedThisFrame) Equip(Gear.Pistol);
            else if (kb.digit7Key.wasPressedThisFrame && LongGun != Gear.Unarmed) Equip(LongGun);
        }

        // ------------------------------------------------------------------ wheel

        private void UpdateWheel()
        {
            if (_wheel.WasPressedThisFrame()) { WheelOpen = true; WheelHighlight = Equipped; }
            if (!WheelOpen) return;

            Vector2 pick = Vector2.zero;
            if (_input.ActiveFamily == InputDeviceFamily.KeyboardMouse && Mouse.current != null)
                pick = Mouse.current.position.ReadValue() - new Vector2(Screen.width, Screen.height) * 0.5f;
            else if (Gamepad.current != null)
                pick = Gamepad.current.rightStick.ReadValue() * 200f;
            if (pick.magnitude > 40f)
            {
                var list = new List<Gear>(Available());
                float angle = Mathf.Repeat(90f - Mathf.Atan2(pick.y, pick.x) * Mathf.Rad2Deg, 360f); // 0 = top, clockwise
                int index = Mathf.FloorToInt((angle + 180f / list.Count) / (360f / list.Count)) % list.Count;
                WheelHighlight = list[index];
            }
            if (!_wheel.IsPressed()) CloseWheel(true);
        }

        private void CloseWheel(bool apply)
        {
            if (!WheelOpen) return;
            WheelOpen = false;
            if (apply && WheelHighlight != Equipped) Equip(WheelHighlight);
        }

        private void OnDisable()
        {
            WheelOpen = false;
            if (_slow < 1f) { Time.timeScale = 1f; Time.fixedDeltaTime = _baseFixedDelta; _slow = 1f; }
        }

        // ------------------------------------------------------------------ slow motion (GTA-style weapon wheel)

        private const float WheelTimeScale = 0.2f;
        private float _slow = 1f;
        private float _baseFixedDelta = -1f;

        /// <summary>0 = normal time, 1 = full weapon-wheel slow motion (eases in and out; the HUD dims with it).</summary>
        public float SlowMoAmount => Mathf.InverseLerp(1f, WheelTimeScale, _slow);

        public static bool SlowMoEnabled = true;   // set false to open the wheel without slowing time

        private void UpdateSlowMo()
        {
            if (_baseFixedDelta < 0f) _baseFixedDelta = Time.fixedDeltaTime;
            if (Time.timeScale <= 0f) return;                       // paused: leave the pause menu in charge
            float target = WheelOpen && SlowMoEnabled ? WheelTimeScale : 1f;
            if (Mathf.Approximately(_slow, target) && Mathf.Approximately(Time.timeScale, target)) return;
            // Ease into slow motion over ~0.3 s, snap back a little faster when the wheel closes.
            float rate = target < _slow ? 2.8f : 4.5f;
            _slow = Mathf.MoveTowards(_slow, target, rate * Time.unscaledDeltaTime);
            Time.timeScale = _slow;
            Time.fixedDeltaTime = _baseFixedDelta * _slow;          // keep physics smooth while slowed
        }

        // ------------------------------------------------------------------ items

        private void FireTaser(Vector2 origin, Vector2 dir)
        {
            if (TaserShots <= 0) { StartReload(); return; }
            TaserShots--;
            GameAudio.PlayAt(SoundLibrary.Pick(GameAudio.Library?.TaserFire), origin, 0.3f);
            MuzzleFlash(origin, dir, 0.35f, new Color(0.6f, 0.85f, 1f));
            _cooldown = 0.5f;
            Ped2D target = AssistTarget(origin, dir, 7.5f, 9f);
            Vector2 end = target != null ? (Vector2)target.transform.position : FirstHitPoint(origin, dir, 7.5f);
            Vector2 side = new Vector2(-dir.y, dir.x) * 0.12f;
            Tracer(origin + side, end + side * 0.5f, new Color(1f, 0.9f, 0.3f, 0.9f), 0.03f, 0.6f);
            Tracer(origin - side, end - side * 0.5f, new Color(1f, 0.9f, 0.3f, 0.9f), 0.03f, 0.6f);
            Haptic(0.2f, 0.5f, 0.15f);
            if (target != null) { GameAudio.PlayAt(SoundLibrary.Pick(GameAudio.Library?.TaserArc), target.transform.position, 0.6f); target.Stun(5f); target.GetComponent<PedPose2D>()?.Shock(2.5f); Say("Taser hit — subject down. Cuff them."); }
            else Say("Taser missed.");
            if (TaserShots == 0 && TaserCartridges > 0) StartReload();
        }

        private void SwingBaton(Vector2 dir)
        {
            _cooldown = 0.5f;
            _swingTime = 0f;
            Haptic(0.3f, 0.2f, 0.1f);
            Ped2D target = NearestInCone(transform.position, dir, 1.9f, 70f);
            if (target == null) return;
            GameAudio.PlayAt(GameAudio.Library?.BatonHit, target.transform.position, 0.2f);
            target.Strike(dir);
            Haptic(0.5f, 0.3f, 0.12f);
            if (target.Current == Ped2D.Mode.Stunned && target.Subdued) Say("Subject subdued.");
        }

        private void Spray(Vector2 origin, Vector2 dir)
        {
            SprayLeft = Mathf.Max(0f, SprayLeft - Time.deltaTime / 12f);   // ~12 s per canister
            _sprayCloud.SetPosition(0, origin);
            _sprayCloud.SetPosition(1, FirstHitPoint(origin, dir, 3.6f));
            if ((_sprayTick -= Time.deltaTime) > 0f) return;
            _sprayTick = 0.1f;
            foreach (Ped2D ped in PedsInCone(origin, dir, 3.6f, 22f))
            {
                _sprayExposure.TryGetValue(ped, out float t);
                t += 0.1f;
                _sprayExposure[ped] = t;
                if (t >= 0.4f && !ped.Incapacitated) { ped.Stun(7f); Say("Subject sprayed — incapacitated."); }
            }
            if (SprayLeft <= 0f) Say("Pepper spray empty — restock at the trunk.");
        }

        private void Cuff(Vector2 dir)
        {
            _cooldown = 0.6f;
            Ped2D target = NearestInCone(transform.position, dir, 1.9f, 80f);
            if (target == null) { Say("No one close enough to cuff."); return; }
            bool ok = target.TryCuff(transform, out string result);
            Say(result);
            if (ok) { _lunge = 1f; GameAudio.PlayAt(GameAudio.Library?.Handcuffs, target.transform.position, 0.5f); GameEvents.Publish(new PedCuffedEvent(target)); }
        }

        private void FireGun(Vector2 origin, Vector2 dir, ref int mag, int pellets, float spread, float range, float cooldown)
        {
            if (mag <= 0) { GameAudio.PlayAt(GameAudio.Library?.EmptyClick, origin, 0.25f); StartReload(); _cooldown = 0.2f; return; }
            mag--;
            PlayGunshot(origin);
            _kick = 1f;
            _kickSide = Random.value < 0.5f ? -1f : 1f;
            MuzzleFlash(origin, dir, pellets > 1 ? 0.9f : Equipped == Gear.Rifle ? 0.75f : 0.55f, new Color(1f, 0.85f, 0.4f));
            _cooldown = cooldown;
            for (int i = 0; i < pellets; i++)
            {
                Vector2 d = Quaternion.Euler(0, 0, Random.Range(-spread, spread)) * dir;
                RaycastHit2D hit = FirstHit(origin, d, range);
                Vector2 end = hit.collider != null ? hit.point : origin + d * range;
                Tracer(origin, end, new Color(1f, 0.92f, 0.6f, 0.85f), pellets > 1 ? 0.025f : 0.04f, 0.08f);
                if (i == 0 && hit.collider != null && hit.collider.GetComponentInParent<Ped2D>() == null) GameAudio.PlayAt(GameAudio.Library?.BulletImpact, hit.point, 0.12f);
                if (hit.collider != null && hit.collider.GetComponentInParent<Ped2D>() is Ped2D ped) { BloodFx2D.Spray(hit.point, d, i == 0 ? 12 : 5); ped.Down(); }
            }
            GameEvents.Publish(new GunshotEvent(origin, 70f, gameObject));
            Haptic(pellets > 1 ? 0.8f : 0.45f, 0.6f, pellets > 1 ? 0.18f : 0.09f);
            if (mag == 0) StartReload();
        }

        private void PlayGunshot(Vector2 origin)
        {
            SoundLibrary lib = GameAudio.Library;
            if (lib == null) return;
            AudioClip clip = Equipped == Gear.Rifle ? SoundLibrary.Pick(lib.Rifle) : Equipped == Gear.Shotgun ? lib.Shotgun : SoundLibrary.Pick(lib.Pistol);
            GameAudio.PlayAt(clip, origin, 1f, 0.05f);
        }

        // ------------------------------------------------------------------ reload

        private void StartReload()
        {
            if (Reloading) return;
            float time;
            switch (Equipped)
            {
                case Gear.Taser when TaserShots < TaserCap && TaserCartridges > 0: time = 1.2f; break;
                case Gear.Pistol when PistolMag < PistolCap && PistolReserve > 0: time = 1.3f; break;
                case Gear.Rifle when RifleMag < RifleCap && RifleReserve > 0: time = 2.0f; break;
                case Gear.Shotgun when ShotgunMag < ShotgunCap && ShotgunReserve > 0: time = 2.4f; break;
                default:
                    if (Equipped == Gear.Taser || GearInfo.IsFirearm(Equipped)) Say("Out of ammo — restock at the trunk.");
                    return;
            }
            _reloadTimer = _reloadTotal = time;
            GameAudio.PlayAt(Equipped == Gear.Taser ? GameAudio.Library?.TaserReload : GameAudio.Library?.Reload, transform.position, 0.45f, 0f);
        }

        private void FinishReload()
        {
            switch (Equipped)
            {
                case Gear.Taser: TaserCartridges--; TaserShots = TaserCap; break;
                case Gear.Pistol: Refill(ref PistolMag, ref PistolReserve, PistolCap); break;
                case Gear.Rifle: Refill(ref RifleMag, ref RifleReserve, RifleCap); break;
                case Gear.Shotgun: Refill(ref ShotgunMag, ref ShotgunReserve, ShotgunCap); break;
            }
        }

        private static void Refill(ref int mag, ref int reserve, int cap)
        {
            int take = Mathf.Min(cap - mag, reserve);
            mag += take; reserve -= take;
        }

        // ------------------------------------------------------------------ helpers

        private static float Range(Gear g) => g switch { Gear.Taser => 7.5f, Gear.Pistol => 45f, Gear.Rifle => 70f, Gear.Shotgun => 24f, _ => 0f };

        private Vector2 Muzzle()
        {
            Vector2 f = _officer.Facing;
            if (!GearInfo.IsLongGun(Equipped)) return (Vector2)transform.position + f * 0.8f;
            // Shouldered: the barrel runs along the right side of the body.
            Vector2 right = new Vector2(f.y, -f.x);
            return (Vector2)transform.position + f * (Equipped == Gear.Rifle ? 1.45f : 1.25f) + right * 0.19f;
        }

        private RaycastHit2D FirstHit(Vector2 origin, Vector2 dir, float range)
        {
            foreach (var h in Physics2D.RaycastAll(origin, dir, range))
            {
                if (h.collider == null || h.collider.isTrigger || h.transform.IsChildOf(transform)) continue;
                if (!Overpass2D.SameLevel(h.collider, this)) continue;   // shots don't hit the road under a bridge, or the bridge from below
                return h;
            }
            return default;
        }

        private Vector2 FirstHitPoint(Vector2 origin, Vector2 dir, float range)
        {
            RaycastHit2D h = FirstHit(origin, dir, range);
            return h.collider != null ? h.point : origin + dir * range;
        }

        private IEnumerable<Ped2D> PedsInCone(Vector2 origin, Vector2 dir, float range, float halfAngle)
        {
            foreach (var col in Physics2D.OverlapCircleAll(origin, range))
            {
                Ped2D ped = col.GetComponentInParent<Ped2D>();
                if (ped == null || !Overpass2D.SameLevel(col, this)) continue;
                Vector2 to = (Vector2)ped.transform.position - origin;
                if (Vector2.Angle(dir, to) > halfAngle && to.magnitude > 0.6f) continue;
                RaycastHit2D h = FirstHit(origin, to.normalized, to.magnitude);
                if (h.collider != null && h.collider.GetComponentInParent<Ped2D>() != ped) continue; // something in the way
                yield return ped;
            }
        }

        private Ped2D NearestInCone(Vector2 origin, Vector2 dir, float range, float halfAngle)
        {
            Ped2D best = null;
            float bestD = float.MaxValue;
            foreach (Ped2D p in PedsInCone(origin, dir, range, halfAngle))
            {
                float d = Vector2.Distance(origin, p.transform.position);
                if (d < bestD) { bestD = d; best = p; }
            }
            return best;
        }

        /// <summary>Slight aim assist: nearest person inside a narrow cone with a clear line.</summary>
        private Ped2D AssistTarget(Vector2 origin, Vector2 dir, float range, float halfAngle)
        {
            Ped2D best = null;
            float bestAngle = halfAngle;
            foreach (Ped2D p in PedsInCone(origin, dir, range, halfAngle))
            {
                if (p.Incapacitated || p.Cuffed) continue;
                float a = Vector2.Angle(dir, (Vector2)p.transform.position - origin);
                if (a <= bestAngle) { bestAngle = a; best = p; }
            }
            return best;
        }

        private void Tracer(Vector2 a, Vector2 b, Color color, float width, float life)
        {
            LineRenderer lr = null;
            for (int i = 0; i < _tracers.Count; i++)
                if (_tracers[i].life <= 0f) { lr = _tracers[i].line; _tracers.RemoveAt(i); break; }
            if (lr == null) lr = NewLine("Tracer", width, color);
            lr.widthMultiplier = width;
            lr.startColor = lr.endColor = color;
            lr.SetPosition(0, a); lr.SetPosition(1, b);
            lr.enabled = true;
            _tracers.Add((lr, life));
        }

        private void TickTracers(float dt)
        {
            for (int i = 0; i < _tracers.Count; i++)
            {
                var (line, life) = _tracers[i];
                if (life <= 0f) continue;
                life -= dt;
                Color c = line.startColor; c.a = Mathf.Clamp01(life * 6f);
                line.startColor = line.endColor = c;
                if (life <= 0f) line.enabled = false;
                _tracers[i] = (line, life);
            }
        }

        private static void Haptic(float low, float high, float time)
        {
            if (Services.TryGet(out IHapticsService h)) h.Pulse(low, high, time);
        }

        /// <summary>Small item drawn in the officer's hands (positions in the 64-unit sprite grid, facing up).</summary>
        // ------------------------------------------------------------------ held-item animation

        private void LateUpdate() { AnimateHeld(Time.deltaTime); UpdateSlowMo(); }

        /// <summary>
        /// Layered on the rest pose every frame: gun recoil (item and body kick back, muzzle climbs),
        /// reload tilt, draw (item grows into the hand), cuffing lunge, spray-can jitter. The baton
        /// swing owns the pose while it plays.
        /// </summary>
        private void AnimateHeld(float dt)
        {
            _kick = Mathf.MoveTowards(_kick, 0f, dt / 0.1f);
            _drawT = Mathf.Min(1f, _drawT + dt / 0.14f);
            _lunge = Mathf.MoveTowards(_lunge, 0f, dt / 0.25f);
            UpdateFlash(dt);
            if (Animator != null)
                Animator.transform.localPosition = _bodyBase + new Vector3(0f, -0.06f * _kick * (Equipped == Gear.Shotgun ? 2f : 1f), 0f);
            if (_held == null || _swingTime >= 0f) return;

            PlaceHeldItem();
            Transform t = _held.transform;
            Vector3 pos = t.localPosition, scale = t.localScale;
            float rot = 0f;
            float kickDistance = Equipped == Gear.Shotgun ? 0.22f : Equipped == Gear.Rifle ? 0.12f : 0.09f;
            pos.y -= kickDistance * _kick;
            rot += _kick * _kickSide * (Equipped == Gear.Pistol ? 10f : 2.5f);
            if (Reloading)
            {
                float k = Mathf.Sin(ReloadProgress * Mathf.PI);
                rot += 40f * k;
                pos += new Vector3(0.06f, -0.1f, 0f) * k;
            }
            float d = 1f - (1f - _drawT) * (1f - _drawT);
            scale *= Mathf.Lerp(0.25f, 1f, d);
            pos.y += 0.2f * Mathf.Sin(_lunge * Mathf.PI);
            if (Equipped == Gear.PepperSpray && _sprayCloud != null && _sprayCloud.enabled) pos += (Vector3)(Random.insideUnitCircle * 0.012f);
            t.localPosition = pos;
            t.localScale = scale;
            t.localRotation = Quaternion.Euler(0f, 0f, rot);
        }

        private SpriteRenderer NewFlash(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _pixel;
            sr.enabled = false;
            Sorting2D.Apply(sr, Sorting2D.Effects, 6);
            return sr;
        }

        /// <summary>A brief star-shaped flash at the muzzle (two crossed quads), fading in 60 ms.</summary>
        private void MuzzleFlash(Vector2 origin, Vector2 dir, float size, Color color)
        {
            if (_flashA == null) return;
            _flashTime = 0f;
            _flashColor = color;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
            Vector3 p = origin + dir * size * 0.35f;
            _flashA.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, 0f, angle));
            _flashA.transform.localScale = new Vector3(size * 0.45f, size, 1f);
            _flashB.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, 0f, angle + 90f));
            _flashB.transform.localScale = new Vector3(size * 0.3f, size * 0.55f, 1f);
            _flashA.enabled = _flashB.enabled = true;
            UpdateFlash(0f);
        }

        private void UpdateFlash(float dt)
        {
            if (_flashTime < 0f || _flashA == null) return;
            _flashTime += dt;
            float k = 1f - _flashTime / 0.06f;
            if (k <= 0f) { _flashA.enabled = _flashB.enabled = false; _flashTime = -1f; return; }
            Color c = _flashColor;
            c.a = k;
            _flashA.color = _flashB.color = c;
        }

        // ------------------------------------------------------------------ baton swing

        private const float SwingOut = 0.15f, SwingBack = 0.2f;
        private const float SwingFrom = -75f, SwingTo = 40f;   // degrees: from out at the right side to across the front

        /// <summary>Baton swing: pivots at the hand, whips across the front, then eases back to rest.</summary>
        private void AnimateSwing(float dt)
        {
            if (_swingTime < 0f || _held == null) return;
            if (Equipped != Gear.Baton) { _swingTime = -1f; _held.transform.localRotation = Quaternion.identity; PlaceHeldItem(); return; }
            _swingTime += dt;
            float angle;
            if (_swingTime < SwingOut)
            {
                float k = _swingTime / SwingOut;
                angle = Mathf.Lerp(SwingFrom, SwingTo, 1f - (1f - k) * (1f - k));                 // fast, ease-out
            }
            else if (_swingTime < SwingOut + SwingBack)
            {
                float k = (_swingTime - SwingOut) / SwingBack;
                angle = Mathf.Lerp(SwingTo, 0f, k * k * (3f - 2f * k));                              // smooth return
            }
            else
            {
                _swingTime = -1f;
                _held.transform.localRotation = Quaternion.identity;
                PlaceHeldItem();
                return;
            }
            // Rest pose from PlaceHeldItem, then rotate the bar about its lower end (the hand).
            PlaceHeldItem();
            Vector3 centre = _held.transform.localPosition;
            Vector3 hand = centre - new Vector3(0f, _held.transform.localScale.y * 0.5f, 0f);
            Quaternion rot = Quaternion.Euler(0f, 0f, angle);
            _held.transform.localRotation = rot;
            _held.transform.localPosition = hand + rot * (centre - hand);
        }

        private void PlaceHeldItem()
        {
            if (_held == null || Animator == null) return;
            SpriteRenderer body = Animator.GetComponent<SpriteRenderer>();
            Vector2 size = body != null && body.sprite != null ? (Vector2)body.sprite.bounds.size : Vector2.one;
            Vector2 P(float x, float y) => new Vector2((x / 64f - 0.5f) * size.x, (0.5f - y / 64f) * size.y);
            Vector2 S(float w, float h) => new Vector2(w / 64f * size.x, h / 64f * size.y);
            (Vector2 pos, Vector2 scale, Color col) item = Equipped switch
            {
                Gear.Taser => (P(32, 9), S(4.5f, 9), new Color(1f, 0.85f, 0.15f)),
                Gear.PepperSpray => (P(32, 10), S(3.6f, 7), new Color(0.95f, 0.45f, 0.1f)),
                // Long guns lie along the right side, stock in the shoulder (y 32), muzzle well ahead.
                Gear.Rifle => (P(38, 9), S(3.2f, 46), new Color(0.09f, 0.09f, 0.1f)),
                Gear.Shotgun => (P(38, 12), S(3.8f, 40), new Color(0.24f, 0.17f, 0.11f)),
                Gear.Baton => (P(46, 18), S(2.4f, 18), new Color(0.07f, 0.07f, 0.08f)),
                Gear.Handcuffs => (P(44.5f, 24), S(5f, 3f), new Color(0.78f, 0.8f, 0.84f)),
                _ => (Vector2.zero, Vector2.zero, Color.clear),
            };
            _held.transform.localPosition = item.pos;
            _held.transform.localScale = new Vector3(item.scale.x, item.scale.y, 1f);
            _held.color = item.col;
            // Long guns sit under the arms and head (hands grip over them); everything else is in the hands.
            Sorting2D.Apply(_held, Sorting2D.People, GearInfo.IsLongGun(Equipped) ? 29 : 31);
        }
    }
}

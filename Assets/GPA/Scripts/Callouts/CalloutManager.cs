using System.Collections;
using System.Collections.Generic;
using GPA.Core;
using GPA.TopDown2D;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GPA.Callouts
{
    /// <summary>
    /// The patrol loop: while on duty, dispatch offers a callout every so often; accept it, drive to
    /// the scene, handle it, and the scene clears ("Code 4") before the next one comes in.
    /// Controls (own bindings so they never clash with driving): accept Y / D-pad Up,
    /// decline N, clear the current call End / View(Select), toggle duty O.
    /// Also the CAD's call source (<see cref="ICadCallSource"/>), so the MDT can show and act on calls
    /// without referencing callout classes. No new calls are offered while the officer is busy or on a stop.
    /// </summary>
    public sealed class CalloutManager : MonoBehaviour, ICadCallSource, IMapMarkerProvider
    {
        [SerializeField] private string _unit = "4127";
        [SerializeField] private bool _startOnDuty = false;   // the player chooses when to go on duty
        [SerializeField] private float _firstCalloutDelay = 30f;
        [SerializeField] private Vector2 _calloutInterval = new Vector2(90f, 180f);   // seconds between calls (min, max)
        [SerializeField] private float _offerSeconds = 15f;
        [SerializeField] private float _resultSeconds = 5f;
        [SerializeField] private float _cleanupDelay = 25f;
        [Tooltip("Metres per map sector for dispatch locations (\"Sector C-4\").")]
        [SerializeField] private float _sectorSize = 44f;

        [SerializeField] private DispatchScanner _scanner;

        private InputAction _accept, _decline, _clear, _duty;
        private CalloutContext _context;
        private float _timer;
        private readonly List<CadCall> _history = new List<CadCall>();
        private List<CadCallType> _callTypes;
        private string[] _dispatchLines;
        private string _cross, _dispatchedAt;

        public static CalloutManager Instance { get; private set; }
        public bool OnDuty { get; private set; }
        public CalloutPhase Phase { get; private set; } = CalloutPhase.None;
        public CalloutScenario Current { get; private set; }
        public CalloutDefinition CurrentDefinition => Current?.Definition;
        public string CurrentSector { get; private set; }
        public float OfferTimeLeft => Phase == CalloutPhase.Offered ? _timer : 0f;
        public string LastResult { get; private set; }
        public bool LastResultSucceeded { get; private set; }
        public float ResultTimeLeft { get; private set; }
        public int Completed { get; private set; }
        public int Failed { get; private set; }
        public int Arrests { get; private set; }
        public DispatchScanner Scanner => _scanner;

        private void Awake()
        {
            Instance = this;
            Services.Register<ICadCallSource>(this);
            if (_scanner == null) _scanner = FindAnyObjectByType<DispatchScanner>();

            _accept = new InputAction("AcceptCallout", InputActionType.Button);
            _accept.AddBinding("<Keyboard>/y"); _accept.AddBinding("<Gamepad>/dpad/up");
            _decline = new InputAction("DeclineCallout", InputActionType.Button);
            _decline.AddBinding("<Keyboard>/n");
            _clear = new InputAction("ClearCallout", InputActionType.Button);
            _clear.AddBinding("<Keyboard>/end"); _clear.AddBinding("<Gamepad>/select");
            _duty = new InputAction("ToggleDuty", InputActionType.Button);
            _duty.AddBinding("<Keyboard>/o");
        }

        private System.IDisposable _cuffSub;

        private void OnEnable()
        {
            _accept.Enable(); _decline.Enable(); _clear.Enable(); _duty.Enable(); MapMarkers.Register(this);
            _cuffSub = GameEvents.Subscribe<PedCuffedEvent>(OnPedCuffed);
        }

        private void OnDisable()
        {
            _accept.Disable(); _decline.Disable(); _clear.Disable(); _duty.Disable(); MapMarkers.Unregister(this);
            _cuffSub?.Dispose(); _cuffSub = null;
        }

        /// <summary>Handcuffing a callout suspect counts as the arrest for that call.</summary>
        private void OnPedCuffed(PedCuffedEvent e)
        {
            if (Current == null || e.Ped == null) return;
            if (Phase != CalloutPhase.EnRoute && Phase != CalloutPhase.OnScene) return;
            if (Current.Owns(e.Ped.gameObject)) Current.OnTrackedPedCuffed(e.Ped);
        }
        private void OnDestroy() { Services.Unregister<ICadCallSource>(this); if (Instance == this) Instance = null; }

        private void Start()
        {
            CalloutLibrary.Reload();
            Services.TryGet(out IHapticsService haptics);
            _context = new CalloutContext
            {
                Sprites = WorldSprites2D.Instance,
                Spawns = SpawnPoints2D.Instance,
                Scanner = _scanner,
                Haptics = haptics,
                Unit = _unit,
            };
            if (_scanner != null) _scanner.Unit = _unit;
            if (_startOnDuty) SetDuty(true);
        }

        public void SetDuty(bool onDuty) => SetDuty(onDuty, true);

        public void SetDuty(bool onDuty, bool announce)
        {
            if (OnDuty == onDuty) return;
            OnDuty = onDuty;
            if (onDuty)
            {
                _timer = _firstCalloutDelay;
                if (announce) _scanner?.Transmit($"{_unit}, you're 10-8, available for calls.");
            }
            else
            {
                if (Phase == CalloutPhase.Offered) Withdraw(false);
                else if (Current != null) EndCall(false, "Cleared — went off duty.");
                if (announce) _scanner?.Transmit($"{_unit}, 10-7, off duty. Good night.");
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            bool hotkeys = !GameplayHotkeys.Blocked;
            if (hotkeys && _duty.WasPerformedThisFrame()) SetDuty(!OnDuty);
            if (ResultTimeLeft > 0f) ResultTimeLeft -= dt;
            if (_context.Player == null && !Services.TryGet(out _context.Player)) return;

            switch (Phase)
            {
                case CalloutPhase.None:
                case CalloutPhase.Completed:
                case CalloutPhase.Failed:
                    if (!OnDuty || OfficerUnavailable()) { _held = true; return; }
                    if (_held) { _held = false; _timer = Mathf.Max(_timer, _firstCalloutDelay); }
                    _timer -= dt;
                    if (_timer <= 0f) Offer();
                    break;

                case CalloutPhase.Offered:
                    if (!OnDuty || OfficerUnavailable()) { Withdraw(true); break; }
                    _timer -= dt;
                    if (hotkeys && _accept.WasPerformedThisFrame()) Accept();
                    else if ((hotkeys && _decline.WasPerformedThisFrame()) || _timer <= 0f) Decline();
                    break;

                case CalloutPhase.EnRoute:
                case CalloutPhase.OnScene:
                    if (hotkeys && _clear.WasPerformedThisFrame()) { EndCall(false, "Call cleared by officer."); break; }
                    Current.Tick(dt);
                    if (Phase == CalloutPhase.EnRoute &&
                        Vector2.Distance(_context.Player.Transform.position, Current.Location) <= Current.ArrivalRadius)
                    {
                        SetPhase(CalloutPhase.OnScene);
                        _scanner?.Transmit($"{_unit}, 10-97, on scene.");
                        Current.OnArrived();
                    }
                    if (Current.Result == CalloutScenario.Status.Succeeded) EndCall(true, Current.ResultText);
                    else if (Current.Result == CalloutScenario.Status.Failed) EndCall(false, Current.ResultText);
                    break;
            }
        }

        private string _forced;
        private bool _held;

        /// <summary>Pulls a pending offer back without counting it as a failed call (officer went busy or off duty).</summary>
        private void Withdraw(bool announce)
        {
            Current?.Cleanup();
            Current = null;
            _held = true;
            _timer = _firstCalloutDelay;
            SetPhase(CalloutPhase.None);
            if (announce) _scanner?.Transmit($"{_unit}, disregard, we'll hold that call for another unit.");
        }

        /// <summary>Testing: offer this callout id next (ignores rank and vehicle requirements). Returns false if unknown.</summary>
        public bool ForceOffer(string id)
        {
            foreach (var d in CalloutLibrary.All) if (d.id == id)
            {
                if (Phase == CalloutPhase.EnRoute || Phase == CalloutPhase.OnScene) EndCall(false, "Call cleared by officer.");
                if (!OnDuty) SetDuty(true);
                _forced = id; _timer = 0f;
                if (Phase == CalloutPhase.None || Phase == CalloutPhase.Completed || Phase == CalloutPhase.Failed) Offer();
                return true;
            }
            return false;
        }

        private void Offer()
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                string forced = _forced;
                CalloutDefinition def = forced != null ? CalloutLibrary.All[CalloutLibrary.All.Count - 1] : CalloutLibrary.PickWeighted(Eligible);
                if (forced != null) foreach (var d in CalloutLibrary.All) if (d.id == forced) def = d;
                if (def == null) { _timer = 30f; return; }
                CalloutScenario scenario = CalloutScenarios.Create(def.type);
                if (scenario == null) continue;
                scenario.Definition = def;
                scenario.Context = _context;
                if (!scenario.Setup()) { scenario.Cleanup(); continue; }

                _forced = null;
                Current = scenario;
                CurrentSector = SectorOf(scenario.Location);
                _timer = _offerSeconds;
                SetPhase(CalloutPhase.Offered);

                string cross = Services.TryGet(out ILocationService loc) ? $", near {loc.NearestIntersection(scenario.Location)}" : "";
                _cross = cross;
                _dispatchedAt = GameClock.Now.ToString("HH:mm");
                _dispatchLines = def.dispatch != null ? Fill(def.dispatch, scenario) : null;
                _scanner?.Transmit($"Dispatch to {_unit}: {def.title}, {CurrentSector}{cross}. {def.ResponseCode}.");
                if (_dispatchLines != null) _scanner?.Transmit(_dispatchLines);
                _context.Haptics?.Pulse(0.15f, 0.5f, 0.12f);
                return;
            }
            // Nothing could be set up here. Drop a forced (debug) request so it doesn't block every future call.
            if (_forced != null) { Debug.LogWarning($"[Callouts] Forced callout '{_forced}' could not be set up here; skipping it."); _forced = null; }
            _timer = 10f;
        }

        private void Accept()
        {
            SetPhase(CalloutPhase.EnRoute);
            _scanner?.Transmit($"{_unit}, show me en route, {CurrentDefinition.ResponseCode}.");
            _context.Haptics?.Pulse(0.1f, 0.35f, 0.1f);
        }

        private void Decline()
        {
            _scanner?.Transmit($"{_unit}, unable. Dispatch reassigning.");
            CalloutScenario old = Current;
            Current = null;
            old.Cleanup();
            SetPhase(CalloutPhase.None);
            _timer = Random.Range(_calloutInterval.x, _calloutInterval.y) * 0.5f;
        }

        private void EndCall(bool success, string text)
        {
            if (Current == null) return;
            if (success) Completed++; else Failed++;
            Arrests += Current.Arrests;
            LastResult = text;
            LastResultSucceeded = success;
            ResultTimeLeft = _resultSeconds;
            _scanner?.Transmit(success ? $"{_unit}, Code 4. {text}" : $"{_unit}, clearing. {text}");
            _history.Add(BuildCall(success ? CalloutPhase.Completed : CalloutPhase.Failed, text, GameClock.Now.ToString("HH:mm")));

            StartCoroutine(CleanupLater(Current, _cleanupDelay));
            Current = null;
            SetPhase(success ? CalloutPhase.Completed : CalloutPhase.Failed);
            _timer = Random.Range(_calloutInterval.x, _calloutInterval.y);
        }

        private static IEnumerator CleanupLater(CalloutScenario scenario, float delay)
        {
            yield return new WaitForSeconds(delay);
            scenario.Cleanup();
        }

        private void SetPhase(CalloutPhase phase)
        {
            Phase = phase;
            CalloutDefinition def = Current?.Definition;
            GameEvents.Publish(new CalloutPhaseChangedEvent(def?.id, def?.title, phase));
        }

        private string[] Fill(string[] lines, CalloutScenario scenario)
        {
            var result = new string[lines.Length];
            for (int i = 0; i < lines.Length; i++)
            {
                string s = lines[i].Replace("{sector}", CurrentSector).Replace("{address}", CurrentSector).Replace("{unit}", _unit);
                if (Services.TryGet(out ILocationService locations))
                    s = s.Replace("{cross}", locations.NearestIntersection(scenario.Location)).Replace("{district}", locations.DistrictAt(scenario.Location));
                foreach (var kv in scenario.Tokens) s = s.Replace("{" + kv.Key + "}", kv.Value);
                result[i] = s;
            }
            return result;
        }

        /// <summary>
        /// Dispatch location text: a street address from the city location database when the map has
        /// one ("125 Grand Blvd"), otherwise a grid sector ("Sector C-4").
        /// </summary>
        public string SectorOf(Vector2 p)
        {
            if (Services.TryGet(out ILocationService locations)) return locations.Describe(p);
            int col = Mathf.Clamp(Mathf.FloorToInt(p.x / _sectorSize), 0, 25);
            int row = Mathf.Max(0, Mathf.FloorToInt(p.y / _sectorSize)) + 1;
            return $"Sector {(char)('A' + col)}-{row}";
        }

        // ------------------------------------------------------------------ ICadCallSource (MDT)

        /// <summary>
        /// Whether a call can be offered right now: rank gate (SWAT needs SWAT Commander), and air calls
        /// only while flying the helicopter (ground calls are held while airborne).
        /// </summary>
        private bool Eligible(CalloutDefinition def)
        {
            int level = Services.TryGet(out IReputationService rep) ? rep.Level : 1;
            if (def.requiredLevel > level) return false;
            bool flying = _context?.Player?.CurrentVehicle is GPA.TopDown2D.Car2D car && car.Hover;
            bool air = def.requires == "helicopter";
            return air == flying;
        }

        /// <summary>Restores the shift tally from a save game.</summary>
        public void RestoreStats(int completed, int failed, int arrests)
        {
            Completed = Mathf.Max(0, completed); Failed = Mathf.Max(0, failed); Arrests = Mathf.Max(0, arrests);
        }

        public string Unit => _unit;

        public bool TryGetCurrentCall(out CadCall call)
        {
            call = default;
            if (Current == null || (Phase != CalloutPhase.Offered && Phase != CalloutPhase.EnRoute && Phase != CalloutPhase.OnScene)) return false;
            string details = CurrentDefinition.description;
            if (_dispatchLines != null && _dispatchLines.Length > 0) details += "\n" + string.Join(" ", _dispatchLines);
            call = BuildCall(Phase, details, _dispatchedAt);
            return true;
        }

        public IReadOnlyList<CadCall> History => _history;

        public IReadOnlyList<CadCallType> CallTypes
        {
            get
            {
                if (_callTypes != null && _callTypes.Count == CalloutLibrary.All.Count) return _callTypes;
                _callTypes = new List<CadCallType>();
                foreach (var d in CalloutLibrary.All)
                    _callTypes.Add(new CadCallType { Code = d.code, Title = d.title, Description = d.description, ResponseCode = d.ResponseCode });
                return _callTypes;
            }
        }

        public bool AcceptOffer()
        {
            if (Phase != CalloutPhase.Offered || Current == null) return false;
            Accept();
            return true;
        }

        public bool DeclineOffer()
        {
            if (Phase != CalloutPhase.Offered || Current == null) return false;
            Decline();
            return true;
        }

        public bool ClearCall()
        {
            if (Current == null || (Phase != CalloutPhase.EnRoute && Phase != CalloutPhase.OnScene)) return false;
            EndCall(false, "Call cleared by officer.");
            return true;
        }

        private CadCall BuildCall(CalloutPhase phase, string details, string time)
        {
            var def = CurrentDefinition;
            return new CadCall
            {
                Id = def.id, Code = def.code, Title = def.title, Location = CurrentSector + _cross,
                Details = details, ResponseCode = def.ResponseCode, Phase = phase, OfferSecondsLeft = OfferTimeLeft, Time = time,
            };
        }

        /// <summary>Busy (10-6) or on a traffic stop: dispatch holds new calls.</summary>
        private static bool OfficerUnavailable() =>
            Services.TryGet(out IOfficerStatusService status) && (status.Status == OfficerStatus.Busy || status.Status == OfficerStatus.TrafficStop);

        /// <summary>Minimap: the offered call blinks; the accepted call gets the GPS route until on scene.</summary>
        public void CollectMarkers(List<MapMarker> into)
        {
            if (Current == null) return;
            string code = CurrentDefinition?.code;
            if (Phase == CalloutPhase.Offered) into.Add(new MapMarker(Current.Location, MapMarkerKind.CalloutOffer, code));
            else if (Phase == CalloutPhase.EnRoute || Phase == CalloutPhase.OnScene)
                into.Add(new MapMarker(Current.Location, MapMarkerKind.Callout, code, route: Phase == CalloutPhase.EnRoute));
        }

        public Vector2? TargetLocation => Current != null && (Phase == CalloutPhase.EnRoute || Phase == CalloutPhase.OnScene) ? Current.Location : (Vector2?)null;
    }
}

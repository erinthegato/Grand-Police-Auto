using System;
using System.Collections.Generic;
using GPA.Core;
using GPA.Input;
using GPA.Records;
using GPA.TopDown2D;
using UnityEngine;
using UnityEngine.InputSystem;
using Option = GPA.Police.InteractionMenu2D.Option;

namespace GPA.Police
{
    /// <summary>
    /// Free-roam traffic stops (spec §10). With emergency lights on, the car ahead is targeted; the
    /// TrafficStop button (Shift / D-pad Right) signals it. The driver either pulls over to the curb
    /// or flees (odds rise with warrants, a stolen car or a suspended licence). Walk up to the
    /// driver's door to open the stop menu: licence and registration, questions, records checks,
    /// warning, citation, order out, frisk, arrest, backup, release. Outcomes are written to the
    /// records database and reported on the radio. Works with no callout running.
    /// </summary>
    public sealed class TrafficStopManager : MonoBehaviour, IFreeRoamStopService, IMapMarkerProvider
    {
        public enum State { None, Signalling, Stopped, Fleeing, FootChase }

        [SerializeField] private float _targetRange = 35f;
        [SerializeField] private string _officer = "Unit 4127";

        private IInputService _input;
        private IPlayerAvatar _player;
        private InputAction _stopAction;
        private Car2D _car;
        private TrafficDriver2D _traffic;
        private PullOverDriver2D _pullOver;
        private SuspectDriver2D _fleeDriver;
        private Ped2D _driverPed;
        private Citizen _citizen;
        private VehicleRecord _vehicle;
        private Ped2D.Personality _personality;
        private bool _idChecked, _driverRun, _plateRun, _frisked, _contraband, _refused, _fled;
        private float _stoppedTimer, _lostTimer;
        private string _location;

        public static TrafficStopManager Instance { get; private set; }
        public State Current { get; private set; }
        public Car2D Candidate { get; private set; }
        public Car2D Target => _car;
        public bool Active => Current != State.None;
        public string TargetLabel => _car == null ? "" : $"{_car.Description} · {_car.Plate}";
        public string Status { get; private set; }

        private RecordsDatabase Db => RecordsDatabase.Instance;
        private IDispatchRadio Radio => Services.TryGet(out IDispatchRadio r) ? r : null;
        private string Unit => Radio != null ? Radio.Unit : "4127";

        private void Awake() { Instance = this; Services.Register<IFreeRoamStopService>(this); MapMarkers.Register(this); }
        private void OnDestroy() { Services.Unregister<IFreeRoamStopService>(this); MapMarkers.Unregister(this); if (Instance == this) Instance = null; }

        private void Start()
        {
            if (Services.TryGet(out _input)) _stopAction = _input.Get(ActionMaps.Vehicle, "TrafficStop");
            if (InteractionMenu2D.Instance == null) gameObject.AddComponent<InteractionMenu2D>();
        }

        // ------------------------------------------------------------------ frame loop

        private void Update()
        {
            if (_player == null && !Services.TryGet(out _player)) return;
            Car2D own = _player.CurrentVehicle as Car2D;
            Candidate = Current == State.None && own != null && LightsOn(own) ? FindCandidate(own) : null;

            if (_stopAction != null && _stopAction.WasPerformedThisFrame() && own != null)
            {
                if (Current == State.None && Candidate != null) TryStartStop(Candidate.transform);
                else if (Current == State.Signalling) CancelStop();
            }

            switch (Current)
            {
                case State.Signalling:
                    if (_car == null) { End(StopOutcome.Cancelled, "Vehicle gone."); break; }
                    if (_pullOver != null && _pullOver.Stopped) OnStopped();
                    break;
                case State.Fleeing:
                    TickPursuit();
                    break;
                case State.FootChase:
                    if (_driverPed == null) { End(StopOutcome.Lost, "Suspect gone."); break; }
                    _lostTimer = Vector2.Distance(_driverPed.transform.position, _player.Transform.position) > 60f ? _lostTimer + Time.deltaTime : 0f;
                    if (_lostTimer > 6f) End(StopOutcome.Lost, "Suspect escaped on foot.");
                    break;
            }
        }

        private static bool LightsOn(Car2D car) => car.TryGetComponent(out Lightbar2D bar) && bar.On;

        private Car2D FindCandidate(Car2D own)
        {
            Car2D best = null;
            float bestD = _targetRange;
            Vector2 pos = own.transform.position, fwd = own.transform.up;
            foreach (TrafficDriver2D d in FindObjectsByType<TrafficDriver2D>(FindObjectsInactive.Exclude))
            {
                if (d.Suspended || !d.TryGetComponent(out Car2D c)) continue;
                Vector2 rel = (Vector2)c.transform.position - pos;
                float dist = rel.magnitude;
                if (dist < bestD && Vector2.Dot(rel.normalized, fwd) > 0.75f) { bestD = dist; best = c; }
            }
            return best;
        }

        // ------------------------------------------------------------------ lifecycle

        public bool TryStartStop(Transform vehicle)
        {
            if (Current != State.None || vehicle == null || !vehicle.TryGetComponent(out Car2D car) || !vehicle.TryGetComponent(out TrafficDriver2D traffic)) return false;
            _car = car; _traffic = traffic;
            _traffic.Suspended = true;
            _citizen = Db != null ? Db.GetCitizen(car.DriverCitizenId) : null;
            _vehicle = Db != null ? Db.GetVehicle(car.VehicleRecordId) : null;
            _personality = PersonalityOf(_citizen);
            _idChecked = _driverRun = _plateRun = _frisked = _contraband = _refused = _fled = false;
            _location = Locate(car.transform.position);
            Radio?.Transmit(TransmissionPriority.Routine, $"{Unit}, traffic stop, {car.Description}, plate {car.Plate}, {_location}.");

            if (UnityEngine.Random.value < FleeChance())
            {
                StartPursuit();
                return true;
            }
            _pullOver = car.gameObject.AddComponent<PullOverDriver2D>();
            _pullOver.Begin(car);
            Current = State.Signalling;
            Status = "Signalling the vehicle to pull over…";
            return true;
        }

        public void CancelStop()
        {
            if (Current == State.None) return;
            Radio?.Transmit(TransmissionPriority.Routine, $"{Unit}, disregard the stop.");
            End(StopOutcome.Cancelled, "Stop cancelled.");
        }

        private void OnStopped()
        {
            Current = State.Stopped;
            Status = "Vehicle stopped. Approach the driver's door.";
            var it = _car.GetComponent<Interactable2D>() ?? _car.gameObject.AddComponent<Interactable2D>();
            it.ClearHandlers();
            it.Prompt = "Speak with driver";
            it.Range = 4.2f;
            it.enabled = true;
            it.Used += _ => OpenDriverMenu();
        }

        private float FleeChance()
        {
            float p = 0.03f;
            if (_citizen != null)
            {
                if (_citizen.Wanted) p += 0.35f;
                if (_citizen.License.Status == LicenseStatus.Suspended || _citizen.License.Status == LicenseStatus.Revoked) p += 0.15f;
                if (_personality == Ped2D.Personality.Fleeing) p += 0.4f;
            }
            if (_vehicle != null && _vehicle.Stolen) p += 0.5f;
            return Mathf.Clamp01(Mathf.Min(p, 0.85f));
        }

        private void End(StopOutcome outcome, string summary)
        {
            InteractionMenu2D.Instance?.Close();
            Car2D car = _car;
            if (car != null && car.TryGetComponent(out Interactable2D it)) { it.ClearHandlers(); it.enabled = false; }
            if (_pullOver != null) Destroy(_pullOver);
            if (_fleeDriver != null) { _fleeDriver.Stop(); Destroy(_fleeDriver); }

            bool driverGone = outcome == StopOutcome.Arrest || _driverPed != null;
            if (car != null && _traffic != null && !driverGone && outcome != StopOutcome.Lost)
            {
                _traffic.Suspended = false;
                car.Input = _traffic;                 // drive on
            }
            else if (car != null)
            {
                car.Input = null; // left at the kerb / abandoned; towed later
                Destroy(car.gameObject, 30f);
            }
            if (_driverPed != null)
            {
                if (_driverPed.Cuffed) { /* custody pipeline owns them now */ }
                else if (_driverPed.TryGetComponent(out Interactable2D it2)) { it2.ClearHandlers(); it2.enabled = false; _driverPed.Ambient = true; _driverPed.Wander(); }
                else { _driverPed.Ambient = true; _driverPed.Wander(); }
            }

            GameEvents.Publish(new StopEndedEvent(outcome, _vehicle?.Id, _citizen?.Id, summary));
            Status = summary;
            Current = State.None;
            _car = null; _traffic = null; _pullOver = null; _fleeDriver = null; _driverPed = null;
            _citizen = null; _vehicle = null;
            CancelInvoke(nameof(ClearStatus));
            Invoke(nameof(ClearStatus), 5f);
        }

        private void ClearStatus() { if (Current == State.None) Status = null; }

        // ------------------------------------------------------------------ minimap

        /// <summary>Minimap: the stopped car, or the fleeing car / driver during a pursuit.</summary>
        public void CollectMarkers(List<MapMarker> into)
        {
            switch (Current)
            {
                case State.Signalling:
                case State.Stopped:
                    if (_car != null) into.Add(new MapMarker(_car.transform.position, MapMarkerKind.TrafficStop, _car.Plate));
                    break;
                case State.Fleeing:
                    if (_car != null) into.Add(new MapMarker(_car.transform.position, MapMarkerKind.Pursuit, _car.Plate));
                    break;
                case State.FootChase:
                    if (_driverPed != null) into.Add(new MapMarker(_driverPed.transform.position, MapMarkerKind.Pursuit, "Foot chase"));
                    break;
            }
        }

        // ------------------------------------------------------------------ pursuit

        private void StartPursuit()
        {
            _fled = true;
            if (_pullOver != null) { Destroy(_pullOver); _pullOver = null; }
            _fleeDriver = _car.gameObject.AddComponent<SuspectDriver2D>();
            _fleeDriver.Begin(_car, _player.Transform);
            Current = State.Fleeing;
            Status = "Vehicle is fleeing — stop it!";
            _stoppedTimer = _lostTimer = 0f;
            Radio?.Transmit(TransmissionPriority.Emergency, $"{Unit}, vehicle failed to yield, in pursuit of {_car.Description}, plate {_car.Plate}, {_location}.");
        }

        private void TickPursuit()
        {
            if (_car == null) { End(StopOutcome.Lost, "Vehicle lost."); return; }
            float d = Vector2.Distance(_car.transform.position, _player.Transform.position);
            _stoppedTimer = _car.SpeedKmh < 6f && d < 30f ? _stoppedTimer + Time.deltaTime : 0f;
            _lostTimer = d > 220f ? _lostTimer + Time.deltaTime : 0f;
            if (_lostTimer > 8f)
            {
                Db?.AddBolo($"{_car.Description}, plate {_car.Plate}", "Fled from a traffic stop", _vehicle?.Id, _citizen?.Id);
                Radio?.Transmit(TransmissionPriority.Critical, $"{Unit}, lost the vehicle. BOLO for {_car.Description}, plate {_car.Plate}.");
                End(StopOutcome.Lost, "Vehicle lost — BOLO issued.");
                return;
            }
            if (_stoppedTimer < 2.5f) return;

            // Pinned: driver gets out, either gives up or runs.
            _fleeDriver.Stop();
            SpawnDriverPed();
            Radio?.Transmit(TransmissionPriority.Critical, $"{Unit}, vehicle stopped, driver is out.");
            if (UnityEngine.Random.value < 0.45f)
            {
                _driverPed.Flee(_player.Transform);
                Current = State.FootChase;
                Status = "Driver is running — catch them!";
                _lostTimer = 0f;
                AddPedInteraction("Tackle and detain", () => { _driverPed.Stun(1.5f); Current = State.Stopped; OpenPedMenu(); });
            }
            else
            {
                Current = State.Stopped;
                Status = "Driver surrendered. Arrest them.";
                AddPedInteraction("Deal with the driver", OpenPedMenu);
            }
        }

        // ------------------------------------------------------------------ menus

        private string MenuTitle => _citizen != null && _idChecked ? _citizen.FullName : "Driver";
        private string MenuSubtitle => $"{_car?.Description} · {_car?.Plate} · {_location}";

        private void OpenDriverMenu()
        {
            InteractionMenu2D.Instance.Open(MenuTitle, MenuSubtitle, DriverOptions);
            InteractionMenu2D.Instance.Say("<i>The window rolls down.</i>");
        }

        private List<Option> DriverOptions()
        {
            var menu = InteractionMenu2D.Instance;
            return new List<Option>
            {
                new Option("Explain the reason for the stop", () => menu.Say(Reply(ReasonReplies))),
                new Option("Request licence, registration and insurance", RequestDocuments),
                new Option("Ask: \"Where are you headed?\"", () => menu.Say(Reply(DestinationReplies))),
                new Option("Ask: \"Anything illegal in the vehicle?\"", () => menu.Say(Reply(ContrabandReplies))),
                new Option("Run the driver", RunDriver),
                new Option("Run the plate", RunPlate),
                new Option("Issue a warning", () => Conclude(StopOutcome.Warning, "Verbal warning issued.")),
                new Option("Issue a citation…", OpenCitationMenu),
                new Option("Order the driver out of the vehicle", OrderOut),
                new Option("Request backup", RequestBackup),
                new Option("Release — free to go", () => Conclude(StopOutcome.Released, "Driver released.")),
            };
        }

        private void OpenPedMenu()
        {
            InteractionMenu2D.Instance.Open(MenuTitle, MenuSubtitle, PedOptions);
        }

        private List<Option> PedOptions()
        {
            var menu = InteractionMenu2D.Instance;
            var options = new List<Option>
            {
                new Option("Request identification", RequestDocuments),
                new Option("Ask: \"Anything on you I should know about?\"", () => menu.Say(Reply(ContrabandReplies))),
                new Option("Frisk for weapons", Frisk),
                new Option("Run the person", RunDriver),
                new Option("Run the plate", RunPlate),
            };
            List<string> charges = Charges();
            options.Add(new Option(charges.Count > 0 ? $"Arrest ({string.Join(", ", charges)})" : "Arrest (no grounds yet — run records / frisk)", ArrestDriver, charges.Count > 0));
            if (_driverPed == null || _driverPed.Current != Ped2D.Mode.Detained)
            {
                options.Add(new Option("Issue a citation…", OpenCitationMenu));
                options.Add(new Option("Issue a warning", () => Conclude(StopOutcome.Warning, "Verbal warning issued.")));
                options.Add(new Option("Release — back to the vehicle", () => Conclude(StopOutcome.Released, "Driver released.")));
            }
            options.Add(new Option("Request backup", RequestBackup));
            return options;
        }

        private void OpenCitationMenu()
        {
            InteractionMenu2D.Instance.Open("Issue citation", MenuSubtitle, () =>
            {
                var list = new List<Option>();
                foreach (var (offense, fine) in CitationTypes())
                    list.Add(new Option($"{offense} — ${fine}", () => Cite(offense, fine)));
                list.Add(new Option("Back", () => { if (_driverPed != null) OpenPedMenu(); else OpenDriverMenu(); }));
                return list;
            });
        }

        private IEnumerable<(string, int)> CitationTypes()
        {
            yield return ("Speeding", 150);
            yield return ("Failure to obey a traffic signal", 150);
            yield return ("Unsafe lane change", 90);
            if (_plateRun && _vehicle != null && _vehicle.Registration != RegistrationStatus.Valid) yield return ("Expired/suspended registration", 75);
            if (_plateRun && _vehicle != null && !_vehicle.Insured) yield return ("No proof of insurance", 250);
            if (_driverRun && _citizen != null && _citizen.License.Status != LicenseStatus.Valid) yield return ("Unlicensed operation of a vehicle", 300);
        }

        // ------------------------------------------------------------------ actions

        private void RequestDocuments()
        {
            var menu = InteractionMenu2D.Instance;
            if (_citizen == null) { menu.Say("The driver hands over an ID you can't verify (no record)."); _idChecked = true; return; }
            if (_personality == Ped2D.Personality.Uncooperative && !_refused)
            {
                _refused = true;
                menu.Say("\"I don't have to show you anything.\"  <i>(ask again)</i>");
                return;
            }
            _idChecked = true;
            menu.Say(_citizen.License.Status == LicenseStatus.None
                ? $"No licence. Gives name {_citizen.FullName}, DOB {_citizen.DateOfBirth}."
                : $"Licence: {_citizen.FullName}, DOB {_citizen.DateOfBirth}, no. {_citizen.License.Number}.");
            if (_vehicle != null)
                menu.Say($"Registration: {_vehicle.Description}, plate {_vehicle.Plate}. Insurance card: {(_vehicle.Insured ? "yes" : "<color=#ff8a65>none</color>")}.");
            menu.SetSubtitle(MenuSubtitle);
        }

        private void RunDriver()
        {
            var menu = InteractionMenu2D.Instance;
            if (!_idChecked) { menu.Say("<i>You need their identification first.</i>"); return; }
            _driverRun = true;
            Radio?.Transmit(TransmissionPriority.Routine, $"{Unit}, requesting a 10-27 on {_citizen?.FullName ?? "the driver"}.");
            if (_citizen == null) { menu.Say("<color=#ff8a65>No record found for that name.</color>"); return; }
            string lic = _citizen.License.Status == LicenseStatus.Valid ? "<color=#7fd88a>VALID</color>" : $"<color=#ff6b5b>{_citizen.License.Status.ToString().ToUpperInvariant()}</color>";
            menu.Say($"Licence {lic}. Address: {_citizen.Address}.");
            if (_citizen.Warrants.Count > 0)
                foreach (var w in _citizen.Warrants) menu.Say($"<color=#ff6b5b>WARRANT {w.Id}: {w.Offense} ({(w.Felony ? "felony" : "misdemeanour")})</color>");
            else menu.Say("No warrants.");
            menu.Say(_citizen.CriminalHistory.Count > 0 ? $"Criminal history: {_citizen.CriminalHistory.Count} entr{(_citizen.CriminalHistory.Count == 1 ? "y" : "ies")}, latest: {_citizen.CriminalHistory[_citizen.CriminalHistory.Count - 1].Offense}." : "No criminal history.");
        }

        private void RunPlate()
        {
            var menu = InteractionMenu2D.Instance;
            _plateRun = true;
            Radio?.Transmit(TransmissionPriority.Routine, $"{Unit}, run plate {_car?.Plate}.");
            if (_vehicle == null) { menu.Say("<color=#ff8a65>Plate not on file.</color>"); return; }
            Citizen owner = Db?.GetCitizen(_vehicle.OwnerId);
            menu.Say($"{_vehicle.Description}, VIN …{_vehicle.Vin.Substring(_vehicle.Vin.Length - 6)}, owner {owner?.FullName ?? "unknown"}.");
            menu.Say($"Registration {(_vehicle.Registration == RegistrationStatus.Valid ? "<color=#7fd88a>VALID</color>" : $"<color=#ff6b5b>{_vehicle.Registration.ToString().ToUpperInvariant()}</color>")}, insurance {(_vehicle.Insured ? "on file" : "<color=#ff6b5b>NONE</color>")}.");
            if (_vehicle.Stolen) menu.Say("<color=#ff6b5b>VEHICLE REPORTED STOLEN — active BOLO.</color>");
        }

        private void OrderOut()
        {
            var menu = InteractionMenu2D.Instance;
            SpawnDriverPed();
            bool risky = (_citizen != null && _citizen.Wanted) || (_vehicle != null && _vehicle.Stolen);
            if (_personality == Ped2D.Personality.Aggressive && risky && UnityEngine.Random.value < 0.4f)
            {
                menu.Close();
                _driverPed.Flee(_player.Transform);
                Current = State.FootChase;
                Status = "Driver shoved past you and ran — catch them!";
                _fled = true;
                Radio?.Transmit(TransmissionPriority.Emergency, $"{Unit}, foot pursuit from my traffic stop, {_location}.");
                AddPedInteraction("Tackle and detain", () => { _driverPed.Stun(1.5f); Current = State.Stopped; OpenPedMenu(); });
                return;
            }
            menu.Say(_personality == Ped2D.Personality.Uncooperative ? "\"Fine. Whatever.\" The driver steps out." : "The driver steps out of the vehicle.");
            AddPedInteraction("Speak with driver", OpenPedMenu);
            OpenPedMenu();
            InteractionMenu2D.Instance.Say("The driver is standing by the car.");
        }

        private void Frisk()
        {
            var menu = InteractionMenu2D.Instance;
            if (_driverPed == null) { menu.Say("<i>Order them out of the vehicle first.</i>"); return; }
            if (_frisked) { menu.Say("Already frisked."); return; }
            _frisked = true;
            bool history = _citizen != null && _citizen.CriminalHistory.Count > 0;
            _contraband = UnityEngine.Random.value < (history ? 0.35f : 0.06f);
            menu.Say(_contraband ? "<color=#ff6b5b>Found: a bag of what appears to be narcotics.</color>" : "Nothing found.");
        }

        private List<string> Charges()
        {
            var charges = new List<string>();
            if (_driverRun && _citizen != null)
            {
                foreach (var w in _citizen.Warrants) charges.Add($"Warrant: {w.Offense}");
                if (_citizen.License.Status == LicenseStatus.Suspended || _citizen.License.Status == LicenseStatus.Revoked)
                    charges.Add("Aggravated unlicensed operation");
            }
            if (_plateRun && _vehicle != null && _vehicle.Stolen) charges.Add("Grand larceny of a vehicle");
            if (_contraband) charges.Add("Criminal possession of a controlled substance");
            if (_fled) charges.Add("Unlawful fleeing a police officer");
            return charges;
        }

        private void ArrestDriver()
        {
            List<string> charges = Charges();
            if (charges.Count == 0) return;
            if (_driverPed == null) SpawnDriverPed();
            // Cuffed and handed to the custody pipeline: escort to your car, drive to the precinct, book.
            if (ArrestPipeline2D.Instance != null) ArrestPipeline2D.Instance.TakeIntoCustody(_driverPed, charges);
            else _driverPed.ForceCuff();
            Radio?.Transmit(TransmissionPriority.Routine, $"{Unit}, one in custody.");
            Conclude(StopOutcome.Arrest, $"In custody: {string.Join(", ", charges)}. Escort them to your car.");
        }

        private void Cite(string offense, int fine)
        {
            if (_citizen != null) Db?.AddCitation(_citizen.Id, _vehicle?.Id, offense, fine, _location, _officer);
            Conclude(StopOutcome.Citation, $"Citation issued: {offense} (${fine}).");
        }

        private void RequestBackup()
        {
            Radio?.Transmit(TransmissionPriority.Critical, $"{Unit}, requesting backup at my traffic stop, {_location}.");
            InteractionMenu2D.Instance.Say("<i>Dispatch: units notified.</i>  (Backup units arrive in a later update.)");
        }

        private void Conclude(StopOutcome outcome, string summary)
        {
            Radio?.Transmit(TransmissionPriority.Routine, $"{Unit}, Code 4 on the stop. {summary}");
            if (outcome != StopOutcome.Arrest && _driverPed != null)
            {
                Destroy(_driverPed.gameObject);       // gets back in and drives off
                _driverPed = null;
            }
            End(outcome, summary);
        }

        // ------------------------------------------------------------------ helpers

        private void SpawnDriverPed()
        {
            if (_driverPed != null || _car == null || WorldSprites2D.Instance == null) return;
            var sprites = WorldSprites2D.Instance;
            int look = _citizen != null && _citizen.LookIndex < sprites.Civilians.Length ? _citizen.LookIndex : -1;
            Sprite sprite = look >= 0 ? sprites.Civilians[look] : null;
            CarDoors2D.Swing(_car, CarDoors2D.Door.DriverFront, 0.5f);
            _driverPed = sprites.SpawnPed(_car.DriverExitPoint, _car.transform.eulerAngles.z + 90f, sprite, look);
            _driverPed.name = "Stopped Driver";
            _driverPed.CitizenId = _citizen?.Id;
            _driverPed.Profile = _personality;
            _driverPed.Idle();
            if (_car.TryGetComponent(out Interactable2D it)) { it.ClearHandlers(); it.enabled = false; }
        }

        private void AddPedInteraction(string prompt, Action action)
        {
            var it = _driverPed.GetComponent<Interactable2D>() ?? _driverPed.gameObject.AddComponent<Interactable2D>();
            it.ClearHandlers();
            it.Prompt = prompt;
            it.Range = 2.4f;
            it.enabled = true;
            it.Used += _ => action();
        }

        private string Locate(Vector2 p) => Services.TryGet(out ILocationService loc) ? loc.Describe(p) : $"{p.x:0}, {p.y:0}";

        private static Ped2D.Personality PersonalityOf(Citizen c)
        {
            if (c == null) return Ped2D.Personality.Cooperative;
            int h = Mathf.Abs(c.Id.GetHashCode()) % 100;
            if (c.Wanted && h < 35) return Ped2D.Personality.Nervous;
            return h < 52 ? Ped2D.Personality.Cooperative : h < 64 ? Ped2D.Personality.Nervous : h < 72 ? Ped2D.Personality.Confused
                 : h < 84 ? Ped2D.Personality.Uncooperative : h < 90 ? Ped2D.Personality.Aggressive : h < 97 ? Ped2D.Personality.Suspicious : Ped2D.Personality.Fleeing;
        }

        private string Reply(Dictionary<Ped2D.Personality, string> table) =>
            table.TryGetValue(_personality, out string s) ? $"\"{s}\"" : "\"...\"";

        private static readonly Dictionary<Ped2D.Personality, string> ReasonReplies = new Dictionary<Ped2D.Personality, string>
        {
            { Ped2D.Personality.Cooperative, "Sorry, officer, I didn't realise. It won't happen again." },
            { Ped2D.Personality.Nervous, "Oh — uh, was I going too fast? I'm really sorry." },
            { Ped2D.Personality.Confused, "Stopped? What did I do?" },
            { Ped2D.Personality.Uncooperative, "Don't you have real criminals to catch?" },
            { Ped2D.Personality.Aggressive, "This is harassment and you know it." },
            { Ped2D.Personality.Suspicious, "Is there a problem, officer?" },
            { Ped2D.Personality.Fleeing, "...Okay." },
        };
        private static readonly Dictionary<Ped2D.Personality, string> DestinationReplies = new Dictionary<Ped2D.Personality, string>
        {
            { Ped2D.Personality.Cooperative, "Just heading home from work." },
            { Ped2D.Personality.Nervous, "My — my friend's place. Over on, um... over there." },
            { Ped2D.Personality.Confused, "I was looking for Pearl Street, I think I'm lost." },
            { Ped2D.Personality.Uncooperative, "That's none of your business." },
            { Ped2D.Personality.Aggressive, "Away from you, hopefully." },
            { Ped2D.Personality.Suspicious, "Around. Just driving." },
            { Ped2D.Personality.Fleeing, "Nowhere. Can I go?" },
        };
        private static readonly Dictionary<Ped2D.Personality, string> ContrabandReplies = new Dictionary<Ped2D.Personality, string>
        {
            { Ped2D.Personality.Cooperative, "No, sir. You can look if you want." },
            { Ped2D.Personality.Nervous, "N-no. Nothing. Why?" },
            { Ped2D.Personality.Confused, "Illegal? Like what?" },
            { Ped2D.Personality.Uncooperative, "I don't consent to any searches." },
            { Ped2D.Personality.Aggressive, "Get a warrant." },
            { Ped2D.Personality.Suspicious, "Why would there be?" },
            { Ped2D.Personality.Fleeing, "No." },
        };
    }
}

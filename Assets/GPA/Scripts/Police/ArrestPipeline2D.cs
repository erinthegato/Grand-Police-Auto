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
    /// Custody from cuffs to booking (spec §9/§10 arrest loop):
    ///   cuffed person → "Escort" (they walk behind you; Backspace / B lets go)
    ///   → at the parked cruiser's rear passenger door, "Put suspect in back seat" (2 seats)
    ///   → drive to the precinct and stop out front → E / A "Book prisoner": confirm charges, record the
    ///     arrest in the database, or release without charge.
    /// Anyone cuffed (handcuffs, traffic-stop arrest, callouts) enters this pipeline.
    /// </summary>
    public sealed class ArrestPipeline2D : MonoBehaviour, IBookingDesk, ICustodySave
    {
        private const int Seats = 2;
        private const float BookingRange = 32f;
        private static readonly string[] ExtraCharges =
            { "Resisting arrest", "Obstructing governmental administration", "Disorderly conduct", "Assault on a police officer", "Criminal trespass" };

        public sealed class Custody
        {
            public Ped2D Ped;
            public string CitizenId;
            public string Location;
            public readonly List<string> Charges = new List<string>();
            public readonly HashSet<string> Selected = new HashSet<string>();
            public Car2D Car;                    // back seat of this car, or null when on foot
            public BookingCase Booking;          // what the MDT Booking tab shows, built on demand
        }

        public static ArrestPipeline2D Instance { get; private set; }

        private readonly Dictionary<Ped2D, Custody> _custody = new Dictionary<Ped2D, Custody>();
        private readonly Dictionary<Car2D, Interactable2D> _seatPoints = new Dictionary<Car2D, Interactable2D>();
        private IDisposable _cuffSub;
        private IInputService _input;
        private IPlayerAvatar _player;
        private InputAction _interact, _back;
        private Vector2? _precinct;
        private string _precinctName = "the precinct";
        private string _officerName = "Unit 4127";

        public Custody Escorting { get; private set; }
        /// <summary>Prompt for the HUD while driving with prisoners near the precinct (null otherwise).</summary>
        public string BookingPrompt { get; private set; }
        public int PrisonersIn(Car2D car) { int n = 0; foreach (var c in _custody.Values) if (c.Car == car) n++; return n; }
        public int Booked { get; private set; }

        private RecordsDatabase Db => RecordsDatabase.Instance;
        private IDispatchRadio Radio => Services.TryGet(out IDispatchRadio r) ? r : null;
        private string Unit => Radio != null ? Radio.Unit : "4127";

        private void Awake() { Instance = this; Services.Register<IBookingDesk>(this); Services.Register<ICustodySave>(this); }
        private void OnDestroy() { Services.Unregister<IBookingDesk>(this); Services.Unregister<ICustodySave>(this); if (Instance == this) Instance = null; }
        private void OnEnable() => _cuffSub = GameEvents.Subscribe<PedCuffedEvent>(e => Register(e.Ped, null));
        private void OnDisable() => _cuffSub?.Dispose();

        private void Start()
        {
            if (Services.TryGet(out _input))
            {
                _interact = _input.Get(ActionMaps.Interaction, "Interact");
                _back = _input.Get(ActionMaps.Interaction, "Back");
            }
            var locations = FindAnyObjectByType<CityLocations>();
            var station = locations != null && locations.Database != null ? locations.Database.Buildings.Find(b => b.Type == "police") : null;
            if (station != null) { _precinct = station.Entrance; _precinctName = station.Name; }
            foreach (var trunk in FindObjectsByType<CruiserTrunk2D>(FindObjectsInactive.Exclude)) AddSeatPoint(trunk.GetComponent<Car2D>());
        }

        // ------------------------------------------------------------------ entry points

        /// <summary>Puts someone into custody with charges already established (e.g. arrest at a traffic stop).</summary>
        public void TakeIntoCustody(Ped2D ped, IEnumerable<string> charges)
        {
            if (ped == null) return;
            ped.ForceCuff();
            Register(ped, charges);
        }

        private void Register(Ped2D ped, IEnumerable<string> charges)
        {
            if (ped == null) return;
            if (!_custody.TryGetValue(ped, out Custody c))
            {
                c = new Custody { Ped = ped, CitizenId = ped.CitizenId, Location = Locate(ped.transform.position) };
                _custody[ped] = c;
                ped.Ambient = false;   // the population manager must not recycle a prisoner
                var it = ped.GetComponent<Interactable2D>() ?? ped.gameObject.AddComponent<Interactable2D>();
                it.ClearHandlers();
                it.Prompt = "Escort suspect";
                it.Range = 2f;
                it.enabled = true;
                it.Used += _ => StartEscort(c);
            }
            if (charges != null)
                foreach (string ch in charges)
                    if (!c.Charges.Contains(ch)) { c.Charges.Add(ch); c.Selected.Add(ch); }
        }

        private void AddSeatPoint(Car2D car)
        {
            if (car == null || _seatPoints.ContainsKey(car)) return;
            // Cars cloned from a cruiser (fleet garage) copy its "Rear Door" child, but not the click handler. Remove those dead copies.
            for (int i = car.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = car.transform.GetChild(i);
                if (child.name == "Rear Door") Destroy(child.gameObject);
            }
            var go = new GameObject("Rear Door");
            go.transform.SetParent(car.transform, false);
            go.transform.localPosition = new Vector3(1.7f, -0.8f, 0f);   // passenger side, rear
            var it = go.AddComponent<Interactable2D>();
            it.Range = 2.2f;
            it.Used += _ => UseRearDoor(car);
            _seatPoints[car] = it;
        }

        // ------------------------------------------------------------------ escort / back seat

        private void StartEscort(Custody c)
        {
            if (Escorting != null || c.Ped == null || _player == null) return;
            Escorting = c;
            c.Ped.Escort(_player.Transform);
            if (c.Ped.TryGetComponent(out Interactable2D it)) it.enabled = false;
        }

        private void StopEscort()
        {
            if (Escorting == null) return;
            var c = Escorting;
            Escorting = null;
            if (c.Ped == null) return;
            c.Ped.StopEscort();
            if (c.Ped.TryGetComponent(out Interactable2D it)) it.enabled = true;
        }

        private void UseRearDoor(Car2D car)
        {
            if (Escorting != null)
            {
                if (PrisonersIn(car) >= Seats) return;
                var c = Escorting;
                Escorting = null;
                c.Car = car;
                c.Ped.StopEscort();
                c.Ped.gameObject.SetActive(false);   // seated in the back
                CarDoors2D.Swing(car, CarDoors2D.Door.PassengerRear, 0.3f);
                Radio?.Transmit(TransmissionPriority.Routine, $"{Unit}, one in the back, {Name(c)}.");
                return;
            }
            // Take the most recent prisoner back out.
            foreach (var c in _custody.Values)
            {
                if (c.Car != car) continue;
                c.Car = null;
                CarDoors2D.Swing(car, CarDoors2D.Door.PassengerRear, 0.5f);
                c.Ped.transform.position = car.transform.TransformPoint(new Vector3(2.6f, -0.8f, 0f));
                c.Ped.gameObject.SetActive(true);
                c.Ped.StopEscort();
                if (c.Ped.TryGetComponent(out Interactable2D it)) it.enabled = true;
                return;
            }
        }

        // ------------------------------------------------------------------ frame loop

        private void Update()
        {
            if (_player == null && !Services.TryGet(out _player)) return;
            Prune();
            Car2D inCar = _player.CurrentVehicle as Car2D;
            // Cruisers signed out from the garage (or restored from a save) appear after Start: give them a rear door too.
            if (inCar != null && !_seatPoints.ContainsKey(inCar) && inCar.TryGetComponent(out CruiserTrunk2D _)) AddSeatPoint(inCar);
            bool menuOpen = InteractionMenu2D.Instance != null && InteractionMenu2D.Instance.IsOpen;

            // Let go of the person being escorted.
            if (Escorting != null && !menuOpen && _back != null && _back.WasPerformedThisFrame()) StopEscort();
            if (Escorting != null && inCar != null) StopEscort();

            // Rear-door prompts on parked cruisers.
            foreach (var kv in _seatPoints)
            {
                Car2D car = kv.Key;
                if (car == null) continue;
                bool parked = !car.IsPlayerDriven && !car.IsAIDriven && Mathf.Abs(car.ForwardSpeed) < 0.5f;
                int n = PrisonersIn(car);
                bool put = Escorting != null && n < Seats, take = Escorting == null && n > 0;
                kv.Value.enabled = parked && inCar == null && (put || take);
                kv.Value.Prompt = put ? "Put suspect in back seat" : "Take suspect out of back seat";
            }

            // Booking at the precinct.
            BookingPrompt = null;
            if (inCar != null && _precinct.HasValue && PrisonersIn(inCar) > 0 && !menuOpen &&
                Vector2.Distance(inCar.transform.position, _precinct.Value) < BookingRange && Mathf.Abs(inCar.ForwardSpeed) < 1f)
            {
                BookingPrompt = $"Book prisoner at {_precinctName} ({PrisonersIn(inCar)} in back)";
                if (_interact != null && _interact.WasPerformedThisFrame()) OpenBooking(inCar);
            }
        }

        private void Prune()
        {
            List<Ped2D> dead = null;
            foreach (var kv in _custody) if (kv.Key == null) (dead ??= new List<Ped2D>()).Add(kv.Key);
            if (dead != null) foreach (var d in dead) _custody.Remove(d);
            if (Escorting != null && Escorting.Ped == null) Escorting = null;

            // The car holding prisoners was destroyed (e.g. swapped at the garage): put them back on their feet, still cuffed.
            foreach (var c in _custody.Values)
            {
                if (ReferenceEquals(c.Car, null) || c.Car != null) continue;
                c.Car = null;
                if (_player != null) c.Ped.transform.position = (Vector2)_player.Transform.position + Vector2.right * 1.5f;
                c.Ped.gameObject.SetActive(true);
                c.Ped.StopEscort();
                if (c.Ped.TryGetComponent(out Interactable2D it)) it.enabled = true;
            }
        }

        // ------------------------------------------------------------------ booking

        private void OpenBooking(Car2D car)
        {
            Custody c = null;
            foreach (var x in _custody.Values) if (x.Car == car) { c = x; break; }
            if (c == null) return;

            Citizen citizen = Db != null && !string.IsNullOrEmpty(c.CitizenId) ? Db.GetCitizen(c.CitizenId) : null;
            if (citizen != null)
                foreach (var w in citizen.Warrants)
                {
                    string ch = $"Warrant: {w.Offense}";
                    if (!c.Charges.Contains(ch)) { c.Charges.Add(ch); c.Selected.Add(ch); }
                }

            // Booking happens on the police computer (Booking tab); the old pop-up menu is only a fallback.
            if (Services.TryGet(out IPoliceComputerService mdt)) { mdt.OpenTo(MdtPages.Booking); return; }

            var menu = InteractionMenu2D.Instance;
            menu.Open($"Booking — {Name(c)}", $"{_precinctName} · arrested at {c.Location}", () =>
            {
                var list = new List<Option>();
                var all = new List<string>(c.Charges);
                foreach (string extra in ExtraCharges) if (!all.Contains(extra)) all.Add(extra);
                foreach (string ch in all)
                {
                    string charge = ch;
                    bool on = c.Selected.Contains(charge);
                    list.Add(new Option($"{(on ? "[x]" : "[  ]")} {charge}", () => { if (!c.Selected.Remove(charge)) c.Selected.Add(charge); }));
                }
                list.Add(new Option($"Book with {c.Selected.Count} charge(s)", () => Book(c), c.Selected.Count > 0));
                list.Add(new Option("Release without charges", () => ReleaseAtPrecinct(c)));
                list.Add(new Option("Cancel", () => menu.Close()));
                return list;
            });
            if (citizen != null)
            {
                menu.Say($"DOB {citizen.DateOfBirth} · {citizen.Address}");
                menu.Say(citizen.Warrants.Count > 0 ? $"<color=#ff6b5b>{citizen.Warrants.Count} active warrant(s)</color>" : "No active warrants.");
            }
            else menu.Say("<i>No record on file — booked as John/Jane Doe.</i>");
        }

        private void Book(Custody c)
        {
            var charges = new List<string>(c.Selected);
            if (Db != null && !string.IsNullOrEmpty(c.CitizenId)) Db.AddArrest(c.CitizenId, charges, c.Location, _officerName);
            Radio?.Transmit(TransmissionPriority.Routine, $"{Unit}, prisoner booked at {_precinctName}, {charges.Count} charge(s).");
            Booked++;
            GameEvents.Publish(new ArrestBookedEvent(c.CitizenId, charges));
            if (InteractionMenu2D.Instance != null && InteractionMenu2D.Instance.IsOpen) InteractionMenu2D.Instance.Close();
            _custody.Remove(c.Ped);
            if (c.Ped != null) Destroy(c.Ped.gameObject);
        }

        private void ReleaseAtPrecinct(Custody c)
        {
            if (InteractionMenu2D.Instance != null && InteractionMenu2D.Instance.IsOpen) InteractionMenu2D.Instance.Close();
            _custody.Remove(c.Ped);
            if (c.Ped == null) return;
            if (c.Ped.TryGetComponent(out Interactable2D it)) { it.ClearHandlers(); it.enabled = false; }
            c.Ped.transform.position = _precinct ?? (Vector2)c.Ped.transform.position;
            c.Ped.gameObject.SetActive(true);
            c.Ped.Release();
        }

        // ------------------------------------------------------------------ booking desk (MDT Booking tab)

        private readonly List<BookingCase> _pending = new List<BookingCase>();

        private bool AtPrecinct(Car2D car) =>
            car != null && _precinct.HasValue && Vector2.Distance(car.transform.position, _precinct.Value) < BookingRange && Mathf.Abs(car.ForwardSpeed) < 1f;

        public IReadOnlyList<BookingCase> Pending
        {
            get
            {
                _pending.Clear();
                Prune();
                foreach (Custody c in _custody.Values)
                    if (AtPrecinct(c.Car)) _pending.Add(CaseFor(c));
                return _pending;
            }
        }

        private BookingCase CaseFor(Custody c)
        {
            Citizen citizen = Db != null && !string.IsNullOrEmpty(c.CitizenId) ? Db.GetCitizen(c.CitizenId) : null;
            if (citizen != null)
                foreach (var w in citizen.Warrants)
                {
                    string ch = $"Warrant: {w.Offense}";
                    if (!c.Charges.Contains(ch)) { c.Charges.Add(ch); c.Selected.Add(ch); }
                }
            var b = c.Booking ??= new BookingCase { Id = c.Ped != null ? c.Ped.GetHashCode().ToString() : Guid.NewGuid().ToString("N") };
            b.Name = Name(c);
            b.CitizenId = c.CitizenId;
            b.ArrestLocation = c.Location;
            b.Notes = citizen != null
                ? $"DOB {citizen.DateOfBirth} · {citizen.Address}\n" + (citizen.Warrants.Count > 0 ? $"<color=#ff6b5b>{citizen.Warrants.Count} active warrant(s)</color>" : "No active warrants.")
                : "<i>No record on file — booked as John/Jane Doe.</i>";
            b.Charges.Clear();
            b.Charges.AddRange(c.Charges);
            foreach (string extra in ExtraCharges) if (!b.Charges.Contains(extra)) b.Charges.Add(extra);
            b.Selected.Clear();
            foreach (string s in c.Selected) b.Selected.Add(s);
            return b;
        }

        private Custody CustodyFor(BookingCase booking)
        {
            foreach (Custody c in _custody.Values) if (c.Booking == booking) return c;
            return null;
        }

        public void ToggleCharge(BookingCase booking, string charge)
        {
            Custody c = CustodyFor(booking);
            if (c == null) return;
            if (!c.Selected.Remove(charge)) c.Selected.Add(charge);
            CaseFor(c);
        }

        public bool Book(BookingCase booking)
        {
            Custody c = CustodyFor(booking);
            if (c == null || c.Selected.Count == 0) return false;
            Book(c);
            return true;
        }

        public void Release(BookingCase booking)
        {
            Custody c = CustodyFor(booking);
            if (c != null) ReleaseAtPrecinct(c);
        }

        // ------------------------------------------------------------------ save game (ICustodySave)

        public List<SavedPrisoner> Snapshot()
        {
            Prune();
            var list = new List<SavedPrisoner>();
            foreach (Custody c in _custody.Values)
            {
                if (c.Ped == null) continue;
                bool seated = c.Car != null;
                Vector2 at = seated ? (Vector2)c.Car.transform.position : (Vector2)c.Ped.transform.position;
                var p = new SavedPrisoner { citizenId = c.CitizenId, location = c.Location, x = at.x, y = at.y, inCar = seated };
                p.charges.AddRange(c.Charges);
                p.selected.AddRange(c.Selected);
                list.Add(p);
            }
            return list;
        }

        public void Restore(List<SavedPrisoner> prisoners, Vector2 carNear)
        {
            // Drop whoever is in custody right now (loading mid-session must not duplicate them).
            StopEscort();
            foreach (Custody c in _custody.Values) if (c.Ped != null) Destroy(c.Ped.gameObject);
            _custody.Clear();
            Escorting = null;
            if (prisoners == null || prisoners.Count == 0) return;

            var sprites = WorldSprites2D.Instance;
            if (sprites == null) return;
            Car2D car = NearestCruiser(carNear);
            if (car != null) AddSeatPoint(car);

            foreach (SavedPrisoner p in prisoners)
            {
                Citizen citizen = Db != null && !string.IsNullOrEmpty(p.citizenId) ? Db.GetCitizen(p.citizenId) : null;
                int look = citizen != null && citizen.LookIndex < sprites.Civilians.Length ? citizen.LookIndex : -1;
                Sprite sprite = look >= 0 ? sprites.Civilians[look] : null;
                Ped2D ped = sprites.SpawnPed(new Vector2(p.x, p.y), 0f, sprite, look);
                if (ped == null) continue;
                ped.name = "Prisoner";
                ped.CitizenId = p.citizenId;
                TakeIntoCustody(ped, p.charges);
                if (!_custody.TryGetValue(ped, out Custody c)) continue;
                if (!string.IsNullOrEmpty(p.location)) c.Location = p.location;
                c.Selected.Clear();
                foreach (string s in p.selected) c.Selected.Add(s);

                if (p.inCar && car != null && PrisonersIn(car) < Seats)
                {
                    c.Car = car;
                    ped.gameObject.SetActive(false);   // back in the back seat
                }
                else if (p.inCar && car != null)
                    ped.transform.position = car.transform.TransformPoint(new Vector3(2.6f, -0.8f, 0f));   // no seat left: standing by the car
            }
        }

        private static Car2D NearestCruiser(Vector2 near)
        {
            Car2D best = null; float bestD = float.MaxValue;
            foreach (var trunk in FindObjectsByType<CruiserTrunk2D>(FindObjectsInactive.Exclude))
            {
                if (!trunk.TryGetComponent(out Car2D c)) continue;
                float d = Vector2.Distance(c.transform.position, near);
                if (d < bestD) { bestD = d; best = c; }
            }
            return best;
        }

        // ------------------------------------------------------------------ helpers

        private string Name(Custody c)
        {
            Citizen citizen = Db != null && !string.IsNullOrEmpty(c.CitizenId) ? Db.GetCitizen(c.CitizenId) : null;
            return citizen != null ? citizen.FullName : "unidentified suspect";
        }

        private static string Locate(Vector2 p) => Services.TryGet(out ILocationService loc) ? loc.Describe(p) : $"{p.x:0}, {p.y:0}";
    }

    /// <summary>Published when a prisoner is booked at the precinct.</summary>
    public readonly struct ArrestBookedEvent
    {
        public readonly string CitizenId;
        public readonly IReadOnlyList<string> Charges;
        public ArrestBookedEvent(string citizenId, IReadOnlyList<string> charges) { CitizenId = citizenId; Charges = charges; }
    }
}

using System.Collections.Generic;
using System.Linq;
using GPA.Callouts;
using GPA.Core;
using GPA.TopDown2D;
using UnityEngine;

namespace GPA.Game
{
    /// <summary>
    /// The off-duty side of New Halden (GTA Online style, cop focused):
    /// - Apartments (Harbor Point, Midtown, Parkside, Linden Heights) and houses in Westbrook for
    ///   sale. Owned homes can be set as the spawn point, slept in, and have a personal garage.
    /// - Halden Motors, a car dealership in Midtown selling every civilian model.
    /// - Harbor Lights Casino in Harbor Point (slots and blackjack, see <see cref="Casino2D"/>).
    /// - Halden Threads, a clothing store in Parkside (hats and shirts, see <see cref="Clothing"/>).
    /// - Every owned property pays rental income each morning at 8:00, and adds garage spaces
    ///   (apartment 2, penthouse 3, house 4, cabin 2, mansion 8, plus 1 street spot) so you can keep several cars.
    /// - Country estates outside the city: Halden Hills mansions and North Woods cabins.
    /// Each place has a marker on the pavement by its front door and an icon on the minimap.
    /// </summary>
    public sealed class OffDutyWorld : MonoBehaviour, IMapMarkerProvider
    {
        public sealed class Property
        {
            public CityLocationDatabase.Building Building;
            public string Title;       // "Harbor Point penthouse"
            public bool House;
            public string Kind = "apartment";   // apartment, house, mansion, cabin
            public int Price;
            public Vector2 Marker;
            public SpriteRenderer Sign;
        }

        public static OffDutyWorld Instance { get; private set; }
        public readonly List<Property> Properties = new List<Property>();
        public Vector2 DealershipPoint { get; private set; }
        public Vector2 CasinoPoint { get; private set; }
        public Vector2 ClothingPoint { get; private set; }
        private CityLocationDatabase _db;
        private readonly Dictionary<string, Car2D> _out = new Dictionary<string, Car2D>();   // personal cars on the street
        private readonly List<string> _outOrder = new List<string>();
        private ITimeOfDay _clock;
        public const int MaxCarsOut = 3;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAnyObjectByType<OffDutyWorld>() != null || FindAnyObjectByType<Officer2D>() == null || FindAnyObjectByType<CityLocations>() == null) return;
            new GameObject("Off-Duty World").AddComponent<OffDutyWorld>();
        }

        private void Awake() => Instance = this;
        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            MapMarkers.Unregister(this);
            if (_clock != null) _clock.HourChanged -= OnHour;
        }

        private void Start()
        {
            _db = FindAnyObjectByType<CityLocations>()?.Database;
            if (_db == null) { enabled = false; return; }
            PickProperties();
            PickBusinesses();
            MapMarkers.Register(this);
            if (Services.TryGet(out _clock)) _clock.HourChanged += OnHour;
        }

        // ------------------------------------------------------------------ placing things

        private static bool Enterable(CityLocationDatabase.Building b) => b.Footprint.width >= 6f && b.Footprint.height >= 6f;

        private void PickProperties()
        {
            // One apartment per downtown/uptown district, priced by neighbourhood.
            var apartments = new (string district, string title, int price)[]
            {
                ("Harbor Point", "Harbor Point penthouse", 48000),
                ("Midtown", "Midtown loft", 32000),
                ("Parkside", "Parkside apartment", 24000),
                ("Linden Heights", "Linden Heights studio", 15000),
            };
            foreach (var (district, title, price) in apartments)
            {
                var b = _db.Buildings.Where(x => x.Type == "apartment" && x.District == district && Enterable(x)).OrderBy(x => x.Id).Skip(1).FirstOrDefault()
                     ?? _db.Buildings.FirstOrDefault(x => x.Type == "apartment" && x.District == district && Enterable(x));
                if (b != null) Add(b, title, false, price);
            }
            // Four Westbrook houses spread across the suburb, priced by plot size.
            var houses = _db.Buildings.Where(x => x.District == "Westbrook" && x.Name == "House" && Enterable(x)).OrderBy(x => x.Id).ToList();
            for (int i = 0; i < 4 && houses.Count > 0; i++)
            {
                var b = houses[(i * houses.Count) / 4 + houses.Count / 8];
                int price = 60000 + Mathf.RoundToInt(b.Footprint.width * b.Footprint.height / 20f) * 2500;
                Add(b, $"Westbrook house, {b.Address}", true, Mathf.Min(price, 150000));
            }
            // Country estates (built by the city builder): Halden Hills mansions along the connector roads,
            // North Woods cabins in clearings off the beltway.
            int m = 0;
            foreach (var b in _db.Buildings.Where(x => x.Name == "Mansion" && Enterable(x)).OrderBy(x => x.Id))
                Add(b, $"Halden Hills mansion, {b.Address}", true, 300000 + (m++ * 5 % 6) * 50000, "mansion");
            int c = 0;
            foreach (var b in _db.Buildings.Where(x => x.Name == "Cabin" && Enterable(x)).OrderBy(x => x.Id))
                Add(b, $"North Woods cabin, {b.Address}", true, 30000 + (c++ * 2 % 5) * 10000, "cabin");
        }

        private void Add(CityLocationDatabase.Building b, string title, bool house, int price, string kind = null)
        {
            if (Properties.Any(p => p.Building.Id == b.Id)) return;
            var p = new Property { Building = b, Title = title, House = house, Price = price, Kind = kind ?? (house ? "house" : "apartment") };
            p.Marker = BesideDoor(b, 2.6f);
            p.Sign = MakeMarker(p.Marker, new Color(0.25f, 0.8f, 0.4f), "home");
            var use = p.Sign.gameObject.AddComponent<Interactable2D>();
            use.Range = 2.2f;
            use.Used += _ => OpenProperty(p);
            Properties.Add(p);
            RefreshPrompt(p);
        }

        private void RefreshPrompt(Property p)
        {
            var use = p.Sign.GetComponent<Interactable2D>();
            bool owned = PlayerLife.Instance != null && PlayerLife.Instance.OwnsProperty(p.Building.Id);
            use.Prompt = owned ? $"Your home: {p.Title}" : $"For sale: {p.Title} — ${p.Price:N0}";
            p.Sign.color = owned ? new Color(0.25f, 0.55f, 0.95f) : new Color(0.25f, 0.8f, 0.4f);
        }

        private void PickBusinesses()
        {
            var dealer = _db.Buildings.Where(x => x.District == "Midtown" && (x.Type == "shop" || x.Type == "restaurant") && Enterable(x))
                                      .OrderByDescending(x => x.Footprint.width * x.Footprint.height).FirstOrDefault();
            if (dealer != null)
            {
                DealershipPoint = BesideDoor(dealer, 2.6f);
                var s = MakeMarker(DealershipPoint, new Color(0.95f, 0.75f, 0.2f), "car");
                var use = s.gameObject.AddComponent<Interactable2D>();
                use.Prompt = "Halden Motors — buy a car"; use.Range = 2.2f;
                use.Used += _ => OpenDealership();
            }
            var casino = _db.Buildings.Where(x => x.District == "Harbor Point" && x.Type == "office" && Enterable(x))
                                      .OrderByDescending(x => x.Footprint.width * x.Footprint.height).FirstOrDefault();
            if (casino != null)
            {
                CasinoPoint = BesideDoor(casino, 2.6f);
                var s = MakeMarker(CasinoPoint, new Color(0.75f, 0.35f, 0.95f), "casino");
                var use = s.gameObject.AddComponent<Interactable2D>();
                use.Prompt = "Harbor Lights Casino"; use.Range = 2.2f;
                use.Used += _ => OpenCasino();
            }
            var threads = _db.Buildings.Where(x => x.District == "Parkside" && (x.Type == "shop" || x.Type == "restaurant") && Enterable(x) && x != dealer)
                                       .OrderByDescending(x => x.Footprint.width * x.Footprint.height).FirstOrDefault()
                       ?? _db.Buildings.Where(x => x.Type == "shop" && Enterable(x) && x != dealer && x != casino).OrderBy(x => x.Id).FirstOrDefault();
            if (threads != null)
            {
                ClothingPoint = BesideDoor(threads, 2.6f);
                var s = MakeMarker(ClothingPoint, new Color(0.95f, 0.4f, 0.6f), "clothes");
                var use = s.gameObject.AddComponent<Interactable2D>();
                use.Prompt = "Halden Threads — hats & shirts"; use.Range = 2.2f;
                use.Used += _ => Clothing.Open(true);
            }
        }

        /// <summary>A pavement spot just outside the front door, a little to one side of it.</summary>
        private static Vector2 BesideDoor(CityLocationDatabase.Building b, float side)
        {
            Vector2 door = InteriorManager2D.DoorPoint(b);
            Vector2 outward = (door - b.Footprint.center);
            outward = Mathf.Abs(outward.x / b.Footprint.width) > Mathf.Abs(outward.y / b.Footprint.height) ? new Vector2(Mathf.Sign(outward.x), 0) : new Vector2(0, Mathf.Sign(outward.y));
            Vector2 along = new Vector2(-outward.y, outward.x);
            return door + outward * 1.6f + along * side;
        }

        private SpriteRenderer MakeMarker(Vector2 at, Color colour, string kind)
        {
            var go = new GameObject("Off-duty marker " + kind);
            go.transform.SetParent(transform, false);
            go.transform.position = at;
            go.transform.localScale = new Vector3(1.4f, 1.4f, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = BuildingInterior2D.Pixel;
            sr.color = colour;
            Sorting2D.Apply(sr, Sorting2D.Props, 30);
            var inner = new GameObject("icon");
            inner.transform.SetParent(go.transform, false);
            inner.transform.localScale = kind == "car" ? new Vector3(0.6f, 0.3f, 1f) : kind == "casino" ? new Vector3(0.4f, 0.4f, 1f)
                                       : kind == "clothes" ? new Vector3(0.55f, 0.4f, 1f) : new Vector3(0.45f, 0.45f, 1f);
            if (kind == "casino") inner.transform.localRotation = Quaternion.Euler(0, 0, 45f);
            var i = inner.AddComponent<SpriteRenderer>();
            i.sprite = BuildingInterior2D.Pixel; i.color = Color.white;
            Sorting2D.Apply(i, Sorting2D.Props, 31);
            return sr;
        }

        // ------------------------------------------------------------------ menus

        private static PlayerLife Life => PlayerLife.Instance;
        private static bool OnDuty => CalloutManager.Instance != null && CalloutManager.Instance.OnDuty;

        private void OpenProperty(Property p)
        {
            var menu = LifeMenu.Get();
            if (Life == null) return;
            if (!Life.OwnsProperty(p.Building.Id))
            {
                menu.Show(p.Title, $"{p.Building.Address} · {Describe(p)} · ${p.Price:N0} · earns ${DailyIncome(p):N0}/day · {SpacesOf(p)}-car garage", () => new List<LifeMenu.Row>
                {
                    new LifeMenu.Row($"Buy for ${p.Price:N0}", Life.CanAfford(p.Price) ? "" : $"need ${p.Price - Life.Money:N0} more", () =>
                    {
                        if (!Life.Spend(p.Price, p.Title)) { menu.Message("Not enough money."); return; }
                        Life.AddProperty(p.Building.Id);
                        RefreshPrompt(p);
                        OpenProperty(p);
                        menu.Message("Purchased! It's yours. Your first home becomes your spawn point.");
                    }, Life.CanAfford(p.Price)),
                    new LifeMenu.Row("Close", "", menu.Close),
                });
                return;
            }
            menu.Show(p.Title, p.Building.Address + (Life.Data.home == p.Building.Id ? " · your spawn point" : ""), () =>
            {
                var rows = new List<LifeMenu.Row>
                {
                    new LifeMenu.Row("Set as spawn point", Life.Data.home == p.Building.Id ? "current" : "", () => Life.SetHome(p.Building.Id), Life.Data.home != p.Building.Id),
                    new LifeMenu.Row("Sleep until morning", OnDuty ? "go off duty first" : "skips to 8:00", () => { Sleep(); menu.Close(); }, !OnDuty),
                    new LifeMenu.Row("Personal garage", Life.Data.cars.Count == 0 ? "no cars yet — visit Halden Motors" : $"{Life.Data.cars.Count} of {GarageSpaces} spaces used", () => OpenGarage(p.Marker), Life.Data.cars.Count > 0),
                    new LifeMenu.Row("Wardrobe", "hats & shirts you own", () => Clothing.Open(false)),
                    new LifeMenu.Row("Rental income", $"${DailyIncome(p):N0} every morning · all homes ${TotalDailyIncome():N0}/day", () => { }, false),
                    new LifeMenu.Row("Close", "", menu.Close),
                };
                return rows;
            });
        }

        private static void Sleep()
        {
            if (!Services.TryGet(out ITimeOfDay clock)) return;
            float h = clock.Hour;
            clock.SetTime(8f);
            Life?.Toast(h < 8f ? "Slept until 8:00" : "Slept until 8:00 the next day", Color.white);
        }

        private void OpenGarage(Vector2 near)
        {
            var menu = LifeMenu.Get();
            menu.Show("Personal garage", $"{Life.Data.cars.Count} of {GarageSpaces} spaces used · up to {MaxCarsOut} cars out at once.", () =>
            {
                var rows = new List<LifeMenu.Row>();
                foreach (string id in Life.Data.cars)
                {
                    var spec = VehicleCatalog.Instance != null ? VehicleCatalog.Instance.Get(id) : null;
                    if (spec == null) continue;
                    bool isOut = IsOut(id);
                    rows.Add(new LifeMenu.Row(spec.DisplayName, isOut ? "on the street · bring it here" : "in garage · bring out", () => { SpawnPersonal(spec, near); menu.Close(); }));
                }
                if (_out.Count > 0) rows.Add(new LifeMenu.Row("Put all cars away", $"{CountOut()} out", () => { StoreAll(); menu.Message("All your cars are back in the garage."); }));
                rows.Add(new LifeMenu.Row("Close", "", menu.Close));
                return rows;
            });
        }

        public static int PriceOf(VehicleSpec s) => Mathf.Clamp(Mathf.RoundToInt((s.TopSpeed * 160f + s.Mass * 6f) / 500f) * 500, 4000, 60000);

        private void OpenDealership()
        {
            var menu = LifeMenu.Get();
            if (Life == null || VehicleCatalog.Instance == null) return;
            var stock = VehicleCatalog.Instance.Civilian().Where(s => s.Sprites != null && s.Sprites.Length > 0).OrderBy(PriceOf).ToList();
            menu.Show("Halden Motors", $"Garage: {Life.Data.cars.Count} of {GarageSpaces} spaces used. Every home you buy adds spaces.", () =>
            {
                var rows = new List<LifeMenu.Row>();
                foreach (var spec in stock)
                {
                    int price = PriceOf(spec);
                    if (Life.OwnsCar(spec.Id))
                        rows.Add(new LifeMenu.Row(spec.DisplayName, "owned · take it out", () => { SpawnPersonal(spec, DealershipPoint); menu.Close(); }));
                    else if (Life.Data.cars.Count >= GarageSpaces)
                        rows.Add(new LifeMenu.Row(spec.DisplayName, $"${price:N0} · garage full", () => { }, false));
                    else
                        rows.Add(new LifeMenu.Row(spec.DisplayName, $"${price:N0}", () =>
                        {
                            if (!Life.Spend(price, spec.DisplayName)) { menu.Message("Not enough money."); return; }
                            Life.AddCar(spec.Id);
                            SpawnPersonal(spec, DealershipPoint);
                            menu.Close();
                        }, Life.CanAfford(price)));
                }
                rows.Add(new LifeMenu.Row("Close", "", menu.Close));
                return rows;
            });
        }

        private void OpenCasino()
        {
            var menu = LifeMenu.Get();
            menu.Show("Harbor Lights Casino", OnDuty ? "Not while you're on duty, officer." : "Minimum bet $50. Please gamble responsibly.", () => new List<LifeMenu.Row>
            {
                new LifeMenu.Row("Slots", "3 reels", () => { menu.Close(); Casino2D.Get().OpenSlots(); }, !OnDuty),
                new LifeMenu.Row("Blackjack", "dealer stands on 17, blackjack pays 3:2", () => { menu.Close(); Casino2D.Get().OpenBlackjack(); }, !OnDuty),
                new LifeMenu.Row("Leave", "", menu.Close),
            });
        }

        /// <summary>
        /// Puts one of your cars on the nearest street lane. Several can be out at once (up to
        /// <see cref="MaxCarsOut"/>; the longest-parked one goes back to the garage). Asking for a car
        /// that is already out brings it to you.
        /// </summary>
        private void SpawnPersonal(VehicleSpec spec, Vector2 near)
        {
            var garage = FleetGarage2D.Instance;
            if (garage == null) return;
            Store(spec.Id);
            Prune();
            while (_outOrder.Count >= MaxCarsOut)
            {
                string oldest = _outOrder.FirstOrDefault(id => !Driving(_out[id])) ?? _outOrder[0];
                Store(oldest);
            }
            var car = garage.SpawnPersonal(spec, near);
            if (car == null) return;
            _out[spec.Id] = car;
            _outOrder.Add(spec.Id);
            Life?.Toast($"{spec.DisplayName} is waiting on the street", Color.white);
        }

        /// <summary>Phone mechanic: brings one of your cars to the street near <paramref name="near"/>.</summary>
        public bool DeliverCar(string id, Vector2 near)
        {
            var spec = VehicleCatalog.Instance != null ? VehicleCatalog.Instance.Get(id) : null;
            if (spec == null || Life == null || !Life.OwnsCar(id)) return false;
            SpawnPersonal(spec, near);
            return true;
        }

        public bool CarIsOut(string id) => IsOut(id);

        private static bool Driving(Car2D car)
        {
            var officer = FindAnyObjectByType<Officer2D>();
            return car != null && officer != null && officer.CurrentCar == car;
        }

        private void Prune()
        {
            for (int i = _outOrder.Count - 1; i >= 0; i--)
                if (!_out.TryGetValue(_outOrder[i], out var c) || c == null) { _out.Remove(_outOrder[i]); _outOrder.RemoveAt(i); }
        }

        private bool IsOut(string id) { Prune(); return _out.ContainsKey(id); }
        private int CountOut() { Prune(); return _out.Count; }

        /// <summary>Sends a car back to the garage (gets you out first if you're in it).</summary>
        private void Store(string id)
        {
            if (!_out.TryGetValue(id, out var car)) return;
            _out.Remove(id); _outOrder.Remove(id);
            if (car == null) return;
            var officer = FindAnyObjectByType<Officer2D>();
            if (officer != null && officer.CurrentCar == car) officer.ExitVehicle(car.transform.position + car.transform.right * 1.8f, car.transform.rotation);
            Destroy(car.gameObject);
        }

        private void StoreAll() { foreach (var id in _out.Keys.ToList()) Store(id); }

        // ------------------------------------------------------------------ garage spaces and income

        public static int SpacesOf(Property p) =>
            p.Kind == "mansion" ? 8 : p.Kind == "cabin" ? 2 : p.House ? 4 : p.Price >= 40000 ? 3 : 2;

        private static string Describe(Property p) =>
            p.Kind == "mansion" ? "country mansion with a pool" : p.Kind == "cabin" ? "log cabin in the woods"
            : p.House ? "detached house with a lawn" : "apartment";

        /// <summary>One street spot plus the garages of every home you own.</summary>
        public int GarageSpaces => 1 + Properties.Where(p => Life != null && Life.OwnsProperty(p.Building.Id)).Sum(SpacesOf);

        /// <summary>Rent / investment return: 2 % of the price per in-game day, rounded to $10.</summary>
        public static int DailyIncome(Property p) => Mathf.RoundToInt(p.Price * 0.02f / 10f) * 10;

        public int TotalDailyIncome() => Properties.Where(p => Life != null && Life.OwnsProperty(p.Building.Id)).Sum(DailyIncome);

        private void OnHour(int hour)
        {
            if (hour != 8 || Life == null) return;
            int total = TotalDailyIncome();
            if (total <= 0) return;
            Life.Data.rentEarned += total;
            Life.Earn(total, "Property income");
        }

        // ------------------------------------------------------------------ minimap

        public void CollectMarkers(List<MapMarker> into)
        {
            if (Life == null) return;
            foreach (var p in Properties)
                into.Add(new MapMarker(p.Marker, Life.OwnsProperty(p.Building.Id) ? MapMarkerKind.Home : MapMarkerKind.ForSale, p.Title));
            if (DealershipPoint != Vector2.zero) into.Add(new MapMarker(DealershipPoint, MapMarkerKind.Shop, "Halden Motors"));
            if (CasinoPoint != Vector2.zero) into.Add(new MapMarker(CasinoPoint, MapMarkerKind.Casino, "Casino"));
            if (ClothingPoint != Vector2.zero) into.Add(new MapMarker(ClothingPoint, MapMarkerKind.Shop, "Halden Threads"));
        }

        /// <summary>Where to wake up: outside the spawn home, or null for the precinct.</summary>
        public Vector2? HomeSpawn()
        {
            if (Life == null || string.IsNullOrEmpty(Life.Data.home)) return null;
            var p = Properties.FirstOrDefault(x => x.Building.Id == Life.Data.home);
            return p != null ? p.Marker + (InteriorManager2D.DoorPoint(p.Building) - p.Marker).normalized * 0.5f : (Vector2?)null;
        }
    }
}

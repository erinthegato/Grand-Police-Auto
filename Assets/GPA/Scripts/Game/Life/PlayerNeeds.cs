using System.Collections.Generic;
using System.Linq;
using GPA.Callouts;
using GPA.Core;
using GPA.TopDown2D;
using UnityEngine;

namespace GPA.Game
{
    /// <summary>
    /// Officer health (no survival needs):
    /// - Health is only lost when something hurts you on foot (you can't be hurt inside a car).
    ///   At zero you're taken to Halden General (medical bill).
    /// - Eating is how you heal: diners and corner stores sell meals and snacks that restore health.
    /// - Sleeping only passes time (handled by the home menu). Hunger and tiredness no longer exist;
    ///   the old energy/food values stay in the save file but do nothing.
    /// The health bar sits bottom-left.
    /// </summary>
    public sealed class PlayerNeeds : MonoBehaviour, IMapMarkerProvider
    {
        public static PlayerNeeds Instance { get; private set; }

        private Officer2D _officer;
        private float _saveTimer, _hurtFlash;
        private Vector2 _lastFootVel;
        private bool _footReady;
        private bool _hospitalised;
        private CityLocationDatabase.Building _hospital;
        private Vector2 _hospitalDoor;
        /// <summary>Pavement outside the hospital entrance (null if there is no hospital).</summary>
        public Vector2? HospitalPoint => _hospital != null ? _hospitalDoor + (_hospitalDoor - _hospital.Footprint.center).normalized * 1.8f : (Vector2?)null;
        private GUIStyle _label, _big;

        private static PlayerLife Life => PlayerLife.Instance;
        public float Health => Life != null ? Life.Data.health : 100f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAnyObjectByType<PlayerNeeds>() != null || FindAnyObjectByType<Officer2D>() == null) return;
            new GameObject("Player Needs").AddComponent<PlayerNeeds>();
        }

        private void Awake() { Instance = this; Officer2D.Damaged += OnDamaged; }
        private void OnDestroy() { if (Instance == this) Instance = null; Officer2D.Damaged -= OnDamaged; MapMarkers.Unregister(this); }

        private void Start()
        {
            _officer = FindAnyObjectByType<Officer2D>();
            var db = FindAnyObjectByType<CityLocations>()?.Database;
            if (db != null) PlaceShops(db);
            MapMarkers.Register(this);
        }

        // ------------------------------------------------------------------ places to eat, hospital

        private void PlaceShops(CityLocationDatabase db)
        {
            foreach (var b in db.Buildings)
            {
                if (b.Footprint.width < 6f || b.Footprint.height < 6f) continue;
                if (b.Type == "restaurant") Shop(b, "Diner", new Color(0.95f, 0.55f, 0.15f), OpenDiner);
                else if (b.Type == "shop" && (b.Id.GetHashCode() & 3) == 0) Shop(b, "Corner store", new Color(0.95f, 0.85f, 0.25f), OpenStore);
            }
            _hospital = db.Buildings.Where(x => x.District == "Midtown" && x.Type == "office" && x.Footprint.width >= 6f && x.Footprint.height >= 6f)
                                    .OrderByDescending(x => x.Footprint.width * x.Footprint.height).FirstOrDefault();
            if (_hospital != null)
            {
                _hospitalDoor = InteriorManager2D.DoorPoint(_hospital);
                var go = new GameObject("Hospital sign");
                go.transform.SetParent(transform, false);
                go.transform.position = _hospital.Footprint.center;
                for (int i = 0; i < 2; i++)
                {
                    var bar = new GameObject("cross").AddComponent<SpriteRenderer>();
                    bar.transform.SetParent(go.transform, false);
                    bar.transform.localScale = i == 0 ? new Vector3(1.4f, 4.5f, 1f) : new Vector3(4.5f, 1.4f, 1f);
                    bar.sprite = BuildingInterior2D.Pixel; bar.color = new Color(0.9f, 0.15f, 0.15f);
                    Sorting2D.Apply(bar, Sorting2D.Roofs, 40);
                }
            }
        }

        private void Shop(CityLocationDatabase.Building b, string kind, Color colour, System.Action<string> open)
        {
            Vector2 door = InteriorManager2D.DoorPoint(b);
            Vector2 outward = door - b.Footprint.center;
            outward = Mathf.Abs(outward.x / b.Footprint.width) > Mathf.Abs(outward.y / b.Footprint.height) ? new Vector2(Mathf.Sign(outward.x), 0) : new Vector2(0, Mathf.Sign(outward.y));
            Vector2 at = door + outward * 1.4f + new Vector2(-outward.y, outward.x) * -2.4f;
            var go = new GameObject(kind + " " + b.Id);
            go.transform.SetParent(transform, false);
            go.transform.position = at;
            go.transform.localScale = new Vector3(0.8f, 0.8f, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = BuildingInterior2D.Pixel; sr.color = colour;
            go.transform.rotation = Quaternion.Euler(0, 0, 45f);
            Sorting2D.Apply(sr, Sorting2D.Props, 30);
            var use = go.AddComponent<Interactable2D>();
            string title = $"{kind} — {b.Address}";
            use.Prompt = $"{kind}: food and drink";
            use.Range = 2f;
            use.Used += _ => open(title);
        }

        private void OpenDiner(string title)
        {
            var menu = LifeMenu.Get();
            menu.Show(title, Status(), () => new List<LifeMenu.Row>
            {
                Item("Hot meal", 18, "+45 health", () => Add(health: 45f)),
                Item("Breakfast special", 12, "+30 health", () => Add(health: 30f)),
                Item("Coffee", 4, "+5 health", () => Add(health: 5f)),
                new LifeMenu.Row("Close", "", menu.Close),
            });
        }

        private void OpenStore(string title)
        {
            var menu = LifeMenu.Get();
            menu.Show(title, Status(), () => new List<LifeMenu.Row>
            {
                Item("Sandwich", 7, "+20 health", () => Add(health: 20f)),
                Item("Snack", 3, "+8 health", () => Add(health: 8f)),
                Item("First-aid kit", 40, "+60 health", () => Add(health: 60f)),
                new LifeMenu.Row("Close", "", menu.Close),
            });
        }

        private LifeMenu.Row Item(string name, int price, string effect, System.Action apply) =>
            new LifeMenu.Row(name, $"${price} · {effect}", () =>
            {
                if (Life == null || !Life.Spend(price, name)) { LifeMenu.Get().Message("Not enough money."); return; }
                apply();
                LifeMenu.Get().Message(Status());
            }, Life != null && Life.CanAfford(price));

        private string Status() => $"Health {Health:0}";

        /// <summary>Changes health (eating heals, injuries hurt).</summary>
        public void Add(float health = 0f)
        {
            if (Life == null) return;
            var d = Life.Data;
            d.health = Mathf.Clamp(d.health + health, 0f, 100f);
            if (health < 0f) _hurtFlash = 1f;
            if (d.health <= 0f && !_hospitalised && isActiveAndEnabled) StartCoroutine(Hospitalise());
        }

        // ------------------------------------------------------------------ simulation

        private void Update()
        {
            if (Life == null) return;
            if (_officer == null) _officer = FindAnyObjectByType<Officer2D>();
            float dtReal = Time.unscaledDeltaTime;
            _hurtFlash = Mathf.Max(0f, _hurtFlash - dtReal * 1.5f);

            if (_officer != null) { _officer.StaminaScale = 1f; _officer.SpeedScale = 1f; }

            _saveTimer += dtReal;
            if (_saveTimer > 30f) { _saveTimer = 0f; Life.Save(); }

            if (!_hospitalised && Health <= 0f) StartCoroutine(Hospitalise());
        }

        // ------------------------------------------------------------------ damage

        private void OnDamaged(DamageInfo info) => Hurt(info.Amount, info.Type == DamageType.Vehicle ? "Hit by a vehicle" : "Injured");

        private void Hurt(float amount, string why)
        {
            if (amount <= 0.5f || _hospitalised) return;
            Add(health: -amount);
            Life?.Toast($"−{amount:0} health  {why}", new Color(1f, 0.4f, 0.4f));
        }

        private void FixedUpdate()
        {
            if (_officer == null || _hospitalised) return;
            // Inside a car you can't be hurt. On foot, being shoved hard (a car hitting you) hurts.
            if (_officer.CurrentCar != null || !_officer.TryGetComponent(out Rigidbody2D rb)) { _footReady = false; return; }
            Vector2 v = rb.linearVelocity;
            if (_footReady)
            {
                float jolt = (v - _lastFootVel).magnitude;
                if (jolt > 7f) Hurt((jolt - 7f) * 4f, "Hit by a vehicle");
            }
            _lastFootVel = v; _footReady = true;
        }

        private System.Collections.IEnumerator Hospitalise()
        {
            _hospitalised = true;
            if (_officer != null) _officer.SetControlLocked(this, true);
            Life.Toast("You collapsed", new Color(1f, 0.4f, 0.4f));
            yield return new WaitForSecondsRealtime(2.5f);

            if (_officer != null && _officer.CurrentCar != null) _officer.ExitVehicle(_officer.CurrentCar.DriverExitPoint, Quaternion.identity);
            var calls = CalloutManager.Instance;
            if (calls != null && calls.OnDuty) calls.SetDuty(false);
            Vector2 at = _hospital != null ? _hospitalDoor + (_hospitalDoor - _hospital.Footprint.center).normalized * 1.8f : (Vector2)_officer.transform.position;
            if (_officer != null)
            {
                _officer.transform.position = at;
                if (_officer.TryGetComponent(out Rigidbody2D rb)) { rb.position = at; rb.linearVelocity = Vector2.zero; }
            }
            Camera2DFollow.SnapAll();
            if (Services.TryGet(out ITimeOfDay clock)) clock.SetTime(clock.Hour + 6f);
            int bill = Mathf.Min(500, Life.Money);
            if (bill > 0) Life.Spend(bill, "Hospital bill");
            Life.Data.health = 60f;
            Life.Save();
            _footReady = false;
            yield return new WaitForSecondsRealtime(1.5f);
            Camera2DFollow.PlayIntro();
            Life.Toast("Discharged from Halden General", Color.white);
            if (_officer != null) _officer.SetControlLocked(this, false);
            _hospitalised = false;
        }

        public void CollectMarkers(List<MapMarker> into)
        {
            if (_hospital != null) into.Add(new MapMarker(_hospitalDoor, MapMarkerKind.Hospital, "Hospital"));
        }

        // ------------------------------------------------------------------ HUD

        private void OnGUI()
        {
            if (Life == null || GameplayHotkeys.Blocked && !_hospitalised) return;
            if (_label == null)
            {
                _label = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } };
                _big = new GUIStyle(GUI.skin.label) { fontSize = 46, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 0.35f, 0.35f) } };
            }
            Matrix4x4 old = UiScale.Begin(out Vector2 canvas);
            float w = canvas.x, h = canvas.y;

            // Screen effects: low-health red edges, damage flash.
            float lowHealth = Mathf.InverseLerp(35f, 0f, Health) * (0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 5f));
            if (lowHealth > 0.01f || _hurtFlash > 0.01f) Edge(new Color(0.75f, 0f, 0f, Mathf.Max(lowHealth * 0.6f, _hurtFlash * 0.7f)), w, h);
            if (_hospitalised) GUI.Label(new Rect(0, h * 0.4f, w, 80), "HOSPITALIZED", _big);

            // Health bar (bottom-left).
            Bar(24f, h - 48f, "HEALTH", Health, new Color(0.9f, 0.25f, 0.25f));
            GUI.matrix = old;
        }

        private void Bar(float x, float y, string label, float value, Color colour)
        {
            const float bw = 170f, bh = 10f;
            GUI.color = new Color(0, 0, 0, 0.55f); GUI.DrawTexture(new Rect(x - 6, y - 4, bw + 92, 24), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(x, y - 4, 80, 24), label, _label);
            var r = new Rect(x + 78, y + 3, bw, bh);
            GUI.color = new Color(1, 1, 1, 0.15f); GUI.DrawTexture(r, Texture2D.whiteTexture);
            bool low = value < 20f && Mathf.Repeat(Time.unscaledTime, 1f) < 0.5f;
            GUI.color = low ? Color.white : colour;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(value / 100f), r.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        private readonly Dictionary<Color, Texture2D> _edges = new Dictionary<Color, Texture2D>();

        /// <summary>A soft coloured border (clear in the middle). The colour is baked into the texture;
        /// GUI.color only sets how strong it is.</summary>
        private void Edge(Color c, float w, float h)
        {
            var key = new Color(c.r, c.g, c.b, 1f);
            if (!_edges.TryGetValue(key, out var tex))
            {
                const int n = 64;
                tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
                for (int yy = 0; yy < n; yy++)
                for (int xx = 0; xx < n; xx++)
                {
                    float d = new Vector2(xx / (n - 1f) - 0.5f, yy / (n - 1f) - 0.5f).magnitude * 2f;
                    tex.SetPixel(xx, yy, new Color(c.r, c.g, c.b, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1.2f, d))));
                }
                tex.Apply();
                _edges[key] = tex;
            }
            GUI.color = new Color(1f, 1f, 1f, c.a);
            GUI.DrawTexture(new Rect(0, 0, w, h), tex);
            GUI.color = Color.white;
        }
    }
}

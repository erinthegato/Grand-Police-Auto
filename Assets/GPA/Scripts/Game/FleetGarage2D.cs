using System.Collections.Generic;
using GPA.Core;
using GPA.TopDown2D;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GPA.Game
{
    /// <summary>
    /// The precinct motor pool. Walk up to the garage marker beside the station and sign out any
    /// fleet vehicle your rank allows: patrol sedan and SUV from day one, then pickup, motorcycle,
    /// unmarked car, prisoner transport, interceptor, BearCat and finally the helicopter.
    /// The signed-out vehicle replaces your previous one and you start in the driver's seat.
    /// </summary>
    public sealed class FleetGarage2D : MonoBehaviour
    {
        public static FleetGarage2D Instance { get; private set; }

        /// <summary>The fleet vehicle the officer currently has signed out.</summary>
        public Car2D Current { get; private set; }
        /// <summary>The street spot outside the precinct garage.</summary>
        public Vector2 PrecinctPoint => _garagePoint;
        public string CurrentId { get; private set; } = "patrol_sedan";

        private GameObject _template;
        private Vector2 _garagePoint, _helipad;
        private Officer2D _officer;
        private bool _open;
        private int _row;
        private float _navRepeat;
        private readonly List<VehicleSpec> _fleet = new List<VehicleSpec>();
        private string _message;
        private GUIStyle _title, _row1, _row2, _small, _locked, _tag;
        private Texture2D _dim, _panel, _accent, _hl;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAnyObjectByType<FleetGarage2D>() != null || FindAnyObjectByType<Officer2D>() == null || VehicleCatalog.Instance == null) return;
            new GameObject("Fleet Garage").AddComponent<FleetGarage2D>();
        }

        /// <summary>Where a newly signed-out (ground) vehicle appears instead of the street lane, if set and non-null (e.g. the basement garage).</summary>
        public System.Func<VehicleSpec, (Vector2, float)?> SpawnOverride;

        /// <summary>Opens the sign-out menu (used by the basement vehicle desk).</summary>
        public void OpenMenu() => Open();

        private void Awake() => Instance = this;
        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Start()
        {
            _officer = FindAnyObjectByType<Officer2D>();
            foreach (var spec in VehicleCatalog.Instance.Fleet()) _fleet.Add(spec);
            _fleet.Sort((a, b) => a.RequiredLevel != b.RequiredLevel ? a.RequiredLevel.CompareTo(b.RequiredLevel) : string.CompareOrdinal(a.DisplayName, b.DisplayName));

            // The scene's patrol car is the first fleet vehicle and the template for the rest.
            foreach (var c in FindObjectsByType<Car2D>(FindObjectsInactive.Exclude))
                if (c.IsEmergencyVehicle && c.GetComponent<Lightbar2D>() != null && !c.name.StartsWith("Backup")) { Current = c; break; }
            if (Current != null)
            {
                _template = Instantiate(Current.gameObject);
                _template.name = "Fleet Template";
                _template.SetActive(false);
                DontDestroyOnLoad(_template);
            }

            // Garage marker on the pavement beside the precinct's front door; helipad on its roof.
            var db = FindAnyObjectByType<CityLocations>()?.Database;
            if (db != null)
                foreach (var b in db.Buildings)
                {
                    if (b.Type != "police") continue;
                    Vector2 door = InteriorManager2D.DoorPoint(b);
                    Vector2 outward = (door - b.Footprint.center).normalized;
                    Vector2 along = new Vector2(-outward.y, outward.x);
                    _garagePoint = door + outward * 2.5f + along * 7f;
                    _helipad = b.Footprint.center;
                    break;
                }
            MakeMarker();
        }

        private void MakeMarker()
        {
            var go = new GameObject("Garage Marker");
            go.transform.SetParent(transform, false);
            go.transform.position = _garagePoint;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = BuildingInterior2D.Pixel;
            sr.color = new Color(0.15f, 0.35f, 0.85f, 0.85f);
            go.transform.localScale = new Vector3(1.6f, 1.6f, 1f);
            Sorting2D.Apply(sr, Sorting2D.Props, 30);
            // A white "P" (parking) built from four strokes: stem, top, middle and the bowl's right side.
            var strokes = new (Vector2 pos, Vector2 size)[]
            {
                (new Vector2(-0.12f, 0f), new Vector2(0.1f, 0.6f)),
                (new Vector2(0.0f, 0.25f), new Vector2(0.34f, 0.1f)),
                (new Vector2(0.0f, 0.0f), new Vector2(0.34f, 0.1f)),
                (new Vector2(0.12f, 0.125f), new Vector2(0.1f, 0.35f)),
            };
            foreach (var (pos, size) in strokes)
            {
                var stroke = new GameObject("Garage Marker P");
                stroke.transform.SetParent(go.transform, false);
                stroke.transform.localPosition = pos;
                stroke.transform.localScale = new Vector3(size.x, size.y, 1f);
                var p = stroke.AddComponent<SpriteRenderer>();
                p.sprite = BuildingInterior2D.Pixel; p.color = Color.white;
                Sorting2D.Apply(p, Sorting2D.Props, 31);
            }
            var use = go.AddComponent<Interactable2D>();
            use.Prompt = "Vehicle garage — sign out a fleet vehicle";
            use.Range = 2.6f;
            use.Used += _ => Open();
        }

        // ---- menu -----------------------------------------------------------------------
        private int Level => Services.TryGet(out IReputationService rep) ? rep.Level : 1;

        private void Open()
        {
            if (_open || _officer == null || _officer.CurrentCar != null) return;
            _open = true; _row = Mathf.Max(0, _fleet.FindIndex(s => s.Id == CurrentId)); _message = null;
            _officer.SetControlLocked(this, true);
            if (Services.TryGet(out IGameStateService st)) st.Push(GameState.Menu);
        }

        private void Close()
        {
            _open = false;
            if (_officer != null) _officer.SetControlLocked(this, false);
            if (Services.TryGet(out IGameStateService st)) st.Pop(GameState.Menu);
            GameplayHotkeys.SuppressThisFrame();
        }

        private void Update()
        {
            if (!_open || _fleet.Count == 0) return;
            var kb = Keyboard.current; var pad = Gamepad.current;
            int dir = 0;
            if (kb != null && (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame)) dir = -1;
            if (kb != null && (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame)) dir = 1;
            if (pad != null)
            {
                float y = pad.dpad.ReadValue().y + pad.leftStick.ReadValue().y;
                if (Mathf.Abs(y) < 0.5f) _navRepeat = 0f;
                else if (_navRepeat <= 0f) { dir = y > 0 ? -1 : 1; _navRepeat = 0.22f; }
                _navRepeat -= Time.unscaledDeltaTime;
            }
            if (dir != 0) _row = (_row + dir + _fleet.Count) % _fleet.Count;
            bool confirm = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)) || (pad != null && pad.buttonSouth.wasPressedThisFrame);
            bool back = (kb != null && kb.escapeKey.wasPressedThisFrame) || (pad != null && pad.buttonEast.wasPressedThisFrame);
            if (confirm) Choose(_fleet[_row]);
            else if (back) Close();
        }

        private void Choose(VehicleSpec spec)
        {
            if (spec.RequiredLevel > Level) { _message = $"Locked: needs {ReputationLevels.Ranks[Mathf.Clamp(spec.RequiredLevel - 1, 0, 5)]} (level {spec.RequiredLevel})."; return; }
            Close();
            var spot = spec.Helicopter ? null : SpawnOverride?.Invoke(spec);
            if (spot.HasValue) SignOut(spec, spot.Value.Item1, true, spot.Value.Item2);
            else SignOut(spec, null, true);
        }

        /// <summary>Replaces the current fleet vehicle with a new one of this type.</summary>
        public Car2D SignOut(VehicleSpec spec, Vector2? at, bool enter, float? heading = null)
        {
            if (spec == null || _template == null) return null;
            Vector2 pos; float rot;
            if (at.HasValue) { pos = at.Value; rot = heading ?? 0f; }
            else if (spec.Helicopter) { pos = _helipad; rot = 0f; }
            else NearestLane(_garagePoint, out pos, out rot);

            if (Current != null && (_officer == null || _officer.CurrentCar != Current)) Destroy(Current.gameObject);
            else if (Current != null && _officer != null && _officer.CurrentCar == Current) { _officer.ExitVehicle(_garagePoint, Quaternion.identity); Destroy(Current.gameObject); }

            GameObject go = Instantiate(_template, pos, Quaternion.Euler(0, 0, rot));
            go.name = "Patrol 4127";
            go.SetActive(true);
            var car = go.GetComponent<Car2D>();
            car.SetPlayerDriven(false, null);
            car.ApplySpec(spec);
            Current = car;
            CurrentId = spec.Id;
            if (enter && _officer != null) _officer.EnterVehicle(car, null);
            if (Services.TryGet(out IDispatchRadio radio))
                radio.Transmit(TransmissionPriority.Background, $"{radio.Unit}, signed out {spec.DisplayName}.");
            return car;
        }

        /// <summary>A personal (civilian) car of this model on the street lane nearest <paramref name="near"/>.</summary>
        public Car2D SpawnPersonal(VehicleSpec spec, Vector2 near)
        {
            if (spec == null || _template == null) return null;
            NearestLane(near, out Vector2 pos, out float rot);
            GameObject go = Instantiate(_template, pos, Quaternion.Euler(0, 0, rot));
            go.name = "Personal " + spec.DisplayName;
            foreach (var lb in go.GetComponents<Lightbar2D>()) Destroy(lb);
            foreach (var mb in go.GetComponents<MonoBehaviour>()) if (mb.GetType().Name == "CruiserTrunk2D") Destroy(mb);   // police kit (other assembly)
            go.SetActive(true);
            var car = go.GetComponent<Car2D>();
            car.SetPlayerDriven(false, null);
            car.ApplySpec(spec, spec.Sprites != null && spec.Sprites.Length > 0 ? spec.Sprites[UnityEngine.Random.Range(0, spec.Sprites.Length)] : null);
            return car;
        }

        /// <summary>Save-game restore: swap to the saved vehicle type if it differs.</summary>
        public void Restore(string id)
        {
            if (string.IsNullOrEmpty(id) || id == CurrentId) return;
            var spec = VehicleCatalog.Instance != null ? VehicleCatalog.Instance.Get(id) : null;
            if (spec == null) return;
            Vector2 p = Current != null ? (Vector2)Current.transform.position : _garagePoint;
            SignOut(spec, p, false, Current != null ? Current.transform.eulerAngles.z : 0f);
        }

        private static void NearestLane(Vector2 near, out Vector2 pos, out float heading)
        {
            pos = near; heading = 0f;
            var g = RoadGraph2D.Instance;
            if (g == null) return;
            float best = float.MaxValue;
            foreach (int e in g.LaneEdges())
            {
                var a = g.Nodes[g.Edges[e].From]; var b = g.Nodes[g.Edges[e].To];
                Vector2 mid = (a.Position + b.Position) * 0.5f;
                float d = (mid - near).sqrMagnitude;
                if (d < best) { best = d; pos = mid; heading = a.Heading; }
            }
        }

        // ---- drawing ----------------------------------------------------------------------
        private void OnGUI()
        {
            if (!_open) return;
            Styles();
            GUI.depth = -150;
            Matrix4x4 old = UiScale.Begin(out Vector2 c);
            GUI.DrawTexture(new Rect(0, 0, c.x, c.y), _dim);
            float rowH = 58f, pw = 820f, ph = 150f + _fleet.Count * rowH + 70f;
            ph = Mathf.Min(ph, c.y - 20f);
            var panel = new Rect(c.x * 0.5f - pw * 0.5f, c.y * 0.5f - ph * 0.5f, pw, ph);
            GUI.DrawTexture(panel, _panel);
            GUI.DrawTexture(new Rect(panel.x, panel.y, pw, 5), _accent);
            GUI.Label(new Rect(panel.x + 28, panel.y + 16, pw - 56, 40), "PRECINCT GARAGE", _title);

            string rank = "Probationary Officer"; int pts = 0, next = -1, level = 1;
            if (Services.TryGet(out IReputationService rep)) { rank = rep.Rank; pts = rep.Points; next = rep.NextLevelAt; level = rep.Level; }
            GUI.Label(new Rect(panel.x + 28, panel.y + 58, pw - 56, 26),
                $"{rank} · level {level} · {pts} rep" + (next > 0 ? $" · next rank at {next}" : " · top rank"), _small);
            if (next > 0)
            {
                float t = Mathf.InverseLerp(ReputationLevels.Thresholds[level - 1], next, pts);
                var bar = new Rect(panel.x + 28, panel.y + 90, pw - 56, 6);
                GUI.DrawTexture(bar, _hl);
                GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * t, bar.height), _accent);
            }

            float y = panel.y + 110;
            for (int i = 0; i < _fleet.Count; i++)
            {
                var s = _fleet[i];
                bool locked = s.RequiredLevel > level;
                var r = new Rect(panel.x + 20, y + i * rowH, pw - 40, rowH - 6);
                if (i == _row) GUI.DrawTexture(r, _hl);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { if (_row == i) Choose(s); _row = i; }
                if (s.Sprites.Length > 0 && s.Sprites[0] != null)
                {
                    // The sprites are packed in an atlas: draw just this sprite's part of the texture.
                    Sprite sp = s.Sprites[0];
                    Texture tex = sp.texture;
                    Rect tr = sp.textureRect;
                    var uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);
                    float boxW = 26f, boxH = r.height - 8f;
                    float k = Mathf.Min(boxW / Mathf.Max(1f, tr.width), boxH / Mathf.Max(1f, tr.height));
                    float iw = tr.width * k, ih = tr.height * k;
                    GUI.color = locked ? new Color(1, 1, 1, 0.35f) : Color.white;
                    GUI.DrawTextureWithTexCoords(new Rect(r.x + 14 + (boxW - iw) * 0.5f, r.y + (r.height - ih) * 0.5f, iw, ih), tex, uv);
                    GUI.color = Color.white;
                }
                bool signedOut = s.Id == CurrentId;
                GUI.Label(new Rect(r.x + 50, r.y + (signedOut ? -8f : 0f), 290, r.height), s.DisplayName, locked ? _locked : (i == _row ? _row1 : _row2));
                if (signedOut) GUI.Label(new Rect(r.x + 50, r.y + r.height - 24f, 290, 20), "signed out", _tag);
                GUI.Label(new Rect(r.x + 350, r.y, r.width - 360, r.height),
                    locked ? $"Locked · level {s.RequiredLevel} · {ReputationLevels.Ranks[Mathf.Clamp(s.RequiredLevel - 1, 0, 5)]}"
                           : $"{s.Blurb}\nSpeed {s.SpeedRating} · Accel {s.AccelRating} · Handling {s.HandlingRating} · Brakes {s.BrakeRating}", locked ? _locked : _small);
            }
            string help = _message ?? (Gamepad.current != null ? "A sign out   B close" : "↑ ↓ choose   Enter sign out   Esc close   (or click twice)");
            GUI.Label(new Rect(panel.x + 28, panel.yMax - 44, pw - 56, 30), help, _small);
            GUI.matrix = old;
        }

        private void Styles()
        {
            if (_title != null) return;
            Texture2D T(Color col) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, col); t.Apply(); return t; }
            _dim = T(new Color(0, 0, 0, 0.6f)); _panel = T(new Color(0.07f, 0.08f, 0.11f, 0.97f));
            _accent = T(new Color(0.2f, 0.45f, 0.95f)); _hl = T(new Color(0.2f, 0.45f, 0.95f, 0.3f));
            _title = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            _row1 = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } };
            _row2 = new GUIStyle(_row1) { fontStyle = FontStyle.Normal, normal = { textColor = new Color(0.86f, 0.88f, 0.92f) } };
            _locked = new GUIStyle(_row2) { fontSize = 17, normal = { textColor = new Color(0.55f, 0.57f, 0.62f) } };
            _small = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleLeft, wordWrap = true, normal = { textColor = new Color(0.72f, 0.76f, 0.82f) } };
            _tag = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(0.45f, 0.75f, 1f) } };
        }
    }
}

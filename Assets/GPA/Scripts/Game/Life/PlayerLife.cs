using System;
using System.Collections.Generic;
using System.IO;
using GPA.Callouts;
using GPA.Core;
using UnityEngine;

namespace GPA.Game
{
    /// <summary>
    /// The officer's life outside the job: money, owned properties and personal cars, and the home
    /// they wake up in. Saved to {persistentDataPath}/GPA/life.json (like the career file, it is
    /// separate from the position save, so it survives "New game" only if the player keeps it).
    ///
    /// Money comes from the job: every completed call, arrest and stop pays a bonus, and going off
    /// duty pays the shift wage (time on duty × hourly rate, higher with rank).
    /// </summary>
    public sealed class PlayerLife : MonoBehaviour
    {
        [Serializable]
        public sealed class State
        {
            public int money;
            public List<string> properties = new List<string>();   // building ids
            public List<string> cars = new List<string>();         // vehicle spec ids
            public string home;                                     // building id of the spawn home ("" = precinct)
            public long earned, spent, casinoWon, casinoLost;
            public float health = 100f, energy = 100f, food = 100f;   // see PlayerNeeds
            public List<string> clothes = new List<string>();      // clothing item ids (see Clothing)
            public string hat = "", shirt = Clothing.DefaultShirt;
            public long rentEarned;
        }

        public static PlayerLife Instance { get; private set; }
        public State Data { get; private set; } = new State();
        public int Money => Data.money;

        // Pay table (dollars).
        public const int PayCall = 1500, PayArrest = 800, PayCitation = 300, PayWarning = 200, PayStop = 100;
        public const float WagePerMinute = 60f;   // real minutes on duty; +15 % per rank above the first

        private IDisposable _calls, _stops;
        private float _dutySeconds;
        private bool _wasOnDuty;
        private readonly List<(string text, Color colour, float until)> _toasts = new List<(string, Color, float)>();
        private float _shownMoney;
        private GUIStyle _moneyStyle, _toastStyle;

        public static string FilePath => Path.Combine(Application.persistentDataPath, "GPA", "life.json");
        public event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAnyObjectByType<PlayerLife>() != null || FindAnyObjectByType<TopDown2D.Officer2D>() == null) return;
            new GameObject("Player Life").AddComponent<PlayerLife>();
        }

        private void Awake()
        {
            Instance = this;
            Load();
            _shownMoney = Data.money;
            _calls = GameEvents.Subscribe<CalloutPhaseChangedEvent>(OnCall);
            _stops = GameEvents.Subscribe<StopEndedEvent>(OnStop);
        }

        private void OnDestroy()
        {
            _calls?.Dispose(); _stops?.Dispose();
            if (Instance == this) Instance = null;
        }

        // ---- earning ------------------------------------------------------------------------
        private void OnCall(CalloutPhaseChangedEvent e)
        {
            if (e.Phase == CalloutPhase.Completed) Earn(PayCall, "Call completed");
        }

        private void OnStop(StopEndedEvent e)
        {
            switch (e.Outcome)
            {
                case StopOutcome.Arrest: Earn(PayArrest, "Arrest bonus"); break;
                case StopOutcome.Citation: Earn(PayCitation, "Citation"); break;
                case StopOutcome.Warning: Earn(PayWarning, "Warning"); break;
                case StopOutcome.Released: Earn(PayStop, "Traffic stop"); break;
            }
        }

        private void Update()
        {
            var calls = CalloutManager.Instance;
            bool onDuty = calls != null && calls.OnDuty;
            if (onDuty && Time.timeScale > 0f) _dutySeconds += Time.unscaledDeltaTime;
            if (_wasOnDuty && !onDuty) PayShift();
            _wasOnDuty = onDuty;
            _shownMoney = Mathf.MoveTowards(_shownMoney, Data.money, Mathf.Max(50f, Mathf.Abs(Data.money - _shownMoney) * 3f) * Time.unscaledDeltaTime);
        }

        /// <summary>Shift wage for the time just spent on duty.</summary>
        private void PayShift()
        {
            int level = Services.TryGet(out IReputationService rep) ? rep.Level : 1;
            int wage = Mathf.RoundToInt(_dutySeconds / 60f * WagePerMinute * (1f + 0.15f * (level - 1)));
            _dutySeconds = 0f;
            if (wage > 0) Earn(wage, "Shift pay");
        }

        public void Earn(int amount, string reason)
        {
            if (amount <= 0) return;
            Data.money += amount; Data.earned += amount;
            Toast($"+${amount:N0}  {reason}", new Color(0.45f, 0.95f, 0.5f));
            Save();
        }

        public bool CanAfford(int amount) => Data.money >= amount;

        public bool Spend(int amount, string reason)
        {
            if (amount < 0 || Data.money < amount) return false;
            Data.money -= amount; Data.spent += amount;
            if (amount > 0) Toast($"-${amount:N0}  {reason}", new Color(1f, 0.5f, 0.45f));
            Save();
            return true;
        }

        /// <summary>Casino results: wins add money, losses are already taken as the stake.</summary>
        public void CasinoResult(int stake, int payout)
        {
            if (payout > 0) { Data.money += payout; Data.casinoWon += payout; }
            Data.casinoLost += stake;
            Save();
        }

        // ---- ownership ----------------------------------------------------------------------
        public bool OwnsProperty(string id) => Data.properties.Contains(id);
        public bool OwnsCar(string id) => Data.cars.Contains(id);

        public void AddProperty(string id) { if (!OwnsProperty(id)) Data.properties.Add(id); if (string.IsNullOrEmpty(Data.home)) Data.home = id; Save(); }
        public void AddCar(string id) { if (!OwnsCar(id)) Data.cars.Add(id); Save(); }
        public void SetHome(string id) { Data.home = id; Save(); Toast("Spawn point set", Color.white); }

        public void ResetAll() { Data = new State(); _shownMoney = 0; Save(); }

        // ---- persistence --------------------------------------------------------------------
        private void Load()
        {
            Data = SafeFile.Read<State>(FilePath, "Life") ?? new State();
            if (Data.properties == null) Data.properties = new List<string>();
            if (Data.cars == null) Data.cars = new List<string>();
            if (Data.clothes == null) Data.clothes = new List<string>();
            if (Data.hat == null) Data.hat = "";
            if (string.IsNullOrEmpty(Data.shirt)) Data.shirt = Clothing.DefaultShirt;
        }

        public void Save()
        {
            try { SafeFile.Write(FilePath, JsonUtility.ToJson(Data, true)); }
            catch (Exception e) { Debug.LogWarning("[Life] " + e.Message); }
            Changed?.Invoke();
        }

        // ---- HUD: money (top-right, under the minimap) and +$ / -$ toasts -------------------
        public void Toast(string text, Color colour)
        {
            _toasts.Add((text, colour, Time.unscaledTime + 3f));
            if (_toasts.Count > 4) _toasts.RemoveAt(0);
        }

        private void OnGUI()
        {
            if (GameplayHotkeys.Blocked && !LifeMenu.AnyOpen) return;
            if (_moneyStyle == null)
            {
                _moneyStyle = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, normal = { textColor = new Color(0.45f, 0.95f, 0.5f) } };
                _toastStyle = new GUIStyle(GUI.skin.label) { fontSize = 19, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
            }
            Matrix4x4 old = UiScale.Begin(out Vector2 canvas);
            float right = canvas.x - 24f;
            float y = UiScale.Handheld ? 300f : 410f;   // below the minimap
            var r = new Rect(right - 300f, y, 300f, 34f);
            GUI.color = new Color(0, 0, 0, 0.6f);
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), $"${Mathf.RoundToInt(_shownMoney):N0}", _moneyStyle);
            GUI.color = Color.white;
            GUI.Label(r, $"${Mathf.RoundToInt(_shownMoney):N0}", _moneyStyle);
            y += 36f;
            for (int i = _toasts.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime > _toasts[i].until) { _toasts.RemoveAt(i); continue; }
                float a = Mathf.Clamp01(_toasts[i].until - Time.unscaledTime);
                _toastStyle.normal.textColor = new Color(_toasts[i].colour.r, _toasts[i].colour.g, _toasts[i].colour.b, a);
                GUI.Label(new Rect(right - 420f, y, 420f, 26f), _toasts[i].text, _toastStyle);
                y += 26f;
            }
            GUI.matrix = old;
        }
    }
}

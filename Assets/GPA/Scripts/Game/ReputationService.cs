using System;
using System.IO;
using GPA.Core;
using UnityEngine;

namespace GPA.Game
{
    /// <summary>
    /// Keeps the officer's career reputation (saved separately from the shift save, so starting a
    /// new shift never loses rank). Awards points from game events, announces promotions on the radio.
    /// </summary>
    public sealed class ReputationService : MonoBehaviour, IReputationService
    {
        [Serializable] private sealed class Career { public int points; public int calls; public int arrests; public int stops; }

        private Career _career = new Career();
        private IDisposable _calls, _stops, _struck;
        private string _toast;
        private float _toastUntil;
        private GUIStyle _style;

        public int Points => _career.points;
        public int Level => ReputationLevels.LevelFor(_career.points);
        public string Rank => ReputationLevels.Ranks[Level - 1];
        public int LevelStart => ReputationLevels.Thresholds[Level - 1];
        public int NextLevelAt => Level < ReputationLevels.MaxLevel ? ReputationLevels.Thresholds[Level] : -1;
        public event Action<int> LevelChanged;

        public static string FilePath => Path.Combine(Application.persistentDataPath, "GPA", "career.json");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAnyObjectByType<ReputationService>() != null) return;
            var go = new GameObject("Reputation");
            go.AddComponent<ReputationService>();
        }

        private void Awake()
        {
            Load();
            Services.Register<IReputationService>(this);
            _calls = GameEvents.Subscribe<CalloutPhaseChangedEvent>(OnCall);
            _stops = GameEvents.Subscribe<StopEndedEvent>(OnStop);
            _struck = GameEvents.Subscribe<PedestrianStruckEvent>(OnStruck);
        }

        private void OnDestroy()
        {
            _calls?.Dispose(); _stops?.Dispose(); _struck?.Dispose();
            Services.Unregister<IReputationService>(this);
        }

        private void OnCall(CalloutPhaseChangedEvent e)
        {
            if (e.Phase == CalloutPhase.Completed) { _career.calls++; Add(100, "Call completed"); }
            else if (e.Phase == CalloutPhase.Failed) Add(-50, "Call failed");
        }

        /// <summary>Running someone over costs reputation and brings dispatch and EMS.</summary>
        private void OnStruck(PedestrianStruckEvent e)
        {
            Add(e.Fatal ? -300 : -100, e.Fatal ? "Pedestrian killed" : "Pedestrian struck");
            if (!Services.TryGet(out IDispatchRadio radio)) return;
            string where = Services.TryGet(out ILocationService loc) ? loc.Describe(e.Position) : "your location";
            radio.Transmit(TransmissionPriority.Critical,
                $"{radio.Unit}, be advised, a pedestrian was struck by your unit at {where}.",
                e.Fatal ? "EMS and a supervisor are responding. Hold the scene." : "EMS is responding. Check on the injured party.");
        }

        private void OnStop(StopEndedEvent e)
        {
            _career.stops++;
            switch (e.Outcome)
            {
                case StopOutcome.Arrest: _career.arrests++; Add(80, "Arrest"); break;
                case StopOutcome.Citation: Add(40, "Citation issued"); break;
                case StopOutcome.Warning: Add(30, "Warning issued"); break;
                case StopOutcome.Released: Add(15, "Stop completed"); break;
                case StopOutcome.Fled: case StopOutcome.Lost: Add(-25, "Suspect got away"); break;
            }
        }

        public void Add(int points, string reason)
        {
            int before = Level;
            _career.points = Mathf.Max(0, _career.points + points);
            Save();
            _toast = $"{(points >= 0 ? "+" : "")}{points} rep · {reason}";
            _toastUntil = Time.unscaledTime + 3f;
            if (Level != before)
            {
                LevelChanged?.Invoke(Level);
                if (Level > before && Services.TryGet(out IDispatchRadio radio))
                    radio.Transmit(TransmissionPriority.Routine, $"{radio.Unit}, congratulations, you've been promoted to {Rank}. New vehicles are available at the precinct garage.");
            }
        }

        private void Load()
        {
            _career = SafeFile.Read<Career>(FilePath, "Reputation") ?? new Career();
        }

        private void Save()
        {
            try { SafeFile.Write(FilePath, JsonUtility.ToJson(_career, true)); }
            catch (Exception e) { Debug.LogWarning("[Reputation] " + e.Message); }
        }

        private void OnGUI()
        {
            if (_toast == null || Time.unscaledTime > _toastUntil) return;
            if (_style == null) _style = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 0.85f, 0.35f) } };
            Matrix4x4 old = UiScale.Begin(out Vector2 canvas);
            GUI.Label(new Rect(canvas.x * 0.5f - 300, canvas.y * 0.22f, 600, 34), _toast, _style);
            GUI.matrix = old;
        }
    }
}

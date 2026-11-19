using System;
using UnityEngine;

namespace GPA.Core
{
    /// <summary>
    /// Registers the core services (event bus, game state, clock). Place one in every playable
    /// scene (or in the Boot scene, marked persistent). Other systems register themselves.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public sealed class GameBootstrapper : MonoBehaviour
    {
        [Header("Clock")]
        [Tooltip("Hour (0-24) the game starts at.")]
        [SerializeField, Range(0f, 24f)] private float _startHour = 21f;
        [Tooltip("In-game minutes that pass per real second.")]
        [SerializeField] private float _minutesPerSecond = 2f;

        [Header("Lifetime")]
        [Tooltip("Keep this object (and its services) alive across scene loads.")]
        [SerializeField] private bool _persistent = true;

        [Header("Initial State")]
        [SerializeField] private GameState _initialState = GameState.OnFoot;

        private static GameBootstrapper _instance;
        private TimeOfDayService _clock;
        private GameStateService _state;
        private JsonSettings _settings;

        private void OnApplicationQuit() => _settings?.Save();

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            if (_persistent) DontDestroyOnLoad(gameObject);

            if (!Services.Has<IEventBus>()) Services.Register<IEventBus>(new EventBus());

            _settings = new JsonSettings();
            Services.Register<ISettings>(_settings);

            _state = new GameStateService();
            Services.Register<IGameStateService>(_state);

            _clock = new TimeOfDayService(_startHour, _minutesPerSecond, new DateTime(2025, 6, 1));
            Services.Register<ITimeOfDay>(_clock);
        }

        private void Start()
        {
            _state.SetBase(_initialState);
        }

        private void Update()
        {
            _clock?.Tick(Time.unscaledDeltaTime);
        }

        private void OnDestroy()
        {
            if (_instance != this) return;
            Services.Unregister<IGameStateService>(_state);
            Services.Unregister<ITimeOfDay>(_clock);
            _settings?.Save();
            Services.Unregister<ISettings>(_settings);
            _instance = null;
        }
    }
}

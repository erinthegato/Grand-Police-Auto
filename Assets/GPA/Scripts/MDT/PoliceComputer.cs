using GPA.Core;
using GPA.Input;
using GPA.Records;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GPA.Mdt
{
    /// <summary>
    /// The Mobile Data Terminal (spec §8): opens over gameplay on foot or in the car, pushes the
    /// <see cref="GameState.PoliceComputer"/> overlay state (which swaps the action maps and frees the
    /// cursor) and locks the officer while it is up. Drawing and record lookups live in
    /// <see cref="MdtScreen"/>; this component only owns its lifecycle and controller input
    /// (d-pad moves focus, A selects, B backs out, X / Y are backspace / space on the MDT keyboard).
    /// </summary>
    public sealed class PoliceComputer : MonoBehaviour, IPoliceComputerService
    {
        [SerializeField] private string _unit = "4127";
        [SerializeField] private float _referenceHeight = 1080f;

        private IInputService _input;
        private IGameStateService _state;
        private IPlayerAvatar _lockedAvatar;
        private InputAction _open, _exit, _cancel, _submit, _navigate, _nextTab, _prevTab, _scroll, _backspace, _space;
        private MdtScreen _screen;
        private bool _waitForRelease;
        private Vector2Int _lastNav;
        private MdtActivity _activity;
        private OfficerStatusService _status;
        private float _navRepeat;

        public static PoliceComputer Instance { get; private set; }
        public bool IsOpen { get; private set; }

        private void Awake()
        {
            Instance = this;
            Services.Register<IPoliceComputerService>(this);
            _activity = new MdtActivity(); // listens all shift, so "recent contacts" covers stops made before the MDT opened
            _status = new OfficerStatusService();
        }

        private void OnDestroy()
        {
            if (IsOpen) Close();
            _activity?.Dispose();
            _status?.Dispose();
            Services.Unregister<IPoliceComputerService>(this);
            if (Instance == this) Instance = null;
        }

        private bool Bind()
        {
            if (_open != null) return true;
            if (!Services.TryGet(out _input) || !Services.TryGet(out _state)) return false;
            _open = _input.Get(ActionMaps.Interaction, "OpenComputer");
            _exit = _input.Get(ActionMaps.PoliceComputer, "ExitComputer");
            _cancel = _input.Get(ActionMaps.PoliceComputer, "Cancel");
            _submit = _input.Get(ActionMaps.PoliceComputer, "Submit");
            _navigate = _input.Get(ActionMaps.PoliceComputer, "Navigate");
            _nextTab = _input.Get(ActionMaps.PoliceComputer, "NextTab");
            _prevTab = _input.Get(ActionMaps.PoliceComputer, "PrevTab");
            _scroll = _input.Get(ActionMaps.PoliceComputer, "Scroll");
            _backspace = _input.Get(ActionMaps.PoliceComputer, "Backspace");
            _space = _input.Get(ActionMaps.PoliceComputer, "Space");
            return true;
        }

        private void Update()
        {
            _status.Tick();
            if (!Bind()) return;
            if (!IsOpen)
            {
                if (_open.WasPerformedThisFrame() && (_state.Current == GameState.OnFoot || _state.Current == GameState.InVehicle)) Open();
                return;
            }

            // Something else (a scene load, a cutscene) took the state away: tidy up.
            if (_state.Current != GameState.PoliceComputer) { Close(); return; }

            // The button that opened the terminal may still be held; don't let it navigate or close it.
            Vector2 nav = _navigate.ReadValue<Vector2>();
            if (_waitForRelease)
            {
                if (nav.sqrMagnitude < 0.04f && !_exit.IsPressed()) _waitForRelease = false;
                return;
            }

            if (_exit.WasPerformedThisFrame()) { Close(); return; }
            if (_cancel.WasPerformedThisFrame())
            {
                if (_screen.KeyboardOpen) _screen.CloseKeyboard();
                else { Close(); return; }
            }
            if (_nextTab.WasPerformedThisFrame()) _screen.CycleTab(1);
            if (_prevTab.WasPerformedThisFrame()) _screen.CycleTab(-1);
            // A physical Enter while typing is handled by the keyboard itself (done / new line).
            var kb = Keyboard.current;
            bool typedEnter = _screen.KeyboardOpen && kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame);
            if (_submit.WasPerformedThisFrame() && !typedEnter) _screen.Activate();
            if (_backspace.WasPerformedThisFrame()) _screen.Backspace();
            if (_space.WasPerformedThisFrame()) _screen.Space();

            // D-pad / stick / arrows move focus, with key repeat while held.
            Vector2Int dir = Mathf.Abs(nav.x) > Mathf.Abs(nav.y)
                ? new Vector2Int(nav.x > 0.5f ? 1 : nav.x < -0.5f ? -1 : 0, 0)
                : new Vector2Int(0, nav.y > 0.5f ? -1 : nav.y < -0.5f ? 1 : 0); // GUI space: y down
            _navRepeat -= Time.unscaledDeltaTime;
            if (dir != Vector2Int.zero && (dir != _lastNav || _navRepeat <= 0f))
            {
                _screen.Navigate(dir);
                _navRepeat = dir != _lastNav ? 0.4f : 0.11f;
            }
            _lastNav = dir;

            // The mouse wheel already drives IMGUI scroll views; the right stick needs help.
            if (_input.IsGamepadActive) _screen.ScrollDetail(-_scroll.ReadValue<Vector2>().y * 900f * Time.unscaledDeltaTime);
        }

        public void Open()
        {
            if (IsOpen || !Bind()) return;
            var db = RecordsDatabase.Instance;
            if (db == null || !db.Ready) return;
            Services.TryGet(out ILocationService locations);
            Services.TryGet(out IPlayerAvatar avatar);
            _screen ??= new MdtScreen(db, locations, _activity, _status, _unit);
            _screen.OnOpened(avatar != null ? avatar.Transform.position : Vector3.zero, _input.IsGamepadActive);

            IsOpen = true;
            _waitForRelease = true;
            _state.Push(GameState.PoliceComputer);
            if (avatar != null) { _lockedAvatar = avatar; avatar.SetControlLocked(this, true); }
        }

        /// <summary>Opens the computer straight on a page (see <see cref="MdtPages"/>), e.g. Booking at the precinct.</summary>
        public void OpenTo(string page)
        {
            Open();
            if (IsOpen) _screen?.ShowPage(page);
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            GameplayHotkeys.SuppressThisFrame();
            _screen?.CloseKeyboard();
            _state?.Pop(GameState.PoliceComputer);
            _lockedAvatar?.SetControlLocked(this, false);
            _lockedAvatar = null;
        }

        private void OnGUI()
        {
            if (!IsOpen || _screen == null) return;
            GUI.depth = -100; // above the patrol HUDs
            // Phones get a 900-high canvas: bigger text, the MDT still lays itself out to fit.
            Matrix4x4 old = UiScale.Begin(out Vector2 canvas, 900f);
            bool gamepad = _input != null && _input.IsGamepadActive;
            _screen.Draw(new Rect(0, 0, canvas.x, canvas.y), gamepad);
            GUI.matrix = old;
        }
    }
}

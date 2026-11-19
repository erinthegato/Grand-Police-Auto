using GPA.Core;
using UnityEngine;

namespace GPA.Input
{
    /// <summary>Locks/hides the cursor during gameplay and frees it in menus and the MDT.</summary>
    public sealed class CursorController : MonoBehaviour
    {
        [Tooltip("When true, the MDT shows the OS cursor (mouse mapped onto the screen).")]
        [SerializeField] private bool _cursorInComputer = true;

        private IGameStateService _state;

        private void Start()
        {
            if (Services.TryGet(out _state))
            {
                _state.StateChanged += OnChanged;
                Apply(_state.Current);
            }
        }

        private void OnDestroy()
        {
            if (_state != null) _state.StateChanged -= OnChanged;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void OnApplicationFocus(bool focus)
        {
            if (focus && _state != null) Apply(_state.Current);
        }

        private void OnChanged(GameState prev, GameState cur) => Apply(cur);

        private void Apply(GameState s)
        {
            bool free = s == GameState.Menu || s == GameState.Paused || s == GameState.Boot ||
                        (s == GameState.PoliceComputer && _cursorInComputer);
            // Gameplay: keep the pointer inside the window but hidden; the game draws its own crosshair
            // (Crosshair2D). Confined, not Locked, so the pointer position still drives mouse aim.
            Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Confined;
            Cursor.visible = free;
        }
    }
}

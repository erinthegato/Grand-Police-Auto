using System;

namespace GPA.Core
{
    public enum GameState
    {
        Boot,
        Menu,
        OnFoot,
        InVehicle,
        PoliceComputer,
        WeaponWheel,
        Radio,
        Interaction,
        Paused
    }

    public interface IGameStateService
    {
        GameState Current { get; }
        GameState Previous { get; }
        /// <summary>Push a modal/overlay state (MDT, wheel, pause...). Pop returns to the underlying state.</summary>
        void Push(GameState state);
        void Pop(GameState expected);
        /// <summary>Replace the base gameplay state (OnFoot / InVehicle / Menu).</summary>
        void SetBase(GameState state);
        event Action<GameState, GameState> StateChanged; // (previous, current)
    }

    public readonly struct GameStateChangedEvent
    {
        public readonly GameState Previous;
        public readonly GameState Current;
        public GameStateChangedEvent(GameState prev, GameState cur) { Previous = prev; Current = cur; }
    }

    /// <summary>
    /// Gate for the systems that read their own hard-wired keys (Y/N callout offers, T to shout,
    /// number keys for gear...): while a full-screen overlay such as the MDT owns the keyboard,
    /// typing into it must not fire them.
    /// </summary>
    public static class GameplayHotkeys
    {
        private static int _suppressedFrame = -1;

        public static bool Blocked =>
            _suppressedFrame == UnityEngine.Time.frameCount ||
            (Services.TryGet(out IGameStateService state) &&
             (state.Current == GameState.PoliceComputer || state.Current == GameState.Radio || state.Current == GameState.Paused || state.Current == GameState.Menu));

        /// <summary>
        /// Call when an overlay closes: the press that closed it (gamepad View also clears a call)
        /// must not reach gameplay hotkeys that update later in the same frame.
        /// </summary>
        public static void SuppressThisFrame() => _suppressedFrame = UnityEngine.Time.frameCount;
    }

    /// <summary>Stack-based game state: one base state plus optional overlay states.</summary>
    public sealed class GameStateService : IGameStateService
    {
        private readonly System.Collections.Generic.List<GameState> _stack = new System.Collections.Generic.List<GameState> { GameState.Boot };

        public GameState Current => _stack[_stack.Count - 1];
        public GameState Previous { get; private set; } = GameState.Boot;
        public event Action<GameState, GameState> StateChanged;

        public void SetBase(GameState state)
        {
            var prev = Current;
            _stack[0] = state;
            Notify(prev);
        }

        public void Push(GameState state)
        {
            if (Current == state) return;
            var prev = Current;
            _stack.Add(state);
            Notify(prev);
        }

        public void Pop(GameState expected)
        {
            int idx = _stack.LastIndexOf(expected);
            if (idx <= 0) return; // never pop the base state
            var prev = Current;
            _stack.RemoveAt(idx);
            Notify(prev);
        }

        private void Notify(GameState prev)
        {
            if (prev == Current) return;
            Previous = prev;
            StateChanged?.Invoke(prev, Current);
            GameEvents.Publish(new GameStateChangedEvent(prev, Current));
        }
    }
}

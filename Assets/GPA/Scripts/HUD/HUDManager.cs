using System;
using System.Collections.Generic;
using GPA.Core;
using UnityEngine;

namespace GPA.HUD
{
    /// <summary>Shows/hides HUD groups depending on the current game state. Holds no gameplay logic.</summary>
    public sealed class HUDManager : MonoBehaviour
    {
        [Serializable]
        public class Group
        {
            public string Name;
            public CanvasGroup CanvasGroup;
            public GameState[] VisibleIn = { GameState.OnFoot, GameState.InVehicle, GameState.Interaction, GameState.Radio };
        }

        [SerializeField] private List<Group> _groups = new List<Group>();
        [Tooltip("Fade speed (alpha per second).")]
        [SerializeField] private float _fadeSpeed = 8f;

        private IGameStateService _state;
        private GameState _current = GameState.OnFoot;
        private bool _hiddenByUser;

        /// <summary>Hide the whole HUD (e.g. photo mode / debug).</summary>
        public bool HiddenByUser { get => _hiddenByUser; set => _hiddenByUser = value; }

        private void Start()
        {
            if (Services.TryGet(out _state))
            {
                _current = _state.Current;
                _state.StateChanged += OnState;
            }
        }

        private void OnDestroy()
        {
            if (_state != null) _state.StateChanged -= OnState;
        }

        private void OnState(GameState prev, GameState cur) => _current = cur;

        private void Update()
        {
            float step = _fadeSpeed * Time.unscaledDeltaTime;
            foreach (var g in _groups)
            {
                if (g.CanvasGroup == null) continue;
                bool visible = !_hiddenByUser && Array.IndexOf(g.VisibleIn, _current) >= 0;
                g.CanvasGroup.alpha = Mathf.MoveTowards(g.CanvasGroup.alpha, visible ? 1f : 0f, step);
                g.CanvasGroup.blocksRaycasts = false;
                g.CanvasGroup.interactable = false;
            }
        }
    }
}

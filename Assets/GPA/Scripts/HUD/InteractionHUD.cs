using System;
using System.Collections.Generic;
using GPA.Core;
using GPA.Input;
using UnityEngine;

namespace GPA.HUD
{
    /// <summary>
    /// Lower-centre context prompts. Displays only the options the current target offers, with
    /// device-correct button labels. Reads from <see cref="IInteractionPromptSource"/>.
    /// </summary>
    public sealed class InteractionHUD : MonoBehaviour
    {
        [SerializeField] private CanvasGroup _root;
        [SerializeField] private TMPro.TMP_Text _title;
        [SerializeField] private RectTransform _rowContainer;
        [SerializeField] private InteractionPromptRow _rowTemplate;
        [SerializeField] private TMPro.TMP_Text _cycleHint;
        [SerializeField] private float _fadeSpeed = 10f;

        private readonly List<InteractionPromptRow> _rows = new List<InteractionPromptRow>();
        private IInteractionPromptSource _source;
        private IInputService _input;
        private bool _dirty = true;

        private void OnEnable()
        {
            Services.ServiceRegistered += OnServiceRegistered;
            TryBind();
        }

        private void OnDisable()
        {
            Services.ServiceRegistered -= OnServiceRegistered;
            Unbind();
        }

        private void OnServiceRegistered(Type t, object s)
        {
            if (t == typeof(IInteractionPromptSource) || t == typeof(IInputService)) { Unbind(); TryBind(); }
        }

        private void TryBind()
        {
            if (_source == null && Services.TryGet(out _source)) _source.Changed += MarkDirty;
            if (_input == null && Services.TryGet(out _input))
            {
                _input.ActiveFamilyChanged += OnFamily;
                _input.BindingsChanged += MarkDirty;
            }
            _dirty = true;
        }

        private void Unbind()
        {
            if (_source != null) _source.Changed -= MarkDirty;
            if (_input != null)
            {
                _input.ActiveFamilyChanged -= OnFamily;
                _input.BindingsChanged -= MarkDirty;
            }
            _source = null;
            _input = null;
        }

        private void OnFamily(InputDeviceFamily f) => MarkDirty();
        private void MarkDirty() => _dirty = true;

        private void Start()
        {
            if (_rowTemplate != null) _rowTemplate.gameObject.SetActive(false);
            if (_root != null) _root.alpha = 0f;
        }

        private void LateUpdate()
        {
            bool has = _source != null && _source.Target != null && _source.Options.Count > 0;
            if (_dirty && has) Rebuild();
            _dirty = false;
            if (_root != null)
                _root.alpha = Mathf.MoveTowards(_root.alpha, has ? 1f : 0f, _fadeSpeed * Time.unscaledDeltaTime);
        }

        private void Rebuild()
        {
            var options = _source.Options;
            _title.text = _source.Target.DisplayName;

            while (_rows.Count < options.Count)
            {
                var row = Instantiate(_rowTemplate, _rowContainer);
                _rows.Add(row);
            }
            for (int i = 0; i < _rows.Count; i++)
            {
                bool active = i < options.Count;
                _rows[i].gameObject.SetActive(active);
                if (!active) continue;
                var o = options[i];
                string key = _input != null ? _input.GetBindingLabel(ActionMaps.Interaction, string.IsNullOrEmpty(o.InputAction) ? "Interact" : o.InputAction) : "E";
                _rows[i].Bind(o.Label, key, i == _source.SelectedIndex, o.Enabled);
            }

            if (_cycleHint != null)
            {
                _cycleHint.gameObject.SetActive(options.Count > 1);
                if (options.Count > 1)
                {
                    string cycleKey = _input != null && _input.IsGamepadActive ? "D-Pad" : "Mouse Wheel";
                    _cycleHint.text = $"{cycleKey}  Select";
                }
            }
        }
    }
}

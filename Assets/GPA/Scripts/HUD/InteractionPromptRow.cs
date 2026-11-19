using UnityEngine;

namespace GPA.HUD
{
    /// <summary>View for one interaction option row: [key] Label.</summary>
    public sealed class InteractionPromptRow : MonoBehaviour
    {
        [SerializeField] private TMPro.TMP_Text _key;
        [SerializeField] private TMPro.TMP_Text _label;
        [SerializeField] private UnityEngine.UI.Image _background;
        [SerializeField] private GameObject _keyCap;

        [Header("Colors")]
        [SerializeField] private Color _selectedBg = new Color(0.11f, 0.33f, 0.75f, 0.92f);
        [SerializeField] private Color _normalBg = new Color(0f, 0f, 0f, 0.55f);
        [SerializeField] private Color _enabledText = Color.white;
        [SerializeField] private Color _disabledText = new Color(1f, 1f, 1f, 0.35f);

        public void Bind(string label, string key, bool selected, bool enabled)
        {
            _label.text = label;
            _label.color = enabled ? _enabledText : _disabledText;
            if (_keyCap != null) _keyCap.SetActive(selected);
            if (_key != null) _key.text = key;
            if (_background != null) _background.color = selected ? _selectedBg : _normalBg;
        }
    }
}

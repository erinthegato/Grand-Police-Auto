using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace GPA.Core
{
    /// <summary>
    /// Generic single-action interactable (doors, lockers, signs, debug terminals).
    /// Behaviour is wired through a UnityEvent in the Inspector.
    /// </summary>
    public sealed class SimpleInteractable : MonoBehaviour, IInteractable
    {
        [SerializeField] private string _displayName = "Object";
        [SerializeField] private string _actionLabel = "Interact";
        [SerializeField] private float _range = 2f;
        [SerializeField] private Transform _promptAnchor;
        [Tooltip("Can only be used on foot.")]
        [SerializeField] private bool _onFootOnly = true;
        [SerializeField] private UnityEvent _onInteract = new UnityEvent();

        public string DisplayName => _displayName;
        public Transform PromptAnchor => _promptAnchor != null ? _promptAnchor : transform;
        public float InteractionRange => _range;
        public UnityEvent OnInteract => _onInteract;

        public bool IsInteractable(IInteractor interactor) => !_onFootOnly || !interactor.IsInVehicle;

        public void GetOptions(IInteractor interactor, List<InteractionOption> options) =>
            options.Add(new InteractionOption("use", _actionLabel));

        public void Execute(string optionId, IInteractor interactor) => _onInteract.Invoke();
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace GPA.Core
{
    /// <summary>Something that can perform interactions (the player officer, AI officers).</summary>
    public interface IInteractor
    {
        Transform Transform { get; }
        Transform ViewPoint { get; }
        bool IsInVehicle { get; }
    }

    /// <summary>A single available action, as shown by the interaction HUD.</summary>
    public readonly struct InteractionOption
    {
        public readonly string Id;
        public readonly string Label;
        /// <summary>Name of the input action to show a glyph for (e.g. "Interact").</summary>
        public readonly string InputAction;
        public readonly bool Enabled;
        public readonly int Priority;

        public InteractionOption(string id, string label, string inputAction = "Interact", bool enabled = true, int priority = 0)
        {
            Id = id; Label = label; InputAction = inputAction; Enabled = enabled; Priority = priority;
        }
    }

    /// <summary>Anything the officer can interact with (doors, peds, vehicles, MDT...).</summary>
    public interface IInteractable
    {
        string DisplayName { get; }
        Transform PromptAnchor { get; }
        /// <summary>Max distance at which this interactable can be used.</summary>
        float InteractionRange { get; }
        bool IsInteractable(IInteractor interactor);
        /// <summary>Fill <paramref name="options"/> with the actions currently available. First is the primary action.</summary>
        void GetOptions(IInteractor interactor, List<InteractionOption> options);
        void Execute(string optionId, IInteractor interactor);
    }

    public readonly struct InteractionTargetChangedEvent
    {
        public readonly IInteractable Target; // null when nothing targeted
        public InteractionTargetChangedEvent(IInteractable target) { Target = target; }
    }

    /// <summary>Raised when the options for the current target change (e.g. after an action).</summary>
    public readonly struct InteractionOptionsDirtyEvent { }
}

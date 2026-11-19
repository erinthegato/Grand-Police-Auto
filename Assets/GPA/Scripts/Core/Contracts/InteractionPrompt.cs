using System;
using System.Collections.Generic;

namespace GPA.Core
{
    /// <summary>
    /// Read-only view of the player's current interaction context. Implemented by the player
    /// interactor; consumed by the interaction HUD (which holds no gameplay logic).
    /// </summary>
    public interface IInteractionPromptSource
    {
        IInteractable Target { get; }
        IReadOnlyList<InteractionOption> Options { get; }
        int SelectedIndex { get; }
        /// <summary>Raised when target, options or selection change.</summary>
        event Action Changed;
    }
}

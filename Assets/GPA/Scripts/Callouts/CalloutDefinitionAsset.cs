using System.Collections.Generic;
using UnityEngine;

namespace GPA.Callouts
{
    /// <summary>One callout's metadata as an asset (data only, no logic).</summary>
    [CreateAssetMenu(menuName = "GPA/Callouts/Callout Definition", fileName = "Callout")]
    public sealed class CalloutDefinitionAsset : ScriptableObject
    {
        public CalloutDefinition Definition = new CalloutDefinition();
    }
}

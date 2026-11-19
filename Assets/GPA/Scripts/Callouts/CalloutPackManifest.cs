using System.Collections.Generic;
using UnityEngine;

namespace GPA.Callouts
{
    /// <summary>A pack of callout definitions (put manifests in a Resources/CalloutPacks folder).</summary>
    [CreateAssetMenu(menuName = "GPA/Callouts/Callout Pack Manifest", fileName = "CalloutPack")]
    public sealed class CalloutPackManifest : ScriptableObject
    {
        public string PackName = "New pack";
        public string Author;
        public string Version = "1.0";
        public bool Enabled = true;
        public List<CalloutDefinitionAsset> Callouts = new List<CalloutDefinitionAsset>();
        [Tooltip("Optional JSON packs bundled with this manifest.")]
        public List<TextAsset> JsonPacks = new List<TextAsset>();
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace GPA.TopDown2D
{
    /// <summary>
    /// Something the on-foot officer can use with the Interact button (talk, arrest, search...).
    /// The nearest enabled one in range wins over getting into a car. Callouts and mods add these
    /// at runtime and hook <see cref="Used"/>.
    /// </summary>
    public sealed class Interactable2D : MonoBehaviour
    {
        private static readonly List<Interactable2D> Active = new List<Interactable2D>();

        [SerializeField] private string _prompt = "Talk";
        [SerializeField] private float _range = 2.4f;

        public string Prompt { get => _prompt; set => _prompt = value; }
        public float Range { get => _range; set => _range = value; }
        public event Action<Interactable2D> Used;

        private void OnEnable() => Active.Add(this);
        private void OnDisable() => Active.Remove(this);

        public void Use() => Used?.Invoke(this);
        /// <summary>Removes every listener (so the same component can be reused with a new action).</summary>
        public void ClearHandlers() => Used = null;

        /// <summary>Nearest enabled interactable within its own range of <paramref name="point"/>.</summary>
        public static Interactable2D FindNearest(Vector2 point)
        {
            Interactable2D best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < Active.Count; i++)
            {
                Interactable2D it = Active[i];
                float d = Vector2.Distance(point, it.transform.position);
                if (d <= it._range && d < bestDistance) { best = it; bestDistance = d; }
            }
            return best;
        }
    }
}

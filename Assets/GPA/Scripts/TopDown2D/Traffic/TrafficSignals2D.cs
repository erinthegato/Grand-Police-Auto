using UnityEngine;

namespace GPA.TopDown2D
{
    /// <summary>
    /// Traffic lights for every junction in the road graph: north-south and east-west take turns
    /// (green → yellow → all-red → other axis). Junctions are offset so the city doesn't change in
    /// lock-step. Signal heads at junction corners show the current colour for their axis.
    /// </summary>
    public sealed class TrafficSignals2D : MonoBehaviour
    {
        public enum Light { Green, Yellow, Red }

        [System.Serializable]
        public struct Head
        {
            public int Junction;
            public bool Vertical;
            public SpriteRenderer Lamp;
        }

        public static TrafficSignals2D Instance { get; private set; }

        [SerializeField] private float _green = 11f;
        [SerializeField] private float _yellow = 3f;
        [SerializeField] private float _allRed = 1.5f;
        public Head[] Heads = new Head[0];

        private static readonly Color GreenColour = new Color(0.25f, 1f, 0.45f);
        private static readonly Color YellowColour = new Color(1f, 0.82f, 0.2f);
        private static readonly Color RedColour = new Color(1f, 0.22f, 0.18f);

        private float Cycle => 2f * (_green + _yellow + _allRed);

        private void Awake() => Instance = this;
        private void OnDestroy() { if (Instance == this) Instance = null; }

        public Light State(int junction, bool vertical)
        {
            if (junction < 0) return Light.Green;
            float t = Mathf.Repeat(Time.time + junction * 3.7f, Cycle);
            float half = _green + _yellow + _allRed;
            bool verticalPhase = t < half;
            float local = verticalPhase ? t : t - half;
            if (verticalPhase != vertical) return Light.Red;
            return local < _green ? Light.Green : local < _green + _yellow ? Light.Yellow : Light.Red;
        }

        private void Update()
        {
            for (int i = 0; i < Heads.Length; i++)
            {
                Head h = Heads[i];
                if (h.Lamp == null) continue;
                Light l = State(h.Junction, h.Vertical);
                h.Lamp.color = l == Light.Green ? GreenColour : l == Light.Yellow ? YellowColour : RedColour;
            }
        }
    }
}

using UnityEngine;

namespace GPA.TopDown2D
{
    /// <summary>
    /// Draw order for the 2D world, bottom to top. Uses the project sorting layers when they are
    /// all registered in the editor/player; otherwise everything goes on the built-in Default layer
    /// with a fixed order range per group, which draws identically. Either way callers just pass a
    /// group name and an order within it.
    /// </summary>
    public static class Sorting2D
    {
        public const string Water = "Water";
        public const string Ground = "Ground";
        public const string Props = "Props";
        public const string Shadows = "Shadows";
        public const string Vehicles = "Vehicles";
        public const string People = "People";
        public const string Roofs = "Roofs";
        public const string Effects = "Effects";

        public static readonly string[] All = { Water, Ground, Props, Shadows, Vehicles, People, Roofs, Effects };
        private const int Range = 1000;   // order budget per group on the Default layer

        private static bool? _layersLive;

        /// <summary>True when every group exists as a real sorting layer.</summary>
        public static bool LayersAvailable
        {
            get
            {
                if (_layersLive == null)
                {
                    var layers = SortingLayer.layers;
                    _layersLive = System.Array.TrueForAll(All, n => System.Array.Exists(layers, l => l.name == n));
                }
                return _layersLive.Value;
            }
        }

        public static void Apply(Renderer renderer, string group, int order = 0)
        {
            if (LayersAvailable)
            {
                renderer.sortingLayerName = group;
                renderer.sortingOrder = order;
                return;
            }
            int index = System.Array.IndexOf(All, group);
            renderer.sortingLayerName = "Default";
            renderer.sortingOrder = (index < 0 ? 0 : (index - 1) * Range) + Mathf.Clamp(order, -Range / 2, Range / 2 - 1);
        }

        /// <summary>Maps the legacy sorting orders (0/5/10/20/30/40) onto groups.</summary>
        public static string LayerForLegacyOrder(int order) =>
            order >= 40 ? Effects : order >= 30 ? People : order >= 20 ? Vehicles : order >= 10 ? Shadows : order >= 5 ? Props : Ground;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() => _layersLive = null;
    }
}

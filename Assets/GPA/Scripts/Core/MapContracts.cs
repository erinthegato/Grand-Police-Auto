using System.Collections.Generic;
using UnityEngine;

namespace GPA.Core
{
    /// <summary>What a minimap marker stands for; decides its shape and colour.</summary>
    public enum MapMarkerKind { Callout, CalloutOffer, TrafficStop, Pursuit, Bolo, Waypoint, Home, ForSale, Shop, Casino, Hospital }

    /// <summary>One point of interest on the minimap (spec §16).</summary>
    public readonly struct MapMarker
    {
        public readonly Vector2 Position;
        public readonly MapMarkerKind Kind;
        public readonly string Label;
        /// <summary>Draw the GPS route to this marker (the first routed marker wins).</summary>
        public readonly bool Route;

        public MapMarker(Vector2 position, MapMarkerKind kind, string label = null, bool route = false)
        {
            Position = position; Kind = kind; Label = label; Route = route;
        }
    }

    /// <summary>
    /// Anything that wants markers on the minimap (callouts, traffic stops...). Providers register
    /// with <see cref="MapMarkers"/>, so the minimap never references their assemblies.
    /// </summary>
    public interface IMapMarkerProvider
    {
        void CollectMarkers(List<MapMarker> into);
    }

    public static class MapMarkers
    {
        private static readonly List<IMapMarkerProvider> Providers = new List<IMapMarkerProvider>();

        public static void Register(IMapMarkerProvider provider)
        {
            if (provider != null && !Providers.Contains(provider)) Providers.Add(provider);
        }

        public static void Unregister(IMapMarkerProvider provider) => Providers.Remove(provider);

        /// <summary>Clears <paramref name="into"/> and fills it with every provider's markers.</summary>
        public static void Collect(List<MapMarker> into)
        {
            into.Clear();
            for (int i = Providers.Count - 1; i >= 0; i--)
            {
                IMapMarkerProvider p = Providers[i];
                if (p == null || (p is Object o && o == null)) { Providers.RemoveAt(i); continue; }
                try { p.CollectMarkers(into); }
                catch (System.Exception e) { Debug.LogException(e); }
            }
        }
    }
}

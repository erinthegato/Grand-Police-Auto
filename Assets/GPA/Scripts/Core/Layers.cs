using UnityEngine;

namespace GPA.Core
{
    /// <summary>Project layer names/indices (configured in Tags and Layers).</summary>
    public static class Layers
    {
        public const string PlayerName = "Player";
        public const string VehicleName = "Vehicle";
        public const string PedestrianName = "Pedestrian";
        public const string InteractableName = "Interactable";
        public const string MinimapGeometryName = "MinimapGeometry";
        public const string MinimapMarkersName = "MinimapMarkers";
        public const string MDTScreenName = "MDTScreen";
        public const string RoadName = "Road";

        public static int Player => LayerMask.NameToLayer(PlayerName);
        public static int Vehicle => LayerMask.NameToLayer(VehicleName);
        public static int Pedestrian => LayerMask.NameToLayer(PedestrianName);
        public static int Interactable => LayerMask.NameToLayer(InteractableName);
        public static int MinimapGeometry => LayerMask.NameToLayer(MinimapGeometryName);
        public static int MinimapMarkers => LayerMask.NameToLayer(MinimapMarkersName);
        public static int MDTScreen => LayerMask.NameToLayer(MDTScreenName);
        public static int Road => LayerMask.NameToLayer(RoadName);

        /// <summary>Everything a world camera should render (excludes minimap-only layers).</summary>
        public static int WorldCameraMask => ~(LayerMask.GetMask(MinimapGeometryName, MinimapMarkersName));
    }
}

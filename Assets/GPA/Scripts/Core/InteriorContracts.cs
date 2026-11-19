using UnityEngine;

namespace GPA.Core
{
    /// <summary>
    /// Where the player is indoors (spec §4A). Implemented by the interior system; read by the
    /// location service (dispatch / MDT / minimap text gets "..., third floor") and the HUD.
    /// </summary>
    public interface IInteriorService
    {
        /// <summary>True while the player is inside a building.</summary>
        bool Inside { get; }
        string BuildingId { get; }
        string BuildingName { get; }
        string Address { get; }
        /// <summary>0 = ground floor.</summary>
        int Floor { get; }
        int FloorCount { get; }
        /// <summary>"ground floor", "second floor"...</summary>
        string FloorName(int floor);
        /// <summary>True when <paramref name="point"/> is inside the building the player is in.</summary>
        bool Contains(Vector2 point);
    }

    /// <summary>Raised when the player enters or leaves a building or changes floor.</summary>
    public readonly struct InteriorChangedEvent
    {
        public readonly string BuildingId;
        public readonly int Floor;
        public readonly bool Inside;
        public InteriorChangedEvent(string buildingId, int floor, bool inside) { BuildingId = buildingId; Floor = floor; Inside = inside; }
    }
}

using UnityEngine;

namespace GPA.Core
{
    /// <summary>
    /// City location lookups for dispatch, callouts, the MDT and the HUD ("125 Grand Blvd",
    /// "Grand Blvd & Exchange Pl", "Harbor Point"). Backed by the city location database.
    /// </summary>
    public interface ILocationService
    {
        string CityName { get; }
        /// <summary>Best human description of a point: building address if close, otherwise street and cross street.</summary>
        string Describe(Vector2 position);
        /// <summary>Nearest named intersection, e.g. "Grand Blvd & Exchange Pl".</summary>
        string NearestIntersection(Vector2 position);
        string DistrictAt(Vector2 position);
        string BoroughAt(Vector2 position);
    }
}

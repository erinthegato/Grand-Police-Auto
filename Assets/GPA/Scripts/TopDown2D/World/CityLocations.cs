using GPA.Core;
using UnityEngine;

namespace GPA.TopDown2D
{
    /// <summary>Scene service exposing the <see cref="CityLocationDatabase"/> as <see cref="ILocationService"/>.</summary>
    public sealed class CityLocations : MonoBehaviour, ILocationService
    {
        [SerializeField] private CityLocationDatabase _database;
        [Tooltip("A building within this distance is used as the address.")]
        [SerializeField] private float _addressRange = 10f;

        public CityLocationDatabase Database => _database;
        public string CityName => _database != null ? _database.CityName : "City";

        private void Awake() { if (_database != null) Services.Register<ILocationService>(this); }
        private void OnDestroy() => Services.Unregister<ILocationService>(this);

        public string Describe(Vector2 p)
        {
            if (_database == null) return $"{p.x:0}, {p.y:0}";
            // Indoors: "125 Water St, Apartment building, third floor" (spec §4A dispatch wording).
            if (Services.TryGet(out IInteriorService interior) && interior.Inside && interior.Contains(p))
                return $"{interior.Address}, {interior.BuildingName}, {interior.FloorName(interior.Floor)}";
            var b = _database.NearestBuilding(p, _addressRange);
            if (b != null) return b.Address;
            var street = _database.StreetAt(p);
            var cross = _database.NearestIntersection(p);
            if (street != null && cross != null)
            {
                string other = cross.StreetA == street.Name ? cross.StreetB : cross.StreetA;
                return $"{street.Name} near {other}";
            }
            return cross != null ? cross.Name : DistrictAt(p);
        }

        public string NearestIntersection(Vector2 p) => _database?.NearestIntersection(p)?.Name ?? "";
        public string DistrictAt(Vector2 p) => _database?.DistrictAt(p)?.Name ?? "";
        public string BoroughAt(Vector2 p) => _database?.DistrictAt(p)?.Borough ?? "";
    }
}

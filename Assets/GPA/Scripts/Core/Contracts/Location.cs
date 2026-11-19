using UnityEngine;

namespace GPA.Core
{
    /// <summary>Human readable location used by dispatch, CAD, HUD and records.</summary>
    [System.Serializable]
    public struct LocationDescription
    {
        public Vector3 Position;
        public string District;
        public string Street;
        public string CrossStreet;
        public int HouseNumber;

        /// <summary>"1420 Commerce Ave, Downtown"</summary>
        public string Address => string.IsNullOrEmpty(Street) ? (District ?? "Unknown") :
            (HouseNumber > 0 ? $"{HouseNumber} {Street}, {District}" : $"{Street}, {District}");

        /// <summary>"5th Ave & W 42nd St"</summary>
        public string Intersection => string.IsNullOrEmpty(CrossStreet) ? Street : $"{Street} & {CrossStreet}";

        public override string ToString() => Address;
    }

    public interface IAddressService
    {
        LocationDescription Describe(Vector3 worldPosition);
        /// <summary>Find the world position of an address (street + number). Returns false if unknown.</summary>
        bool TryResolve(string street, int houseNumber, out Vector3 position);
    }
}

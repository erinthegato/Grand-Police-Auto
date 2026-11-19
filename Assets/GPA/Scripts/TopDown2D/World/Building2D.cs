using UnityEngine;

namespace GPA.TopDown2D
{
    /// <summary>
    /// A building in the world, linked to its record in the location database by id. Holds the
    /// solid footprint collider; enterable interiors (spec 4A) attach to this later.
    /// </summary>
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class Building2D : MonoBehaviour
    {
        [SerializeField] private string _buildingId;
        [SerializeField] private string _address;
        [SerializeField] private string _type;

        public string BuildingId => _buildingId;
        public string Address => _address;
        public string Type => _type;

        public void Configure(string id, string address, string type)
        {
            _buildingId = id;
            _address = address;
            _type = type;
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace GPA.TopDown2D
{
    public enum VehicleRole { Civilian, Police }

    /// <summary>One drivable vehicle type: look, size and handling. Data only.</summary>
    [Serializable]
    public sealed class VehicleSpec
    {
        public string Id = "sedan";
        public string DisplayName = "Sedan";
        public VehicleRole Role;
        [Tooltip("Body sprites; civilian types have one per paint colour.")]
        public Sprite[] Sprites = new Sprite[0];
        [Tooltip("Paint colour name for each sprite (records use these: \"Red\", \"Silver\"...).")]
        public string[] ColourNames = new string[0];
        public string[] Models = new string[0];
        [Header("Size (m)")]
        public float Width = 2.3f;
        public float Length = 4.6f;
        [Header("Handling")]
        public float Mass = 1500f;
        public float TopSpeed = 34f;
        public float Acceleration = 7.5f;
        public float Braking = 26f;
        public float TurnRate = 190f;
        public float Grip = 9f;
        [Header("Police")]
        public bool Lightbar;
        [Tooltip("Lightbar centre, metres forward of the vehicle centre.")]
        public float LightbarForward = 0.19f;
        public float LightbarHalfWidth = 0.38f;
        [Tooltip("Reputation level needed to sign it out (police fleet).")]
        public int RequiredLevel = 1;
        public string Blurb;
        [Header("Special")]
        public bool Helicopter;
        public Sprite RotorSprite;
        public bool Motorcycle;
        [Header("Traffic")]
        [Tooltip("Relative share of civilian traffic.")]
        public float TrafficWeight = 1f;

        public Sprite SpriteFor(string colour)
        {
            if (Sprites.Length == 0) return null;
            for (int i = 0; i < ColourNames.Length && i < Sprites.Length; i++)
                if (string.Equals(ColourNames[i], colour, StringComparison.OrdinalIgnoreCase)) return Sprites[i];
            return Sprites[0];
        }
    }

    /// <summary>
    /// Every vehicle type in the game (Resources/VehicleCatalog). Civilian types feed traffic and the
    /// vehicle records; police types make up the fleet the officer can sign out at the precinct.
    /// Built by Tools ▸ GPA ▸ Vehicles ▸ Rebuild vehicle catalog.
    /// </summary>
    [CreateAssetMenu(menuName = "GPA/Vehicle Catalog", fileName = "VehicleCatalog")]
    public sealed class VehicleCatalog : ScriptableObject
    {
        public List<VehicleSpec> Vehicles = new List<VehicleSpec>();

        private static VehicleCatalog _instance;
        public static VehicleCatalog Instance
        {
            get
            {
                if (_instance == null) _instance = Resources.Load<VehicleCatalog>("VehicleCatalog");
                return _instance;
            }
        }

        public VehicleSpec Get(string id) => Vehicles.Find(v => v.Id == id);

        /// <summary>Finds the type (and paint) whose sprite has this name, e.g. "car_suv_black".</summary>
        public VehicleSpec BySprite(string spriteName, out Sprite sprite)
        {
            sprite = null;
            if (string.IsNullOrEmpty(spriteName)) return null;
            foreach (var v in Vehicles)
                foreach (var s in v.Sprites)
                    if (s != null && s.name == spriteName) { sprite = s; return v; }
            return null;
        }

        public IEnumerable<VehicleSpec> Fleet() { foreach (var v in Vehicles) if (v.Role == VehicleRole.Police) yield return v; }
        public IEnumerable<VehicleSpec> Civilian() { foreach (var v in Vehicles) if (v.Role == VehicleRole.Civilian) yield return v; }
    }
}

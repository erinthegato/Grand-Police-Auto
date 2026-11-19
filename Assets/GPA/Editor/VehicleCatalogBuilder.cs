using System.Collections.Generic;
using GPA.TopDown2D;
using UnityEditor;
using UnityEngine;

namespace GPA.EditorTools
{
    /// <summary>
    /// Builds Resources/VehicleCatalog.asset from the vehicle sprites (made by
    /// Art/Sprites2D/_Source/generate_vehicles.py) and sets their import settings to match the rest
    /// of the 2D art (Sprite, 64 PPU, no mipmaps).
    /// </summary>
    public static class VehicleCatalogBuilder
    {
        private const string SpriteDir = "Assets/GPA/Art/Sprites2D/";
        private const string CatalogPath = "Assets/GPA/Resources/VehicleCatalog.asset";

        [MenuItem("Tools/GPA/Vehicles/Rebuild vehicle catalog")]
        public static void Build()
        {
            var specs = new List<VehicleSpec>
            {
                // ---------------- civilian
                Civ("sedan", "Sedan", 2.3f, 4.6f, 1500, 34, 7.5f, 190, 3f, new[] { "car_sedan_maroon", "car_sedan_gunmetal", "car_sedan_olive" }, new[] { "Red", "Grey", "Green" }, new[] { "Meridian", "Sable", "Crestline" }),
                Civ("taxi", "Taxi", 2.3f, 4.6f, 1500, 34, 7.5f, 190, 0.5f, new[] { "car_taxi" }, new[] { "Yellow" }, new[] { "Metro Cab" }),
                Civ("compact", "Compact", 2.0f, 3.5f, 1100, 30, 8f, 210, 1.2f, new[] { "car_compact_red", "car_compact_blue", "car_compact_white", "car_compact_green" }, new[] { "Red", "Blue", "White", "Green" }, new[] { "Pixie", "Zip", "Metro" }),
                Civ("suv", "SUV", 2.45f, 4.85f, 2100, 32, 7f, 175, 1.6f, new[] { "car_suv_black", "car_suv_silver", "car_suv_blue", "car_suv_white" }, new[] { "Black", "Silver", "Blue", "White" }, new[] { "Trailhead", "Summit", "Ridgeline" }),
                Civ("pickup", "Pickup Truck", 2.45f, 5.35f, 2300, 31, 6.8f, 170, 1.1f, new[] { "car_pickup_white", "car_pickup_red", "car_pickup_black", "car_pickup_tan" }, new[] { "White", "Red", "Black", "Tan" }, new[] { "Hauler", "Workhorse", "Ranchero" }),
                Civ("delivery_van", "Delivery Van", 2.6f, 5.7f, 2800, 26, 5.5f, 150, 0.5f, new[] { "van_delivery_white", "van_delivery_brown" }, new[] { "White", "Brown" }, new[] { "Courier Van", "Sprint Cargo" }),
                Civ("box_truck", "Box Truck", 2.9f, 8.7f, 6500, 22, 4f, 120, 0.3f, new[] { "truck_box_white", "truck_box_yellow" }, new[] { "White", "Yellow" }, new[] { "Box 16", "Cargo Master" }, braking: 18f),

                // ---------------- police fleet (level = reputation level needed)
                Pd("patrol_sedan", "Patrol Sedan", 1, "car_police_cruiser", 2.3f, 4.6f, 1600, 40, 9f, 195, 0.19f, 0.38f, "All-round patrol car."),
                Pd("patrol_suv", "Patrol SUV", 1, "pd_suv", 2.45f, 4.85f, 2300, 38, 8.5f, 180, 0.67f, 0.39f, "Roomier, tougher, a bit slower."),
                Pd("patrol_pickup", "Patrol Pickup", 2, "pd_pickup", 2.45f, 5.35f, 2500, 36, 8f, 172, 0.79f, 0.39f, "Heavy-duty truck, good at pushing."),
                Pd("motorcycle", "Traffic Motorcycle", 2, "pd_motorcycle", 0.9f, 2.2f, 300, 46, 12f, 240, -0.52f, 0.19f, "Nimble, quick, slips through traffic.", motorcycle: true),
                Pd("unmarked", "Unmarked Sedan", 3, "pd_unmarked", 2.3f, 4.6f, 1600, 42, 9.5f, 195, 0.42f, 0.2f, "Plain car with hidden dash lights."),
                Pd("transport", "Prisoner Transport", 3, "pd_transport", 2.7f, 6.4f, 3200, 30, 6f, 150, 1.75f, 0.44f, "Van with a caged rear for prisoners."),
                Pd("interceptor", "Highway Interceptor", 4, "pd_interceptor", 2.3f, 4.3f, 1600, 50, 12f, 205, 0.0f, 0.3f, "Fastest car in the fleet, for pursuits."),
                Pd("bearcat", "BearCat (armored)", 5, "pd_bearcat", 2.9f, 6.0f, 8000, 28, 5f, 135, 1.16f, 0.33f, "Armored rescue vehicle. Nothing stops it."),
                Pd("helicopter", "Air Unit Helicopter", 6, "pd_helicopter", 2.6f, 11.6f, 3000, 55, 10f, 110, 1.1f, 0.25f, "Flies over everything. Land on open ground.", heli: true),
            };

            AssetDatabase.StartAssetEditing();
            try { foreach (string n in AllSpriteNames(specs)) Import(n); }
            finally { AssetDatabase.StopAssetEditing(); }
            Import("pd_helicopter_rotor");

            foreach (var s in specs) Resolve(s);
            specs.Find(s => s.Id == "helicopter").RotorSprite = Load("pd_helicopter_rotor");

            var catalog = AssetDatabase.LoadAssetAtPath<VehicleCatalog>(CatalogPath);
            if (catalog == null) { catalog = ScriptableObject.CreateInstance<VehicleCatalog>(); AssetDatabase.CreateAsset(catalog, CatalogPath); }
            catalog.Vehicles = specs;
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            int missing = 0; foreach (var s in specs) foreach (var sp in s.Sprites) if (sp == null) missing++;
            Debug.Log($"[Vehicles] Catalog built: {specs.Count} vehicle types, {missing} missing sprites.");
        }

        private static readonly Dictionary<VehicleSpec, string[]> Names = new Dictionary<VehicleSpec, string[]>();

        private static VehicleSpec Civ(string id, string name, float w, float l, float mass, float top, float acc, float turn, float weight, string[] sprites, string[] colours, string[] models, float braking = 26f)
        {
            var s = new VehicleSpec { Id = id, DisplayName = name, Role = VehicleRole.Civilian, Width = w, Length = l, Mass = mass, TopSpeed = top, Acceleration = acc, TurnRate = turn, Braking = braking, TrafficWeight = weight, ColourNames = colours, Models = models };
            Names[s] = sprites;
            return s;
        }

        private static VehicleSpec Pd(string id, string name, int level, string sprite, float w, float l, float mass, float top, float acc, float turn, float lbFwd, float lbHalf, string blurb, bool motorcycle = false, bool heli = false)
        {
            var s = new VehicleSpec
            {
                Id = id, DisplayName = name, Role = VehicleRole.Police, RequiredLevel = level, Width = w, Length = l, Mass = mass, TopSpeed = top,
                Acceleration = acc, TurnRate = turn, Lightbar = true, LightbarForward = lbFwd, LightbarHalfWidth = lbHalf, Blurb = blurb,
                Motorcycle = motorcycle, Helicopter = heli, Grip = heli ? 2.2f : motorcycle ? 12f : 9f, Braking = heli ? 12f : 26f,
                ColourNames = new[] { "Police" },
            };
            Names[s] = new[] { sprite };
            return s;
        }

        private static IEnumerable<string> AllSpriteNames(List<VehicleSpec> specs)
        {
            foreach (var s in specs) foreach (var n in Names[s]) yield return n;
        }

        private static void Resolve(VehicleSpec s)
        {
            string[] names = Names[s];
            s.Sprites = new Sprite[names.Length];
            for (int i = 0; i < names.Length; i++) s.Sprites[i] = Load(names[i]);
        }

        private static Sprite Load(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(SpriteDir + name + ".png");

        private static void Import(string name)
        {
            string path = SpriteDir + name + ".png";
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) return;
            if (imp.textureType == TextureImporterType.Sprite && Mathf.Approximately(imp.spritePixelsPerUnit, 64f) && !imp.mipmapEnabled) return;
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.spritePixelsPerUnit = 64f;
            imp.mipmapEnabled = false;
            imp.alphaIsTransparency = true;
            imp.filterMode = FilterMode.Bilinear;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
        }
    }
}

namespace GPA.EditorTools
{
    internal static class ReputationCheat
    {
        [MenuItem("Tools/GPA/Vehicles/Add 1000 reputation (play mode)")]
        private static void Add()
        {
            if (!Application.isPlaying) { Debug.Log("Enter play mode first."); return; }
            if (GPA.Core.Services.TryGet(out GPA.Core.IReputationService rep)) rep.Add(1000, "Test bonus");
        }
    }
}

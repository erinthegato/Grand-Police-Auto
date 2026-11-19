using System.Collections.Generic;
using GPA.Callouts;
using GPA.Input;
using GPA.TopDown2D;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GPA.EditorTools
{
    /// <summary>
    /// Builds Assets/GPA/Scenes/City_TopDown2D.unity: a 4 x 4 grid of city blocks drawn with the
    /// generated sprites, a drivable patrol car, parked traffic and the twin-stick officer. This is a
    /// test bed for driving feel and controls; the real five-borough map replaces the grid later.
    /// Rebuilding regenerates the scene from scratch (it owns no hand edits).
    /// </summary>
    public static class TopDown2DSceneBuilder
    {
        private const string ScenePath = "Assets/GPA/Scenes/City_TopDown2D.unity";
        private const string GameSystemsPrefab = "Assets/GPA/Prefabs/Systems/GameSystems.prefab";
        private const float Tile = 4f;            // metres per ground tile
        private const int Blocks = 4;
        private const int BlockTiles = 6;         // roof tiles per block side
        private const int RoadTiles = 3;          // lane, centre line, lane
        private const int Period = BlockTiles + 2 + RoadTiles; // block + sidewalk ring + road

        // Sorting orders.
        private const int Ground = 0, Props = 5, Shadows = 10, Cars = 20, People = 30, Lights = 40;

        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        [MenuItem("GPA/Top-Down 2D/Reimport Sprites")]
        public static void ReimportSprites()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Sprites2DImporter.Folder.TrimEnd('/') }))
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
            Cache.Clear();
            Debug.Log("[TopDown2D] Sprites reimported.");
        }

        [MenuItem("GPA/Top-Down 2D/Build Test City Scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Cache.Clear();
            if (S("car_police_cruiser") == null)
            {
                ReimportSprites();
                if (S("car_police_cruiser") == null)
                {
                    EditorUtility.DisplayDialog("Top-Down 2D", "Sprites not found in " + Sprites2DImporter.Folder, "OK");
                    return;
                }
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var rng = new System.Random(1977);

            var systems = AssetDatabase.LoadAssetAtPath<GameObject>(GameSystemsPrefab);
            if (systems != null) PrefabUtility.InstantiatePrefab(systems, scene);
            else Debug.LogWarning("[TopDown2D] GameSystems prefab missing; input will not work.");
            new GameObject("Haptics").AddComponent<HapticsService>();

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.07f, 0.07f, 0.075f);
            cam.orthographic = true;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<Camera2DFollow>();
            camGo.transform.position = new Vector3(0, 0, -20);

            BuildCity(rng);
            BuildSpawnPoints();
            BuildPatrolSystems();

            // Player car on the first north-south road, officer on the sidewalk next to it.
            float roadX = RoadCentre(0);
            // Northbound lane is east of the centre line (traffic drives on the right).
            Car2D cruiser = SpawnCar("car_police_cruiser", "Patrol 4127", new Vector2(roadX + 3.6f, 30f), 0f, emergency: true);
            cruiser.gameObject.AddComponent<Car2DHaptics>();
            AddLightbar(cruiser);

            var officer = SpawnOfficer(new Vector2(roadX + 7.6f, 26f));

            // Parked traffic along the curbs.
            string[] civ = { "car_sedan_maroon", "car_sedan_gunmetal", "car_sedan_olive", "car_taxi" };
            for (int i = 0; i < 10; i++)
            {
                int road = rng.Next(Blocks);
                float x = RoadCentre(road) + (rng.Next(2) == 0 ? -4.9f : 4.9f);
                float y = rng.Next(Blocks) * Period * Tile + (2 + rng.Next(5)) * Tile;
                float heading = x < RoadCentre(road) ? 180f : 0f;
                var parked = SpawnCar(civ[i % civ.Length], "Parked", new Vector2(x, y), heading, emergency: false);
                parked.gameObject.name = "Parked " + civ[i % civ.Length];
            }

            EditorSceneManager.SaveScene(scene, ScenePath);
            Selection.activeGameObject = officer;
            Debug.Log($"[TopDown2D] Built {ScenePath}. Press Play: WASD/left stick to walk, E/(A) near the cruiser to drive.");
        }

        // ------------------------------------------------------------------ city layout

        /// <summary>World X of the centre line of the north-south road east of block column <paramref name="index"/>.</summary>
        private static float RoadCentre(int index) => (index * Period + BlockTiles + 3) * Tile + Tile * 0.5f;

        private static void BuildCity(System.Random rng)
        {
            var root = new GameObject("City").transform;
            var ground = new GameObject("Ground").transform; ground.SetParent(root);
            var buildings = new GameObject("Buildings").transform; buildings.SetParent(root);
            var props = new GameObject("Props").transform; props.SetParent(root);

            int size = Blocks * Period;
            for (int ty = 0; ty < size; ty++)
            for (int tx = 0; tx < size; tx++)
            {
                int lx = tx % Period, ly = ty % Period;
                Vector2 pos = new Vector2(tx * Tile + Tile * 0.5f, ty * Tile + Tile * 0.5f);
                bool roadX = lx >= BlockTiles + 2;  // vertical road column
                bool roadY = ly >= BlockTiles + 2;  // horizontal road row
                string sprite; float rot = 0f;

                if (roadX && roadY) sprite = Pick(rng, "tile_asphalt", "tile_asphalt", "tile_asphalt_manhole", "tile_asphalt_oil");
                else if (roadX)
                {
                    int lane = lx - (BlockTiles + 2);
                    bool nearJunction = ly == 0 || ly == BlockTiles + 1;
                    sprite = nearJunction ? "tile_crosswalk" : lane == 1 ? "tile_asphalt_lane_double_yellow" : Pick(rng, "tile_asphalt", "tile_asphalt", "tile_asphalt_oil");
                    rot = 0f; // stripes and centre lines run with the traffic (north-south)
                }
                else if (roadY)
                {
                    int lane = ly - (BlockTiles + 2);
                    bool nearJunction = lx == 0 || lx == BlockTiles + 1;
                    sprite = nearJunction ? "tile_crosswalk" : lane == 1 ? "tile_asphalt_lane_double_yellow" : Pick(rng, "tile_asphalt", "tile_asphalt", "tile_asphalt_oil");
                    rot = 90f;
                }
                else
                {
                    bool west = lx == 0, east = lx == BlockTiles + 1, south = ly == 0, north = ly == BlockTiles + 1;
                    // Roads sit west/south of block 0 only via wrap; first row/column face the map edge.
                    if ((west || east) && (south || north))
                    {
                        sprite = "tile_sidewalk_corner";
                        rot = west && south ? 0f : west && north ? -90f : east && north ? 180f : 90f;
                    }
                    else if (west || east || south || north)
                    {
                        sprite = "tile_sidewalk_curb";
                        rot = west ? 0f : east ? 180f : north ? -90f : 90f;
                    }
                    else
                    {
                        sprite = Pick(rng, "tile_roof_tar", "tile_roof_gravel", "tile_roof_tar");
                        rot = 90f * rng.Next(4);
                    }
                }

                Place(sprite, pos, rot, Ground, ground);

                // Props on sidewalks (not corners).
                bool sidewalk = !roadX && !roadY && (lx == 0 || ly == 0 || lx == BlockTiles + 1 || ly == BlockTiles + 1);
                if (sidewalk && rng.NextDouble() < 0.22)
                {
                    string prop = Pick(rng, "prop_hydrant", "prop_trash_bags", "prop_streetlight", "prop_trash_bags");
                    Place(prop, pos + new Vector2((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * 2f, rng.Next(360), Props, props);
                }
            }

            // One solid collider per building footprint.
            for (int by = 0; by < Blocks; by++)
            for (int bx = 0; bx < Blocks; bx++)
            {
                var b = new GameObject($"Building {bx},{by}");
                b.transform.SetParent(buildings);
                b.transform.position = new Vector3((bx * Period + 1 + BlockTiles * 0.5f) * Tile, (by * Period + 1 + BlockTiles * 0.5f) * Tile, 0f);
                b.AddComponent<BoxCollider2D>().size = new Vector2(BlockTiles * Tile, BlockTiles * Tile);
                b.isStatic = true;

                if (rng.NextDouble() < 0.6)
                {
                    Vector2 corner = new Vector2((bx * Period + 0.5f) * Tile, (by * Period + 0.7f) * Tile);
                    var dumpster = Place("prop_dumpster", corner + new Vector2(Tile * 2, 0f), 0f, Props, props);
                    dumpster.AddComponent<BoxCollider2D>();
                }
            }

            // Map boundary.
            float extent = size * Tile;
            var bounds = new GameObject("Bounds");
            bounds.transform.SetParent(root);
            var edge = bounds.AddComponent<EdgeCollider2D>();
            edge.points = new[] { new Vector2(0, 0), new Vector2(extent, 0), new Vector2(extent, extent), new Vector2(0, extent), new Vector2(0, 0) };
        }

        // ------------------------------------------------------------------ gameplay systems

        /// <summary>Lane centres (facing the direction of travel, driving on the right) and sidewalk spots.</summary>
        private static void BuildSpawnPoints()
        {
            var road = new List<SpawnPoints2D.Point>();
            var walk = new List<SpawnPoints2D.Point>();
            for (int r = 0; r < Blocks; r++)
            {
                float c = RoadCentre(r);
                for (int b = 0; b < Blocks; b++)
                for (int i = 1; i <= BlockTiles; i++)
                {
                    float along = (b * Period + i) * Tile + Tile * 0.5f;
                    road.Add(new SpawnPoints2D.Point { Position = new Vector2(c + Tile, along), Heading = 0f });     // northbound
                    road.Add(new SpawnPoints2D.Point { Position = new Vector2(c - Tile, along), Heading = 180f });   // southbound
                    road.Add(new SpawnPoints2D.Point { Position = new Vector2(along, c - Tile), Heading = -90f });   // eastbound
                    road.Add(new SpawnPoints2D.Point { Position = new Vector2(along, c + Tile), Heading = 90f });    // westbound
                }
            }
            for (int by = 0; by < Blocks; by++)
            for (int bx = 0; bx < Blocks; bx++)
            for (int i = 1; i <= BlockTiles; i++)
            {
                float x0 = bx * Period * Tile, y0 = by * Period * Tile;
                float along = i * Tile + Tile * 0.5f, edge = (BlockTiles + 1) * Tile + Tile * 0.5f;
                walk.Add(new SpawnPoints2D.Point { Position = new Vector2(x0 + Tile * 0.5f, y0 + along) });
                walk.Add(new SpawnPoints2D.Point { Position = new Vector2(x0 + edge, y0 + along) });
                walk.Add(new SpawnPoints2D.Point { Position = new Vector2(x0 + along, y0 + Tile * 0.5f) });
                walk.Add(new SpawnPoints2D.Point { Position = new Vector2(x0 + along, y0 + edge) });
            }
            var go = new GameObject("Spawn Points");
            var sp = go.AddComponent<SpawnPoints2D>();
            sp.Road = road.ToArray();
            sp.Sidewalk = walk.ToArray();
        }

        internal static void BuildPatrolSystems()
        {
            var sprites = new GameObject("World Sprites").AddComponent<WorldSprites2D>();
            string[] looks = { "civ_jacket_brown", "civ_hoodie_grey", "civ_coat_green", "civ_suit_navy", "civ_athletic_red", "civ_dress_purple", "civ_jacket_tan_f", "civ_vest_orange" };
            var idle = new List<Sprite>(); var walkA = new List<Sprite>(); var walkB = new List<Sprite>();
            foreach (string look in looks)
            {
                if (S(look) == null) continue;
                idle.Add(S(look)); walkA.Add(S(look + "_walk_a")); walkB.Add(S(look + "_walk_b"));
            }
            sprites.Civilians = idle.ToArray();
            sprites.CivilianWalkA = walkA.ToArray();
            sprites.CivilianWalkB = walkB.ToArray();
            sprites.CivilianCars = new[] { S("car_sedan_maroon"), S("car_sedan_gunmetal"), S("car_sedan_olive"), S("car_taxi") };
            sprites.CarShadow = S("car_shadow");
            sprites.PersonShadow = S("person_shadow");
            sprites.GlowRed = S("glow_red");
            sprites.GlowBlue = S("glow_blue");

            var patrol = new GameObject("Patrol");
            var scanner = patrol.AddComponent<DispatchScanner>();
            var manager = patrol.AddComponent<CalloutManager>();
            var so = new SerializedObject(manager);
            so.FindProperty("_scanner").objectReferenceValue = scanner;
            so.ApplyModifiedPropertiesWithoutUndo();
            patrol.AddComponent<CalloutHUD>();
        }

        // ------------------------------------------------------------------ spawning

        internal static Car2D SpawnCar(string sprite, string label, Vector2 position, float heading, bool emergency)
        {
            var car = new GameObject(label);
            car.transform.SetPositionAndRotation(position, Quaternion.Euler(0, 0, heading));
            car.AddComponent<Rigidbody2D>();
            var box = car.AddComponent<BoxCollider2D>();
            box.size = new Vector2(2.3f, 4.6f);
            Place("car_shadow", new Vector2(0.18f, -0.22f), 0f, Shadows, car.transform, local: true);
            var body = Place(sprite, Vector2.zero, 0f, Cars, car.transform, local: true);
            body.name = "Body";
            var c = car.AddComponent<Car2D>();
            var so = new SerializedObject(c);
            so.FindProperty("_body").objectReferenceValue = body.transform;
            so.FindProperty("_emergencyVehicle").boolValue = emergency;
            so.FindProperty("_description").stringValue = label;
            so.ApplyModifiedPropertiesWithoutUndo();
            return c;
        }

        internal static void AddLightbar(Car2D car)
        {
            Transform body = car.transform.Find("Body");
            var red = Place("glow_red", new Vector2(-0.38f, 0.19f), 0f, Lights, body, local: true);
            var blue = Place("glow_blue", new Vector2(0.38f, 0.19f), 0f, Lights, body, local: true);
            red.transform.localScale = blue.transform.localScale = Vector3.one * 2.4f;
            var lb = car.gameObject.AddComponent<Lightbar2D>();
            var so = new SerializedObject(lb);
            so.FindProperty("_red").objectReferenceValue = red.GetComponent<SpriteRenderer>();
            so.FindProperty("_blue").objectReferenceValue = blue.GetComponent<SpriteRenderer>();
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static GameObject SpawnOfficer(Vector2 position)
        {
            var go = new GameObject("Officer");
            go.transform.position = position;
            go.AddComponent<Rigidbody2D>();
            go.AddComponent<CircleCollider2D>().radius = 0.32f;
            var shadow = Place("person_shadow", new Vector2(0.08f, -0.1f), 0f, Shadows, go.transform, local: true);
            // Starts unarmed; equipment swaps to the two-handed pose when something is drawn.
            var body = Place("officer_unarmed", Vector2.zero, 0f, People, go.transform, local: true);
            var anim = body.AddComponent<SpriteAnimator2D>();
            anim.Idle = S("officer_unarmed");
            anim.StepA = S("officer_unarmed_walk_a");
            anim.StepB = S("officer_unarmed_walk_b");
            var officer = go.AddComponent<Officer2D>();
            var so = new SerializedObject(officer);
            var arr = so.FindProperty("_renderers");
            arr.arraySize = 2;
            arr.GetArrayElementAtIndex(0).objectReferenceValue = body.GetComponent<SpriteRenderer>();
            arr.GetArrayElementAtIndex(1).objectReferenceValue = shadow.GetComponent<SpriteRenderer>();
            so.ApplyModifiedPropertiesWithoutUndo();

            var eq = go.AddComponent<OfficerEquipment2D>();
            eq.Animator = anim;
            eq.UnarmedIdle = S("officer_unarmed"); eq.UnarmedStepA = S("officer_unarmed_walk_a"); eq.UnarmedStepB = S("officer_unarmed_walk_b");
            eq.ArmedIdle = S("officer_armed"); eq.ArmedStepA = S("officer_armed_walk_a"); eq.ArmedStepB = S("officer_armed_walk_b");
            eq.LongGunIdle = S("officer_longgun"); eq.LongGunStepA = S("officer_longgun_walk_a"); eq.LongGunStepB = S("officer_longgun_walk_b");
            go.AddComponent<EquipmentHUD2D>();
            return go;
        }

        // ------------------------------------------------------------------ helpers

        internal static GameObject Place(string sprite, Vector2 position, float rotation, int order, Transform parent, bool local = false)
        {
            var go = new GameObject(sprite);
            go.transform.SetParent(parent, false);
            if (local) go.transform.localPosition = position; else go.transform.position = position;
            go.transform.localRotation = Quaternion.Euler(0, 0, rotation);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = S(sprite);
            Sorting2D.Apply(sr, Sorting2D.LayerForLegacyOrder(order), order);
            return go;
        }

        internal static Sprite S(string name)
        {
            if (Cache.TryGetValue(name, out var s) && s != null) return s;
            s = AssetDatabase.LoadAssetAtPath<Sprite>(Sprites2DImporter.Folder + name + ".png");
            Cache[name] = s;
            return s;
        }

        private static string Pick(System.Random rng, params string[] options) => options[rng.Next(options.Length)];
    }
}

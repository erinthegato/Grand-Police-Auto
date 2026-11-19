using System.Collections.Generic;
using System.IO;
using System.Linq;
using GPA.Core;
using GPA.TopDown2D;
using UnityEditor;
using UnityEngine;

namespace GPA.EditorTools
{
    /// <summary>
    /// Stages police-action scenes on the real city streets (in play mode) and captures them from the
    /// game camera as main-menu slides: Assets/GPA/Resources/MenuSlides/slide_NN_name.png.
    /// Usage (play mode): Stage(i), let a frame pass (culling / lights update), then Capture(i).
    /// Tools ▸ GPA ▸ Menu slides ▸ Clear stage removes everything it spawned.
    /// </summary>
    public static class MenuSlideCapture
    {
        public const string Folder = "Assets/GPA/Resources/MenuSlides";
        private const float T = 4f;
        private static GameObject _root;

        public sealed class Shot { public string Name; public float Hour; public Vector2 Centre; public float Size; public float Tilt; }
        public static readonly string[] Names = { "traffic_stop", "swat_raid", "foot_pursuit", "vehicle_pursuit", "collision", "felony_stop", "bridge_patrol" };
        public static int Count => Names.Length;

        // ------------------------------------------------------------------ helpers

        private static Sprite S(string name) => AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/GPA/Art/Sprites2D/{name}.png");
        private static Quaternion Face(float degreesFromEast) => Quaternion.Euler(0, 0, degreesFromEast - 90f);   // sprites face +Y

        private static GameObject Sprite(string name, Vector2 pos, float faceDeg, string layer, int order, float scale = 1f, Transform parent = null, Color? tint = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : _root.transform, false);
            if (parent == null) { go.transform.position = pos; go.transform.rotation = Face(faceDeg); }
            else { go.transform.localPosition = pos; go.transform.localRotation = Quaternion.Euler(0, 0, faceDeg); }
            go.transform.localScale = Vector3.one * scale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = S(name);
            if (tint.HasValue) sr.color = tint.Value;
            Sorting2D.Apply(sr, layer, order);
            return go;
        }

        private static GameObject Car(string sprite, Vector2 pos, float faceDeg, bool lights = false, bool headlights = false, bool lightsBlue = false)
        {
            var car = Sprite(sprite, pos, faceDeg, Sorting2D.Vehicles, 20);
            Sprite("car_shadow", new Vector2(0.18f, -0.22f), 0, Sorting2D.Shadows, 10, 1f, car.transform);
            if (lights)
            {
                // Roof lightbar (red left, blue right) plus a wide coloured wash on the street.
                Sprite("glow_red", new Vector2(-0.5f, 0.1f), 0, Sorting2D.Effects, 900, 1.1f, car.transform, new Color(1, 1, 1, 0.95f));
                Sprite("glow_blue", new Vector2(0.5f, 0.1f), 0, Sorting2D.Effects, 900, 1.1f, car.transform, new Color(1, 1, 1, 0.95f));
                Sprite(lightsBlue ? "glow_blue" : "glow_red", new Vector2(-2.2f, 0.4f), 0, Sorting2D.Effects, 880, 5.5f, car.transform, new Color(1, 1, 1, 0.4f));
                Sprite("glow_blue", new Vector2(2.2f, 0.4f), 0, Sorting2D.Effects, 880, 5.5f, car.transform, new Color(1, 1, 1, 0.42f));
            }
            if (headlights)
            {
                // headlight_cone.png is narrow and bright at its bottom edge and fans out toward the top,
                // so it already points forward (+Y). Put that narrow end on the front bumper so the beams
                // start at the lamps (it used to be flipped 180°, leaving the bright end out in mid-air).
                const float coneScale = 1.3f;
                var body = car.GetComponent<SpriteRenderer>();
                float bumper = body != null && body.sprite != null ? body.sprite.bounds.extents.y : 2.3f;
                float y = bumper - 0.1f + coneScale * (2f - 0.25f);   // cone is 4 units tall; its light starts 0.25 up from the bottom
                float x = body != null && body.sprite != null ? body.sprite.bounds.extents.x * 0.55f : 0.62f;
                Sprite("headlight_cone", new Vector2(-x, y), 0f, Sorting2D.Effects, 870, coneScale, car.transform, new Color(1, 0.97f, 0.85f, 0.55f));
                Sprite("headlight_cone", new Vector2(x, y), 0f, Sorting2D.Effects, 870, coneScale, car.transform, new Color(1, 0.97f, 0.85f, 0.55f));
            }
            return car;
        }

        private static GameObject Person(string sprite, Vector2 pos, float faceDeg)
        {
            var p = Sprite(sprite, pos, faceDeg, Sorting2D.People, 30);
            Sprite("person_shadow", new Vector2(0.08f, -0.1f), 0, Sorting2D.Shadows, 10, 1f, p.transform);
            // The long-gun sprite only has the arms raised; in game the rifle is a separate dark bar
            // under them (see OfficerEquipment2D.PlaceHeldItem). Draw the same bar here.
            if (sprite.StartsWith("officer_longgun")) Rifle(p.transform);
            return p;
        }

        private static Sprite _pixel;

        /// <summary>Rifle along the right side, stock at the shoulder, muzzle ahead (same numbers as in game).</summary>
        private static void Rifle(Transform officer)
        {
            if (_pixel == null)
            {
                var tex = new Texture2D(2, 2) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
                tex.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white }); tex.Apply();
                _pixel = UnityEngine.Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), 2f);
                _pixel.hideFlags = HideFlags.HideAndDontSave;
            }
            var body = officer.GetComponent<SpriteRenderer>();
            Vector2 size = body != null && body.sprite != null ? (Vector2)body.sprite.bounds.size : new Vector2(2f, 2f);
            var go = new GameObject("rifle");
            go.transform.SetParent(officer, false);
            go.transform.localPosition = new Vector2((38f / 64f - 0.5f) * size.x, (0.5f - 9f / 64f) * size.y);
            go.transform.localScale = new Vector3(3.2f / 64f * size.x, 46f / 64f * size.y, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _pixel;
            sr.color = new Color(0.09f, 0.09f, 0.1f);
            Sorting2D.Apply(sr, Sorting2D.People, 29);   // under the arms, like in game
        }

        private static readonly List<Renderer> _hidden = new List<Renderer>();

        public static void Clear()
        {
            if (_root != null) Object.Destroy(_root);
            var old = GameObject.Find("Menu Slide Stage");
            if (old != null) Object.Destroy(old);
            foreach (var r in _hidden) if (r != null) r.enabled = true;
            _hidden.Clear();
            var cam = Camera.main;
            if (cam != null && cam.TryGetComponent(out Camera2DFollow follow)) follow.enabled = true;
        }

        // ------------------------------------------------------------------ scenes

        /// <summary>Builds scene <paramref name="i"/> and points the camera at it. Play mode only.</summary>
        public static string Stage(int i)
        {
            if (!Application.isPlaying) return "Enter play mode first.";
            Clear();
            _root = new GameObject("Menu Slide Stage");

            // Keep the player's officer out of shot.
            var officer = Object.FindAnyObjectByType<Officer2D>();
            if (officer != null) foreach (var r in officer.GetComponentsInChildren<Renderer>()) if (r.enabled) { r.enabled = false; _hidden.Add(r); }

            Shot shot = Build(i);
            if (Services.TryGet(out ITimeOfDay clock)) clock.SetTime(shot.Hour);
            var cam = Camera.main;
            if (cam.TryGetComponent(out Camera2DFollow follow)) follow.enabled = false;
            cam.transform.SetPositionAndRotation(new Vector3(shot.Centre.x, shot.Centre.y, -20f), Quaternion.Euler(0, 0, shot.Tilt));
            cam.orthographicSize = shot.Size;
            return $"Staged {i} {shot.Name} at {shot.Centre}";
        }

        private static Shot Build(int i)
        {
            switch (Names[i])
            {
                case "traffic_stop":
                {
                    // Exchange Place, eastbound curb lane: cruiser behind a pulled-over sedan, officer at the driver's window.
                    float y = 30 * T + T * 0.5f;
                    Car("car_sedan_maroon", new Vector2(210f, y), 0f);
                    Car("car_police_cruiser", new Vector2(200.5f, y), 0f, lights: true, headlights: true);
                    Person("officer_armed", new Vector2(208.6f, y + 1.85f), -100f);   // night stop: pistol out
                    Car("car_suv_silver", new Vector2(186f, 33 * T + 2f), 180f, headlights: true);   // passing traffic, westbound
                    return new Shot { Name = "traffic_stop", Hour = 22.5f, Centre = new Vector2(206f, y + 2.5f), Size = 8.5f, Tilt = -4f };
                }
                case "swat_raid":
                {
                    // A Midtown apartment door: SWAT stack on the wall, armoured truck and cruisers in the street.
                    var loc = Object.FindAnyObjectByType<CityLocations>();
                    var b = loc.Database.Buildings.Where(x => x.Type == "apartment" && x.District == "Midtown" && x.Footprint.width >= 12f && x.Footprint.height >= 10f).OrderBy(x => x.Id).Skip(3).First();
                    Vector2 door = InteriorManager2D.DoorPoint(b);
                    Vector2 c = b.Footprint.center;
                    Vector2 outv = door - c;
                    outv = Mathf.Abs(outv.x / b.Footprint.width) > Mathf.Abs(outv.y / b.Footprint.height) ? new Vector2(Mathf.Sign(outv.x), 0) : new Vector2(0, Mathf.Sign(outv.y));
                    Vector2 along = new Vector2(-outv.y, outv.x);
                    float faceIn = Mathf.Atan2(-outv.y, -outv.x) * Mathf.Rad2Deg;
                    for (int k = 0; k < 5; k++)   // stacked along the wall, first man at the door
                        Person("officer_longgun", door + outv * 0.9f + along * (1.3f + k * 1.05f), faceIn + 70f - k * 6f);
                    Person("officer_longgun", door + outv * 3.2f - along * 2.4f, faceIn + 15f);   // cover man
                    float face = Mathf.Atan2(along.y, along.x) * Mathf.Rad2Deg;
                    Car("pd_bearcat", door + outv * 9.5f + along * 1.5f, face);
                    Car("car_police_cruiser", door + outv * 9.8f + along * 10f, face + 25f, lights: true);
                    Car("pd_suv", door + outv * 9.6f - along * 8f, face - 15f, lights: true);
                    return new Shot { Name = "swat_raid", Hour = 6.4f, Centre = door + outv * 5f + along * 1.5f, Size = 12f, Tilt = 0f };
                }
                case "foot_pursuit":
                {
                    // Garnet Street north sidewalk: suspect sprinting east, officer right behind, cruiser abandoned at the kerb.
                    float y = 57 * T + 2f;
                    Person("civ_hoodie_grey_walk_a", new Vector2(286f, y + 0.3f), 8f);
                    Person("officer_armed_walk_b", new Vector2(280.5f, y - 0.2f), 5f);
                    Car("car_police_cruiser", new Vector2(272f, 56 * T + 2f), 15f, lights: true);
                    Person("civ_suit_navy", new Vector2(291f, y + 1.3f), 200f);   // bystander stepping aside
                    Person("civ_dress_purple", new Vector2(276f, y + 1.1f), -40f);
                    return new Shot { Name = "foot_pursuit", Hour = 15.5f, Centre = new Vector2(282f, y - 2f), Size = 10f, Tilt = 0f };
                }
                case "vehicle_pursuit":
                {
                    // Grand Boulevard northbound at night: suspect car, two units in pursuit, the helicopter's spotlight on top.
                    float x0 = 62 * T + 2f, x1 = 63 * T + 2f;
                    Car("car_sedan_gunmetal", new Vector2(x1, 612f), 92f, headlights: true);
                    Car("car_police_cruiser", new Vector2(x0 + 0.6f, 601f), 95f, lights: true, headlights: true);
                    Car("pd_interceptor", new Vector2(x1 + 1.5f, 591f), 88f, lights: true, headlights: true);
                    Car("car_compact_white", new Vector2(60 * T + 2f, 596f), 270f, headlights: true);   // oncoming traffic
                    Sprite("glow_white", new Vector2(x1 - 0.5f, 613f), 0, Sorting2D.Effects, 860, 4.2f, null, new Color(1, 1, 0.92f, 0.55f));   // spotlight
                    var heli = Sprite("pd_helicopter", new Vector2(x1 - 6f, 620f), 80f, Sorting2D.Effects, 950, 1.0f);
                    Sprite("pd_helicopter_rotor", Vector2.zero, 33f, Sorting2D.Effects, 951, 1f, heli.transform, new Color(1, 1, 1, 0.75f));
                    Sprite("car_shadow", new Vector2(4f, -5f), 0, Sorting2D.Shadows, 9, 2.2f, heli.transform, new Color(1, 1, 1, 0.5f));
                    return new Shot { Name = "vehicle_pursuit", Hour = 23.5f, Centre = new Vector2(x1 - 1f, 604f), Size = 13f, Tilt = -8f };
                }
                case "collision":
                {
                    // Park Avenue at 20th Street: T-bone in the junction, cruiser blocking, officer with the drivers.
                    Vector2 j = new Vector2(78 * T + 2f, 75 * T + 2f);
                    Car("car_pickup_red", j + new Vector2(-1.5f, -1f), 62f);
                    Car("car_compact_blue", j + new Vector2(1.4f, 1.6f), 155f);
                    Car("car_police_cruiser", j + new Vector2(-9f, -7f), 35f, lights: true);
                    Person("officer_unarmed", j + new Vector2(-3.8f, 3.2f), -20f);
                    Person("civ_jacket_brown", j + new Vector2(-2.6f, 4.6f), -130f);
                    Person("civ_vest_orange", j + new Vector2(4.6f, -0.8f), 160f);
                    Sprite("glow_white", j + new Vector2(0.2f, 0.4f), 0, Sorting2D.Effects, 700, 1.6f, null, new Color(0.7f, 0.7f, 0.7f, 0.35f));   // smoke/steam
                    return new Shot { Name = "collision", Hour = 18.6f, Centre = j + new Vector2(-1.5f, 0f), Size = 11f, Tilt = 6f };
                }
                case "felony_stop":
                {
                    // Park Avenue at night: high-risk stop, two cruisers angled behind, officers covering, suspect prone.
                    float x = 79 * T + 2f, y = 392f;
                    Car("car_suv_black", new Vector2(x, y), 90f);
                    Car("car_police_cruiser", new Vector2(x - 2.3f, y - 10f), 70f, lights: true, headlights: true);
                    Car("pd_interceptor", new Vector2(x + 2.4f, y - 10.5f), 110f, lights: true, headlights: true);
                    Person("officer_armed", new Vector2(x - 4.3f, y - 8.2f), 75f);
                    Person("officer_longgun", new Vector2(x + 4.5f, y - 8.6f), 100f);
                    Sprite("civ_jacket_brown_down", new Vector2(x - 0.4f, y - 4.4f), 90f, Sorting2D.People, 29);
                    return new Shot { Name = "felony_stop", Hour = 1.5f, Centre = new Vector2(x, y - 5f), Size = 11f, Tilt = 0f };
                }
                default:   // "bridge_patrol"
                {
                    // Morning patrol over the east bridge to Eastport.
                    float y = 96 * T + 2f;
                    Car("car_police_cruiser", new Vector2(128f * T, y - 2f), 0f);
                    Car("car_suv_white", new Vector2(132f * T, y + 6f), 180f);
                    Car("truck_box_yellow", new Vector2(124f * T, y + 6f), 180f);
                    return new Shot { Name = "bridge_patrol", Hour = 7.4f, Centre = new Vector2(129f * T, y), Size = 15f, Tilt = 0f };
                }
            }
        }

        // ------------------------------------------------------------------ capture

        /// <summary>Renders the game camera (no HUD) into a 16:9 PNG under Resources/MenuSlides.</summary>
        public static string Capture(int i, int width = 2048, int height = 1152)
        {
            var cam = Camera.main;
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var oldTarget = cam.targetTexture;
            cam.targetTexture = rt;
            cam.aspect = (float)width / height;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = oldTarget;
            cam.ResetAspect();
            Object.Destroy(rt);

            Directory.CreateDirectory(Folder);
            string path = $"{Folder}/slide_{i:00}_{Names[i]}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.Destroy(tex);
            return path;
        }

        /// <summary>Import settings for the captured slides (run in edit mode after capturing).</summary>
        public static void ConfigureImports()
        {
            AssetDatabase.Refresh();
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Folder }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(p) is TextureImporter ti)) continue;
                ti.textureType = TextureImporterType.Default;
                ti.mipmapEnabled = false;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Bilinear;
                ti.maxTextureSize = 2048;
                // Normal quality: the slides are opaque photos, so this is DXT1 on PC / ASTC on iPhone,
                // half the size of HQ (BC7) with no visible difference behind the menu.
                ti.textureCompression = TextureImporterCompression.Compressed;
                ti.SaveAndReimport();
            }
        }

        [MenuItem("Tools/GPA/Menu slides/Clear stage")]
        private static void ClearMenu() => Clear();
    }
}

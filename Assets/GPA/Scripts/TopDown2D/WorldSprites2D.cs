using UnityEngine;

namespace GPA.TopDown2D
{
    /// <summary>
    /// Sprite set used to spawn people and cars at runtime (callouts, traffic). One per scene,
    /// filled in by the scene builder. Mods can swap entries to re-skin spawned actors.
    /// </summary>
    public sealed class WorldSprites2D : MonoBehaviour
    {
        public static WorldSprites2D Instance { get; private set; }

        public Sprite[] Civilians = new Sprite[0];
        [Tooltip("Walk frames matching Civilians by index (step A / step B).")]
        public Sprite[] CivilianWalkA = new Sprite[0];
        public Sprite[] CivilianWalkB = new Sprite[0];
        public Sprite[] CivilianCars = new Sprite[0];
        [Tooltip("Bodies lying splayed on the ground, one per look (\"<look>_down\"), plus officer_down.")]
        public Sprite[] Downed = new Sprite[0];

        /// <summary>The lying-down sprite for a body sprite (civ_coat_green_walk_a -> civ_coat_green_down).</summary>
        public Sprite DownedFor(Sprite body)
        {
            if (body == null) return null;
            string n = body.name.Replace("_walk_a", "").Replace("_walk_b", "");
            if (n.StartsWith("officer")) n = "officer";
            foreach (var s in Downed) if (s != null && s.name == n + "_down") return s;
            return null;
        }
        public Sprite CarShadow;
        public Sprite PersonShadow;
        public Sprite GlowRed;
        public Sprite GlowBlue;

        public const int ShadowOrder = 10, CarOrder = 20, PersonOrder = 30, LightOrder = 40;

        private void Awake() => Instance = this;
        private void OnDestroy() { if (Instance == this) Instance = null; }

        public Sprite RandomCivilian() => Civilians.Length > 0 ? Civilians[Random.Range(0, Civilians.Length)] : null;
        public Sprite RandomCivilianCar() => CivilianCars.Length > 0 ? CivilianCars[Random.Range(0, CivilianCars.Length)] : null;

        /// <summary>Spawns a pedestrian with a shadow, collider and <see cref="Ped2D"/>.</summary>
        public Ped2D SpawnPed(Vector2 position, float heading, Sprite sprite = null, int look = -1)
        {
            if (sprite == null && Civilians.Length > 0)
            {
                look = Random.Range(0, Civilians.Length);
                sprite = Civilians[look];
            }
            var go = new GameObject("Ped");
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0, 0, heading));
            go.AddComponent<Rigidbody2D>();
            go.AddComponent<CircleCollider2D>().radius = 0.32f;
            AddSprite(go.transform, PersonShadow, new Vector2(0.08f, -0.1f), ShadowOrder);
            GameObject body = AddSprite(go.transform, sprite, Vector2.zero, PersonOrder);
            if (look >= 0 && look < CivilianWalkA.Length && look < CivilianWalkB.Length)
            {
                var anim = body.AddComponent<SpriteAnimator2D>();
                anim.Idle = sprite;
                anim.StepA = CivilianWalkA[look];
                anim.StepB = CivilianWalkB[look];
            }
            return go.AddComponent<Ped2D>();
        }

        /// <summary>Spawns a drivable car (parked until something takes its controls).</summary>
        public Car2D SpawnCar(Vector2 position, float heading, Sprite sprite = null, string label = "Car")
        {
            var go = new GameObject(label);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0, 0, heading));
            go.AddComponent<Rigidbody2D>();
            go.AddComponent<BoxCollider2D>().size = new Vector2(2.3f, 4.6f);
            AddSprite(go.transform, CarShadow, new Vector2(0.18f, -0.22f), ShadowOrder);
            var body = AddSprite(go.transform, sprite != null ? sprite : RandomCivilianCar(), Vector2.zero, CarOrder);
            body.name = "Body";
            return go.AddComponent<Car2D>();
        }

        public static GameObject AddSprite(Transform parent, Sprite sprite, Vector2 localPosition, int order)
        {
            var go = new GameObject(sprite != null ? sprite.name : "Sprite");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            Sorting2D.Apply(sr, Sorting2D.LayerForLegacyOrder(order), order);
            return go;
        }
    }
}

using GPA.Core;
using GPA.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GPA.TopDown2D
{
    /// <summary>
    /// Mouse-aim crosshair drawn where the (hidden) OS cursor is, while playing on foot with
    /// keyboard and mouse. The shape follows what you hold: a gap cross for guns (wider for the
    /// shotgun, kicks open on each shot), a yellow cross for the taser, a small ring otherwise.
    /// Turns red over a person. Hidden in menus, in vehicles, with the weapon wheel open and on
    /// controller.
    /// </summary>
    [DefaultExecutionOrder(500)]
    public sealed class Crosshair2D : MonoBehaviour
    {
        /// <summary>Test / screenshot hook: draw here instead of at the mouse.</summary>
        public static Vector2? DebugPosition;

        private Officer2D _officer;
        private OfficerEquipment2D _equipment;
        private IInputService _input;
        private Texture2D _pixel, _ring;
        private float _kick, _overTarget;
        private int _lastAmmo = -1;
        private readonly Collider2D[] _hits = new Collider2D[8];
        private ContactFilter2D _filter;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAnyObjectByType<Crosshair2D>() != null || FindAnyObjectByType<Officer2D>() == null) return;
            new GameObject("Crosshair").AddComponent<Crosshair2D>();
        }

        private void Awake()
        {
            _pixel = Texture2D.whiteTexture;
            _ring = MakeRing(64, 0.78f, 1f);
            _filter = ContactFilter2D.noFilter;
        }

        private void OnDestroy() { if (_ring != null) Destroy(_ring); }

        private bool Visible(out Vector2 mouse)
        {
            mouse = default;
            if (_officer == null) { _officer = FindAnyObjectByType<Officer2D>(); if (_officer == null) return false; }
            if (_equipment == null) _equipment = OfficerEquipment2D.Instance;
            if (_input == null) Services.TryGet(out _input);
            if (Mouse.current == null || Cursor.visible || (!Application.isFocused && DebugPosition == null)) return false;   // menus show the OS cursor
            if (_input != null && _input.ActiveFamily != InputDeviceFamily.KeyboardMouse) return false;
            if (_officer.CurrentVehicle != null || !_officer.isActiveAndEnabled) return false;
            if (_equipment != null && _equipment.WheelOpen) return false;
            mouse = DebugPosition ?? Mouse.current.position.ReadValue();
            return true;
        }

        private void Update()
        {
            if (!Visible(out Vector2 mouse)) return;
            // Kick open when a round is fired (ammo text changes while a gun is out).
            int ammo = AmmoCount();
            if (ammo >= 0 && _lastAmmo >= 0 && ammo < _lastAmmo) _kick = 1f;
            _lastAmmo = ammo;
            _kick = Mathf.MoveTowards(_kick, 0f, Time.unscaledDeltaTime * 5f);

            // Red over a person.
            bool hit = false;
            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector2 world = cam.ScreenToWorldPoint(new Vector3(mouse.x, mouse.y, -cam.transform.position.z));
                int n = Physics2D.OverlapCircle(world, 0.45f, _filter, _hits);
                for (int i = 0; i < n; i++)
                    if (_hits[i] != null && _hits[i].GetComponentInParent<Ped2D>() != null) { hit = true; break; }
            }
            _overTarget = Mathf.MoveTowards(_overTarget, hit ? 1f : 0f, Time.unscaledDeltaTime * 10f);
        }

        private int AmmoCount()
        {
            if (_equipment == null) return -1;
            switch (_equipment.Equipped)
            {
                case Gear.Pistol: return _equipment.PistolMag;
                case Gear.Rifle: return _equipment.RifleMag;
                case Gear.Shotgun: return _equipment.ShotgunMag;
                case Gear.Taser: return _equipment.TaserShots;
                default: return -1;
            }
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || !Visible(out Vector2 mouse)) return;
            GUI.depth = -1000;
            float s = Mathf.Max(0.75f, Screen.height / 1080f);
            Vector2 c = new Vector2(mouse.x, Screen.height - mouse.y);

            var gear = _equipment != null ? _equipment.Equipped : Gear.Unarmed;
            bool gun = gear == Gear.Pistol || gear == Gear.Rifle || gear == Gear.Shotgun;
            bool taser = gear == Gear.Taser;

            Color baseCol = taser ? new Color(1f, 0.88f, 0.3f) : Color.white;
            Color col = Color.Lerp(baseCol, new Color(1f, 0.25f, 0.2f), _overTarget);
            Color shadow = new Color(0f, 0f, 0f, 0.65f);
            bool reloading = _equipment != null && _equipment.Reloading;
            if (reloading) col.a = 0.45f;

            if (gun || taser)
            {
                float gap = (gear == Gear.Shotgun ? 13f : gear == Gear.Rifle ? 6f : 8f) + _kick * 9f;
                float len = 10f, th = 2.5f;
                gap *= s; len *= s; th *= s;
                for (int pass = 0; pass < 2; pass++)
                {
                    float o = pass == 0 ? 1.2f * s : 0f;           // shadow first, offset down-right
                    GUI.color = pass == 0 ? shadow : col;
                    Bar(c.x - th / 2 + o, c.y - gap - len + o, th, len);   // up
                    Bar(c.x - th / 2 + o, c.y + gap + o, th, len);         // down
                    Bar(c.x - gap - len + o, c.y - th / 2 + o, len, th);   // left
                    Bar(c.x + gap + o, c.y - th / 2 + o, len, th);         // right
                    Bar(c.x - th / 2 + o, c.y - th / 2 + o, th, th);       // centre dot
                }
                if (gear == Gear.Shotgun)
                {
                    float r = (gap + len * 0.5f) * 2f;
                    GUI.color = new Color(col.r, col.g, col.b, col.a * 0.35f);
                    GUI.DrawTexture(new Rect(c.x - r / 2, c.y - r / 2, r, r), _ring);
                }
                if (reloading)
                {
                    // Reload progress as a bar under the crosshair.
                    float w = 34f * s, h = 3f * s, y = c.y + gap + len + 6f * s;
                    GUI.color = shadow; GUI.DrawTexture(new Rect(c.x - w / 2, y, w, h), _pixel);
                    GUI.color = Color.white; GUI.DrawTexture(new Rect(c.x - w / 2, y, w * _equipment.ReloadProgress, h), _pixel);
                }
            }
            else
            {
                float r = 18f * s;
                GUI.color = shadow; GUI.DrawTexture(new Rect(c.x - r / 2 + s, c.y - r / 2 + s, r, r), _ring);
                GUI.color = new Color(col.r, col.g, col.b, 0.9f); GUI.DrawTexture(new Rect(c.x - r / 2, c.y - r / 2, r, r), _ring);
                float d = 3f * s;
                GUI.DrawTexture(new Rect(c.x - d / 2, c.y - d / 2, d, d), _pixel);
            }
            GUI.color = Color.white;
        }

        private void Bar(float x, float y, float w, float h) => GUI.DrawTexture(new Rect(x, y, w, h), _pixel);

        private static Texture2D MakeRing(int size, float inner, float outer)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            float h = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - h) * (x + 0.5f - h) + (y + 0.5f - h) * (y + 0.5f - h)) / h;
                    float a = Mathf.Clamp01((d - inner) * h) * Mathf.Clamp01((outer - d) * h);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            t.SetPixels32(px);
            t.Apply(false, true);
            return t;
        }
    }
}

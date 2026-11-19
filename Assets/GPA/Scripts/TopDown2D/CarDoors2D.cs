using UnityEngine;

namespace GPA.TopDown2D
{
    /// <summary>
    /// Animated car doors seen from above. Each door is cut out of the car's own body sprite (so it
    /// has the right paint and window), hinged at its front edge, and swings outward when someone
    /// gets in or out. While a door is open, a dark gap shows the cabin behind it.
    /// Doors are hidden while closed, so the car looks exactly as before until one opens.
    ///
    /// Use: <c>CarDoors2D.Swing(car, CarDoors2D.Door.DriverFront);</c> (the component adds itself).
    /// Helicopters and motorcycles have no doors, so the call does nothing for them.
    /// </summary>
    [RequireComponent(typeof(Car2D))]
    public sealed class CarDoors2D : MonoBehaviour
    {
        public enum Door { DriverFront, PassengerFront, DriverRear, PassengerRear }

        private const float OpenAngle = 68f;        // degrees the door swings out
        private const float OpenTime = 0.18f;       // seconds to swing open
        private const float CloseTime = 0.22f;      // seconds to swing shut
        private const string ChildName = "CarDoor";

        private static Sprite _dot;
        private static readonly Color Cabin = new Color(0.07f, 0.07f, 0.09f, 0.95f);
        private static readonly Color FallbackPaint = new Color(0.2f, 0.21f, 0.24f, 1f);

        private enum Phase { Closed, Opening, Held, Closing }

        private sealed class Leaf
        {
            public Transform Hinge;
            public SpriteRenderer Panel, Gap;
            public float Side;                      // -1 driver (left), +1 passenger (right)
            public Phase Phase;
            public float T;                         // 0 = shut, 1 = fully open
            public float HoldLeft;
            public bool StayOpen;
        }

        private readonly Leaf[] _leaves = new Leaf[4];
        private Car2D _car;
        private SpriteRenderer _body;
        private Sprite _builtFor;

        // ------------------------------------------------------------------ public API

        /// <summary>Opens a door, keeps it open for <paramref name="hold"/> seconds, then shuts it.</summary>
        public static void Swing(Car2D car, Door door, float hold = 0.35f)
        {
            var doors = For(car);
            if (doors != null) doors.SwingDoor(door, hold);
        }

        /// <summary>Opens a door and leaves it open until <see cref="Shut"/> is called.</summary>
        public static void Open(Car2D car, Door door)
        {
            var doors = For(car);
            if (doors == null || !doors.Ready()) return;
            Leaf l = doors._leaves[(int)door];
            l.StayOpen = true;
            if (l.Phase == Phase.Closed || l.Phase == Phase.Closing) doors.StartOpening(l);
        }

        /// <summary>Shuts a door that was opened with <see cref="Open"/>.</summary>
        public static void Shut(Car2D car, Door door)
        {
            var doors = car != null ? car.GetComponent<CarDoors2D>() : null;
            if (doors == null || doors._leaves[(int)door] == null) return;
            Leaf l = doors._leaves[(int)door];
            l.StayOpen = false;
            if (l.Phase == Phase.Opening || l.Phase == Phase.Held) { l.Phase = Phase.Held; l.HoldLeft = 0f; }
        }

        private static CarDoors2D For(Car2D car)
        {
            if (car == null || car.Hover || (car.Spec != null && car.Spec.Motorcycle)) return null;
            var doors = car.GetComponent<CarDoors2D>();
            return doors != null ? doors : car.gameObject.AddComponent<CarDoors2D>();
        }

        // ------------------------------------------------------------------ setup

        private void Awake()
        {
            _car = GetComponent<Car2D>();
            if (_dot == null)
            {
                var tex = new Texture2D(4, 4) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
                var px = new Color[16];
                for (int i = 0; i < 16; i++) px[i] = Color.white;
                tex.SetPixels(px); tex.Apply();
                _dot = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
                _dot.hideFlags = HideFlags.HideAndDontSave;
            }
        }

        private void SwingDoor(Door door, float hold)
        {
            if (!Ready()) return;
            Leaf l = _leaves[(int)door];
            l.HoldLeft = Mathf.Max(l.HoldLeft, hold);
            if (l.Phase == Phase.Closed || l.Phase == Phase.Closing) StartOpening(l);
        }

        private void StartOpening(Leaf l)
        {
            if (l.Phase == Phase.Closed) PlayDoorSound(false);   // latch click
            l.Phase = Phase.Opening;
            l.Panel.enabled = l.Gap.enabled = true;
        }

        /// <summary>Builds (or rebuilds, after a re-skin) the four doors from the current body sprite.</summary>
        private bool Ready()
        {
            if (_body == null) _body = FindBody();
            if (_body == null) return false;
            if (_leaves[0] != null && _builtFor == _body.sprite) return true;
            Build();
            return _leaves[0] != null;
        }

        private SpriteRenderer FindBody()
        {
            SpriteRenderer biggest = null; float area = 0f;
            foreach (var sr in GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sr.name == "Body") return sr;
                if (sr.name.StartsWith(ChildName) || sr.sprite == null) continue;
                Vector3 s = sr.bounds.size;
                if (s.x * s.y > area) { area = s.x * s.y; biggest = sr; }
            }
            return biggest;
        }

        private void Build()
        {
            // Clear old doors (including copies cloned from another car).
            for (int i = transform.childCount - 1; i >= 0; i--)
                if (transform.GetChild(i).name.StartsWith(ChildName)) Destroy(transform.GetChild(i).gameObject);
            _builtFor = _body.sprite;

            // Car size in car-local metres: the collider matches the catalog size; fall back to the sprite.
            float w = 2.3f, len = 4.6f;
            var col = GetComponent<BoxCollider2D>();
            if (col != null) { w = col.size.x / 0.92f; len = col.size.y / 0.96f; }
            else if (_body.sprite != null) { w = _body.sprite.bounds.size.x * _body.transform.localScale.x; len = _body.sprite.bounds.size.y * _body.transform.localScale.y; }

            float thick = w * 0.18f;                      // outline + paint + window edge, seen from above
            float frontTop = len * 0.26f, split = len * 0.02f, rearBottom = -len * 0.22f;

            _leaves[(int)Door.DriverFront] = MakeLeaf(-1f, frontTop, split, w, thick);
            _leaves[(int)Door.PassengerFront] = MakeLeaf(1f, frontTop, split, w, thick);
            _leaves[(int)Door.DriverRear] = MakeLeaf(-1f, split, rearBottom, w, thick);
            _leaves[(int)Door.PassengerRear] = MakeLeaf(1f, split, rearBottom, w, thick);
        }

        private Leaf MakeLeaf(float side, float top, float bottom, float width, float thick)
        {
            float doorLen = top - bottom;
            float outer = side * width * 0.5f;              // outside edge of the car
            float inner = outer - side * thick;
            int order = _body.sortingOrder;

            // Dark cabin gap, drawn over the body where the door normally sits.
            var gapGo = new GameObject(ChildName + " Gap");
            gapGo.transform.SetParent(transform, false);
            gapGo.transform.localPosition = new Vector3((outer + inner) * 0.5f, (top + bottom) * 0.5f, 0f);
            gapGo.transform.localScale = new Vector3(thick, doorLen, 1f);
            var gap = gapGo.AddComponent<SpriteRenderer>();
            gap.sprite = _dot;
            gap.color = Cabin;
            gap.sortingLayerID = _body.sortingLayerID;
            gap.sortingOrder = order + 1;
            gap.enabled = false;

            // Hinge at the door's front outer corner; the panel hangs back from it.
            var hinge = new GameObject(ChildName + " Hinge").transform;
            hinge.SetParent(transform, false);
            hinge.localPosition = new Vector3(outer, top, 0f);

            var panelGo = new GameObject(ChildName + " Panel");
            panelGo.transform.SetParent(hinge, false);
            var panel = panelGo.AddComponent<SpriteRenderer>();
            panel.sortingLayerID = _body.sortingLayerID;
            panel.sortingOrder = order + 2;
            panel.enabled = false;

            Sprite slice = SliceBody(side, top, bottom, outer, inner);
            if (slice != null)
            {
                panel.sprite = slice;
                panel.color = _body.color;
                panelGo.transform.localScale = _body.transform.localScale;
            }
            else
            {
                // Couldn't cut the body sprite (packed atlas etc.): plain painted panel.
                panel.sprite = _dot;
                panel.color = FallbackPaint;
                panelGo.transform.localPosition = new Vector3(-side * thick * 0.5f, -doorLen * 0.5f, 0f);
                panelGo.transform.localScale = new Vector3(thick, doorLen, 1f);
            }

            return new Leaf { Hinge = hinge, Panel = panel, Gap = gap, Side = side };
        }

        /// <summary>Cuts the door strip out of the body sprite, pivoted at the hinge corner.</summary>
        private Sprite SliceBody(float side, float top, float bottom, float outer, float inner)
        {
            Sprite s = _body.sprite;
            if (s == null || s.texture == null) return null;
            // Sprites in an atlas can be cut only if they're packed as plain, unrotated rectangles.
            if (s.packed && (s.packingMode != SpritePackingMode.Rectangle || s.packingRotation != SpritePackingRotation.None)) return null;
            try
            {
                Vector3 bs = _body.transform.localScale, bp = _body.transform.localPosition;
                if (Mathf.Approximately(bs.x, 0f) || Mathf.Approximately(bs.y, 0f)) return null;
                float ppu = s.pixelsPerUnit;
                Rect r = s.textureRect;                   // where the sprite's pixels sit in the (atlas) texture
                Vector2 off = s.textureRectOffset;        // trimmed-away border, in sprite pixels

                // Car-local metres → sprite pixels (measured from the sprite's corner) → texture pixels.
                float x0 = (Mathf.Min(outer, inner) - bp.x) / bs.x, x1 = (Mathf.Max(outer, inner) - bp.x) / bs.x;
                float y0 = (bottom - bp.y) / bs.y, y1 = (top - bp.y) / bs.y;
                float px0 = r.x + x0 * ppu + s.pivot.x - off.x, px1 = r.x + x1 * ppu + s.pivot.x - off.x;
                float py0 = r.y + y0 * ppu + s.pivot.y - off.y, py1 = r.y + y1 * ppu + s.pivot.y - off.y;
                px0 = Mathf.Clamp(px0, r.xMin, r.xMax); px1 = Mathf.Clamp(px1, r.xMin, r.xMax);
                py0 = Mathf.Clamp(py0, r.yMin, r.yMax); py1 = Mathf.Clamp(py1, r.yMin, r.yMax);
                var cut = Rect.MinMaxRect(Mathf.Floor(px0), Mathf.Floor(py0), Mathf.Ceil(px1), Mathf.Ceil(py1));
                if (cut.width < 1f || cut.height < 1f) return null;

                // Pivot = hinge: outer edge, top of the door.
                var pivot = new Vector2(side < 0 ? 0f : 1f, 1f);
                var slice = Sprite.Create(s.texture, cut, pivot, ppu, 0, SpriteMeshType.FullRect);
                slice.name = "Door slice";
                return slice;
            }
            catch (System.Exception) { return null; }
        }

        // ------------------------------------------------------------------ animation

        private void Update()
        {
            if (_leaves[0] == null) return;
            bool driving = Mathf.Abs(_car.ForwardSpeed) > 2f;   // pulling away slams any open door
            float dt = Time.deltaTime;

            foreach (Leaf l in _leaves)
            {
                if (l == null || l.Phase == Phase.Closed) continue;
                switch (l.Phase)
                {
                    case Phase.Opening:
                        l.T = Mathf.MoveTowards(l.T, 1f, dt / OpenTime);
                        if (l.T >= 1f) l.Phase = Phase.Held;
                        break;
                    case Phase.Held:
                        if (!l.StayOpen) l.HoldLeft -= dt;
                        if ((l.HoldLeft <= 0f && !l.StayOpen) || driving) { l.Phase = Phase.Closing; l.StayOpen = false; }
                        break;
                    case Phase.Closing:
                        l.T = Mathf.MoveTowards(l.T, 0f, dt / (driving ? CloseTime * 0.5f : CloseTime));
                        if (l.T <= 0f)
                        {
                            l.Phase = Phase.Closed;
                            l.HoldLeft = 0f;
                            l.Panel.enabled = l.Gap.enabled = false;
                            PlayDoorSound(true);      // the thunk of the door shutting
                        }
                        break;
                }
                if (driving && l.Phase == Phase.Opening) l.Phase = Phase.Closing;

                // Ease out when opening (quick swing that settles), ease in when closing (speeds up into the slam).
                float eased = l.Phase == Phase.Closing ? l.T * l.T : 1f - (1f - l.T) * (1f - l.T);
                // Driver side swings clockwise (outward to the left); passenger side anticlockwise.
                l.Hinge.localRotation = Quaternion.Euler(0f, 0f, l.Side * eased * OpenAngle);
            }
        }

        // ------------------------------------------------------------------ sound

        // car_door.wav holds two sounds: a soft latch click, then (~0.5 s later) the slam.
        // We split it once into two short clips so opening plays only the click and closing only the slam.
        private static AudioClip _splitSource, _click, _slam;
        private const float ClickVolume = 0.3f, SlamVolume = 0.4f;

        private void PlayDoorSound(bool closing)
        {
            AudioClip src = GameAudio.Library != null ? GameAudio.Library.Door : null;
            if (src == null) return;
            if (src != _splitSource) SplitDoorClip(src);
            AudioClip clip = closing ? _slam : _click;
            if (clip != null) GameAudio.PlayAt(clip, transform.position, closing ? SlamVolume : ClickVolume, 0.06f);
        }

        private static void SplitDoorClip(AudioClip src)
        {
            _splitSource = src;
            _click = null;
            _slam = src;   // fallback: if we can't read the samples, play the whole clip on closing only
            try
            {
                int ch = src.channels, freq = src.frequency, frames = src.samples;
                var data = new float[frames * ch];
                if (!src.GetData(data, 0)) return;

                // Loudness per 10 ms window.
                int win = Mathf.Max(1, freq / 100);
                int windows = (frames + win - 1) / win;
                var level = new float[windows];
                for (int w = 0; w < windows; w++)
                {
                    float m = 0f;
                    int end = Mathf.Min((w + 1) * win, frames) * ch;
                    for (int i = w * win * ch; i < end; i++) m = Mathf.Max(m, Mathf.Abs(data[i]));
                    level[w] = m;
                }

                // The slam is the loudest moment; walk back to where it rises out of the silence.
                int loud = 0;
                for (int w = 1; w < windows; w++) if (level[w] > level[loud]) loud = w;
                float quiet = level[loud] * 0.06f;
                int slamStart = loud;
                while (slamStart > 0 && level[slamStart - 1] > quiet) slamStart--;
                slamStart = Mathf.Max(0, slamStart - 2);        // 20 ms of lead-in
                if (slamStart < 5) return;                       // no separate click in this clip: keep the fallback

                // The click runs from the start until it goes quiet (and never into the slam).
                int clickEnd = 1;
                while (clickEnd < slamStart && !(level[clickEnd] < quiet && (clickEnd + 1 >= slamStart || level[clickEnd + 1] < quiet))) clickEnd++;
                clickEnd = Mathf.Min(slamStart, clickEnd + 3);   // let the tail ring for 30 ms

                _click = Cut(src.name + " (click)", data, ch, freq, 0, clickEnd * win);
                _slam = Cut(src.name + " (slam)", data, ch, freq, slamStart * win, frames);
            }
            catch (System.Exception) { _click = null; _slam = src; }
        }

        /// <summary>Copies frames [from, to) into a new clip, fading the last 8 ms out so it ends without a pop.</summary>
        private static AudioClip Cut(string name, float[] data, int ch, int freq, int from, int to)
        {
            int len = Mathf.Max(1, to - from);
            var part = new float[len * ch];
            System.Array.Copy(data, from * ch, part, 0, Mathf.Min(part.Length, data.Length - from * ch));
            int fade = Mathf.Min(len, freq / 125);
            for (int f = 0; f < fade; f++)
            {
                float g = (float)f / fade;
                int frame = len - 1 - f;
                for (int c = 0; c < ch; c++) part[frame * ch + c] *= g;
            }
            var clip = AudioClip.Create(name, len, ch, freq, false);
            clip.SetData(part, 0);
            return clip;
        }

        private void OnDisable()
        {
            // Snap shut so a car that's pooled/disabled mid-swing doesn't come back with a door hanging open.
            foreach (Leaf l in _leaves)
            {
                if (l == null) continue;
                l.Phase = Phase.Closed; l.T = 0f; l.HoldLeft = 0f; l.StayOpen = false;
                if (l.Hinge != null) l.Hinge.localRotation = Quaternion.identity;
                if (l.Panel != null) l.Panel.enabled = false;
                if (l.Gap != null) l.Gap.enabled = false;
            }
        }
    }
}

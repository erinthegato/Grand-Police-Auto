using GPA.Callouts;
using GPA.Core;
using GPA.TopDown2D;
using UnityEngine;

namespace GPA.Game
{
    /// <summary>
    /// Starting the game and starting a shift.
    /// - Start game: the officer begins off duty — at their spawn home if they own one (and there
    ///   is no saved position), otherwise where they left off or outside the precinct — and the
    ///   camera drops in from high above (GTA V style).
    /// - Going on duty (O key / MDT) starts a shift with the same drop-in and a radio call.
    /// </summary>
    public sealed class ShiftDirector : MonoBehaviour
    {
        private bool _wasOnDuty;
        private bool _started;
        private float _fade;          // black fade-in at game start
        private float _lockUntil;
        private Officer2D _officer;
        private GUIStyle _banner;
        private string _bannerText;
        private float _bannerUntil;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAnyObjectByType<ShiftDirector>() != null || FindAnyObjectByType<Officer2D>() == null) return;
            new GameObject("Shift Director").AddComponent<ShiftDirector>();
        }

        private void Awake() => GameMenu2D.GameStarted += OnGameStarted;
        private void OnDestroy() => GameMenu2D.GameStarted -= OnGameStarted;

        private void Start()
        {
            _officer = FindAnyObjectByType<Officer2D>();
            if (GameMenu2D.HasStarted) OnGameStarted(false);   // the menu was skipped (editor play-testing)
        }

        private void OnGameStarted(bool continued)
        {
            if (_started) return;
            _started = true;
            if (_officer == null) _officer = FindAnyObjectByType<Officer2D>();
            var calls = CalloutManager.Instance;
            if (calls != null && calls.OnDuty) calls.SetDuty(false, announce: false);
            _wasOnDuty = false;

            // Wake up at home (unless continuing from a saved spot).
            Vector2? home = OffDutyWorld.Instance != null ? OffDutyWorld.Instance.HomeSpawn() : null;
            if (!continued && home.HasValue && _officer != null && _officer.CurrentCar == null)
            {
                _officer.transform.position = home.Value;
                if (_officer.TryGetComponent(out Rigidbody2D rb)) { rb.position = home.Value; rb.linearVelocity = Vector2.zero; }
            }
            _fade = 1f;
            DropIn("NEW HALDEN", "Off duty · press O to start a shift");
        }

        private void DropIn(string title, string subtitle)
        {
            Camera2DFollow.PlayIntro();
            _lockUntil = Time.unscaledTime + Camera2DFollow.IntroSeconds * 0.75f;
            if (_officer != null) _officer.SetControlLocked(this, true);
            _bannerText = $"<size=44><b>{title}</b></size>\n{subtitle}";
            _bannerUntil = Time.unscaledTime + Camera2DFollow.IntroSeconds + 1.5f;
            GameAudio.UiClick();
        }

        private void Update()
        {
            if (_officer != null && _lockUntil > 0f && Time.unscaledTime >= _lockUntil) { _officer.SetControlLocked(this, false); _lockUntil = 0f; }
            if (_fade > 0f) _fade = Mathf.MoveTowards(_fade, 0f, Time.unscaledDeltaTime * 1.4f);

            var calls = CalloutManager.Instance;
            bool onDuty = calls != null && calls.OnDuty;
            if (_started && onDuty && !_wasOnDuty)
            {
                string rank = Services.TryGet(out IReputationService r2) ? r2.Rank : "Officer";
                DropIn("SHIFT STARTED", $"{rank} · Unit 4127 · 10-8");
            }
            else if (_started && !onDuty && _wasOnDuty)
            {
                _bannerText = "<size=40><b>OFF DUTY</b></size>\nYour time is your own — homes, cars, the casino.";
                _bannerUntil = Time.unscaledTime + 3f;
            }
            _wasOnDuty = onDuty;
        }

        private void OnGUI()
        {
            bool banner = _bannerText != null && Time.unscaledTime < _bannerUntil;
            if (_fade <= 0f && !banner) return;
            Matrix4x4 old = UiScale.Begin(out Vector2 canvas);
            if (_fade > 0f)
            {
                GUI.color = new Color(0, 0, 0, _fade);
                GUI.DrawTexture(new Rect(0, 0, canvas.x, canvas.y), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }
            if (banner)
            {
                if (_banner == null) _banner = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter, richText = true, normal = { textColor = Color.white } };
                float a = Mathf.Clamp01(_bannerUntil - Time.unscaledTime);
                _banner.normal.textColor = new Color(1f, 1f, 1f, a);
                var r = new Rect(0, canvas.y * 0.68f, canvas.x, 110f);
                GUI.color = new Color(0, 0, 0, 0.45f * a); GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = Color.white;
                GUI.Label(r, _bannerText, _banner);
            }
            GUI.matrix = old;
        }
    }
}

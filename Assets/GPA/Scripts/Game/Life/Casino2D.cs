using System.Collections.Generic;
using GPA.Core;
using GPA.TopDown2D;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GPA.Game
{
    /// <summary>
    /// Harbor Lights Casino table games, drawn full screen over the world:
    /// - Slots: three reels, bet $50–$1,000. Three 7s pay 50×, three of a kind 10×, two cherries
    ///   3×, one cherry returns the bet.
    /// - Blackjack: one deck shuffled each hand, dealer stands on all 17s, blackjack pays 3:2,
    ///   double down on the first two cards.
    /// Keyboard, mouse, controller and touch.
    /// </summary>
    public sealed class Casino2D : MonoBehaviour
    {
        private enum Game { None, Slots, Blackjack }
        private enum BjState { Betting, Player, Dealer, Done }

        private static readonly int[] Bets = { 50, 100, 250, 500, 1000 };
        private static readonly string[] Symbols = { "7", "BAR", "♥", "★", "◆", "CHERRY" };
        private static readonly string[] SymbolNames = { "7", "BAR", "HEART", "STAR", "DIAMOND", "CHERRY" };
        private static readonly Color[] SymbolColours =
        {
            new Color(1f, 0.25f, 0.25f), new Color(0.3f, 0.3f, 0.35f), new Color(0.95f, 0.3f, 0.5f),
            new Color(1f, 0.82f, 0.2f), new Color(0.3f, 0.7f, 1f), new Color(0.85f, 0.1f, 0.2f),
        };

        public static Casino2D Instance { get; private set; }
        private Game _game;
        private int _bet = 1;
        private int _cursor;
        private string _status;
        private Officer2D _officer;
        private float _navRepeat;
        private int _openedFrame;

        // slots
        private readonly int[] _reels = { 0, 1, 2 };
        private float _spinUntil;
        private readonly float[] _reelStop = new float[3];
        private int _slotStake;

        // blackjack
        private BjState _bj;
        private readonly List<int> _deck = new List<int>(), _player = new List<int>(), _dealer = new List<int>();
        private int _bjStake;
        private float _dealerNext;

        private GUIStyle _big, _mid, _small, _card, _btn, _btnSel, _reel, _reelSmall;

        public static Casino2D Get()
        {
            if (Instance == null) Instance = new GameObject("Casino").AddComponent<Casino2D>();
            return Instance;
        }

        private static PlayerLife Life => PlayerLife.Instance;
        private int Bet => Bets[Mathf.Clamp(_bet, 0, Bets.Length - 1)];

        public void OpenSlots() { Open(Game.Slots); _status = "Pick a bet and spin."; }
        public void OpenBlackjack() { Open(Game.Blackjack); _bj = BjState.Betting; _player.Clear(); _dealer.Clear(); _status = "Place your bet."; }

        private void Open(Game g)
        {
            _game = g; _cursor = 0; _openedFrame = Time.frameCount;
            if (_officer == null) _officer = FindAnyObjectByType<Officer2D>();
            if (_officer != null) _officer.SetControlLocked(this, true);
            if (Services.TryGet(out IGameStateService st)) st.Push(GameState.Menu);
        }

        private void Close()
        {
            if (_game == Game.None) return;
            // Leaving mid-hand forfeits the stake (it was already taken).
            _game = Game.None;
            if (_officer != null) _officer.SetControlLocked(this, false);
            if (Services.TryGet(out IGameStateService st)) st.Pop(GameState.Menu);
            GameplayHotkeys.SuppressThisFrame();
        }

        // ------------------------------------------------------------------ input

        private List<(string label, System.Action act, bool enabled)> Buttons()
        {
            var b = new List<(string, System.Action, bool)>();
            bool canBet = Life != null && Life.CanAfford(Bet);
            if (_game == Game.Slots)
            {
                bool spinning = Time.unscaledTime < _spinUntil;
                b.Add(($"Bet ${Bet:N0}  ◀ ▶", () => _bet = (_bet + 1) % Bets.Length, !spinning));
                b.Add(("SPIN", Spin, !spinning && canBet));
                b.Add(("Leave", Close, !spinning));
            }
            else if (_game == Game.Blackjack)
            {
                if (_bj == BjState.Betting || _bj == BjState.Done)
                {
                    b.Add(($"Bet ${Bet:N0}  ◀ ▶", () => _bet = (_bet + 1) % Bets.Length, true));
                    b.Add(("DEAL", Deal, canBet));
                    b.Add(("Leave", Close, true));
                }
                else if (_bj == BjState.Player)
                {
                    b.Add(("HIT", Hit, true));
                    b.Add(("STAND", Stand, true));
                    b.Add(("DOUBLE", Double, _player.Count == 2 && Life != null && Life.CanAfford(_bjStake)));
                }
            }
            return b;
        }

        private void Update()
        {
            if (_game == Game.None) return;
            if (_game == Game.Blackjack && _bj == BjState.Dealer && Time.unscaledTime >= _dealerNext) DealerStep();

            var buttons = Buttons();
            if (Time.frameCount == _openedFrame || buttons.Count == 0) return;
            var kb = Keyboard.current; var pad = Gamepad.current;
            int dir = 0, side = 0;
            if (kb != null && (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame)) side = -1;
            if (kb != null && (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame)) side = 1;
            if (kb != null && (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame)) dir = -1;
            if (kb != null && (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame)) dir = 1;
            if (pad != null)
            {
                Vector2 v = pad.dpad.ReadValue() + pad.leftStick.ReadValue();
                if (v.magnitude < 0.5f) _navRepeat = 0f;
                else if (_navRepeat <= 0f) { if (Mathf.Abs(v.x) > Mathf.Abs(v.y)) side = v.x > 0 ? 1 : -1; else dir = v.y > 0 ? -1 : 1; _navRepeat = 0.22f; }
                _navRepeat -= Time.unscaledDeltaTime;
            }
            if (dir != 0) _cursor = (_cursor + dir + buttons.Count) % buttons.Count;
            _cursor = Mathf.Clamp(_cursor, 0, buttons.Count - 1);
            // Left / right on the bet row changes the bet.
            if (side != 0 && buttons[_cursor].label.StartsWith("Bet") && buttons[_cursor].enabled) _bet = Mathf.Clamp(_bet + side, 0, Bets.Length - 1);
            bool confirm = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame)) || (pad != null && pad.buttonSouth.wasPressedThisFrame);
            bool back = (kb != null && kb.escapeKey.wasPressedThisFrame) || (pad != null && pad.buttonEast.wasPressedThisFrame);
            if (confirm && buttons[_cursor].enabled) { buttons[_cursor].act(); GameAudio.UiClick(); }
            else if (back && (_game == Game.Slots ? Time.unscaledTime >= _spinUntil : _bj != BjState.Player && _bj != BjState.Dealer)) Close();
        }

        // ------------------------------------------------------------------ slots

        private void Spin()
        {
            if (Life == null || !Life.Spend(Bet, "Slots")) { _status = "Not enough money."; return; }
            _slotStake = Bet;
            for (int i = 0; i < 3; i++) { _reels[i] = Weighted(); _reelStop[i] = Time.unscaledTime + 1.0f + i * 0.45f; }
            _spinUntil = _reelStop[2];
            _status = "Spinning...";
            Invoke(nameof(SettleSlots), _spinUntil - Time.unscaledTime + 0.05f);
        }

        /// <summary>Common symbols come up more often; 7s and BARs are rare.</summary>
        private static int Weighted()
        {
            int[] weights = { 2, 3, 5, 5, 6, 5 };
            int total = 0; foreach (int w in weights) total += w;
            int r = Random.Range(0, total);
            for (int i = 0; i < weights.Length; i++) { r -= weights[i]; if (r < 0) return i; }
            return weights.Length - 1;
        }

        private void SettleSlots()
        {
            int a = _reels[0], b = _reels[1], c = _reels[2];
            int mult = 0; string why = "No win.";
            int cherries = (a == 5 ? 1 : 0) + (b == 5 ? 1 : 0) + (c == 5 ? 1 : 0);
            if (a == b && b == c) { mult = a == 0 ? 50 : 10; why = a == 0 ? "JACKPOT! Three 7s" : $"Three {SymbolNames[a]}s"; }
            else if (cherries == 2) { mult = 3; why = "Two cherries"; }
            else if (cherries == 1) { mult = 1; why = "Cherry — bet returned"; }
            int payout = _slotStake * mult;
            Life?.CasinoResult(_slotStake, payout);
            _status = payout > 0 ? $"{why}: you win ${payout:N0}" : why;
            if (payout > _slotStake) Life?.Toast($"+${payout - _slotStake:N0}  Slots", new Color(0.45f, 0.95f, 0.5f));
        }

        // ------------------------------------------------------------------ blackjack

        private void Deal()
        {
            if (Life == null || !Life.Spend(Bet, "Blackjack")) { _status = "Not enough money."; return; }
            _bjStake = Bet;
            _deck.Clear();
            for (int i = 0; i < 52; i++) _deck.Add(i);
            for (int i = _deck.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (_deck[i], _deck[j]) = (_deck[j], _deck[i]); }
            _player.Clear(); _dealer.Clear();
            _player.Add(Draw()); _dealer.Add(Draw()); _player.Add(Draw()); _dealer.Add(Draw());
            _bj = BjState.Player; _cursor = 0;
            _status = "Hit or stand?";
            if (Score(_player) == 21) Finish(Score(_dealer) == 21 ? "Push — both blackjack" : "BLACKJACK!", Score(_dealer) == 21 ? _bjStake : _bjStake + _bjStake * 3 / 2);
        }

        private int Draw() { int c = _deck[_deck.Count - 1]; _deck.RemoveAt(_deck.Count - 1); return c; }

        private void Hit()
        {
            _player.Add(Draw());
            int s = Score(_player);
            if (s > 21) Finish("Bust", 0);
            else if (s == 21) Stand();
        }

        private void Double()
        {
            if (Life == null || !Life.Spend(_bjStake, "Blackjack double")) return;
            _bjStake *= 2;
            _player.Add(Draw());
            if (Score(_player) > 21) Finish("Bust", 0); else Stand();
        }

        private void Stand() { _bj = BjState.Dealer; _dealerNext = Time.unscaledTime + 0.6f; _status = "Dealer's turn..."; }

        private void DealerStep()
        {
            if (Score(_dealer) < 17) { _dealer.Add(Draw()); _dealerNext = Time.unscaledTime + 0.7f; return; }
            int p = Score(_player), d = Score(_dealer);
            if (d > 21) Finish("Dealer busts — you win", _bjStake * 2);
            else if (p > d) Finish("You win", _bjStake * 2);
            else if (p == d) Finish("Push", _bjStake);
            else Finish("Dealer wins", 0);
        }

        private void Finish(string text, int payout)
        {
            _bj = BjState.Done; _cursor = 1;
            Life?.CasinoResult(_bjStake, payout);
            _status = payout > _bjStake ? $"{text}: +${payout - _bjStake:N0}" : payout == _bjStake ? $"{text}: bet returned" : $"{text}: -${_bjStake:N0}";
            if (payout > _bjStake) Life?.Toast($"+${payout - _bjStake:N0}  Blackjack", new Color(0.45f, 0.95f, 0.5f));
        }

        private static int Rank(int card) => card % 13;   // 0 = A, 1..9 = 2..10, 10 J, 11 Q, 12 K
        private static int Score(List<int> hand)
        {
            int total = 0, aces = 0;
            foreach (int c in hand) { int r = Rank(c); if (r == 0) { aces++; total += 11; } else total += Mathf.Min(r + 1, 10); }
            while (total > 21 && aces > 0) { total -= 10; aces--; }
            return total;
        }
        private static string CardText(int c) => new[] { "A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K" }[Rank(c)] + new[] { "♠", "♥", "♦", "♣" }[c / 13];
        private static bool Red(int c) => c / 13 == 1 || c / 13 == 2;

        // ------------------------------------------------------------------ drawing

        private void OnGUI()
        {
            if (_game == Game.None) return;
            Styles();
            GUI.depth = -150;
            Matrix4x4 old = UiScale.Begin(out Vector2 canvas);
            float w = canvas.x, h = canvas.y;
            GUI.color = new Color(0.02f, 0.18f, 0.1f, 0.96f); GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);   // felt
            GUI.color = new Color(0.85f, 0.7f, 0.25f); GUI.DrawTexture(new Rect(0, 0, w, 6), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(0, 24, w, 50), _game == Game.Slots ? "HARBOR LIGHTS · SLOTS" : "HARBOR LIGHTS · BLACKJACK", _big);
            GUI.Label(new Rect(w - 340, 30, 310, 40), Life != null ? $"${Life.Money:N0}" : "", _mid);

            if (_game == Game.Slots) DrawSlots(w, h); else DrawBlackjack(w, h);

            GUI.Label(new Rect(0, h * 0.62f, w, 40), _status ?? "", _mid);
            var buttons = Buttons();
            float bw = 260f, gap = 16f, total = buttons.Count * bw + (buttons.Count - 1) * gap;
            for (int i = 0; i < buttons.Count; i++)
            {
                var r = new Rect(w * 0.5f - total * 0.5f + i * (bw + gap), h * 0.72f, bw, 64f);
                bool sel = i == _cursor;
                GUI.color = !buttons[i].enabled ? new Color(1, 1, 1, 0.15f) : sel ? new Color(0.95f, 0.78f, 0.25f) : new Color(1, 1, 1, 0.12f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = Color.white;
                if (GUI.Button(r, GUIContent.none, GUIStyle.none) && buttons[i].enabled) { _cursor = i; buttons[i].act(); }
                GUI.Label(r, buttons[i].label, sel && buttons[i].enabled ? _btnSel : _btn);
            }
            string help = Gamepad.current != null ? "D-pad choose · ◀ ▶ change bet · A select · B leave" : "← → ↑ ↓ choose / change bet · Enter select · Esc leave";
            GUI.Label(new Rect(0, h - 50, w, 30), help, _small);
            GUI.matrix = old;
        }

        private void DrawSlots(float w, float h)
        {
            float size = 170f, gap = 24f, x0 = w * 0.5f - (size * 3 + gap * 2) * 0.5f, y = h * 0.25f;
            for (int i = 0; i < 3; i++)
            {
                var r = new Rect(x0 + i * (size + gap), y, size, size * 1.15f);
                GUI.color = new Color(0.95f, 0.95f, 0.9f); GUI.DrawTexture(r, Texture2D.whiteTexture);
                bool rolling = Time.unscaledTime < _reelStop[i];
                int sym = rolling ? (int)(Time.unscaledTime * 18f + i * 3) % Symbols.Length : _reels[i];
                _reel.normal.textColor = SymbolColours[sym];
                GUI.color = Color.white;
                if (sym == 5) { _reelSmall.normal.textColor = SymbolColours[sym]; GUI.Label(r, "CHERRY", _reelSmall); }
                else GUI.Label(r, Symbols[sym], _reel);
            }
            GUI.Label(new Rect(0, y + size * 1.2f + 10, w, 30), "7 7 7 pays 50×   ·   three of a kind 10×   ·   two cherries 3×   ·   one cherry returns your bet", _small);
        }

        private void DrawBlackjack(float w, float h)
        {
            bool hideHole = _bj == BjState.Player;
            DrawHand("DEALER" + (hideHole ? "" : $"  {Score(_dealer)}"), _dealer, hideHole, w, h * 0.16f);
            DrawHand(_player.Count > 0 ? $"YOU  {Score(_player)}" : "YOU", _player, false, w, h * 0.38f);
            if (_bjStake > 0 && _bj != BjState.Betting) GUI.Label(new Rect(w * 0.5f + 260, h * 0.38f + 40, 300, 40), $"Stake ${_bjStake:N0}", _mid);
        }

        private void DrawHand(string label, List<int> hand, bool hideSecond, float w, float y)
        {
            GUI.Label(new Rect(0, y - 6, w, 30), label, _small);
            float cw = 96f, ch = 136f, gap = 14f, x0 = w * 0.5f - (hand.Count * cw + (hand.Count - 1) * gap) * 0.5f;
            for (int i = 0; i < hand.Count; i++)
            {
                var r = new Rect(x0 + i * (cw + gap), y + 26, cw, ch);
                bool hidden = hideSecond && i == 1;
                GUI.color = hidden ? new Color(0.6f, 0.12f, 0.15f) : Color.white;
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = Color.white;
                if (!hidden) { _card.normal.textColor = Red(hand[i]) ? new Color(0.8f, 0.1f, 0.15f) : new Color(0.08f, 0.08f, 0.1f); GUI.Label(r, CardText(hand[i]), _card); }
            }
        }

        private void Styles()
        {
            if (_big != null) return;
            _big = new GUIStyle(GUI.skin.label) { fontSize = 36, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.95f, 0.82f, 0.4f) } };
            _mid = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            _small = new GUIStyle(GUI.skin.label) { fontSize = 17, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.75f, 0.85f, 0.78f) } };
            _card = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _reel = new GUIStyle(GUI.skin.label) { fontSize = 52, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _reelSmall = new GUIStyle(_reel) { fontSize = 28 };
            _btn = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            _btnSel = new GUIStyle(_btn) { normal = { textColor = new Color(0.1f, 0.08f, 0.02f) } };
        }

        private void OnDisable() { if (_game != Game.None) Close(); }
    }
}

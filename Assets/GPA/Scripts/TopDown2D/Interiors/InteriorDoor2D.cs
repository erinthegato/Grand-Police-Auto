using UnityEngine;

namespace GPA.TopDown2D
{
    /// <summary>A hinged interior door: blocks while shut, swings 90° when opened.</summary>
    public sealed class InteriorDoor2D : MonoBehaviour
    {
        public bool Open { get; private set; }
        public bool Locked;
        public System.Action Changed;

        private Transform _leaf;
        private BoxCollider2D _blocker;
        private Interactable2D _use;
        private float _angle, _target;
        private bool _forceArmed;

        public void Setup(Transform leaf, BoxCollider2D blocker, Interactable2D use, bool locked)
        {
            _leaf = leaf; _blocker = blocker; _use = use; Locked = locked;
            _use.ClearHandlers();
            _use.Used += _ => Toggle();
            Refresh();
        }

        /// <summary>Opens the door whatever its state (SWAT breaching, scripted scenes).</summary>
        public void ForceOpen()
        {
            Locked = false;
            _forceArmed = false;
            if (!Open) { Open = true; Refresh(); Changed?.Invoke(); }
        }

                public void SetLocked(bool locked)
        {
            Locked = locked;
            _forceArmed = false;
            if (locked && Open) { Open = false; }
            Refresh();
        }

        private void Toggle()
        {
            if (!Open && Locked)
            {
                if (!_forceArmed) { _forceArmed = true; _use.Prompt = "Locked — press again to force"; return; }
                Locked = false;
            }
            Open = !Open;
            _forceArmed = false;
            GameAudio.UiClick();
            Refresh();
            Changed?.Invoke();
        }

        private void Refresh()
        {
            _target = Open ? 90f : 0f;
            _blocker.enabled = !Open;
            _use.Prompt = Open ? "Close door" : Locked ? "Locked door" : "Open door";
        }

        private void Update()
        {
            if (Mathf.Approximately(_angle, _target)) return;
            _angle = Mathf.MoveTowards(_angle, _target, 540f * Time.deltaTime);
            _leaf.localRotation = Quaternion.Euler(0, 0, _angle);
        }
    }
}

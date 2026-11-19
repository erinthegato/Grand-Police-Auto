using UnityEngine;

namespace GPA.TopDown2D
{
    /// <summary>
    /// Every game sound in one asset (Assets/GPA/Resources/SoundLibrary.asset), loaded by
    /// <see cref="GameAudio"/>. Swap a clip here to change a sound everywhere. Arrays pick a random take.
    /// </summary>
    [CreateAssetMenu(menuName = "GPA/Sound Library")]
    public sealed class SoundLibrary : ScriptableObject
    {
        [Header("Weapons")]
        public AudioClip[] Pistol;
        public AudioClip[] Rifle;
        public AudioClip Shotgun;
        public AudioClip EmptyClick;
        public AudioClip Reload;
        public AudioClip TaserReload;
        public AudioClip Draw;
        public AudioClip BulletImpact;
        public AudioClip[] TaserFire;
        public AudioClip[] TaserArc;

        [Header("Less lethal and arrests")]
        public AudioClip BatonHit;
        public AudioClip PepperSpray;
        public AudioClip Handcuffs;

        [Header("People")]
        public AudioClip Footsteps;
        public AudioClip PedPain;

        [Header("Vehicles")]
        public AudioClip Engine;
        public AudioClip Skid;
        public AudioClip Crash;
        public AudioClip Door;
        public AudioClip CivilianHorn;
        public AudioClip PoliceHorn;
        public AudioClip SirenWail;
        public AudioClip SirenYelp;
        public AudioClip SirenPriority;

        [Header("World, radio and UI")]
        public AudioClip CityAmbience;
        public AudioClip RadioChirp;
        public AudioClip RadioStatic;
        public AudioClip UiClick;

        public static AudioClip Pick(AudioClip[] clips) =>
            clips == null || clips.Length == 0 ? null : clips[Random.Range(0, clips.Length)];
    }
}

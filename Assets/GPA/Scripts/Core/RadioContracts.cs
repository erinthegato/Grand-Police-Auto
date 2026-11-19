namespace GPA.Core
{
    /// <summary>
    /// The radio mix (spec §13): volume and mute for the in-game dispatch radio. The live internet
    /// police radio feed was removed; the dispatch radio only reads <see cref="DispatchGain"/>.
    /// </summary>
    public interface IRadioAudioManager
    {
        float DispatchVolume { get; set; }
        bool DispatchMuted { get; set; }
        /// <summary>Volume multiplier the dispatch radio applies to its own audio (0 when muted).</summary>
        float DispatchGain { get; }
    }
}

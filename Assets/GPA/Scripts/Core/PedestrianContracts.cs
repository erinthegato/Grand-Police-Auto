namespace GPA.Core
{
    /// <summary>The player's vehicle struck a pedestrian (reputation, dispatch, records can react).</summary>
    public readonly struct PedestrianStruckEvent
    {
        public readonly UnityEngine.Vector2 Position;
        public readonly bool Fatal;
        public PedestrianStruckEvent(UnityEngine.Vector2 position, bool fatal) { Position = position; Fatal = fatal; }
    }
}

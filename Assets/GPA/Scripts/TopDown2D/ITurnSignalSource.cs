namespace GPA.TopDown2D
{
    /// <summary>A driver (AI or scripted) that can say which way it's about to turn: -1 left, 0 none, 1 right.</summary>
    public interface ITurnSignalSource
    {
        int TurnSignal { get; }
    }
}

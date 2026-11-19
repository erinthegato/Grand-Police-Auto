namespace GPA.Core
{
    /// <summary>
    /// Raised when the in-game dispatcher keys up and releases. The live scanner (a user-supplied
    /// stream) ducks or pauses between Started and Ended so callouts are never talked over.
    /// </summary>
    public readonly struct DispatchTransmissionEvent
    {
        public readonly bool Started;
        public readonly string Text;
        public DispatchTransmissionEvent(bool started, string text) { Started = started; Text = text; }
    }

    public enum CalloutPhase { None, Offered, EnRoute, OnScene, Completed, Failed }

    /// <summary>Raised whenever the active callout changes phase (HUD, MDT, scanner, stats).</summary>
    public readonly struct CalloutPhaseChangedEvent
    {
        public readonly string CalloutId;
        public readonly string Title;
        public readonly CalloutPhase Phase;
        public CalloutPhaseChangedEvent(string id, string title, CalloutPhase phase) { CalloutId = id; Title = title; Phase = phase; }
    }
}

namespace GPA.Core
{
    /// <summary>Radio priority (spec §13D): lower number = more urgent; urgent traffic jumps the queue.</summary>
    public enum TransmissionPriority { Emergency = 1, Critical = 2, Callout = 3, Routine = 4, Background = 5 }

    /// <summary>
    /// The in-game police radio as a public interface, so callouts, traffic stops, pursuits and any
    /// free-roam system can transmit without referencing each other or touching audio directly.
    /// </summary>
    public interface IDispatchRadio
    {
        string Unit { get; }
        void Transmit(TransmissionPriority priority, params string[] lines);
    }
}

namespace GPA.Core
{
    /// <summary>
    /// Public entry point to the free-roam traffic-stop system. Traffic stops never depend on
    /// callouts; a "traffic stop" callout may ask for one through this interface.
    /// </summary>
    public interface IFreeRoamStopService
    {
        bool Active { get; }
        /// <summary>Starts a stop on the given car (must be civilian traffic). Returns false if it can't.</summary>
        bool TryStartStop(UnityEngine.Transform vehicle);
        void CancelStop();
    }

    public enum StopOutcome { Released, Warning, Citation, Arrest, Fled, Lost, Cancelled }

    public readonly struct StopEndedEvent
    {
        public readonly StopOutcome Outcome;
        public readonly string VehicleId, CitizenId, Summary;
        public StopEndedEvent(StopOutcome outcome, string vehicleId, string citizenId, string summary)
        { Outcome = outcome; VehicleId = vehicleId; CitizenId = citizenId; Summary = summary; }
    }
}

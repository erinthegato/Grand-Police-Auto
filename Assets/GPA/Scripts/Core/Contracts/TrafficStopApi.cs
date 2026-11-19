using System;

namespace GPA.Core
{
    public enum TrafficStopState
    {
        Idle,
        Signaling,      // lights on, waiting for target to react
        PullingOver,    // target searching for / moving to a stop location
        Stopped,        // target stopped, officer not yet engaged
        Engaged,        // officer interacting with occupants
        Refusing,       // target ignoring signals but not fleeing
        Fleeing,        // handed over to pursuit
        Concluded
    }

    public enum TrafficStopEndReason { Released, Warning, Citation, Arrest, Cancelled, Fled, TargetLost, ExternalAbort }

    public enum TrafficStopSource { FreeRoam, External }

    /// <summary>Who requested the stop. Free-roam stops never carry a caller.</summary>
    public struct TrafficStopRequest
    {
        public TrafficStopSource Source;
        /// <summary>Opaque id of an external requester (e.g. "callout:dui"). Null for free roam.</summary>
        public string CallerId;
        /// <summary>Optional pre-set reason (e.g. "Speeding").</summary>
        public string Reason;
        /// <summary>If true the driver is forced to flee (used by scripted scenarios).</summary>
        public bool ForceFlee;

        public static TrafficStopRequest FreeRoam(string reason = null) =>
            new TrafficStopRequest { Source = TrafficStopSource.FreeRoam, Reason = reason };

        public static TrafficStopRequest External(string callerId, string reason = null, bool forceFlee = false) =>
            new TrafficStopRequest { Source = TrafficStopSource.External, CallerId = callerId, Reason = reason, ForceFlee = forceFlee };
    }

    public interface ITrafficStopSession
    {
        string SessionId { get; }
        IVehicle Target { get; }
        TrafficStopRequest Request { get; }
        TrafficStopState State { get; }
        string Reason { get; set; }
        /// <summary>Citizen id of the driver once known (null until identified).</summary>
        string DriverCitizenId { get; }
        /// <summary>Whether the officer has positively identified the driver.</summary>
        bool DriverIdentified { get; }
        DateTime StartedAt { get; }
        LocationDescription Location { get; }
        TrafficStopEndReason? EndReason { get; }
    }

    /// <summary>
    /// Public entry point to the independent traffic stop subsystem. Implemented by
    /// GPA.TrafficStop.TrafficStopManager. Callouts may use this to request a stop but the
    /// subsystem never depends on callouts.
    /// </summary>
    public interface ITrafficStopService
    {
        ITrafficStopSession Current { get; }
        TrafficStopState State { get; }
        bool CanTarget(IVehicle vehicle);
        bool TryBeginStop(IVehicle target, TrafficStopRequest request);
        void Cancel(TrafficStopEndReason reason = TrafficStopEndReason.Cancelled);
        event Action<ITrafficStopSession> StopStarted;
        event Action<ITrafficStopSession> StopStateChanged;
        event Action<ITrafficStopSession> StopEnded;
    }

    public readonly struct TrafficStopEvent
    {
        public readonly ITrafficStopSession Session;
        public readonly TrafficStopState State;
        public TrafficStopEvent(ITrafficStopSession s) { Session = s; State = s.State; }
    }
}

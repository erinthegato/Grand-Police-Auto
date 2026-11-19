using System.Collections.Generic;

namespace GPA.Core
{
    /// <summary>Officer status codes shown on the MDT and announced on the radio (spec §8, §15).</summary>
    public enum OfficerStatus { Available, EnRoute, OnScene, TrafficStop, Busy, OutOfService }

    /// <summary>Raised whenever the officer's displayed status changes (MDT, HUD, dispatch).</summary>
    public readonly struct OfficerStatusChangedEvent
    {
        public readonly OfficerStatus Previous, Current;
        public OfficerStatusChangedEvent(OfficerStatus previous, OfficerStatus current) { Previous = previous; Current = current; }
    }

    /// <summary>
    /// The officer's CAD status (spec §8, §15). Activity statuses (en route, on scene, traffic stop,
    /// out of service when off duty) follow the game automatically; the officer chooses between
    /// available and busy, or goes out of service.
    /// </summary>
    public interface IOfficerStatusService
    {
        OfficerStatus Status { get; }
        /// <summary>Officer request: Available (10-8), Busy (10-6) or OutOfService (10-7). Others are ignored.</summary>
        void Request(OfficerStatus status);
    }

    /// <summary>Radio codes and labels for officer statuses, kept in one place so they can be changed.</summary>
    public static class OfficerStatusCodes
    {
        public static string Code(OfficerStatus s)
        {
            switch (s)
            {
                case OfficerStatus.Available: return RadioCodes.Get("available", "10-8");
                case OfficerStatus.EnRoute: return RadioCodes.Get("en_route", "10-76");
                case OfficerStatus.OnScene: return RadioCodes.Get("on_scene", "10-97");
                case OfficerStatus.TrafficStop: return RadioCodes.Get("traffic_stop", "10-38");
                case OfficerStatus.Busy: return RadioCodes.Get("busy", "10-6");
                default: return RadioCodes.Get("out_of_service", "10-7");
            }
        }

        public static string Label(OfficerStatus s)
        {
            switch (s)
            {
                case OfficerStatus.Available: return "Available";
                case OfficerStatus.EnRoute: return "En route";
                case OfficerStatus.OnScene: return "On scene";
                case OfficerStatus.TrafficStop: return "Traffic stop";
                case OfficerStatus.Busy: return "Busy";
                default: return "Out of service";
            }
        }
    }

    /// <summary>One line actually read out by the dispatcher, for the MDT's dispatch history.</summary>
    public readonly struct DispatchLineEvent
    {
        public readonly string Text;
        public readonly TransmissionPriority Priority;
        public DispatchLineEvent(string text, TransmissionPriority priority) { Text = text; Priority = priority; }
    }

    /// <summary>A call as the CAD shows it: no scenario logic, just what an officer would read.</summary>
    public struct CadCall
    {
        public string Id;
        public string Code;          // "10-16", "459"
        public string Title;
        public string Location;      // address / cross street
        public string Details;
        public string ResponseCode;  // "Code 2" / "Code 3"
        public CalloutPhase Phase;
        public float OfferSecondsLeft;
        public string Time;          // "HH:mm" dispatched (active calls) or closed (history)
    }

    /// <summary>An entry in the catalogue of callout types dispatch can assign.</summary>
    public struct CadCallType
    {
        public string Code, Title, Description, ResponseCode, Pack;
    }

    /// <summary>
    /// The CAD's view of the callout system. The MDT talks to this interface only, so it never
    /// references callout classes; the callout framework implements it.
    /// </summary>
    public interface ICadCallSource
    {
        bool OnDuty { get; }
        void SetDuty(bool onDuty);
        /// <summary>The offered or active call, if any.</summary>
        bool TryGetCurrentCall(out CadCall call);
        IReadOnlyList<CadCallType> CallTypes { get; }
        /// <summary>Calls this shift that have finished (newest last).</summary>
        IReadOnlyList<CadCall> History { get; }
        bool AcceptOffer();
        bool DeclineOffer();
        bool ClearCall();
    }
}

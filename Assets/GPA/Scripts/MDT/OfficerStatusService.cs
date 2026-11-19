using GPA.Core;

namespace GPA.Mdt
{
    /// <summary>
    /// Tracks the officer's CAD status. What the officer is doing decides it first — off duty is out
    /// of service, a traffic stop is 10-38, an accepted call is en route then on scene — and otherwise
    /// the officer's own choice of available (10-8) or busy (10-6) applies. Changes are published as
    /// <see cref="OfficerStatusChangedEvent"/>; only the officer's own requests are radioed, because
    /// stops and calls already announce themselves.
    /// </summary>
    internal sealed class OfficerStatusService : IOfficerStatusService
    {
        private OfficerStatus _chosen = OfficerStatus.Available;

        public OfficerStatus Status { get; private set; } = OfficerStatus.Available;

        public OfficerStatusService() => Services.Register<IOfficerStatusService>(this);

        public void Dispose() => Services.Unregister<IOfficerStatusService>(this);

        public void Tick()
        {
            Services.TryGet(out ICadCallSource cad);
            Services.TryGet(out IFreeRoamStopService stops);
            OfficerStatus next = _chosen;
            if (cad != null && !cad.OnDuty) next = OfficerStatus.OutOfService;
            else if (stops != null && stops.Active) next = OfficerStatus.TrafficStop;
            else if (cad != null && cad.TryGetCurrentCall(out var call))
            {
                if (call.Phase == CalloutPhase.EnRoute) next = OfficerStatus.EnRoute;
                else if (call.Phase == CalloutPhase.OnScene) next = OfficerStatus.OnScene;
            }
            if (next == Status) return;
            var previous = Status;
            Status = next;
            GameEvents.Publish(new OfficerStatusChangedEvent(previous, next));
        }

        public void Request(OfficerStatus status)
        {
            Services.TryGet(out ICadCallSource cad);
            Services.TryGet(out IDispatchRadio radio);
            string unit = radio != null ? radio.Unit : "4127";
            switch (status)
            {
                case OfficerStatus.OutOfService:
                    cad?.SetDuty(false); // the callout system radios the 10-7
                    break;
                case OfficerStatus.Available:
                case OfficerStatus.Busy:
                    bool wasOff = cad != null && !cad.OnDuty;
                    if (wasOff) cad.SetDuty(true); // radios the 10-8
                    if (status != _chosen || wasOff)
                    {
                        if (status == OfficerStatus.Busy) radio?.Transmit(TransmissionPriority.Routine, $"{unit}, {OfficerStatusCodes.Code(status)}, busy.");
                        else if (!wasOff) radio?.Transmit(TransmissionPriority.Routine, $"{unit}, {OfficerStatusCodes.Code(status)}, available for calls.");
                    }
                    _chosen = status;
                    break;
            }
            Tick();
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace GPA.Core
{
    /// <summary>
    /// The set of vehicles currently running emergency equipment. Traffic AI polls this at a low rate
    /// (M4) instead of raycasting for police cars, and it is deliberately allocation-free on read.
    /// </summary>
    public static class EmergencySignalRegistry
    {
        private static readonly List<IEmergencySignalSource> Sources = new List<IEmergencySignalSource>(8);

        public static IReadOnlyList<IEmergencySignalSource> Active => Sources;
        public static int Count => Sources.Count;

        public static void Register(IEmergencySignalSource source)
        {
            if (source == null || Sources.Contains(source)) return;
            Sources.Add(source);
        }

        public static void Unregister(IEmergencySignalSource source)
        {
            if (source == null) return;
            Sources.Remove(source);
        }

        /// <summary>Sources within <paramref name="radius"/> of <paramref name="position"/>, nearest first.</summary>
        public static int Query(Vector3 position, float radius, List<IEmergencySignalSource> results)
        {
            if (results == null) return 0;
            results.Clear();
            float sqr = radius * radius;
            for (int i = 0; i < Sources.Count; i++)
            {
                IEmergencySignalSource source = Sources[i];
                if (source == null) continue;
                if ((source.SignalPosition - position).sqrMagnitude <= sqr) results.Add(source);
            }

            results.Sort((a, b) =>
                (a.SignalPosition - position).sqrMagnitude.CompareTo((b.SignalPosition - position).sqrMagnitude));
            return results.Count;
        }

        /// <summary>
        /// True when a signalling vehicle is close enough to matter and roughly behind or beside
        /// <paramref name="observer"/> (i.e. it is about to overtake, so the observer should yield).
        /// </summary>
        public static bool AnyApproaching(Vector3 observerPosition, Vector3 observerForward, float radius,
            out IEmergencySignalSource nearest)
        {
            nearest = null;
            float best = float.MaxValue;
            for (int i = 0; i < Sources.Count; i++)
            {
                IEmergencySignalSource source = Sources[i];
                if (source == null) continue;
                if (source.Lights == EmergencyLightMode.Off && !source.SirenOn) continue;

                Vector3 toSource = source.SignalPosition - observerPosition;
                if (toSource.sqrMagnitude > radius * radius) continue;
                if (Vector3.Dot(toSource.normalized, observerForward) > 0.35f) continue; // it is ahead, not behind
                if (toSource.sqrMagnitude >= best) continue;

                best = toSource.sqrMagnitude;
                nearest = source;
            }

            return nearest != null;
        }

        /// <summary>Static state survives Play sessions in the Editor, so it is cleared on every load.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Sources.Clear();
    }
}

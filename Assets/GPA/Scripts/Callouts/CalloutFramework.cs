using System;
using System.Collections.Generic;
using UnityEngine;

namespace GPA.Callouts
{
    // Spec §14: three layers.
    //   Framework  — CalloutLibrary (discovery, validation, weighting) + CalloutManager (offers,
    //                acceptance, tracking, completion, dispatch/CAD integration).
    //   Callouts   — one class per scenario implementing ICallout (usually via CalloutScenario),
    //                tagged [CalloutType("name")]. Found automatically: no framework edits needed.
    //   Definitions — data only: CalloutDefinitionAsset ScriptableObjects, grouped by a
    //                CalloutPackManifest, or JSON packs for players' mods.

    /// <summary>A running callout. Scenario-specific gameplay lives behind this.</summary>
    public interface ICallout
    {
        CalloutDefinition Definition { get; }
        Vector2 Location { get; }
        float ArrivalRadius { get; }
        string Objective { get; }
        CalloutScenario.Status Result { get; }
        string ResultText { get; }
        int Arrests { get; }
        IReadOnlyDictionary<string, string> DispatchTokens { get; }
        /// <summary>Choose a location and spawn the scene. False = can't run here right now.</summary>
        bool Setup();
        void OnArrived();
        void Tick(float dt);
    }

    /// <summary>Makes callout instances of one scenario type.</summary>
    public interface ICalloutFactory
    {
        string Type { get; }
        CalloutScenario Create();
    }

    /// <summary>
    /// A bundle of callouts from one source (a code mod, a DLC folder...). Implement with a public
    /// parameterless constructor anywhere in the project; it's discovered at startup. No runtime
    /// DLL loading is involved: providers must be compiled into the build.
    /// </summary>
    public interface ICalloutProvider
    {
        string Name { get; }
        IEnumerable<ICalloutFactory> Factories { get; }
        IEnumerable<CalloutDefinition> Definitions { get; }
    }

    /// <summary>
    /// Tag a CalloutScenario subclass to register it as a scenario type. Derives from Preserve so
    /// IL2CPP (iOS) code stripping keeps classes that are only found by reflection.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class CalloutTypeAttribute : UnityEngine.Scripting.PreserveAttribute
    {
        public readonly string Type;
        public CalloutTypeAttribute(string type) { Type = type; }
    }

    /// <summary>Factory built from a delegate.</summary>
    public sealed class DelegateCalloutFactory : ICalloutFactory
    {
        private readonly Func<CalloutScenario> _make;
        public string Type { get; }
        public DelegateCalloutFactory(string type, Func<CalloutScenario> make) { Type = type; _make = make; }
        public CalloutScenario Create() => _make();
    }

    /// <summary>Checks a definition and fixes what it safely can. Returns false if it can't be used.</summary>
    public static class CalloutValidator
    {
        public static bool Validate(CalloutDefinition d, string source, List<string> problems)
        {
            if (d == null) { problems.Add($"{source}: empty entry"); return false; }
            if (string.IsNullOrEmpty(d.id)) { problems.Add($"{source}: a callout has no id"); return false; }
            if (string.IsNullOrEmpty(d.type)) { problems.Add($"{source}: '{d.id}' has no type"); return false; }
            if (!CalloutScenarios.IsRegistered(d.type)) { problems.Add($"{source}: '{d.id}' uses unknown type '{d.type}'"); return false; }
            if (string.IsNullOrEmpty(d.title)) { d.title = d.id; problems.Add($"{source}: '{d.id}' has no title (using id)"); }
            if (d.dispatch == null || d.dispatch.Length == 0) { d.dispatch = new[] { d.title + ", {sector}." }; problems.Add($"{source}: '{d.id}' has no dispatch lines (added one)"); }
            if (d.maxDistance < d.minDistance) { (d.minDistance, d.maxDistance) = (d.maxDistance, d.minDistance); problems.Add($"{source}: '{d.id}' min/max distance swapped"); }
            d.weight = Mathf.Max(0f, d.weight);
            d.fleeChance = Mathf.Clamp01(d.fleeChance);
            d.offenceChance = Mathf.Clamp01(d.offenceChance);
            d.lockedChance = Mathf.Clamp01(d.lockedChance);
            d.priority = Mathf.Clamp(d.priority, 1, 3);
            return true;
        }
    }
}

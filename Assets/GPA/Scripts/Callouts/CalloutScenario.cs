using System;
using System.Collections.Generic;
using GPA.Core;
using GPA.TopDown2D;
using UnityEngine;

namespace GPA.Callouts
{
    /// <summary>
    /// Behaviour behind a callout type. A scenario picks its scene location, spawns its actors,
    /// updates the on-screen objective, and finishes with success or failure. Anything it spawns
    /// through <see cref="Track"/> is cleaned up automatically after the call.
    /// </summary>
    public abstract class CalloutScenario : ICallout
    {
        public enum Status { Running, Succeeded, Failed }

        private readonly List<GameObject> _spawned = new List<GameObject>();

        public CalloutDefinition Definition { get; internal set; }
        public CalloutContext Context { get; internal set; }
        public Vector2 Location { get; protected set; }
        /// <summary>Distance at which the officer counts as on scene.</summary>
        public virtual float ArrivalRadius => 30f;
        public string Objective { get; protected set; } = "Respond to the call";
        public Status Result { get; protected set; } = Status.Running;
        public string ResultText { get; protected set; }
        /// <summary>Extra dispatch token values (e.g. {vehicle}, {plate}) filled in during Setup.</summary>
        public readonly Dictionary<string, string> Tokens = new Dictionary<string, string>();
        public int Arrests { get; protected set; }
        IReadOnlyDictionary<string, string> ICallout.DispatchTokens => Tokens;

        /// <summary>Choose a location and spawn the scene. Return false if it can't be set up here.</summary>
        public abstract bool Setup();
        public virtual void OnArrived() { }
        public abstract void Tick(float dt);

        protected void Succeed(string text) { if (Result == Status.Running) { Result = Status.Succeeded; ResultText = text; } }
        protected void Fail(string text) { if (Result == Status.Running) { Result = Status.Failed; ResultText = text; } }

        protected T Track<T>(T component) where T : Component { if (component != null) _spawned.Add(component.gameObject); return component; }

        /// <summary>True if this call spawned <paramref name="go"/>.</summary>
        internal bool Owns(GameObject go) => go != null && _spawned.Contains(go);

        /// <summary>
        /// One of this call's people was handcuffed by the officer. The arrest pipeline now owns them
        /// (escort, back seat, booking), so by default that counts as a successful arrest for the call.
        /// </summary>
        internal virtual void OnTrackedPedCuffed(Ped2D ped)
        {
            if (Result != Status.Running) return;
            Arrests++;
            Succeed("Suspect taken into custody.");
        }

        internal void Cleanup()
        {
            foreach (GameObject go in _spawned)
            {
                if (go == null) continue;
                // Never delete someone in handcuffs: they're a prisoner in the arrest pipeline now.
                if (go.TryGetComponent(out Ped2D ped) && ped.Cuffed) continue;
                UnityEngine.Object.Destroy(go);
            }
            _spawned.Clear();
        }

        // ---- helpers for scenario authors
        protected Vector2 PlayerPosition => Context.Player.Transform.position;
        protected bool PlayerOnFoot => Context.Player.CurrentVehicle == null;
        protected float DistanceToPlayer(Component c) => c == null ? float.MaxValue : Vector2.Distance(c.transform.position, PlayerPosition);

        protected Interactable2D AddInteraction(Component on, string prompt, Action onUse, float range = 2.4f)
        {
            var it = on.gameObject.GetComponent<Interactable2D>();
            if (it == null) it = on.gameObject.AddComponent<Interactable2D>();
            it.ClearHandlers();
            it.Prompt = prompt;
            it.Range = range;
            it.enabled = true;
            it.Used += _ => onUse();
            return it;
        }

        protected static void ClearInteraction(Component on)
        {
            var it = on != null ? on.GetComponent<Interactable2D>() : null;
            if (it == null) return;
            it.ClearHandlers();
            it.enabled = false;
        }
    }

    /// <summary>Everything a scenario can use from the running game.</summary>
    public sealed class CalloutContext
    {
        public IPlayerAvatar Player;
        public WorldSprites2D Sprites;
        public SpawnPoints2D Spawns;
        public DispatchScanner Scanner;
        public IHapticsService Haptics;
        public string Unit = "4127";
    }

    /// <summary>
    /// Registry of scenario types. Built-in types are registered here; a code mod registers its
    /// own with <see cref="Register"/> before packs load, then JSON packs can use that type name.
    /// </summary>
    public static class CalloutScenarios
    {
        private static readonly Dictionary<string, ICalloutFactory> Factories = new Dictionary<string, ICalloutFactory>();
        private static readonly List<ICalloutProvider> ProviderList = new List<ICalloutProvider>();
        private static bool _discovered;

        /// <summary>Code-mod providers found at startup (their definitions are loaded by CalloutLibrary).</summary>
        public static IReadOnlyList<ICalloutProvider> Providers { get { Discover(); return ProviderList; } }
        public static IEnumerable<string> Types { get { Discover(); return Factories.Keys; } }

        public static void Register(string type, Func<CalloutScenario> factory) => Register(new DelegateCalloutFactory(type, factory));
        public static void Register(ICalloutFactory factory) { if (factory != null && !string.IsNullOrEmpty(factory.Type)) Factories[factory.Type] = factory; }
        public static bool IsRegistered(string type) { Discover(); return type != null && Factories.ContainsKey(type); }
        public static CalloutScenario Create(string type) { Discover(); return type != null && Factories.TryGetValue(type, out var f) ? f.Create() : null; }

        /// <summary>
        /// Finds every [CalloutType] scenario class and every ICalloutProvider compiled into the game,
        /// so adding a callout never means editing this file, the manager, CAD or dispatch.
        /// </summary>
        private static void Discover()
        {
            if (_discovered) return;
            _discovered = true;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); } catch (System.Reflection.ReflectionTypeLoadException e) { types = Array.FindAll(e.Types, t => t != null); }
                foreach (Type t in types)
                {
                    if (t.IsAbstract) continue;
                    if (typeof(CalloutScenario).IsAssignableFrom(t))
                    {
                        var attr = (CalloutTypeAttribute)Attribute.GetCustomAttribute(t, typeof(CalloutTypeAttribute));
                        if (attr == null || t.GetConstructor(Type.EmptyTypes) == null) continue;
                        Type captured = t;
                        Register(attr.Type, () => (CalloutScenario)Activator.CreateInstance(captured));
                    }
                    else if (typeof(ICalloutProvider).IsAssignableFrom(t) && !t.IsInterface && t.GetConstructor(Type.EmptyTypes) != null)
                    {
                        try
                        {
                            var provider = (ICalloutProvider)Activator.CreateInstance(t);
                            ProviderList.Add(provider);
                            if (provider.Factories != null) foreach (var f in provider.Factories) Register(f);
                        }
                        catch (Exception e) { Debug.LogWarning($"[Callouts] Provider {t.Name} failed: {e.Message}"); }
                    }
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GPA.Core
{
    /// <summary>
    /// Runtime performance housekeeping, created automatically after the first scene loads.
    /// - IMGUI: none of the game's HUDs use GUILayout, so every behaviour with an OnGUI gets
    ///   useGUILayout = false. Unity then skips the Layout pass and calls OnGUI once per repaint
    ///   instead of twice (the HUDs were the biggest single cost per frame).
    /// - Re-checks every few seconds so HUDs created later (menus, garages, callouts) are covered.
    ///
    /// The list of GPA types that have an OnGUI is worked out once at start-up; each re-check then
    /// looks up only those types instead of every MonoBehaviour in the scene (which caused a small
    /// hitch every few seconds).
    /// </summary>
    internal sealed class PerfTuning : MonoBehaviour
    {
        private static Type[] _guiTypes;
        private float _next;
        private int _passes;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (FindAnyObjectByType<PerfTuning>() != null) return;
            var go = new GameObject("Perf Tuning") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go);
            go.AddComponent<PerfTuning>();
        }

        private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
        private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;
        private void OnSceneLoaded(Scene s, LoadSceneMode m) { _passes = 0; _next = 0f; }   // check a new scene right away

        private void Update()
        {
            if (Time.unscaledTime < _next) return;
            _passes++;
            _next = Time.unscaledTime + (_passes < 4 ? 0.5f : 4f);
            if (_guiTypes == null) _guiTypes = FindGuiTypes();
            foreach (Type t in _guiTypes)
                foreach (UnityEngine.Object o in FindObjectsByType(t, FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (o is MonoBehaviour mb && mb.useGUILayout) mb.useGUILayout = false;
        }

        /// <summary>Every GPA MonoBehaviour type that declares an OnGUI method.</summary>
        private static Type[] FindGuiTypes()
        {
            var list = new List<Type>();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name = asm.GetName().Name;
                if (!name.StartsWith("GPA") && name != "Assembly-CSharp") continue;   // game code only
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (System.Reflection.ReflectionTypeLoadException e) { types = Array.FindAll(e.Types, x => x != null); }
                foreach (Type t in types)
                {
                    if (t.IsAbstract || !typeof(MonoBehaviour).IsAssignableFrom(t)) continue;
                    if (t.Namespace == null || !t.Namespace.StartsWith("GPA")) continue;
                    if (t.GetMethod("OnGUI", flags) != null) list.Add(t);
                }
            }
            return list.ToArray();
        }
    }
}

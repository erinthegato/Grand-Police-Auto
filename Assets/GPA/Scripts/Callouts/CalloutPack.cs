using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GPA.Callouts
{
    /// <summary>
    /// One callout as written in a pack file. Packs are plain JSON so anyone can add calls without
    /// touching code: pick an existing scenario <see cref="type"/> and write new titles, dispatch
    /// lines and odds. Code mods can register new scenario types (see <see cref="CalloutScenarios"/>).
    /// </summary>
    [Serializable]
    public sealed class CalloutDefinition
    {
        public string id;
        public string title;
        [Tooltip("Radio code shown on the MDT, e.g. \"10-52\".")]
        public string code;
        [Tooltip("Scenario type: suspicious_person, stolen_vehicle, traffic_collision, or one a mod registers.")]
        public string type;
        public string description;
        [Tooltip("Lines the dispatcher reads. Tokens: {sector} {vehicle} {plate} {unit}.")]
        public string[] dispatch;
        [Tooltip("1 = routine (Code 2), 2 = urgent, 3 = emergency (Code 3, lights and siren).")]
        public int priority = 1;
        [Tooltip("Relative chance of being picked.")]
        public float weight = 1f;
        public float minDistance = 60f;
        public float maxDistance = 170f;
        [Tooltip("Chance (0-1) the suspect runs.")]
        public float fleeChance = 0.3f;
        [Tooltip("Chance (0-1) of an arrestable offence when a stop goes quietly.")]
        public float offenceChance = 0.25f;
        // ---- indoor calls (type "indoor_call")
        /// <summary>Building type to pick: apartment, office, shop, restaurant, police.</summary>
        public string buildingType = "apartment";
        /// <summary>Room kind the call is in (unit, office, sales, stock, kitchen...). Empty = any room with a door.</summary>
        public string roomKind = "unit";
        /// <summary>Chance the room door is locked (force it open to get in).</summary>
        public float lockedChance;
        /// <summary>A second person on scene (complainant / victim) who tells you what happened.</summary>
        public bool complainant;
        /// <summary>What the complainant says.</summary>
        public string complainantLine = "";
        /// <summary>Reputation level needed before this call is offered (0 = always). SWAT calls need SWAT Commander (7).</summary>
        public int requiredLevel;
        /// <summary>"helicopter" = only offered while flying the air unit (and ground calls aren't offered then).</summary>
        public string requires = "";

        public string ResponseCode => priority >= 3 ? "Code 3" : "Code 2";
    }

    [Serializable]
    public sealed class CalloutPack
    {
        public string name;
        public string author;
        public string version;
        public CalloutDefinition[] callouts = new CalloutDefinition[0];
    }

    /// <summary>
    /// Loads callout packs from (in order): Resources/CalloutPacks (built in), StreamingAssets/CalloutPacks
    /// (shipped extras) and {persistentDataPath}/CalloutPacks (player mods). Later packs can override
    /// earlier callouts with the same id. Callouts whose scenario type is unknown are skipped with a warning.
    /// </summary>
    public static class CalloutLibrary
    {
        public const string Folder = "CalloutPacks";
        private static readonly List<CalloutDefinition> Loaded = new List<CalloutDefinition>();
        private static readonly List<string> PackNames = new List<string>();

        public static IReadOnlyList<CalloutDefinition> All => Loaded;
        public static IReadOnlyList<string> Packs => PackNames;
        public static string ModFolder => Path.Combine(Application.persistentDataPath, Folder);

        public static void Reload()
        {
            Loaded.Clear();
            PackNames.Clear();
            var byId = new Dictionary<string, int>();

            // 1. ScriptableObject packs and loose definition assets (data only, spec §14C).
            foreach (CalloutPackManifest m in Resources.LoadAll<CalloutPackManifest>(Folder))
            {
                if (m == null || !m.Enabled) continue;
                int n = 0;
                foreach (CalloutDefinitionAsset a in m.Callouts) if (a != null && AddOne(a.Definition, "pack:" + m.PackName, byId)) n++;
                foreach (TextAsset j in m.JsonPacks) if (j != null) Add(j.text, "pack:" + m.PackName + "/" + j.name, byId);
                PackNames.Add($"{m.PackName} ({n})");
            }
            foreach (CalloutDefinitionAsset a in Resources.LoadAll<CalloutDefinitionAsset>("CalloutDefinitions"))
                if (a != null) AddOne(a.Definition, "asset:" + a.name, byId);
            // 2. Code-mod providers compiled into the game.
            foreach (ICalloutProvider provider in CalloutScenarios.Providers)
            {
                int n = 0;
                if (provider.Definitions != null) foreach (var d in provider.Definitions) if (AddOne(d, "provider:" + provider.Name, byId)) n++;
                PackNames.Add($"{provider.Name} ({n})");
            }
            // 3. JSON packs: built in, StreamingAssets, then the player's mod folder (later ones override by id).
            foreach (TextAsset asset in Resources.LoadAll<TextAsset>(Folder)) Add(asset.text, "builtin:" + asset.name, byId);
            LoadFolder(Path.Combine(Application.streamingAssetsPath, Folder), byId);
            try { Directory.CreateDirectory(ModFolder); } catch (Exception) { /* read-only platforms */ }
            LoadFolder(ModFolder, byId);

            Debug.Log($"[Callouts] {Loaded.Count} callouts from {PackNames.Count} pack(s). Mod folder: {ModFolder}");
        }

        private static void LoadFolder(string folder, Dictionary<string, int> byId)
        {
            if (!Directory.Exists(folder)) return;
            foreach (string file in Directory.GetFiles(folder, "*.json"))
            {
                try { Add(File.ReadAllText(file), Path.GetFileName(file), byId); }
                catch (Exception e) { Debug.LogWarning($"[Callouts] Could not read {file}: {e.Message}"); }
            }
        }

        private static void Add(string json, string source, Dictionary<string, int> byId)
        {
            CalloutPack pack;
            try { pack = JsonUtility.FromJson<CalloutPack>(json); }
            catch (Exception e) { Debug.LogWarning($"[Callouts] Bad JSON in {source}: {e.Message}"); return; }
            if (pack?.callouts == null) return;

            PackNames.Add($"{pack.name ?? source} ({pack.callouts.Length})");
            foreach (CalloutDefinition def in pack.callouts) AddOne(def, source, byId);
        }

        private static readonly List<string> Problems = new List<string>();

        /// <summary>Validates one definition and adds it (a later one with the same id replaces it).</summary>
        private static bool AddOne(CalloutDefinition def, string source, Dictionary<string, int> byId)
        {
            Problems.Clear();
            bool ok = CalloutValidator.Validate(def, source, Problems);
            foreach (string p in Problems) Debug.LogWarning("[Callouts] " + p + (ok ? "" : ", skipped."));
            if (!ok) return false;
            if (byId.TryGetValue(def.id, out int index)) Loaded[index] = def;
            else { byId[def.id] = Loaded.Count; Loaded.Add(def); }
            return true;
        }

        public static CalloutDefinition PickWeighted(Func<CalloutDefinition, bool> allowed = null)
        {
            float total = 0f;
            CalloutDefinition last = null;
            foreach (var d in Loaded) if (allowed == null || allowed(d)) { total += Mathf.Max(0f, d.weight); last = d; }
            if (last == null) return null;
            float roll = UnityEngine.Random.value * total;
            foreach (var d in Loaded)
            {
                if (allowed != null && !allowed(d)) continue;
                roll -= Mathf.Max(0f, d.weight);
                if (roll <= 0f) return d;
            }
            return last;
        }
    }
}

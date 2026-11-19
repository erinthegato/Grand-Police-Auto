using System;
using System.Collections.Generic;
using System.IO;
using GPA.Records;
using GPA.TopDown2D;
using UnityEditor;
using UnityEngine;

namespace GPA.EditorTools
{
    /// <summary>
    /// After the city is rebuilt, building ids change, so citizens' home addresses in records.json can
    /// point at buildings that no longer exist. This moves every such citizen into a residential
    /// building of the new city (same one each run, picked from a hash of the citizen id) and keeps
    /// the apartment number. Everything else (vehicles, citations, arrests, reports, BOLOs) is left
    /// untouched. A dated backup of records.json is written first.
    /// Menu: GPA > Records > Re-home citizens after city rebuild.
    /// </summary>
    public static class RecordsRehome
    {
        private const string DatabasePath = "Assets/GPA/Data/City/Halden_Locations.asset";

        [MenuItem("GPA/Records/Re-home citizens after city rebuild")]
        private static void Menu() => Debug.Log("[Records] " + Run());

        public static string Run()
        {
            string path = RecordsDatabase.FilePath;
            if (!File.Exists(path)) return "No records.json yet: it will be generated from the new city on first play.";
            var city = AssetDatabase.LoadAssetAtPath<CityLocationDatabase>(DatabasePath);
            if (city == null) return "City location database not found.";

            var homes = new List<CityLocationDatabase.Building>();
            foreach (var b in city.Buildings) if (b.Type == "apartment") homes.Add(b);
            if (homes.Count == 0) return "The new city has no apartment buildings.";
            // Old ids can collide with new ones (both cities number HP-0001...), so a citizen only keeps
            // their home when the id names an apartment building AND the stored address matches it.
            var byId = new Dictionary<string, CityLocationDatabase.Building>();
            foreach (var b in city.Buildings) byId[b.Id] = b;

            string json = File.ReadAllText(path);
            RecordsData data = JsonUtility.FromJson<RecordsData>(json);
            if (data == null || data.Citizens == null) return "records.json could not be read; nothing changed.";

            string backup = path + $".before-city-rebuild-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Copy(path, backup);

            int moved = 0;
            foreach (Citizen c in data.Citizens)
            {
                if (string.IsNullOrEmpty(c.BuildingId)) continue;
                if (byId.TryGetValue(c.BuildingId, out var current) && current.Type == "apartment" &&
                    c.Address != null && c.Address.StartsWith(current.Address + ",", StringComparison.Ordinal)) continue;
                var home = homes[(int)(Hash(c.Id) % (uint)homes.Count)];
                string apt = "";
                int at = c.Address != null ? c.Address.IndexOf(", Apt ", StringComparison.Ordinal) : -1;
                if (at >= 0) apt = c.Address.Substring(at);
                c.BuildingId = home.Id;
                c.Address = home.Address + apt;
                moved++;
            }

            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(data));
            File.Copy(temp, path, true);
            File.Delete(temp);
            return $"Re-homed {moved} of {data.Citizens.Count} citizens into {homes.Count} apartment buildings. Backup: {backup}";
        }

        private static uint Hash(string s)
        {
            uint h = 2166136261;
            foreach (char ch in s ?? "") { h ^= ch; h *= 16777619; }
            return h;
        }
    }
}

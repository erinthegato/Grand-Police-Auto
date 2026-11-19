using System.IO;
using GPA.Records;
using UnityEditor;
using UnityEngine;

namespace GPA.EditorTools
{
    /// <summary>Developer tools for the persistent records (citizens, vehicles, police records).</summary>
    public static class RecordsTools
    {
        [MenuItem("GPA/Records/Open Records Folder")]
        public static void Open()
        {
            Directory.CreateDirectory(RecordsDatabase.Folder);
            EditorUtility.RevealInFinder(RecordsDatabase.FilePath);
        }

        [MenuItem("GPA/Records/Print Sample Records (Play Mode)")]
        public static void Sample()
        {
            var db = RecordsDatabase.Instance;
            if (db == null || !db.Ready) { Debug.Log("[Records] Enter Play Mode in City_Halden first."); return; }
            var data = db.Data;
            int wanted = data.Citizens.FindAll(c => c.Wanted).Count;
            int stolen = data.Vehicles.FindAll(v => v.Stolen).Count;
            Debug.Log($"[Records] {data.Citizens.Count} citizens ({wanted} with warrants), {data.Vehicles.Count} vehicles ({stolen} stolen), " +
                      $"{data.Bolos.Count} BOLOs, {data.Citations.Count} citations, {data.Arrests.Count} arrests, {data.Reports.Count} reports.\nFile: {RecordsDatabase.FilePath}");
            for (int i = 0; i < 3 && i < data.Citizens.Count; i++)
            {
                var c = data.Citizens[i * 97 % data.Citizens.Count];
                Debug.Log($"[Records] {c.Id} {c.FullName} ({c.Sex}, DOB {c.DateOfBirth}) — {c.Address} — licence {c.License.Status} — " +
                          $"{c.Warrants.Count} warrant(s), {c.CriminalHistory.Count} prior(s), {c.VehicleIds.Count} vehicle(s)");
            }
        }

        [MenuItem("GPA/Records/Delete Saved Records (new population next Play)")]
        public static void Delete()
        {
            if (!File.Exists(RecordsDatabase.FilePath)) { Debug.Log("[Records] No saved records."); return; }
            if (!EditorUtility.DisplayDialog("Delete records", "Delete the saved population and all police records (citations, arrests, reports)? A new population is generated next time you play.", "Delete", "Cancel")) return;
            File.Delete(RecordsDatabase.FilePath);
            Debug.Log("[Records] Deleted " + RecordsDatabase.FilePath);
        }
    }
}

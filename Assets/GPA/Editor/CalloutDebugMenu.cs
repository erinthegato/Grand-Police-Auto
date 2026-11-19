using UnityEditor;
using UnityEngine;

namespace GPA.EditorTools
{
    /// <summary>Play-mode shortcuts for testing callouts (rank and vehicle gates are skipped).</summary>
    internal static class CalloutDebugMenu
    {
        private static void Force(string id)
        {
            if (!Application.isPlaying) { Debug.Log("Enter play mode first."); return; }
            var m = Object.FindAnyObjectByType<GPA.Callouts.CalloutManager>();
            if (m == null || !m.ForceOffer(id)) Debug.LogWarning("Couldn't offer " + id);
        }

        [MenuItem("Tools/GPA/Callouts/Offer SWAT barricade")] private static void Swat() => Force("core.swat_barricade");
        [MenuItem("Tools/GPA/Callouts/Offer SWAT warehouse")] private static void SwatWh() => Force("core.swat_warehouse");
        [MenuItem("Tools/GPA/Callouts/Offer air pursuit")] private static void AirP() => Force("core.air_pursuit");
        [MenuItem("Tools/GPA/Callouts/Offer air search")] private static void AirS() => Force("core.air_search");
        [MenuItem("Tools/GPA/Callouts/Offer domestic (indoor)")] private static void Dom() => Force("core.domestic");
    }
}

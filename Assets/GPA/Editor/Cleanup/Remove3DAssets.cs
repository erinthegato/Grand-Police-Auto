using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GPA.EditorTools
{
    /// <summary>
    /// One-off cleanup for the switch to 2D: moves the 3D art, scenes and 3D-only code to the
    /// operating system's trash (Recycle Bin), so it can be restored from there. Lives in its own
    /// assembly with no references so it keeps compiling while the 3D code is removed.
    /// </summary>
    public static class Remove3DAssets
    {
        private static readonly string[] Targets =
        {
            // Third-party 3D art and samples
            "Assets/City", "Assets/AmbientCity", "Assets/Cars", "Assets/KINEMATION", "Assets/RP_Character",
            "Assets/UNITY+RIG.fbx", "Assets/textures", "Assets/Demo", "Assets/TutorialInfo", "Assets/Readme.asset",
            "Assets/AI Toolkit", "Assets/_Recovery", "Assets/Plans/StunGun_Assets", "Assets/Plans/StunGun.prefab",
            "Assets/51MsiJ6-jHL._AC_UL450_SY450_QL70_.png", "Assets/images.png",
            // GPA 3D content
            "Assets/GPA/Scenes/City_Midtown.unity", "Assets/GPA/Scenes/Precinct_Interior.unity", "Assets/GPA/Scenes/Sandbox_Player.unity",
            "Assets/GPA/Prefabs/Player", "Assets/GPA/Prefabs/Vehicles", "Assets/GPA/Prefabs/Weapons",
            "Assets/GPA/Art/City", "Assets/GPA/Art/Meshes", "Assets/GPA/Art/Materials", "Assets/GPA/Materials",
            "Assets/GPA/Kinemation", "Assets/GPA/Animators", "Assets/GPA/Resources/PoliceCarCatalog.asset",
            "Assets/GPA/Data/Config/DistrictProfiles",
            // 3D-only code
            "Assets/GPA/Scripts/Player", "Assets/GPA/Scripts/World",
            "Assets/GPA/Editor/KinemationPlayerBuilder.cs", "Assets/GPA/Editor/PoliceCarPrefabBuilder.cs",
            "Assets/GPA/Editor/PrecinctInteriorBuilder.cs", "Assets/GPA/Editor/MidtownRoadGeometryRepair.cs",
            "Assets/GPA/Editor/MidtownGeometryCaptures.cs",
        };

        [MenuItem("GPA/Maintenance/Move 3D Assets to Recycle Bin")]
        public static void Run()
        {
            var existing = new List<string>();
            foreach (string path in Targets)
                if (AssetDatabase.IsValidFolder(path) || AssetDatabase.LoadMainAssetAtPath(path) != null) existing.Add(path);

            if (existing.Count == 0) { Debug.Log("[Cleanup] Nothing to remove."); return; }

            if (!EditorUtility.DisplayDialog("Move 3D assets to Recycle Bin",
                    $"{existing.Count} folders/files will be moved to the Recycle Bin (restorable from there).\n\n" +
                    string.Join("\n", existing.ToArray(), 0, Mathf.Min(existing.Count, 20)) + (existing.Count > 20 ? "\n..." : ""),
                    "Move to Recycle Bin", "Cancel"))
                return;

            // Leave any 3D scene that is about to disappear.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var failed = new List<string>();
            AssetDatabase.MoveAssetsToTrash(existing.ToArray(), failed);
            AssetDatabase.Refresh();

            var log = new StringBuilder($"[Cleanup] Moved {existing.Count - failed.Count} of {existing.Count} items to the Recycle Bin.");
            foreach (string f in failed) log.Append("\n  FAILED: ").Append(f);
            if (failed.Count > 0) Debug.LogWarning(log.ToString()); else Debug.Log(log.ToString());
        }
    }
}

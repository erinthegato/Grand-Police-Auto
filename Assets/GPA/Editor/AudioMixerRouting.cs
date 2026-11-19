using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace GPA.EditorTools
{
    /// <summary>Preserves MainMixer assignments in both existing and rebuilt GPA prefabs.</summary>
    public static class AudioMixerRouting
    {
        public const string MixerPath = "Assets/GPA/Audio/MainMixer.mixer";

        public static int Apply(GameObject root)
        {
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            if (mixer == null)
            {
                Debug.LogWarning("[Audio] MainMixer is missing; existing routing was left unchanged.", root);
                return 0;
            }

            int changed = 0;
            foreach (MonoBehaviour component in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null) continue;
                string type = component.GetType().FullName;
                string groupName;
                if (type == "GPA.Player.OfficerWeaponLoadout" || type == "GPA.Player.TaserWeapon")
                    groupName = "SFX";
                else if (type == "GPA.Vehicles.SirenController")
                    groupName = "Sirens";
                else
                    continue;

                AudioMixerGroup group = null;
                foreach (AudioMixerGroup candidate in mixer.FindMatchingGroups(groupName))
                    if (candidate.name == groupName) { group = candidate; break; }
                if (group == null)
                    throw new System.InvalidOperationException("MainMixer is missing group " + groupName);

                var serialized = new SerializedObject(component);
                SerializedProperty property = serialized.FindProperty("_audioMixerGroup");
                if (property == null)
                    throw new System.InvalidOperationException(type + " is missing its mixer routing field.");
                if (property.objectReferenceValue != group)
                {
                    Undo.RecordObject(component, "Route GPA audio");
                    property.objectReferenceValue = group;
                    serialized.ApplyModifiedProperties();
                    if (PrefabUtility.IsPartOfPrefabInstance(component))
                        PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                    changed++;
                }

                foreach (AudioSource source in component.GetComponents<AudioSource>())
                {
                    if (source.outputAudioMixerGroup == group) continue;
                    Undo.RecordObject(source, "Route GPA audio source");
                    source.outputAudioMixerGroup = group;
                    if (PrefabUtility.IsPartOfPrefabInstance(source))
                        PrefabUtility.RecordPrefabInstancePropertyModifications(source);
                    changed++;
                }
            }
            return changed;
        }
    }
}

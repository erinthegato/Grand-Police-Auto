using UnityEditor;

namespace GPA.EditorTools
{
    /// <summary>The title screen is skipped when you press Play in the editor; tick this to see it.</summary>
    internal static class TitleScreenToggle
    {
        private const string Menu = "Tools/GPA/Show title screen in editor";
        private const string Pref = "GPA.ShowTitleInEditor";

        [MenuItem(Menu)]
        private static void Toggle() => EditorPrefs.SetBool(Pref, !EditorPrefs.GetBool(Pref, false));

        [MenuItem(Menu, true)]
        private static bool Validate() { UnityEditor.Menu.SetChecked(Menu, EditorPrefs.GetBool(Pref, false)); return true; }
    }
}

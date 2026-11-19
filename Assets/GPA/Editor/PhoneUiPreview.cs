using GPA.Core;
using UnityEditor;

namespace GPA.EditorTools
{
    /// <summary>Preview the phone HUD layout in the editor (set the Game view to 1334x750 for an iPhone SE).</summary>
    internal static class PhoneUiPreview
    {
        private const string Menu = "Tools/GPA/Preview phone UI (iPhone SE)";

        [MenuItem(Menu)]
        private static void Toggle() => UiScale.ForceHandheld = !UiScale.ForceHandheld;

        [MenuItem(Menu, true)]
        private static bool Validate() { Menu.Checked(UiScale.ForceHandheld); return true; }

        private static void Checked(this string menu, bool on) => UnityEditor.Menu.SetChecked(menu, on);
    }
}

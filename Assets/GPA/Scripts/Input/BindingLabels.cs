using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace GPA.Input
{
    /// <summary>Produces short, device-correct labels for bindings (keyboard keys, Xbox / PlayStation names).</summary>
    public static class BindingLabels
    {
        private static readonly Dictionary<string, (string xbox, string ps)> GamepadNames = new Dictionary<string, (string, string)>
        {
            { "buttonSouth", ("A", "Cross") },
            { "buttonEast", ("B", "Circle") },
            { "buttonWest", ("X", "Square") },
            { "buttonNorth", ("Y", "Triangle") },
            { "leftShoulder", ("LB", "L1") },
            { "rightShoulder", ("RB", "R1") },
            { "leftTrigger", ("LT", "L2") },
            { "rightTrigger", ("RT", "R2") },
            { "leftStickPress", ("LS", "L3") },
            { "rightStickPress", ("RS", "R3") },
            { "start", ("Menu", "Options") },
            { "select", ("View", "Share") },
            { "dpad/up", ("D-Pad Up", "D-Pad Up") },
            { "dpad/down", ("D-Pad Down", "D-Pad Down") },
            { "dpad/left", ("D-Pad Left", "D-Pad Left") },
            { "dpad/right", ("D-Pad Right", "D-Pad Right") },
            { "dpad", ("D-Pad", "D-Pad") },
            { "leftStick", ("L Stick", "L Stick") },
            { "rightStick", ("R Stick", "R Stick") },
            { "leftStick/x", ("L Stick", "L Stick") },
        };

        public static string GroupFor(InputDeviceFamily family) =>
            family == InputDeviceFamily.KeyboardMouse ? "Keyboard&Mouse" : "Gamepad";

        public static string Get(InputAction action, InputDeviceFamily family)
        {
            if (action == null) return "?";
            string group = GroupFor(family);
            var parts = new List<string>(2);
            for (int i = 0; i < action.bindings.Count; i++)
            {
                var b = action.bindings[i];
                if (b.isPartOfComposite) continue;
                if (b.isComposite)
                {
                    // Use the composite's display string for its parts, if in group.
                    if (i + 1 < action.bindings.Count && InGroup(action.bindings[i + 1], group))
                        parts.Add(action.GetBindingDisplayString(i, InputBinding.DisplayStringOptions.DontIncludeInteractions));
                    continue;
                }
                if (!InGroup(b, group)) continue;
                parts.Add(LabelForPath(b.effectivePath, family, action, i));
                if (parts.Count >= 1) break; // primary binding is enough for prompts
            }
            return parts.Count > 0 ? parts[0] : "Unbound";
        }

        private static bool InGroup(InputBinding b, string group) =>
            !string.IsNullOrEmpty(b.groups) && b.groups.Contains(group);

        private static string LabelForPath(string path, InputDeviceFamily family, InputAction action, int bindingIndex)
        {
            if (string.IsNullOrEmpty(path)) return "Unbound";
            if (path.StartsWith("<Gamepad>/") || path.StartsWith("<XInputController>/") || path.StartsWith("<DualShockGamepad>/"))
            {
                string control = path.Substring(path.IndexOf('/') + 1);
                if (GamepadNames.TryGetValue(control, out var names))
                    return family == InputDeviceFamily.PlayStation ? names.ps : names.xbox;
            }
            if (path == "<Mouse>/leftButton") return "LMB";
            if (path == "<Mouse>/rightButton") return "RMB";
            if (path == "<Mouse>/middleButton") return "MMB";
            if (path.StartsWith("<Mouse>/scroll")) return "Wheel";
            if (path == "<Mouse>/delta") return "Mouse";
            return action.GetBindingDisplayString(bindingIndex, InputBinding.DisplayStringOptions.DontIncludeInteractions);
        }
    }
}

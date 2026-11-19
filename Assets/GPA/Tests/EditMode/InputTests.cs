using GPA.Core;
using GPA.Input;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.InputSystem;

namespace GPA.Tests
{
    public class InputTests
    {
        private const string AssetPath = "Assets/GPA/Input/GPAControls.inputactions";

        private static InputActionAsset Load() => AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetPath);

        [Test]
        public void Asset_HasAllRequiredMaps()
        {
            var a = Load();
            Assert.NotNull(a);
            foreach (var m in new[] { ActionMaps.OnFoot, ActionMaps.Interaction, ActionMaps.Firearms, ActionMaps.WeaponWheel,
                                      ActionMaps.Vehicle, ActionMaps.PoliceComputer, ActionMaps.PoliceRadio, ActionMaps.UI })
                Assert.NotNull(a.FindActionMap(m), m);
        }

        [Test]
        public void EveryAction_HasKeyboardAndGamepadBinding_ForEssentialMaps()
        {
            var a = Load();
            // Radio presets are optional keyboard shortcuts (gamepad uses navigation).
            foreach (var map in a.actionMaps)
            {
                foreach (var action in map.actions)
                {
                    if (action.name.StartsWith("Preset") || action.name == "DebugConsole" || action.name.EndsWith("Click") || action.name == "Point" || action.name == "ScrollWheel") continue;
                    bool kb = false, gp = false;
                    foreach (var b in action.bindings)
                    {
                        if (b.groups == null) continue;
                        kb |= b.groups.Contains("Keyboard&Mouse");
                        gp |= b.groups.Contains("Gamepad");
                    }
                    Assert.IsTrue(kb, $"{map.name}/{action.name} missing keyboard binding");
                    Assert.IsTrue(gp, $"{map.name}/{action.name} missing gamepad binding");
                }
            }
        }

        [Test]
        public void Labels_AreDeviceCorrect()
        {
            var a = Load();
            var interact = a.FindAction("Interaction/Interact");
            Assert.AreEqual("E", BindingLabels.Get(interact, InputDeviceFamily.KeyboardMouse));
            Assert.AreEqual("A", BindingLabels.Get(interact, InputDeviceFamily.Xbox));
            Assert.AreEqual("Cross", BindingLabels.Get(interact, InputDeviceFamily.PlayStation));
            var wheel = a.FindAction("WeaponWheel/OpenWheel");
            Assert.AreEqual("L1", BindingLabels.Get(wheel, InputDeviceFamily.PlayStation));
        }

        [Test]
        public void DefaultContexts_NeverEnableOnFootAndVehicleTogether()
        {
            foreach (var c in InputManager.DefaultContexts())
            {
                bool foot = System.Array.IndexOf(c.Maps, ActionMaps.OnFoot) >= 0;
                bool veh = System.Array.IndexOf(c.Maps, ActionMaps.Vehicle) >= 0;
                Assert.IsFalse(foot && veh, c.State.ToString());
            }
            var ctx = InputManager.DefaultContexts().Find(c => c.State == GameState.PoliceComputer);
            Assert.IsFalse(System.Array.IndexOf(ctx.Maps, ActionMaps.OnFoot) >= 0);
        }
    }
}

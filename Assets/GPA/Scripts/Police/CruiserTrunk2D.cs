using System.Collections.Generic;
using GPA.TopDown2D;
using UnityEngine;
using Option = GPA.Police.InteractionMenu2D.Option;

namespace GPA.Police
{
    /// <summary>
    /// The patrol car's trunk. Walk to the back of the parked cruiser and interact (E / A) to open it:
    /// take the patrol rifle or the shotgun (one long gun at a time), put it back, or restock
    /// ammunition, taser cartridges and pepper spray.
    /// </summary>
    [RequireComponent(typeof(Car2D))]
    public sealed class CruiserTrunk2D : MonoBehaviour
    {
        private Car2D _car;
        private Interactable2D _point;

        private void Awake()
        {
            _car = GetComponent<Car2D>();
            var go = new GameObject("Trunk");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, -2.9f, 0f);
            _point = go.AddComponent<Interactable2D>();
            _point.Prompt = "Open trunk";
            _point.Range = 1.6f;
            _point.Used += _ => Open();
        }

        private void Update()
        {
            // Only reachable when the car is parked and nobody is driving it.
            _point.enabled = !_car.IsPlayerDriven && !_car.IsAIDriven && Mathf.Abs(_car.ForwardSpeed) < 0.5f;
        }

        private void Open()
        {
            var eq = OfficerEquipment2D.Instance;
            var menu = InteractionMenu2D.Instance;
            if (eq == null || menu == null) return;
            menu.Open("Patrol car trunk", $"{_car.Description} · {_car.Plate}", () =>
            {
                var list = new List<Option>();
                if (eq.LongGun == Gear.Unarmed)
                {
                    list.Add(new Option("Take patrol rifle", () => { eq.TakeLongGun(Gear.Rifle); menu.Close(); }));
                    list.Add(new Option("Take shotgun", () => { eq.TakeLongGun(Gear.Shotgun); menu.Close(); }));
                }
                else
                {
                    Gear other = eq.LongGun == Gear.Rifle ? Gear.Shotgun : Gear.Rifle;
                    list.Add(new Option($"Stow {GearInfo.Name(eq.LongGun)}", () => { eq.StowLongGun(); menu.Close(); }));
                    list.Add(new Option($"Swap for {GearInfo.Name(other)}", () => { eq.StowLongGun(); eq.TakeLongGun(other); menu.Close(); }));
                }
                list.Add(new Option("Restock ammo, cartridges and spray", () => { eq.Restock(); menu.Say("Restocked."); }));
                list.Add(new Option("Close trunk", () => menu.Close()));
                return list;
            });
            menu.Say("<i>The trunk pops open.</i>");
        }
    }
}

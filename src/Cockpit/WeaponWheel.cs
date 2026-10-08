using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using VTOLVR.DLC.EW;

namespace VirtualJoystick.Cockpit
{
    // Weapon wheel (optional): holding the weapon cycle key (right hand A / menu button) lists the aircraft's weapons
    // beside the stick overlay, and the mouse wheel picks one. A short press is still the menu button: it is sent on
    // release (down, then up the next frame), since a press can't be known to be short until it ends. With a hold
    // time of 0 the list opens on the press and the key no longer cycles at all.
    //  - The list is the weapons the game's weapon cycle goes through, by short name in hardpoint order, with the
    //    combined count of their hardpoints, named as the HUD names the selected weapon (short name in capitals).
    //  - EF-24: weapons of the other arming mode (AA / AG) are listed too; picking one switches the arming mode first.
    //    In EW master mode the button cycles transmitters (a hold cycles back), so the wheel stays out of the way.
    // While it's open the mouse wheel does nothing else (SOI zoom, FlatScreen 3 zoom, cockpit knobs).
    internal static class WeaponWheel
    {
        public struct Item
        {
            public string Name;   // as on the HUD
            public string ShortName;
            public int Count;
            public bool Current;
        }

        public static bool Open { get; private set; }
        public static readonly List<Item> Items = new List<Item>();

        private static readonly System.Reflection.MethodInfo AllowControl = AccessTools.Method(typeof(WeaponManager), "AllowControl");

        private static float _downAt = -1f;  // < 0: key not held
        private static bool _tapUp;          // a tap's button-down was sent last frame; send the up now

        private static VirtualJoystickSettings S => VirtualJoystickSettings.Current;

        public static void Reset()
        {
            Open = false;
            _downAt = -1f;
            _tapUp = false;
            Items.Clear();
        }

        // Called in place of reading the menu key for the right hand: whether its menu button is down this frame.
        public static bool MenuHeld(string key, WeaponManager wm, EF24Hotas hotas)
        {
            bool held = KeyActions.Held(key);
            bool usable = S.weaponWheel && wm != null && !(hotas != null && hotas.mode == EF24Hotas.MasterModes.EW);
            if (!usable)
            {
                // Switched off (or into EW mode) mid-press: carry on as a plain button.
                Reset();
                return held;
            }

            if (_tapUp)
            {
                _tapUp = false;
                if (held)
                    _downAt = Time.unscaledTime;
                return false;
            }

            if (held)
            {
                if (_downAt < 0f)
                    _downAt = Time.unscaledTime;
                // Hold time 0: opens on the press itself, so a tap never cycles.
                if (!Open && Time.unscaledTime - _downAt >= Mathf.Clamp(S.weaponWheelHoldSeconds, 0f, 1f))
                    Open = true;
                if (Open)
                {
                    Refresh(wm, hotas);
                    float scroll = Input.mouseScrollDelta.y;
                    if (scroll != 0f)
                        Step(scroll > 0f ? -1 : +1, wm, hotas); // wheel up = up the list
                }
                return false;
            }

            if (_downAt < 0f)
                return false;
            bool wasOpen = Open;
            Open = false;
            _downAt = -1f;
            Items.Clear();
            if (wasOpen)
                return false;
            _tapUp = true;
            return true; // a tap: button down now, up next frame
        }

        // The weapon cycle's list: armable weapons by short name, in hardpoint order (the game's own order), counting
        // only hardpoints that are armed (as the HUD count does). EF-24: unarmed ones too (the other arming mode).
        private static void Refresh(WeaponManager wm, EF24Hotas hotas)
        {
            Items.Clear();
            HPEquippable current = wm.currentEquip;
            for (int i = 0; i < wm.equipCount; i++)
            {
                HPEquippable eq = wm.GetEquip(i);
                if (eq == null || !eq.armable || (!eq.armed && hotas == null))
                    continue;
                int idx = IndexOf(eq.shortName);
                Item item = idx >= 0 ? Items[idx] : new Item
                {
                    ShortName = eq.shortName,
                    Name = eq.shortName.ToUpperInvariant(),
                    Current = current != null && current.shortName == eq.shortName,
                };
                item.Count += eq.GetCount();
                if (idx >= 0) Items[idx] = item;
                else Items.Add(item);
            }
        }

        // Selects the item `dir` steps from the current one (wrapping, as the weapon cycle does). Empty weapons can be
        // selected, as with the cycle.
        private static void Step(int dir, WeaponManager wm, EF24Hotas hotas)
        {
            if (Items.Count == 0)
                return;
            if (!wm.isMasterArmed)
            {
                VirtualJoystickBehaviour.ShowToastStatic("Master arm safe");
                return;
            }
            if (AllowControl != null && !(bool)AllowControl.Invoke(wm, null))
            {
                VirtualJoystickBehaviour.ShowToastStatic("Weapons controlled by the other seat");
                return;
            }
            int cur = -1;
            for (int i = 0; i < Items.Count; i++)
                if (Items[i].Current)
                    cur = i;
            int next = cur < 0 ? (dir > 0 ? 0 : Items.Count - 1) : (cur + dir + Items.Count) % Items.Count;
            Select(Items[next].ShortName, wm, hotas);
            Refresh(wm, hotas);
        }

        private static void Select(string shortName, WeaponManager wm, EF24Hotas hotas)
        {
            try
            {
                // EF-24: a weapon that isn't armed belongs to the other arming mode (the HOTAS arms exactly the
                // weapons of the selected mode).
                if (hotas != null && !IsArmed(shortName, wm))
                {
                    if (hotas.armingMode == EF24Hotas.ArmingModes.AA) hotas.SetArmingAG();
                    else hotas.SetArmingAA();
                }
                wm.SetWeapon(shortName);
            }
            catch (System.Exception e)
            {
                Log.Warn($"Weapon wheel: selecting {shortName} threw: {e.Message}");
            }
        }

        private static int IndexOf(string shortName)
        {
            for (int i = 0; i < Items.Count; i++)
                if (Items[i].ShortName == shortName)
                    return i;
            return -1;
        }

        private static bool IsArmed(string shortName, WeaponManager wm)
        {
            for (int i = 0; i < wm.equipCount; i++)
            {
                HPEquippable eq = wm.GetEquip(i);
                if (eq != null && eq.shortName == shortName && eq.armed)
                    return true;
            }
            return false;
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine.Events;

namespace VirtualJoystick.Cockpit
{
    // Describes what a controller input does in the current aircraft, by reading what the game has wired to that
    // input's UnityEvent: listeners baked into the aircraft prefab (persistent) and those added in code at runtime
    // (e.g. EF24Hotas, AH94CollectiveFunctions). Known handlers get a readable description, including what they do
    // with the modifier (the left controller's trigger) held; anything else is shown as its handler name.
    internal static class EventHints
    {
        private static readonly FieldInfo CallsField = AccessTools.Field(typeof(UnityEventBase), "m_Calls");

        // Handler ("Type.Method" or "Method") -> description. "·" separates the modifier variant.
        private static readonly Dictionary<string, string> Known = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Weapons
            { "WeaponManager.CycleActiveWeapons", "Cycle weapons" },
            { "WeaponManager.UserCycleActiveWeapon", "Cycle weapons" },
            { "WeaponManager.StartFire", "Fire weapon" },
            { "WeaponManager.EndFire", "Fire weapon" },
            { "WeaponManager.FireWeapon", "Fire weapon" },
            // SOI / MFD
            { "MFDManager.OnInputAxis", "Slew SOI cursor" },
            { "MFDPortalManager.OnInputAxis", "Slew SOI cursor" },
            { "MFDManager.OnInputButtonDown", "SOI select (thumbstick press)" },
            { "MFDPortalManager.OnInputButtonDown", "SOI select (thumbstick press)" },
            { "MFDManager.OnInputButton", "SOI select (thumbstick press)" },
            { "MFDPortalManager.OnInputButton", "SOI select (thumbstick press)" },
            { "ThrottleSOISwitcher.OnSetThumbstick", "Switch SOI page" },
            { "MultiPortalSOISwitcher.OnSetThumbstick", "Switch SOI page" },
            { "VehicleInputManager.SetThrottleThumbstick", "Throttle thumbstick: SOI switch / VTOL nozzles / thumb rudder" },
            { "VehicleInputManager.SetVirtualBrakes", "Wheel brakes" },
            { "VehicleInputManager.SetJoystickPYR", "Flight controls" },
            // AH-94 combat collective (its trigger is the modifier)
            { "AH94CollectiveFunctions.CombatOnTriggerDown", "MODIFIER (hold): menu → countermeasures, thumbstick → gun pod aim" },
            { "AH94CollectiveFunctions.CombatMenuButtonDown", "Switch SOI · with trigger held: countermeasures" },
            { "AH94CollectiveFunctions.CombatOnSetThumbstick", "SOI control · with trigger held: aim articulating gun pod" },
            { "AH94CollectiveFunctions.CombatOnStickPressDown", "Gun pod: back to auto aim" },
            { "AH94CollectiveFunctions.FlightMenuButtonDown", "Flight collective menu" },
            { "AH94CollectiveFunctions.OnFlightCollectiveThumbstick", "Flight collective thumbstick" },
            // EF-24 HOTAS (rear seat left stick trigger is the modifier)
            { "EF24Hotas.OnJoyMenuButtonDown", "Cycle weapons (EW mode: cycle transmitters)" },
            { "EF24Hotas.OnJoyTriggerDown", "Trigger: fire / EW transmit" },
            { "EF24Hotas.ThrottleThumbstickAxis", "Up/down: radar elevation, or TGP / TSD / map zoom" },
            { "EF24Hotas.RearLeftStickThumbstickAxis", "Switch SOI; up/down: radar elevation or zoom · with trigger held: EW transmit power" },
            { "EF24Hotas.OnEWOLeftStickTriggerDown", "MODIFIER (hold): menu → EM band, thumbstick up/down → transmit power" },
            { "EF24Hotas.EWOLeftStickMenuButtonDown", "EW transmitter control · with trigger held: cycle EM band" },
        };

        // Handlers that are plumbing rather than a function the pilot would recognise.
        private static bool Ignored(string type, string method) =>
            type.IndexOf("Sync", StringComparison.Ordinal) >= 0
            || type == "VRJoystick" || type == "VRThrottle" || type == "VRInteractable"
            || method.StartsWith("Haptic", StringComparison.Ordinal);

        public static string Describe(params UnityEventBase[] events)
        {
            var parts = new List<string>();
            foreach (var e in events)
            {
                if (e == null)
                    continue;
                foreach (var (type, method) in Listeners(e))
                {
                    if (Ignored(type, method))
                        continue;
                    string text;
                    if (!Known.TryGetValue(type + "." + method, out text) && !Known.TryGetValue(method, out text))
                        text = Pretty(type, method);
                    if (!parts.Contains(text))
                        parts.Add(text);
                }
            }
            return parts.Count == 0 ? null : string.Join(" · ", parts);
        }

        // Every listener: persistent (prefab) via the public API, runtime ones (AddListener) via the call list.
        private static IEnumerable<(string type, string method)> Listeners(UnityEventBase e)
        {
            var result = new List<(string, string)>();
            for (int i = 0; i < e.GetPersistentEventCount(); i++)
            {
                var target = e.GetPersistentTarget(i);
                string method = e.GetPersistentMethodName(i);
                if (target != null && !string.IsNullOrEmpty(method))
                    result.Add((target.GetType().Name, method));
            }
            try
            {
                object calls = CallsField?.GetValue(e);
                var runtime = calls == null ? null : AccessTools.Field(calls.GetType(), "m_RuntimeCalls")?.GetValue(calls) as IList;
                if (runtime != null)
                {
                    foreach (var call in runtime)
                    {
                        if (call == null)
                            continue;
                        var del = AccessTools.Field(call.GetType(), "Delegate")?.GetValue(call) as Delegate;
                        if (del == null)
                            continue;
                        string type = del.Method.DeclaringType != null ? del.Method.DeclaringType.Name : "?";
                        // Lambdas compile to "<Start>b__12_0" on a nested "<>c" class: name the owning class instead.
                        if (type.StartsWith("<", StringComparison.Ordinal) && del.Method.DeclaringType?.DeclaringType != null)
                            type = del.Method.DeclaringType.DeclaringType.Name;
                        result.Add((type, del.Method.Name));
                    }
                }
            }
            catch (Exception)
            {
                // Unity changed its event internals: persistent listeners are still shown.
            }
            return result;
        }

        // "CycleActiveWeapons" on "WeaponManager" -> "Cycle Active Weapons (Weapon Manager)".
        private static string Pretty(string type, string method)
        {
            if (method.StartsWith("<", StringComparison.Ordinal))
                return Words(type);
            string m = method;
            if (m.StartsWith("On", StringComparison.Ordinal) && m.Length > 2 && char.IsUpper(m[2]))
                m = m.Substring(2);
            if (m.StartsWith("Vrint_", StringComparison.Ordinal))
                m = m.Substring(6);
            return $"{Words(m)} ({Words(type)})";
        }

        private static string Words(string s)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '_')
                {
                    sb.Append(' ');
                    continue;
                }
                if (i > 0 && char.IsUpper(c) && (char.IsLower(s[i - 1]) || (i + 1 < s.Length && char.IsLower(s[i + 1]) && char.IsUpper(s[i - 1]))))
                    sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString().Trim();
        }
    }
}

using HarmonyLib;
using UnityEngine;

namespace MouseStick
{
    // VehicleInputManager.Update pushes its stored stick value to the flight controls every frame. Every active
    // VRJoystick (F/A-26B has a side and a centre stick) and BYOJoystick write that value during Update too, in
    // no fixed order, so a value written from LateUpdate is always overwritten before it is used. Overriding
    // it right before VehicleInputManager consumes it is the one point that always wins.
    // Applied by the mod loader's PatchAll.
    [HarmonyPatch(typeof(VehicleInputManager), "Update")]
    internal static class VehicleInputPatch
    {
        private static void Prefix(VehicleInputManager __instance, ref Vector3 ___vrJoyPYR, bool ___remoteCtrl)
        {
            if (!MouseStickBehaviour.IsActive || ___remoteCtrl || __instance != MouseStickBehaviour.TargetInputManager)
                return;
            ___vrJoyPYR = MouseStickBehaviour.Output;
        }
    }
}

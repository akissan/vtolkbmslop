using System;
using HarmonyLib;

namespace MouseStick
{
    // FlatScreen 3 raycasts from the cursor every frame and clicks whatever cockpit control is under it on LMB.
    // While the mouse is flying the aircraft that would flip random switches, so we suppress its hover pass.
    // FlatScreen 3 is an optional soft dependency: patched by name, at runtime, only if it is loaded.
    internal static class FlatScreenCompat
    {
        private const string HarmonyId = "vtolmouse.mousestick.flatscreen3";
        private const string TypeName = "Triquetra.FlatScreen3.FlatScreen3MonoBehaviour";

        private static Harmony _harmony;
        private static bool _attempted;

        public static void TryPatch()
        {
            if (_attempted)
                return;

            Type type = AccessTools.TypeByName(TypeName);
            if (type == null)
                return; // Not loaded (yet). Retried on the next activation.

            _attempted = true;
            _harmony = new Harmony(HarmonyId);

            try
            {
                var hover = AccessTools.Method(type, "GetHoveredObject");
                if (hover == null)
                    Log.Warn("FlatScreen 3 found but GetHoveredObject is missing; cockpit clicks will not be suppressed.");
                else
                {
                    _harmony.Patch(hover, prefix: new HarmonyMethod(typeof(FlatScreenCompat), nameof(GetHoveredObjectPrefix)));
                    Log.Info("Hooked FlatScreen 3 hover");
                }
            }
            catch (Exception e)
            {
                Log.Error("Failed to hook FlatScreen 3 hover: " + e);
            }
        }

        public static void Unpatch()
        {
            _harmony?.UnpatchSelf();
            _harmony = null;
            _attempted = false;
        }

        private static System.Collections.Generic.IEnumerable<VRInteractable> _fsListSeen;
        private static System.Collections.Generic.List<VRInteractable> _fsListFiltered;
        private static int _fsFilterVersion = -1;

        private static bool GetHoveredObjectPrefix(ref VRInteractable ___targetedVRInteractable,
            ref System.Collections.Generic.IEnumerable<VRInteractable> ___vrInteractables)
        {
            // Screen elements (MFD on-screen buttons, touchscreens) are handled by ScreenPointer: take them out of
            // FlatScreen 3's candidates. Re-filter when FlatScreen 3 rebuilds its list (new reference) or when the set
            // of screen elements changes (ScreenPointer.Version), always from FlatScreen 3's own, unfiltered list.
            if (___vrInteractables != null)
            {
                bool newList = !ReferenceEquals(___vrInteractables, _fsListFiltered);
                if (newList)
                    _fsListSeen = ___vrInteractables;
                if (newList || _fsFilterVersion != ScreenPointer.Version)
                {
                    _fsFilterVersion = ScreenPointer.Version;
                    _fsListFiltered = new System.Collections.Generic.List<VRInteractable>();
                    foreach (var v in _fsListSeen)
                        if (!ScreenPointer.IsOwned(v))
                            _fsListFiltered.Add(v);
                }
                ___vrInteractables = _fsListFiltered;
            }

            bool block = MouseStickBehaviour.SuppressCockpitHover || SettingsWindow.CursorOverWindow || ScreenPointer.HasHover;
            if (!block)
                return true;
            ___targetedVRInteractable = null;
            return false;
        }
    }
}

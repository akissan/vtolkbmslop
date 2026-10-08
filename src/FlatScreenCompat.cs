using System;
using System.Reflection;
using HarmonyLib;

namespace VirtualJoystick
{
    // FlatScreen 3 raycasts from the cursor every frame and clicks whatever cockpit control is under it on LMB.
    // While the mouse is flying the aircraft that would flip random switches, so we suppress its hover pass.
    // FlatScreen 3 is an optional soft dependency: patched by name, at runtime, only if it is loaded.
    internal static class FlatScreenCompat
    {
        private const string HarmonyId = "vtolmouse.kbmslop.flatscreen3";
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

            try
            {
                // Hover / clicks aim through GetMouseRay; centred in clickable mode + free look (CenterCursor).
                var mouseRay = AccessTools.Method(type, "GetMouseRay");
                SuppressRmbCamera = AccessTools.Field(type, "suppressRightMouseCamera");
                TargetedField = AccessTools.Field(type, "targetedVRInteractable");
                if (mouseRay == null)
                    Log.Warn("FlatScreen 3 found but GetMouseRay is missing; the centre cursor won't aim its clicks.");
                else
                {
                    _harmony.Patch(mouseRay, prefix: new HarmonyMethod(typeof(FlatScreenCompat), nameof(GetMouseRayPrefix)));
                    Log.Info("Hooked FlatScreen 3 mouse ray");
                }
            }
            catch (Exception e)
            {
                Log.Error("Failed to hook FlatScreen 3 mouse ray: " + e);
            }

            try
            {
                var update = AccessTools.Method(type, "Update");
                var setFov = AccessTools.Method(type, "SetCameraFOV");
                TargetFov = AccessTools.Field(type, "_targetFoV");
                _instanceProp = AccessTools.Property(type, "instance");
                if (update == null || setFov == null || TargetFov == null || _instanceProp == null)
                {
                    TargetFov = null;
                    Log.Warn("FlatScreen 3 found but its FOV members are missing; scroll zoom can't be blocked, FOV keys disabled.");
                }
                else
                {
                    _harmony.Patch(update, prefix: new HarmonyMethod(typeof(FlatScreenCompat), nameof(UpdatePrefix)),
                        finalizer: new HarmonyMethod(typeof(FlatScreenCompat), nameof(UpdateFinalizer)));
                    _harmony.Patch(setFov, prefix: new HarmonyMethod(typeof(FlatScreenCompat), nameof(SetCameraFovPrefix)));
                    Log.Info("Hooked FlatScreen 3 FOV");
                }
            }
            catch (Exception e)
            {
                TargetFov = null;
                Log.Error("Failed to hook FlatScreen 3 FOV: " + e);
            }
        }

        // ---------------------------------------------------------------- centre cursor

        private static FieldInfo SuppressRmbCamera; // FlatScreen3MonoBehaviour.suppressRightMouseCamera

        // RMB went down on a rotary knob: FlatScreen 3 pressed it instead of starting free look.
        public static bool RmbOnControl
        {
            get
            {
                object fs = SuppressRmbCamera == null ? null : FsInstance();
                return fs != null && (bool)SuppressRmbCamera.GetValue(fs);
            }
        }

        private static FieldInfo TargetedField; // FlatScreen3MonoBehaviour.targetedVRInteractable

        // The cockpit control FlatScreen 3 has under the cursor (for the log), or null.
        public static VRInteractable Targeted
        {
            get
            {
                object fs = TargetedField == null ? null : FsInstance();
                return fs == null ? null : TargetedField.GetValue(fs) as VRInteractable;
            }
        }

        private static bool GetMouseRayPrefix(UnityEngine.Camera camera, ref UnityEngine.Ray __result)
        {
            if (!VirtualJoystickBehaviour.CenterCursor || camera == null)
                return true;
            __result = new UnityEngine.Ray(camera.transform.position, camera.transform.forward);
            return false;
        }

        // ---------------------------------------------------------------- camera FOV

        // FlatScreen 3's own limits and starting FOV.
        public const float MinFov = 30f, MaxFov = 120f, DefaultFov = 60f;

        private static FieldInfo TargetFov;     // FlatScreen3MonoBehaviour._targetFoV: the FOV it eases the camera to
        private static PropertyInfo _instanceProp;  // FlatScreen3MonoBehaviour.instance
        private static bool _inFsUpdate;
        private static float _targetFovAtUpdateStart;

        // FlatScreen 3's scroll-wheel zoom is ignored when turned off in the settings, in SOI cursor mode (the wheel zooms
        // the SOI page / TGP there), while the weapon wheel is open and over the settings window.
        private static bool BlockScrollZoom => VirtualJoystickSettings.Current.disableFlatScreenScrollZoom
            || VirtualJoystickBehaviour.SoiMode || Cockpit.WeaponWheel.Open || SettingsWindow.CursorOverWindow;

        public static bool FovAvailable => TargetFov != null && FsInstance() != null;

        private static object FsInstance() => _instanceProp?.GetValue(null);

        // FlatScreen 3's target FOV: the camera eases there itself. False without FlatScreen 3.
        public static bool TryGetFov(out float fov)
        {
            fov = 0f;
            object fs = TargetFov == null ? null : FsInstance();
            if (fs == null)
                return false;
            fov = (float)TargetFov.GetValue(fs);
            return true;
        }

        public static void SetFov(float fov)
        {
            object fs = TargetFov == null ? null : FsInstance();
            if (fs != null)
                TargetFov.SetValue(fs, UnityEngine.Mathf.Clamp(fov, MinFov, MaxFov));
        }

        // Within FlatScreen3MonoBehaviour.Update only its scroll-wheel zoom changes _targetFoV (then applies it with
        // SetCameraFOV); its per-frame easing calls SetCameraFOV with the value unchanged. So a SetCameraFOV during
        // Update with a changed target is the scroll zoom: undo the change and skip it.
        private static void UpdatePrefix(float ____targetFoV)
        {
            _inFsUpdate = true;
            _targetFovAtUpdateStart = ____targetFoV;
        }

        private static void UpdateFinalizer() => _inFsUpdate = false;

        private static bool SetCameraFovPrefix(ref float ____targetFoV)
        {
            if (!_inFsUpdate || ____targetFoV == _targetFovAtUpdateStart || !BlockScrollZoom)
                return true;
            ____targetFoV = _targetFovAtUpdateStart;
            return false;
        }

        // Recentre the view: FlatScreen 3's own camera reset (exactly what its Ctrl+Z calls) if it's loaded, else the
        // game's VR recentre. Returns false (and logs why) if it couldn't.
        public static bool RecenterView()
        {
            try
            {
                Type type = AccessTools.TypeByName(TypeName);
                if (type == null)
                {
                    VRHead.ReCenter();
                    Log.Info("View recentred (VR recentre; FlatScreen 3 not loaded)");
                    return true;
                }
                object fs = AccessTools.Property(type, "instance")?.GetValue(null);
                MethodInfo reset = AccessTools.Method(type, "ResetCameraRotation");
                if (fs == null || reset == null)
                {
                    Log.Warn($"View recentre: FlatScreen 3 {(fs == null ? "instance" : "ResetCameraRotation")} not found");
                    return false;
                }
                // ResetCameraRotation dereferences the eye camera; FlatScreen 3's own Ctrl+Z skips it while that's null.
                var eye = AccessTools.Field(type, "cameraEyeGameObject")?.GetValue(fs) as UnityEngine.Object;
                if (eye == null)
                {
                    Log.Warn("View recentre: FlatScreen 3 camera not set up yet");
                    return false;
                }
                reset.Invoke(fs, null);
                Log.Info("View recentred (FlatScreen 3 camera reset)");
                return true;
            }
            catch (Exception e)
            {
                Exception inner = e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
                Log.Warn("View recentre failed: " + inner);
                return false;
            }
        }

        public static void Unpatch()
        {
            _harmony?.UnpatchSelf();
            _harmony = null;
            _attempted = false;
            TargetFov = null;
            SuppressRmbCamera = null;
            TargetedField = null;
            _instanceProp = null;
            _inFsUpdate = false;
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

            bool block = VirtualJoystickBehaviour.SuppressCockpitHover || VirtualJoystickBehaviour.HeadModeOwnsLmb
                || SettingsWindow.CursorOverWindow || ScreenPointer.HasHover;
            if (!block)
                return true;
            ___targetedVRInteractable = null;
            return false;
        }
    }
}

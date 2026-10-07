using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MouseStick
{
    // FPS-style targeting pod aiming: each frame's mouse movement becomes an exact pod rotation (scaled by the
    // pod's zoom), instead of the rate-based thumbstick slew the game normally uses.
    //
    // How the game slews (OpticalTargeter.SlewAtEndOfFrame): it takes the point 100 m along the camera,
    // moves it by (right*dir.x + up*dir.y) * slewRate * deltaTime metres, and re-aims at it. So passing
    // dir = tan(angle) and slewRate = 100 / deltaTime turns the camera by exactly `angle`.
    internal class TgpControl
    {
        // At sensitivity 1: 0.1 degrees per pixel at a 60 degree FOV, scaled with the current FOV (zoom).
        private const float DegreesPerPixelAt60 = 0.1f;
        // Re-lock the pod onto whatever it's pointing at once the mouse has been still this long.
        private const float RelockAfterSeconds = 0.15f;

        // Private game members, looked up without throwing: if a game update renames them, TGP mode reports it
        // and does nothing instead of breaking the rest of the mod.
        private static readonly MethodInfo SetModeMethod = AccessTools.Method(typeof(TargetingMFDPage), "SetMode");
        private static readonly FieldInfo StoppedEofSlewTime = AccessTools.Field(typeof(OpticalTargeter), "stoppedEofSlewTime");

        private TargetingMFDPage _page;
        private bool _slewing;
        private float _lastMoveTime;

        // Starts aiming with this TGP page (the SOI one). Returns a message for the player if it can't be aimed.
        public string Begin(TargetingMFDPage page)
        {
            _page = null;
            _slewing = false;
            if (SetModeMethod == null)
            {
                Log.Warn("TargetingMFDPage.SetMode not found (game update?); TGP aiming disabled");
                return "TGP aiming unavailable (game changed)";
            }
            if (page == null || page.opticalTargeter == null)
                return "TGP: no targeting pod";
            if (page.remoteOnly)
                return "TGP is controlled by the other seat";
            _page = page;
            if (!_page.powered)
                return "TGP is off";
            return null;
        }

        // Call every frame while TGP mode is held, with this frame's mouse movement in pixels (+x right, +y up).
        public void Update(Vector2 px, float sensitivity)
        {
            if (_page == null || !_page.powered)
                return;
            OpticalTargeter ot = _page.opticalTargeter;
            if (ot == null)
                return;

            if (px == Vector2.zero)
            {
                if (_slewing && Time.unscaledTime - _lastMoveTime > RelockAfterSeconds)
                    Relock();
                return;
            }

            float dt = Time.deltaTime;
            if (dt <= 0f)
                return; // paused

            // Slewing only works on a locked pod (TGT mode); this locks it where it's currently looking.
            if (!ot.locked || _page.tgpMode != TargetingMFDPage.TGPModes.TGT)
            {
                SetModeMethod.Invoke(_page, new object[] { TargetingMFDPage.TGPModes.TGT, true });
                if (!ot.locked)
                    return;
            }
            _page.StopAutoSlew();

            float fov = _page.fovs != null && _page.fovIdx < _page.fovs.Length ? _page.fovs[_page.fovIdx] : 60f;
            Vector2 deg = px * (DegreesPerPixelAt60 * sensitivity * fov / 60f);
            Vector2 dir = new Vector2(Mathf.Tan(deg.x * Mathf.Deg2Rad), Mathf.Tan(deg.y * Mathf.Deg2Rad));

            // A re-lock blocks slews for 0.2 s; let this one through so moving right after a pause isn't dropped.
            StoppedEofSlewTime?.SetValue(ot, -10f);
            ot.Slew(dir, 100f / dt);

            _slewing = true;
            _lastMoveTime = Time.unscaledTime;
        }

        // Leaving TGP mode: settle the pod onto whatever it's pointing at.
        public void End()
        {
            if (_slewing)
                Relock();
            _page = null;
        }

        private void Relock()
        {
            _slewing = false;
            if (_page != null && _page.powered && _page.opticalTargeter != null && _page.opticalTargeter.locked)
                _page.OnResetThumbstick();
        }
    }
}

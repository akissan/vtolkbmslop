using UnityEngine;

namespace VirtualJoystick.Cockpit
{
    // Engine tilt keys (AV-42C, F-45A...). Tilt goes from 0 = hover (engines vertical) to maxTilt = forward flight:
    // the game blocks lowering tilt above its airspeed limit, which is the "back towards hover" direction.
    // Input goes through TiltController.PadInputScaled, the same call the throttle thumbstick uses, so the keys move
    // at the game's tilt speed and keep its battery drain, airspeed lockout and soft-close.
    // Detents: there are none in between; the only one is the soft-close at the forward-flight end (within
    // softCloseThresh of maxTilt and released, the nacelles close fully on their own), which "tilt max" reaches.
    internal static class TiltKeys
    {
        // A preset that hasn't moved the tilt for this long has been stopped by the game (e.g. airspeed lockout).
        private const float StallSeconds = 0.4f;

        public static TiltController Tilt { get; private set; }

        private static int _presetDir;      // +1 to max, -1 to min, 0 none
        private static float _lastTilt, _stallTime;

        private static VirtualJoystickSettings S => VirtualJoystickSettings.Current;

        public static void Find(GameObject vehicle)
        {
            _presetDir = 0;
            Tilt = null;
            // The vehicle input manager's tilt controller is the one the thumbstick drives.
            foreach (var vim in vehicle.GetComponentsInChildren<VehicleInputManager>(true))
            {
                if (vim.tiltController != null)
                {
                    Tilt = vim.tiltController;
                    return;
                }
            }
            Tilt = vehicle.GetComponentInChildren<TiltController>(true);
        }

        public static void Clear()
        {
            Tilt = null;
            _presetDir = 0;
        }

        public static string Describe() =>
            Tilt == null ? null : $"tilt {Tilt.currentTilt:0}° of {Tilt.maxTilt:0}° (0° = hover, {Tilt.maxTilt:0}° = forward flight)";

        public static void Update(float dt)
        {
            if (Tilt == null || !Tilt.isActiveAndEnabled)
                return;

            if (KeyActions.Pressed(S.tiltMaxKey)) StartPreset(+1);
            if (KeyActions.Pressed(S.tiltMinKey)) StartPreset(-1);

            // Held up/down: full thumbstick deflection. Up = engines toward vertical (hover) = lower tilt.
            float dir = (KeyActions.Held(S.tiltDownKey) ? 1f : 0f) - (KeyActions.Held(S.tiltUpKey) ? 1f : 0f);
            if (dir != 0f)
            {
                _presetDir = 0;
                Tilt.PadInputScaled(new Vector3(0f, dir, 0f));
                return;
            }

            if (_presetDir == 0)
                return;
            float target = _presetDir > 0 ? Tilt.maxTilt : 0f;
            if (Mathf.Abs(Tilt.currentTilt - target) < 0.01f)
            {
                _presetDir = 0;
                return;
            }
            Tilt.PadInputScaled(new Vector3(0f, _presetDir, 0f));

            // Stop if the game won't move it any further (airspeed lockout, no battery).
            if (Mathf.Abs(Tilt.currentTilt - _lastTilt) > 0.001f)
            {
                _lastTilt = Tilt.currentTilt;
                _stallTime = 0f;
            }
            else if ((_stallTime += dt) > StallSeconds)
                _presetDir = 0;
        }

        private static void StartPreset(int dir)
        {
            _presetDir = dir;
            _lastTilt = Tilt.currentTilt;
            _stallTime = 0f;
        }
    }
}

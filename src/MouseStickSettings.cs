using System;
using System.IO;
using UnityEngine;

namespace MouseStick
{
    // Stored as JSON next to the game's other save data (same place FlatScreen 3 keeps flatscreen3.xml),
    // so it survives mod updates. Re-read every time the stick is switched on, so it can be tuned live.
    [Serializable]
    public class MouseStickSettings
    {
        // "Tap": tap a toggle key on its own to toggle. "Hold": active only while a toggle key is held.
        public string toggleMode = "Tap";
        // Switch stick control on automatically once per spawn, when you enter the cockpit.
        public bool enableOnSpawn = true;
        // Either key toggles stick control (tap).
        public string toggleKey = "C";
        public string toggleKey2 = "None";
        // A tap longer than this (seconds) is treated as a held modifier and ignored.
        public float tapMaxSeconds = 0.4f;

        // At 1.0 the dot follows the mouse pixel for pixel, so full deflection = half the control area of travel.
        public float sensitivity = 1.0f;
        // Mouse up = stick forward (nose down), like a real stick. Set true for mouse up = nose up.
        public bool invertPitch = false;
        // Round centre zone, as a fraction of full deflection, where the stick outputs zero.
        public float deadzone = 0.05f;
        // Inverse cubic: blends linear with 1-(1-x)^3, so the closer to centre, the stronger the sensitivity.
        // 0 = linear; 1 = 3x at centre, flattening toward the edge. Full deflection is still reached at the edge.
        public float curve = 0f;
        // Stick drifts back to centre at this rate (full deflections per second). 0 = stick stays where you leave it.
        public float autoCenterRate = 0f;
        // Square limit (pitch and roll each reach 1.0 independently) or round limit.
        public bool circularLimit = false;

        public string rudderLeftKey = "Q";
        public string rudderRightKey = "E";
        // How quickly rudder ramps toward full / back to zero (per second).
        public float rudderRate = 4f;

        // Keyboard pitch / roll, added on top of the mouse position. Springs back to centre when released.
        public string pitchDownKey = "W";
        public string pitchUpKey = "S";
        public string rollLeftKey = "A";
        public string rollRightKey = "D";
        // How quickly keyboard input ramps toward full while a key is held (per second).
        public float keyboardRate = 3f;
        // How quickly keyboard input returns to centre once the keys are released (per second).
        public float keyboardReturnRatePitch = 4.8f;
        public float keyboardReturnRateRoll = 4.8f;

        // Opens the settings window.
        public string menuKey = "F8";

        // Hold to flip stick control while held: if the mouse stick is off it's on until released; if it's on you get
        // clickable mode (mouse stops flying and clicks cockpit controls; WASD still flies).
        public string clickModeKey = "LeftAlt";
        // Opacity of the minimal dot-and-line overlay shown in clickable and TGP modes (independent of overlayOpacity).
        public float clickModeOpacity = 0.5f;

        // SOI cursor mode (the mouse drives the SOI MFD page): tgpModeKey toggles it, soiHoldKey flips it while held.
        public string tgpModeKey = "G";
        // Unity counts mouse buttons from 0: "Mouse3" is mouse button 4 (side "back" button).
        public string soiHoldKey = "Mouse3";
        // Pod rotation per pixel of mouse movement. At 1.0: 0.1 degrees per pixel at a 60 degree FOV, scaled with
        // zoom, so aim gets finer as you zoom in.
        public float tgpSensitivity = 1f;
        // G mode on radar / ARAD / map / TSD: at 1.0, 50 px of mouse moves the page's cursor as far as one second of
        // full thumbstick does.
        public float cursorSensitivity = 1f;

        // LMB = stick trigger (gun / weapon release) while active.
        public bool leftMouseFiresTrigger = true;
        // Middle mouse snaps the stick back to centre.
        public bool middleMouseRecenters = true;
        // Re-centre the virtual stick every time the mode is switched on.
        public bool recenterOnEnable = true;

        // Keys that should also switch the mode off (so FlatScreen 3's menus get a free cursor).
        public string[] releaseKeys = { "Escape", "F9" };

        // Side length of the square control area, in screen pixels.
        public float overlaySize = 300f;
        public float overlayOpacity = 0.85f;

        // Cockpit screens (MFD on-screen buttons, touchscreen drag) are clicked by this mod instead of FlatScreen 3,
        // using the game's real hitboxes. Off = leave them to FlatScreen 3.
        public bool handleScreens = true;
        // Outline the on-screen hitbox under the cursor.
        public bool showScreenHitbox = true;
        // Debug: tooltip next to the cursor naming the hovered screen element, its hitbox source and size.
        public bool showScreenTooltip = false;

        public static MouseStickSettings Current { get; private set; } = new MouseStickSettings();

        // Set by the settings window while sliders move; written out when the window closes.
        private static bool _dirty;

        public static void MarkDirty() => _dirty = true;

        public static void SaveIfDirty()
        {
            if (_dirty)
                Save();
        }

        public static void ResetToDefaults()
        {
            Current = new MouseStickSettings();
            Save();
        }

        public static string FilePath
        {
            get
            {
                string dir = PilotSaveManager.saveDataPath;
                if (string.IsNullOrEmpty(dir))
                    dir = Application.persistentDataPath;
                return Path.Combine(dir, "mousestick.json");
            }
        }

        public static void Load()
        {
            string path = FilePath;
            try
            {
                if (File.Exists(path))
                {
                    var loaded = new MouseStickSettings();
                    JsonUtility.FromJsonOverwrite(File.ReadAllText(path), loaded);
                    loaded.Normalize();
                    Current = loaded;
                    // Rewrite so options added in newer versions show up in the file.
                    Save();
                }
                else
                {
                    Current = new MouseStickSettings();
                    Save();
                    Log.Info("Wrote default settings to " + path);
                }
            }
            catch (Exception e)
            {
                Log.Error("Failed to load " + path + ", using defaults: " + e.Message);
                Current = new MouseStickSettings();
            }
        }

        public static void Save()
        {
            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(Current, true));
                _dirty = false;
            }
            catch (Exception e)
            {
                Log.Error("Failed to save settings: " + e.Message);
            }
        }

        private void Normalize()
        {
            sensitivity = Clamp(sensitivity, 0.05f, 20f, 1f);
            deadzone = Clamp(deadzone, 0f, 0.5f, 0.05f);
            keyboardRate = Clamp(keyboardRate, 0.1f, 100f, 3f);
            keyboardReturnRatePitch = Clamp(keyboardReturnRatePitch, 0.1f, 100f, 4.8f);
            keyboardReturnRateRoll = Clamp(keyboardReturnRateRoll, 0.1f, 100f, 4.8f);
            curve = Clamp(curve, 0f, 1f, 0f);
            autoCenterRate = Clamp(autoCenterRate, 0f, 20f, 0f);
            rudderRate = Clamp(rudderRate, 0.1f, 100f, 4f);
            tapMaxSeconds = Clamp(tapMaxSeconds, 0.05f, 5f, 0.4f);
            overlaySize = Clamp(overlaySize, 60f, 2000f, 300f);
            overlayOpacity = Clamp(overlayOpacity, 0.05f, 1f, 0.85f);
            clickModeOpacity = Clamp(clickModeOpacity, 0f, 1f, 0.5f);
            tgpSensitivity = Clamp(tgpSensitivity, 0.05f, 10f, 1f);
            cursorSensitivity = Clamp(cursorSensitivity, 0.05f, 10f, 1f);
            if (releaseKeys == null)
                releaseKeys = new string[0];
        }

        private static float Clamp(float v, float min, float max, float fallback)
        {
            if (float.IsNaN(v) || float.IsInfinity(v))
                return fallback;
            return Mathf.Clamp(v, min, max);
        }

        public bool HoldMode => string.Equals(toggleMode, "Hold", StringComparison.OrdinalIgnoreCase);

        public static KeyCode ParseKey(string name)
        {
            if (string.IsNullOrEmpty(name))
                return KeyCode.None;
            try
            {
                return (KeyCode)Enum.Parse(typeof(KeyCode), name, true);
            }
            catch (ArgumentException)
            {
                Log.Warn("Unknown key name '" + name + "'");
                return KeyCode.None;
            }
        }
    }
}

using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace VirtualJoystick
{
    // Every setting and its default value. Changed live from the settings window (F8) and saved to a JSON file
    // (see FilePath) as they change, so settings and key bindings carry over between game sessions.
    [Serializable]
    public class VirtualJoystickSettings
    {
        // "Tap": tap a toggle key on its own to toggle. "Hold": active only while a toggle key is held.
        public string toggleMode = "Tap";
        // Open the KBM SLOP window (F8) when the game starts.
        public bool openWindowOnStart = true;
        // Switch stick control on automatically once per spawn, when you enter the cockpit.
        public bool enableOnSpawn = false;
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
        public float keyboardRatePitch = 3f;   // W/S
        public float keyboardRateRoll = 3f;    // A/D
        // How quickly keyboard input returns to centre once the keys are released (per second).
        public float keyboardReturnRatePitch = 4.8f;
        public float keyboardReturnRateRoll = 4.8f;
        // Off: that axis stays where the keys left it instead of returning to centre.
        public bool keyboardReturnPitch = true;
        public bool keyboardReturnRoll = true;

        // Bindings tab. Stick = right VR controller, throttle = left VR controller, aircraft = named cockpit
        // controls found in whatever aircraft you fly. All global (the same keys in every aircraft).
        // Stick (right controller); pitch/roll/rudder keys are above.
        public string triggerKey = "Space";         // stick trigger
        public string weaponCycleKey = "R";         // stick A / menu button (weapon cycle)
        public string stickBKey = "None";         // stick B / second button
        public string thumbLeftKey = "None";
        public string thumbRightKey = "None";
        public string thumbUpKey = "None";
        public string thumbDownKey = "None";
        public string thumbPressKey = "None";

        // Throttle (left controller).
        public string throttleUpKey = "LeftShift";
        public string throttleDownKey = "LeftControl";
        public float throttleRate = 1f;           // throttle travel per second while held (3x above the AB detent)
        public string throttleFullKey = "Alpha1";   // full throttle (afterburner on aircraft that have it)
        public string throttleMilKey = "Alpha2";    // military power: just below the afterburner detent
        public string throttleZeroKey = "Alpha3";   // idle / minimum
        public string throttleTriggerKey = "B";
        public float throttleTriggerRamp = 0.2f;  // seconds for the trigger axis to go from 0 to full while held
        public string throttleMenuKey = "None";
        public string leftSecondKey = "None";      // second button, when the left controller is a stick (EF-24 rear seat)
        public string throttleThumbLeftKey = "None";
        public string throttleThumbRightKey = "None";
        public string throttleThumbUpKey = "None";
        public string throttleThumbDownKey = "None";
        public string throttleThumbPressKey = "None";

        // SOI (works the same in every aircraft, separately from the thumbstick bindings).
        public string soiPrevKey = "None";        // switch SOI left
        public string soiNextKey = "Tab";         // switch SOI right
        public string soiSlewUpKey = "None";
        public string soiSlewDownKey = "None";
        public string soiSlewLeftKey = "None";
        public string soiSlewRightKey = "None";
        public string soiSelectKey = "None";
        public string soiZoomInKey = "None";
        public string soiZoomOutKey = "None";

        // Engine tilt (AV-42C, F-45A...). Up = engines toward vertical (hover), down = toward forward flight.
        public string tiltUpKey = "None";
        public string tiltDownKey = "None";
        public string tiltMaxKey = "None";        // full forward flight
        public string tiltMinKey = "None";        // full hover

        // Engine controls. Engine 1 = left (or the only) engine, engine 2 = right.
        public string engine1OnKey = "None";
        public string engine1OffKey = "None";
        public string engine1ToggleKey = "None";
        public string engine2OnKey = "None";
        public string engine2OffKey = "None";
        public string engine2ToggleKey = "None";
        public string apuOnKey = "None";
        public string apuOffKey = "None";
        public string apuToggleKey = "None";
        public string batteryOnKey = "None";
        public string batteryOffKey = "None";
        public string batteryToggleKey = "None";

        // Aircraft controls.
        public string canopyOpenKey = "None";
        public string canopyCloseKey = "None";
        public string canopyToggleKey = "None";
        public string parkingBrakeOnKey = "None";   // brake lock
        public string parkingBrakeOffKey = "None";
        public string parkingBrakeToggleKey = "H";
        public string wheelBrakeKey = "None";          // wheel brakes, while held
        public string airbrakeHoldKey = "None";     // airbrake (speed brake), while held
        public string airbrakeToggleKey = "None";   // airbrake: press for full, press again to retract
        public string flapsDownKey = "None";      // one step more flaps
        public string flapsUpKey = "None";        // one step less flaps
        public string flapsCycleKey = "F";
        public string gearUpKey = "None";
        public string gearDownKey = "None";
        public string gearToggleKey = "G";
        public string launchBarExtendKey = "None";
        public string launchBarRetractKey = "None";
        public string launchBarToggleKey = "None";
        public string hookExtendKey = "None";
        public string hookRetractKey = "None";
        public string hookToggleKey = "None";
        public string sweepMinKey = "None";       // wing sweep: wings fully forward
        public string sweepMaxKey = "None";       // wing sweep: wings fully back
        public string sweepAutoKey = "None";
        public string sweepIncreaseKey = "None";  // more sweep (hold)
        public string sweepDecreaseKey = "None";  // less sweep (hold)

        // Combat. Master arm on also lifts its switch cover.
        public string countermeasureKey = "X";      // release countermeasures, while held (no helicopter combo needed)
        public string radarOnKey = "None";
        public string radarOffKey = "None";
        public string radarToggleKey = "None";
        public string rwrOnKey = "None";
        public string rwrMuteKey = "None";
        public string rwrOffKey = "None";
        public string rwrCycleKey = "None";       // on -> mute -> off -> on
        public string masterArmOnKey = "None";
        public string masterArmOffKey = "None";
        public string masterArmToggleKey = "None";
        // Air-to-air / air-to-ground arming mode (EF-24).
        public string armingAaKey = "None";
        public string armingAgKey = "None";
        public string armingToggleKey = "None";
        // EW / WPN master mode knob (EF-24).
        public string masterModeEwKey = "None";
        public string masterModeWpnKey = "None";
        public string masterModeToggleKey = "None";
        // TGP zoom: one step in per tap, back to the widest after the narrowest. "BackQuote" = the ~ key.
        public string tgpZoomCycleKey = "BackQuote";
        // Holding the zoom cycle key this long snaps straight to the widest (1x) zoom. 0 = no hold action (steps on press).
        public float tgpZoomResetHoldSeconds = 0.5f;

        // Pilot.
        public string visorDownKey = "None";
        public string visorUpKey = "None";
        public string visorToggleKey = "M";
        public string nvgOnKey = "None";
        public string nvgOffKey = "None";
        public string nvgToggleKey = "N";
        // Camera field of view (FlatScreen 3), while held.
        public string cameraFovIncreaseKey = "PageUp";
        public string cameraFovDecreaseKey = "PageDown";
        public float cameraFovRate = 45f;          // degrees per second while increase / decrease is held
        public string cameraFovSaveKey = "End";    // saves the current FOV as the one Reset returns to
        public string cameraFovResetKey = "Home";
        public float savedFov = FlatScreenCompat.DefaultFov;
        public string magnifierKey = "Z";          // zooms to magnifierFov while held
        public float magnifierFov = 40f;

        // Opens the settings window.
        public string menuKey = "F8";

        // Hold to flip stick control while held: if the virtual joystick is off it's on until released; if it's on you get
        // clickable mode (mouse stops flying and clicks cockpit controls; WASD still flies).
        public string clickModeKey = "LeftAlt";
        // Opacity of the minimal dot-and-line overlay shown in clickable mode (independent of overlayOpacity).
        public float clickModeOpacity = 0.5f;

        // SOI cursor mode (the mouse drives the SOI MFD page): tgpModeKey toggles it, soiHoldKey flips it while held.
        public string tgpModeKey = "T";
        // Unity counts mouse buttons from 0: "Mouse3" is mouse button 4 (side "back" button).
        public string soiHoldKey = "Mouse3";
        // Pod rotation per pixel of mouse movement. At 1.0: 0.1 degrees per pixel at a 60 degree FOV, scaled with
        // zoom, so aim gets finer as you zoom in.
        public float tgpSensitivity = 1f;
        // SOI cursor mode on radar / ARAD / map / TSD: at 1.0, 50 px of mouse moves the page's cursor as far as one second of
        // full thumbstick does. Defaults to a third of that, which pans at a usable speed.
        public float cursorSensitivity = 1f / 3f;

        // Middle mouse snaps the stick back to centre.
        public bool middleMouseRecenters = true;
        // Holding middle mouse for middleHoldSeconds recentres the camera view (FlatScreen 3's reset, else the game's
        // VR recentre), with the stick on or off.
        public bool middleHoldRecentersView = true;
        public float middleHoldSeconds = 1f;
        // Ignore FlatScreen 3's scroll-wheel zoom (camera FOV) entirely; the FOV keys still work. It is always ignored in
        // SOI cursor mode, where the wheel zooms the SOI page.
        public bool disableFlatScreenScrollZoom = true;
        // Re-centre the virtual stick every time the mode is switched on.
        public bool recenterOnEnable = true;

        // Keys that should also switch the mode off (so FlatScreen 3's menus get a free cursor).
        public string[] releaseKeys = { "Escape" };

        // Side length of the square control area, in screen pixels.
        public float overlaySize = 460f;
        public float overlayOpacity = 0.85f;
        // Radius of the mouse dot in px; the WASD ring is drawn 2 px larger.
        public float stickDotSize = 5f;

        // Cockpit screens (MFD on-screen buttons, touchscreen drag) are clicked by this mod instead of FlatScreen 3,
        // using the game's real hitboxes. Off = leave them to FlatScreen 3.
        public bool handleScreens = true;
        // Debug: tooltip next to the cursor naming the hovered screen element, its hitbox source and size.
        public bool showScreenTooltip = false;

        // Live settings: loaded from FilePath at startup, falling back to the field initializers above for anything the
        // file doesn't have (no file yet, or settings added since it was written).
        public static VirtualJoystickSettings Current { get; private set; } = Load();

        public static void ResetToDefaults() => Current = new VirtualJoystickSettings();

        // ---------------------------------------------------------------- persistence

        // Outside the mod's own folder, so a mod update or Workshop re-download doesn't wipe it:
        // %USERPROFILE%\AppData\LocalLow\Boundless Dynamics, LLC\VTOLVR\KBMSlop\settings.json
        public static string FilePath => SettingsPathIn("KBMSlop");

        private static string SettingsPathIn(string folder) =>
            Path.Combine(Path.Combine(Application.persistentDataPath, folder), "settings.json");

        // Folders the settings lived in under the mod's earlier names, newest first.
        private static readonly string[] OldFolders = { "VirtualJoystick", "MouseStick" };

        private static string _savedJson;   // what the file holds, so unchanged settings aren't rewritten

        private static VirtualJoystickSettings Load()
        {
            var settings = new VirtualJoystickSettings();
            string path = FilePath;
            try
            {
                // The mod used to be called Virtual Joystick / Mouse Stick: carry its settings over the first time.
                foreach (string folder in OldFolders)
                {
                    string oldPath = SettingsPathIn(folder);
                    if (File.Exists(path) || !File.Exists(oldPath))
                        continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.Copy(oldPath, path);
                    Log.Info("Settings copied from " + oldPath);
                }
                if (!File.Exists(path))
                {
                    Log.Info("No settings file yet, using defaults (" + path + ")");
                    return settings;
                }
                string json = File.ReadAllText(path);
                JsonUtility.FromJsonOverwrite(json, settings);
                if (settings.releaseKeys == null)
                    settings.releaseKeys = new VirtualJoystickSettings().releaseKeys;
                _savedJson = json;
                Log.Info("Settings loaded from " + path);
            }
            catch (Exception e)
            {
                // Keep the broken file for inspection; the next save replaces it.
                Log.Error("Could not read settings (" + path + "), using defaults: " + e.Message);
                settings = new VirtualJoystickSettings();
                try
                {
                    File.Copy(path, path + ".bad", true);
                }
                catch (Exception) { }
            }
            return settings;
        }

        // Writes the settings if they differ from what's on disk. Cheap enough to call every second or so.
        public static void SaveIfChanged()
        {
            string json;
            try
            {
                json = JsonUtility.ToJson(Current, true);
            }
            catch (Exception e)
            {
                Log.Error("Could not serialize settings: " + e.Message);
                return;
            }
            if (json == _savedJson)
                return;
            string path = FilePath;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                // Temp file first, so a crash mid-write can't leave a truncated settings file.
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(path))
                    File.Delete(path);
                File.Move(tmp, path);
                Log.Info("Settings saved to " + path);
            }
            catch (Exception e)
            {
                Log.Error("Could not save settings (" + path + "): " + e.Message);
            }
            _savedJson = json; // on failure too: don't retry (and log) every second; the next change tries again
        }

        // Every key binding (string fields named *Key / *Key2, and the release keys) back to its default; other
        // settings are kept.
        public static void ResetKeysToDefaults()
        {
            var defaults = new VirtualJoystickSettings();
            foreach (var f in typeof(VirtualJoystickSettings).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if ((f.FieldType == typeof(string) && (f.Name.EndsWith("Key") || f.Name.EndsWith("Key2"))) || f.Name == nameof(releaseKeys))
                    f.SetValue(Current, f.GetValue(defaults));
            }
        }

        public bool HoldMode => string.Equals(toggleMode, "Hold", StringComparison.OrdinalIgnoreCase);

        // Per-frame lookups: cached so a key string isn't re-parsed (or warned about) every frame.
        private static readonly System.Collections.Generic.Dictionary<string, KeyCode> KeyCache = new System.Collections.Generic.Dictionary<string, KeyCode>();

        public static KeyCode ParseKeyQuiet(string name)
        {
            if (string.IsNullOrEmpty(name))
                return KeyCode.None;
            if (!KeyCache.TryGetValue(name, out KeyCode k))
            {
                k = ParseKey(name);
                KeyCache[name] = k;
            }
            return k;
        }

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

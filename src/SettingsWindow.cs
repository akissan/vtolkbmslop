using UnityEngine;

namespace VirtualJoystick
{
    // In-game settings window (default F8). Changes apply immediately and last until the game closes
    // (nothing is saved to disk). While it is open the overlay is shown as a live preview.
    internal static class SettingsWindow
    {
        private const int WindowId = 0x4D53; // "MS"

        // Upper-left corner with a small gap.
        private static Rect _rect = new Rect(12f, 12f, 640f, 10f);

        public static bool IsOpen { get; private set; }

        // True while the cursor is over the window, so FlatScreen 3 doesn't click cockpit controls behind it.
        public static bool CursorOverWindow
        {
            get
            {
                if (!IsOpen)
                    return false;
                Vector2 m = Input.mousePosition;
                return _rect.Contains(new Vector2(m.x, Screen.height - m.y));
            }
        }

        public static void Open() => IsOpen = true;

        public static void Toggle()
        {
            if (IsOpen)
                Close();
            else
                IsOpen = true;
        }

        public static void Close()
        {
            if (!IsOpen)
                return;
            IsOpen = false;
            _rebinding = null;
            VirtualJoystickSettings.SaveIfChanged();
        }

        public static void Draw()
        {
            if (!IsOpen)
                return;
            float height = Mathf.Clamp(Screen.height - 120f, 320f, 900f);
            _rect.width = WindowWidth;
            _rect.height = height;
            _rect.x = Mathf.Clamp(_rect.x, 0f, Mathf.Max(0f, Screen.width - _rect.width));
            _rect.y = Mathf.Clamp(_rect.y, 0f, Mathf.Max(0f, Screen.height - 60f));
            // The window keeps the skin active when it's created, so the themed skin applies to all of its contents.
            GUISkin saved = GUI.skin;
            GUI.skin = Theme.Skin;
            _rect = GUILayout.Window(WindowId, _rect, DrawContents, "KBM SLOP", Theme.Window, GUILayout.Width(WindowWidth), GUILayout.Height(height));
            GUI.skin = saved;
        }

        private const float WindowWidth = 640f;
        private const float TabGap = Theme.Gap;
        private static readonly string[] Tabs = { "Settings", "Virtual Joystick", "Bindings" };
        private static int _tab;
        private static readonly Vector2[] Scroll = new Vector2[3];

        private static void DrawContents(int id)
        {
            var s = VirtualJoystickSettings.Current;
            // Window callbacks run after the rest of OnGUI, so the overlay draws the markers gathered here one
            // frame later: rebuild the list on each Repaint pass.
            if (Event.current.type == EventType.Repaint)
                HighlightPositions.Clear();

            // The tab drawn this event is the one the layout pass used; a newly picked tab shows from the next frame.
            int shown = _tab;
            // The content scroll view always keeps its scrollbar column, so the tab bar and the bottom row leave the
            // same space on the right: everything shares the same left and right edges.
            int tab = _tab;
            GUILayout.BeginHorizontal();
            for (int i = 0; i < Tabs.Length; i++)
            {
                if (i > 0)
                    GUILayout.Space(TabGap);
                if (GUILayout.Toggle(_tab == i, Tabs[i], Theme.Tab, GUILayout.ExpandWidth(true)) && _tab != i)
                    tab = i;
            }
            GUILayout.Space(Theme.ScrollbarSpace);
            GUILayout.EndHorizontal();
            if (tab != _tab)
            {
                _tab = tab;
                _rebinding = null;
            }
            GUILayout.Space(Theme.Gap);

            Scroll[shown] = GUILayout.BeginScrollView(Scroll[shown], false, true);
            if (shown == 0)
                DrawSettingsTab(s);
            else if (shown == 1)
                DrawJoystickTab(s);
            else
                DrawBindingsTab(s);
            GUILayout.EndScrollView();

            GUILayout.Space(Theme.Gap);
            GUILayout.BeginHorizontal();
            if (shown < 2)
            {
                if (GUILayout.Button("Reset settings to defaults", Theme.Flush))
                {
                    VirtualJoystickSettings.ResetToDefaults();
                    _rebinding = null;
                    KeysChanged = true;
                }
                GUILayout.Space(TabGap);
            }
            if (GUILayout.Button("Close", Theme.Flush))
                Close();
            GUILayout.Space(Theme.ScrollbarSpace);
            GUILayout.EndHorizontal();
            GUILayout.Label(_rebinding != null
                ? "Press a key to bind. Esc cancels, Delete or Backspace clears."
                : $"{s.menuKey} toggles this window. Click a binding, then press the new key.", Small());

            GUI.DragWindow();
        }

        // Main settings: behaviour of the mod as a whole.
        private static void DrawSettingsTab(VirtualJoystickSettings s)
        {
            BeginPanel("General");
            s.openWindowOnStart = Toggle("Open this window when the game starts", s.openWindowOnStart);
            s.enableOnSpawn = Toggle("Enable stick control when entering cockpit", s.enableOnSpawn);
            s.middleHoldRecentersView = Toggle("Hold middle mouse to recentre", s.middleHoldRecentersView);
            s.middleHoldSeconds = Slider("Hold time", s.middleHoldSeconds, 0.2f, 2f, "0.0' s'");
            EndPanel();

            BeginPanel("Keyboard");
            s.keyboardRatePitch = Slider("W/S pitch speed", s.keyboardRatePitch, 0.5f, 10f, "0.0'/s'");
            s.keyboardRateRoll = Slider("A/D roll speed", s.keyboardRateRoll, 0.5f, 10f, "0.0'/s'");
            s.keyboardReturnRatePitch = Slider("W/S return to centre", s.keyboardReturnRatePitch, 0.5f, 10f, "0.0'/s'");
            s.keyboardReturnRateRoll = Slider("A/D return to centre", s.keyboardReturnRateRoll, 0.5f, 10f, "0.0'/s'");
            s.rudderRate = Slider("Rudder (Q/E) speed", s.rudderRate, 0.5f, 10f, "0.0'/s'");
            EndPanel();

            BeginPanel("SOI cursor");
            s.tgpSensitivity = Slider("TGP sensitivity", s.tgpSensitivity, 0.1f, 2f, "0.00'x'");
            s.cursorSensitivity = Slider("Radar/map/ARAD cursor", s.cursorSensitivity, 0.1f, 2f, "0.00'x'");
            EndPanel();

            BeginPanel("Overlay");
            s.clickModeOpacity = Slider("Clickable mode overlay opacity", s.clickModeOpacity, 0f, 1f, "0%");
            EndPanel();

            BeginPanel("Cockpit screens");
            s.handleScreens = Toggle("Handle screen buttons & touch drag (not FlatScreen 3)", s.handleScreens);
            s.showScreenTooltip = Toggle("Debug tooltip: element name, hitbox source, size", s.showScreenTooltip);
            EndPanel();
        }

        // The mouse-driven virtual joystick: its control area, response and look.
        private static void DrawJoystickTab(VirtualJoystickSettings s)
        {
            BeginPanel("Control area");
            s.overlaySize = Slider("Size", s.overlaySize, 100f, 900f, "0' px'");
            s.deadzone = Slider("Deadzone", s.deadzone, 0f, 0.3f, "0%");
            s.sensitivity = Slider("Mouse sensitivity", s.sensitivity, 0.25f, 4f, "0.00'x'");
            s.autoCenterRate = Slider("Auto-centre", s.autoCenterRate, 0f, 5f, "0.0'/s'");
            s.circularLimit = Toggle("Round travel limit", s.circularLimit);
            s.invertPitch = Toggle("Invert pitch (mouse up = nose up)", s.invertPitch);
            EndPanel();

            BeginPanel("Response curve");
            s.curve = Slider("Centre curve (inverse cubic)", s.curve, 0f, 1f, "0%");
            DrawResponseGraph();
            EndPanel();

            BeginPanel("Look");
            s.overlayOpacity = Slider("Opacity", s.overlayOpacity, 0.1f, 1f, "0%");
            s.stickDotSize = Slider("Dot size", s.stickDotSize, 2f, 12f, "0.0' px'");
            EndPanel();
        }

        // A grey semi-transparent panel with hard corners; optional accent-coloured title.
        private static void BeginPanel(string title)
        {
            GUILayout.BeginVertical(Theme.Panel);
            if (title != null)
                GUILayout.Label(title.ToUpperInvariant(), Theme.SectionTitle);
        }

        private static void EndPanel() => GUILayout.EndVertical();

        // ------------------------------------------------------------------ key binding rows

        private class Bind
        {
            public string Label;
            public System.Func<VirtualJoystickSettings, string> Get;
            public System.Action<VirtualJoystickSettings, string> Set;
            public System.Func<string> Hint; // what this key does in the current aircraft (small text), or null
        }

        private static Bind _rebinding;
        private static KeyCode[] _keyCandidates;

        // True while waiting for a key press; the mod ignores its own shortcuts meanwhile.
        public static bool IsCapturingKey => IsOpen && _rebinding != null;

        // Set when a binding changes; the behaviour re-reads its keys and clears it.
        public static bool KeysChanged;

        // One "label | key" row; the key button starts capture. A key that's also bound elsewhere shows in yellow, with
        // a wrapped "Also used by: ..." line under the row naming every clash.
        private static void KeyRow(Bind b, float labelWidth = 300f)
        {
            string current = b.Get(VirtualJoystickSettings.Current);
            var clashes = Clashes(b, current);
            string hint = null;
            if (b.Hint != null)
            {
                try { hint = b.Hint(); } catch (System.Exception) { hint = null; }
            }
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(labelWidth));
            GUILayout.Label(b.Label, Theme.LabelWrap);
            if (hint != null)
                GUILayout.Label(hint, HintStyle()); // what this input does in the aircraft you're sitting in
            GUILayout.EndVertical();
            bool capturing = _rebinding == b;
            string text = capturing ? "Press a key…" : KeyDisplayName(current);
            Color saved = GUI.contentColor;
            if (capturing)
                GUI.contentColor = Theme.Menu.Accent;
            else if (clashes.Count > 0)
                GUI.contentColor = Theme.Menu.Warning;
            if (GUILayout.Button(text, Theme.KeyButton, GUILayout.ExpandWidth(true)))
                _rebinding = capturing ? null : b;
            GUI.contentColor = saved;
            GUILayout.EndHorizontal();
            if (clashes.Count > 0 && !capturing)
                GUILayout.Label("Also used by: " + string.Join(", ", clashes), Theme.WarningText);
        }

        // Every other binding in the Bindings tab using this key, as "SECTION › Card › Action".
        private static System.Collections.Generic.List<string> Clashes(Bind self, string key)
        {
            var result = new System.Collections.Generic.List<string>();
            if (string.IsNullOrEmpty(key) || key == "None")
                return result;
            var s = VirtualJoystickSettings.Current;
            foreach (var section in Sections)
                foreach (var card in section.Cards)
                    foreach (var b in card.Binds)
                        if (b != self && b.Get(s) == key)
                            result.Add($"{section.Title} › {card.Title} › {b.Label}");
            return result;
        }

        private static bool CardHasClash(BindCard card)
        {
            var s = VirtualJoystickSettings.Current;
            foreach (var b in card.Binds)
                if (Clashes(b, b.Get(s)).Count > 0)
                    return true;
            return false;
        }

        // Unity numbers mouse buttons from 0, so show them the way people count them (Mouse3 = "Mouse button 4").
        private static string KeyDisplayName(string key)
        {
            if (string.IsNullOrEmpty(key) || key == "None")
                return "—";
            if (key.StartsWith("Mouse") && int.TryParse(key.Substring(5), out int n))
                return "Mouse button " + (n + 1);
            return key;
        }

        // Called from the behaviour's Update, before it handles any of its own keys.
        // Returns true if this frame's key press was used for rebinding.
        public static bool UpdateKeyCapture()
        {
            if (!IsCapturingKey)
                return false;
            if (_keyCandidates == null)
            {
                var all = (KeyCode[])System.Enum.GetValues(typeof(KeyCode));
                var list = new System.Collections.Generic.List<KeyCode>();
                foreach (var k in all)
                    // Keyboard, plus extra mouse buttons (4-7). Left/right/middle are the mod's own fire / free look / recentre.
                    if ((k > KeyCode.None && k < KeyCode.Mouse0) || (k >= KeyCode.Mouse3 && k <= KeyCode.Mouse6))
                        list.Add(k);
                _keyCandidates = list.ToArray();
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                _rebinding = null;
                return true;
            }
            if (Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.Backspace))
            {
                Apply("None");
                return true;
            }
            foreach (var k in _keyCandidates)
            {
                if (Input.GetKeyDown(k))
                {
                    Apply(k.ToString());
                    return true;
                }
            }
            return true; // still waiting: swallow input so shortcuts don't fire
        }

        private static void Apply(string key)
        {
            _rebinding.Set(VirtualJoystickSettings.Current, key);
            _rebinding = null;
            KeysChanged = true;
        }

        // ------------------------------------------------------------------ bindings tab

        // A card: a group of keys under one control. Locate (optional) gives the cockpit control's position for the
        // orange marker; Available (optional) says whether the current aircraft has it.
        private class BindCard
        {
            public string Title;
            public Bind[] Binds;
            public System.Func<Vector3?> Locate;
            public System.Func<bool> Available;
            public bool ThrottleRate;
        }

        private class BindSection
        {
            public string Title;
            public System.Func<string> Device; // which controller this hand is in the current seat
            public BindCard[] Cards;
        }

        private static Bind B(string label, System.Func<VirtualJoystickSettings, string> get, System.Action<VirtualJoystickSettings, string> set,
            System.Func<string> hint = null) =>
            new Bind { Label = label, Get = get, Set = set, Hint = hint };

        private static System.Func<Vector3?> At(Cockpit.KeyActions.NamedControl c) => () => c.Found ? c.Position : (Vector3?)null;

        // On / Off / Toggle card for a two-position cockpit switch.
        private static BindCard OnOff(string title, Cockpit.KeyActions.NamedControl c,
            System.Func<VirtualJoystickSettings, string> getOn, System.Action<VirtualJoystickSettings, string> setOn,
            System.Func<VirtualJoystickSettings, string> getOff, System.Action<VirtualJoystickSettings, string> setOff,
            System.Func<VirtualJoystickSettings, string> getToggle, System.Action<VirtualJoystickSettings, string> setToggle) =>
            new BindCard
            {
                Title = title,
                Locate = At(c),
                Available = () => c.Found,
                Binds = new[]
            {
                B("On", getOn, setOn),
                B("Off", getOff, setOff),
                B("Toggle", getToggle, setToggle),
            }
            };


        private static readonly BindSection[] Sections =
        {
            new BindSection { Title = "COMMON", Cards = new[]
            {
                new BindCard { Title = "Stick control", Binds = new[]
                {
                    B("Toggle", s => s.toggleKey, (s, v) => s.toggleKey = v),
                    B("Toggle (second key)", s => s.toggleKey2, (s, v) => s.toggleKey2 = v),
                    B("While held", s => s.clickModeKey, (s, v) => s.clickModeKey = v),
                } },
                new BindCard { Title = "SOI cursor mode", Binds = new[]
                {
                    B("Toggle", s => s.tgpModeKey, (s, v) => s.tgpModeKey = v),
                    B("While held", s => s.soiHoldKey, (s, v) => s.soiHoldKey = v),
                } },
                new BindCard { Title = "Settings window", Binds = new[]
                {
                    B("Open / close", s => s.menuKey, (s, v) => s.menuKey = v),
                } },
            } },
            new BindSection { Title = "RIGHT HAND", Device = () => Cockpit.KeyActions.Right.DeviceName, Cards = new[]
            {
                new BindCard { Title = "Stick movement", Binds = new[]
                {
                    B("Pitch down (stick forward)", s => s.pitchDownKey, (s, v) => s.pitchDownKey = v),
                    B("Pitch up (stick back)", s => s.pitchUpKey, (s, v) => s.pitchUpKey = v),
                    B("Roll left", s => s.rollLeftKey, (s, v) => s.rollLeftKey = v),
                    B("Roll right", s => s.rollRightKey, (s, v) => s.rollRightKey = v),
                    B("Rudder left", s => s.rudderLeftKey, (s, v) => s.rudderLeftKey = v),
                    B("Rudder right", s => s.rudderRightKey, (s, v) => s.rudderRightKey = v),
                } },
                new BindCard { Title = "Trigger", Binds = new[] { B("Trigger", s => s.triggerKey, (s, v) => s.triggerKey = v, Cockpit.KeyActions.HintRightTrigger) } },
                new BindCard { Title = "A button", Binds = new[] { B("A / menu button", s => s.weaponCycleKey, (s, v) => s.weaponCycleKey = v, Cockpit.KeyActions.HintRightMenu) } },
                new BindCard { Title = "B button", Binds = new[] { B("B / second button", s => s.stickBKey, (s, v) => s.stickBKey = v, Cockpit.KeyActions.HintRightSecond) } },
                new BindCard { Title = "Thumbstick", Binds = new[]
                {
                    B("Left", s => s.thumbLeftKey, (s, v) => s.thumbLeftKey = v, () => Dir("left", Cockpit.KeyActions.HintRightThumb())),
                    B("Right", s => s.thumbRightKey, (s, v) => s.thumbRightKey = v, () => Dir("right", Cockpit.KeyActions.HintRightThumb())),
                    B("Up", s => s.thumbUpKey, (s, v) => s.thumbUpKey = v, () => Dir("up", Cockpit.KeyActions.HintRightThumb())),
                    B("Down", s => s.thumbDownKey, (s, v) => s.thumbDownKey = v, () => Dir("down", Cockpit.KeyActions.HintRightThumb())),
                    B("Press", s => s.thumbPressKey, (s, v) => s.thumbPressKey = v, Cockpit.KeyActions.HintRightPress),
                } },
            } },
            new BindSection { Title = "LEFT HAND", Device = () => Cockpit.KeyActions.Left.DeviceName, Cards = new[]
            {
                new BindCard { Title = "Throttle / collective movement", ThrottleRate = true, Binds = new[]
                {
                    B("Throttle up", s => s.throttleUpKey, (s, v) => s.throttleUpKey = v, Cockpit.KeyActions.HintThrottleMove),
                    B("Throttle down", s => s.throttleDownKey, (s, v) => s.throttleDownKey = v, Cockpit.KeyActions.HintThrottleMove),
                    B("Full throttle", s => s.throttleFullKey, (s, v) => s.throttleFullKey = v, Cockpit.KeyActions.HintThrottleMove),
                    B("MIL power (below afterburner)", s => s.throttleMilKey, (s, v) => s.throttleMilKey = v, Cockpit.KeyActions.HintThrottleMove),
                    B("Zero throttle", s => s.throttleZeroKey, (s, v) => s.throttleZeroKey = v, Cockpit.KeyActions.HintThrottleMove),
                } },
                new BindCard { Title = "Trigger / modifier", Binds = new[] { B("Trigger", s => s.throttleTriggerKey, (s, v) => s.throttleTriggerKey = v, Cockpit.KeyActions.HintLeftTrigger) } },
                new BindCard { Title = "Menu button", Binds = new[] { B("Menu button", s => s.throttleMenuKey, (s, v) => s.throttleMenuKey = v, Cockpit.KeyActions.HintLeftMenu) } },
                new BindCard { Title = "Second button", Binds = new[] { B("Second button", s => s.leftSecondKey, (s, v) => s.leftSecondKey = v, Cockpit.KeyActions.HintLeftSecond) } },
                new BindCard { Title = "Thumbstick", Binds = new[]
                {
                    B("Left", s => s.throttleThumbLeftKey, (s, v) => s.throttleThumbLeftKey = v, () => Dir("left", Cockpit.KeyActions.HintLeftThumb())),
                    B("Right", s => s.throttleThumbRightKey, (s, v) => s.throttleThumbRightKey = v, () => Dir("right", Cockpit.KeyActions.HintLeftThumb())),
                    B("Up", s => s.throttleThumbUpKey, (s, v) => s.throttleThumbUpKey = v, () => Dir("up", Cockpit.KeyActions.HintLeftThumb())),
                    B("Down", s => s.throttleThumbDownKey, (s, v) => s.throttleThumbDownKey = v, () => Dir("down", Cockpit.KeyActions.HintLeftThumb())),
                    B("Press", s => s.throttleThumbPressKey, (s, v) => s.throttleThumbPressKey = v, Cockpit.KeyActions.HintLeftPress),
                } },
            } },
            new BindSection { Title = "SOI", Cards = new[]
            {
                new BindCard { Title = "Switch SOI", Available = () => Cockpit.SoiKeys.CanSwitch, Binds = new[]
                {
                    B("SOI left (previous)", s => s.soiPrevKey, (s, v) => s.soiPrevKey = v),
                    B("SOI right (next)", s => s.soiNextKey, (s, v) => s.soiNextKey = v),
                } },
                new BindCard { Title = "SOI cursor", Available = () => Cockpit.SoiKeys.HasMfds, Binds = new[]
                {
                    B("Slew up", s => s.soiSlewUpKey, (s, v) => s.soiSlewUpKey = v),
                    B("Slew down", s => s.soiSlewDownKey, (s, v) => s.soiSlewDownKey = v),
                    B("Slew left", s => s.soiSlewLeftKey, (s, v) => s.soiSlewLeftKey = v),
                    B("Slew right", s => s.soiSlewRightKey, (s, v) => s.soiSlewRightKey = v),
                    B("Select / lock", s => s.soiSelectKey, (s, v) => s.soiSelectKey = v),
                } },
                new BindCard { Title = "SOI zoom", Available = () => Cockpit.SoiKeys.HasMfds, Binds = new[]
                {
                    B("Zoom in", s => s.soiZoomInKey, (s, v) => s.soiZoomInKey = v),
                    B("Zoom out", s => s.soiZoomOutKey, (s, v) => s.soiZoomOutKey = v),
                } },
            } },
            new BindSection { Title = "TILT", Device = Cockpit.TiltKeys.Describe, Cards = new[]
            {
                new BindCard { Title = "Engine tilt", Available = () => Cockpit.TiltKeys.Tilt != null, Binds = new[]
                {
                    B("Tilt up: toward hover", s => s.tiltUpKey, (s, v) => s.tiltUpKey = v),
                    B("Tilt down: toward forward flight", s => s.tiltDownKey, (s, v) => s.tiltDownKey = v),
                    B("Tilt max: full forward flight", s => s.tiltMaxKey, (s, v) => s.tiltMaxKey = v),
                    B("Tilt min: full hover", s => s.tiltMinKey, (s, v) => s.tiltMinKey = v),
                } },
            } },
            new BindSection { Title = "ENGINE", Cards = new[]
            {
                OnOff("Engine 1 (left)", Cockpit.KeyActions.Engine1,
                    s => s.engine1OnKey, (s, v) => s.engine1OnKey = v, s => s.engine1OffKey, (s, v) => s.engine1OffKey = v,
                    s => s.engine1ToggleKey, (s, v) => s.engine1ToggleKey = v),
                OnOff("Engine 2 (right)", Cockpit.KeyActions.Engine2,
                    s => s.engine2OnKey, (s, v) => s.engine2OnKey = v, s => s.engine2OffKey, (s, v) => s.engine2OffKey = v,
                    s => s.engine2ToggleKey, (s, v) => s.engine2ToggleKey = v),
                OnOff("APU", Cockpit.KeyActions.Apu,
                    s => s.apuOnKey, (s, v) => s.apuOnKey = v, s => s.apuOffKey, (s, v) => s.apuOffKey = v,
                    s => s.apuToggleKey, (s, v) => s.apuToggleKey = v),
                OnOff("Main battery", Cockpit.KeyActions.Battery,
                    s => s.batteryOnKey, (s, v) => s.batteryOnKey = v, s => s.batteryOffKey, (s, v) => s.batteryOffKey = v,
                    s => s.batteryToggleKey, (s, v) => s.batteryToggleKey = v),
            } },
            new BindSection { Title = "AIRCRAFT", Cards = new[]
            {
                new BindCard { Title = "Canopy", Locate = At(Cockpit.KeyActions.Canopy), Available = () => Cockpit.KeyActions.Canopy.Found, Binds = new[]
                {
                    B("Open", s => s.canopyOpenKey, (s, v) => s.canopyOpenKey = v),
                    B("Close", s => s.canopyCloseKey, (s, v) => s.canopyCloseKey = v),
                    B("Toggle", s => s.canopyToggleKey, (s, v) => s.canopyToggleKey = v),
                } },
                OnOff("Parking brake (brake lock)", Cockpit.KeyActions.ParkingBrake,
                    s => s.parkingBrakeOnKey, (s, v) => s.parkingBrakeOnKey = v, s => s.parkingBrakeOffKey, (s, v) => s.parkingBrakeOffKey = v,
                    s => s.parkingBrakeToggleKey, (s, v) => s.parkingBrakeToggleKey = v),
                new BindCard { Title = "Wheel brakes", Available = () => Cockpit.KeyActions.HasWheels, Binds = new[]
                {
                    B("Brake", s => s.wheelBrakeKey, (s, v) => s.wheelBrakeKey = v),
                } },
                new BindCard { Title = "Flaps", Locate = At(Cockpit.KeyActions.Flaps), Available = () => Cockpit.KeyActions.Flaps.Found, Binds = new[]
                {
                    B("Flaps down (one step more)", s => s.flapsDownKey, (s, v) => s.flapsDownKey = v),
                    B("Flaps up (one step less)", s => s.flapsUpKey, (s, v) => s.flapsUpKey = v),
                    B("Cycle", s => s.flapsCycleKey, (s, v) => s.flapsCycleKey = v),
                } },
                new BindCard { Title = "Landing gear", Locate = At(Cockpit.KeyActions.Gear), Available = () => Cockpit.KeyActions.Gear.Found, Binds = new[]
                {
                    B("Gear up", s => s.gearUpKey, (s, v) => s.gearUpKey = v),
                    B("Gear down", s => s.gearDownKey, (s, v) => s.gearDownKey = v),
                    B("Toggle", s => s.gearToggleKey, (s, v) => s.gearToggleKey = v),
                } },
                new BindCard { Title = "Wing sweep", Available = () => Cockpit.KeyActions.Sweep != null,
                    Locate = () => Cockpit.KeyActions.Sweep != null && Cockpit.KeyActions.Sweep.leverTransform != null
                        ? Cockpit.KeyActions.Sweep.leverTransform.position : (Vector3?)null,
                    Binds = new[]
                {
                    B("Min sweep (wings forward)", s => s.sweepMinKey, (s, v) => s.sweepMinKey = v),
                    B("Max sweep (wings back)", s => s.sweepMaxKey, (s, v) => s.sweepMaxKey = v),
                    B("Auto", s => s.sweepAutoKey, (s, v) => s.sweepAutoKey = v),
                    B("Increase sweep", s => s.sweepIncreaseKey, (s, v) => s.sweepIncreaseKey = v),
                    B("Decrease sweep", s => s.sweepDecreaseKey, (s, v) => s.sweepDecreaseKey = v),
                } },
                new BindCard { Title = "Launch bar", Locate = At(Cockpit.KeyActions.LaunchBar), Available = () => Cockpit.KeyActions.LaunchBar.Found, Binds = new[]
                {
                    B("Extend", s => s.launchBarExtendKey, (s, v) => s.launchBarExtendKey = v),
                    B("Retract", s => s.launchBarRetractKey, (s, v) => s.launchBarRetractKey = v),
                    B("Toggle", s => s.launchBarToggleKey, (s, v) => s.launchBarToggleKey = v),
                } },
                new BindCard { Title = "Arrestor hook", Locate = At(Cockpit.KeyActions.Hook), Available = () => Cockpit.KeyActions.Hook.Found, Binds = new[]
                {
                    B("Extend", s => s.hookExtendKey, (s, v) => s.hookExtendKey = v),
                    B("Retract", s => s.hookRetractKey, (s, v) => s.hookRetractKey = v),
                    B("Toggle", s => s.hookToggleKey, (s, v) => s.hookToggleKey = v),
                } },
            } },
            new BindSection { Title = "COMBAT", Cards = new[]
            {
                new BindCard { Title = "Countermeasures", Available = () => Cockpit.KeyActions.HasCountermeasures, Binds = new[]
                {
                    B("Release", s => s.countermeasureKey, (s, v) => s.countermeasureKey = v),
                } },
                OnOff("Radar power", Cockpit.KeyActions.Radar,
                    s => s.radarOnKey, (s, v) => s.radarOnKey = v, s => s.radarOffKey, (s, v) => s.radarOffKey = v,
                    s => s.radarToggleKey, (s, v) => s.radarToggleKey = v),
                new BindCard { Title = "RWR", Available = () => Cockpit.KeyActions.HasRwr,
                    Locate = () => Cockpit.KeyActions.RwrSwitch.Found ? Cockpit.KeyActions.RwrSwitch.Position : (Vector3?)null,
                    Binds = new[]
                {
                    B("On", s => s.rwrOnKey, (s, v) => s.rwrOnKey = v),
                    B("Mute", s => s.rwrMuteKey, (s, v) => s.rwrMuteKey = v),
                    B("Off", s => s.rwrOffKey, (s, v) => s.rwrOffKey = v),
                    B("Cycle (on, mute, off)", s => s.rwrCycleKey, (s, v) => s.rwrCycleKey = v),
                } },
                new BindCard { Title = "Master arm", Locate = At(Cockpit.KeyActions.MasterArm), Available = () => Cockpit.KeyActions.MasterArm.Found, Binds = new[]
                {
                    B("On (lifts the cover)", s => s.masterArmOnKey, (s, v) => s.masterArmOnKey = v),
                    B("Off", s => s.masterArmOffKey, (s, v) => s.masterArmOffKey = v),
                    B("Toggle", s => s.masterArmToggleKey, (s, v) => s.masterArmToggleKey = v),
                } },
                new BindCard { Title = "Arming mode (AA / AG)", Available = () => Cockpit.KeyActions.Arming != null, Binds = new[]
                {
                    B("Air-to-air (AA)", s => s.armingAaKey, (s, v) => s.armingAaKey = v),
                    B("Air-to-ground (AG)", s => s.armingAgKey, (s, v) => s.armingAgKey = v),
                    B("Toggle", s => s.armingToggleKey, (s, v) => s.armingToggleKey = v),
                } },
                new BindCard { Title = "TGP zoom", Available = () => Cockpit.SoiKeys.HasTgp, Binds = new[]
                {
                    B("Cycle zoom (hold: back to 1x)", s => s.tgpZoomCycleKey, (s, v) => s.tgpZoomCycleKey = v),
                } },
            } },
            new BindSection { Title = "PILOT", Cards = new[]
            {
                new BindCard { Title = "Helmet visor", Available = () => Cockpit.KeyActions.Helmet != null, Binds = new[]
                {
                    B("Visor down", s => s.visorDownKey, (s, v) => s.visorDownKey = v),
                    B("Visor up", s => s.visorUpKey, (s, v) => s.visorUpKey = v),
                    B("Toggle", s => s.visorToggleKey, (s, v) => s.visorToggleKey = v),
                } },
                new BindCard { Title = "Night vision", Available = () => Cockpit.KeyActions.Helmet != null, Binds = new[]
                {
                    B("NVG on", s => s.nvgOnKey, (s, v) => s.nvgOnKey = v),
                    B("NVG off", s => s.nvgOffKey, (s, v) => s.nvgOffKey = v),
                    B("Toggle", s => s.nvgToggleKey, (s, v) => s.nvgToggleKey = v),
                } },
            } },
        };

        // Thumbstick rows share one event; say which way this key pushes it.
        private static string Dir(string dir, string hint) => hint == null ? null : hint.StartsWith("(") ? hint : $"push {dir}: {hint}";

        private static readonly System.Collections.Generic.HashSet<string> OpenCards = new System.Collections.Generic.HashSet<string>();

        // World positions of the aircraft controls whose cards are open: the overlay marks them in the cockpit.
        public static readonly System.Collections.Generic.List<Vector3> HighlightPositions = new System.Collections.Generic.List<Vector3>();

        private static void DrawBindingsTab(VirtualJoystickSettings s)
        {
            string vehicle = Cockpit.KeyActions.VehicleName;
            BeginPanel(null);
            GUILayout.BeginHorizontal();
            GUILayout.Label(vehicle != null
                ? $"In {vehicle}. Keys apply to every aircraft; the grey lines say what they do here."
                : "Keys apply to every aircraft. Sit in a cockpit to see what each one does there.", Small());
            if (vehicle != null && GUILayout.Button("Rescan", GUILayout.Width(84f)))
                Cockpit.KeyActions.Rescan();
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Reset key bindings to defaults"))
            {
                VirtualJoystickSettings.ResetKeysToDefaults();
                _rebinding = null;
                KeysChanged = true;
            }
            EndPanel();

            foreach (var section in Sections)
            {
                string device = section.Device?.Invoke();
                GUILayout.BeginVertical(Theme.Panel);
                GUILayout.Label(section.Title + (device != null ? $"   ({device})" : ""), CategoryHeader());
                foreach (var card in section.Cards)
                    DrawCard(s, section.Title + "/" + card.Title, card);
                GUILayout.EndVertical();
            }
        }

        // An expanding card: header (title, number of keys bound, availability); key rows when open.
        private static void DrawCard(VirtualJoystickSettings s, string id, BindCard card)
        {
            bool open = OpenCards.Contains(id);
            int bound = CountBound(card.Binds);
            bool missing = Cockpit.KeyActions.VehicleName != null && card.Available != null && !card.Available();
            bool clash = CardHasClash(card);
            string header = card.Title + (bound > 0 ? $"   ({bound} bound)" : "") + (missing ? "   (not in this aircraft)" : "");
            Color savedColor = GUI.contentColor;
            if (clash)
                GUI.contentColor = Theme.Menu.Warning; // one of its keys is also bound elsewhere
            bool clicked = GUILayout.Button(header, open ? Theme.CardHeaderOpen : CardHeader());
            GUI.contentColor = savedColor;
            if (clicked)
            {
                // Takes effect next frame: IMGUI needs this event to keep the layout it was laid out with.
                if (open) OpenCards.Remove(id); else OpenCards.Add(id);
                _rebinding = null;
            }
            if (!open)
                return;
            if (card.Locate != null && Event.current.type == EventType.Repaint)
            {
                Vector3? at = card.Locate();
                if (at.HasValue)
                    HighlightPositions.Add(at.Value);
            }
            // Body continues straight under the header, same shade, so the open card reads as one block.
            GUILayout.BeginVertical(Theme.CardBody);
            foreach (var b in card.Binds)
                KeyRow(b);
            if (card.ThrottleRate)
                s.throttleRate = Slider("Throttle speed", s.throttleRate, 0.1f, 3f, "0.00'/s'");
            GUILayout.EndVertical();
        }

        private static int CountBound(Bind[] binds)
        {
            int n = 0;
            foreach (var b in binds)
            {
                string k = b.Get(VirtualJoystickSettings.Current);
                if (!string.IsNullOrEmpty(k) && k != "None") n++;
            }
            return n;
        }

        private static GUIStyle HintStyle() => Theme.Hint;

        private static GUIStyle CardHeader() => Theme.CardHeader;

        private static GUIStyle CategoryHeader() => Theme.SectionTitle;

        // Stick deflection along one axis (x) vs output sent to the aircraft (y): deadzone + centre curve, live.
        private static void DrawResponseGraph()
        {
            const float graphW = 220f, graphH = 140f;
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            Rect g = GUILayoutUtility.GetRect(graphW, graphH, GUILayout.Width(graphW), GUILayout.Height(graphH));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Label("Response: stick deflection → output", Small());

            if (Event.current.type != EventType.Repaint)
                return;

            var s = VirtualJoystickSettings.Current;
            Fill(g, Theme.Menu.GraphBg);

            // Quarter grid.
            Color grid = Theme.Menu.GraphGrid;
            for (int i = 1; i < 4; i++)
            {
                Fill(new Rect(g.x + g.width * i / 4f, g.y, 1f, g.height), grid);
                Fill(new Rect(g.x, g.y + g.height * i / 4f, g.width, 1f), grid);
            }

            // Deadzone band.
            float dzW = s.deadzone * g.width;
            if (dzW >= 1f)
                Fill(new Rect(g.x, g.y, dzW, g.height), Theme.Menu.GraphDeadzone);

            // Linear reference diagonal, then the actual response, drawn column by column.
            Color linear = Theme.Menu.GraphLinear;
            Color curve = Theme.Menu.GraphCurve;
            float prevLin = g.yMax, prevOut = g.yMax;
            int cols = Mathf.RoundToInt(g.width);
            for (int i = 0; i <= cols; i++)
            {
                float a = i / (float)cols;
                float x = g.x + i;
                float yLin = g.yMax - a * g.height;
                float yOut = g.yMax - VirtualJoystickBehaviour.AxisResponse(a) * g.height;
                VSpan(x, prevLin, yLin, 1f, linear);
                VSpan(x, prevOut, yOut, 2f, curve);
                prevLin = yLin;
                prevOut = yOut;
            }

            Outline(g, Theme.Menu.GraphOutline);
        }

        // Vertical bar joining two consecutive samples, so steep parts of the curve stay continuous.
        private static void VSpan(float x, float y0, float y1, float thickness, Color color)
        {
            float top = Mathf.Min(y0, y1) - thickness * 0.5f;
            float h = Mathf.Abs(y1 - y0) + thickness;
            Fill(new Rect(x, top, thickness, h), color);
        }

        private static Texture2D _white;

        private static void Fill(Rect r, Color color)
        {
            if (_white == null)
            {
                _white = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                _white.SetPixel(0, 0, Color.white);
                _white.Apply();
            }
            Color saved = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r, _white);
            GUI.color = saved;
        }

        private static void Outline(Rect r, Color color)
        {
            Fill(new Rect(r.x, r.y, r.width, 1f), color);
            Fill(new Rect(r.x, r.yMax - 1f, r.width, 1f), color);
            Fill(new Rect(r.x, r.y, 1f, r.height), color);
            Fill(new Rect(r.xMax - 1f, r.y, 1f, r.height), color);
        }

        private static float Slider(string label, float value, float min, float max, string format)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, Theme.Label, GUILayout.Width(260f));
            float v = GUILayout.HorizontalSlider(value, min, max, Theme.Slider, Theme.SliderThumb, GUILayout.Width(230f));
            GUILayout.Label(v.ToString(format), Theme.Label, GUILayout.Width(70f));
            GUILayout.EndHorizontal();
            return v;
        }

        // Checkbox: grey border on dark when off, bright blue when on. Drawn by hand so it looks exactly like that.
        private static bool Toggle(string label, bool value)
        {
            // The box is as tall as the text's capitals, centred on the text line.
            float box = Theme.CheckSize;
            Rect row = GUILayoutUtility.GetRect(new GUIContent(label), Theme.Label, GUILayout.ExpandWidth(true), GUILayout.MinHeight(box + 8f));
            bool hover = row.Contains(Event.current.mousePosition);
            var boxRect = new Rect(row.x + 2f, Mathf.Round(row.y + (row.height - box) * 0.5f), box, box);
            if (Event.current.type == EventType.Repaint)
            {
                Texture2D tex = value ? (hover ? Theme.CheckOnHover : Theme.CheckOn) : (hover ? Theme.CheckOffHover : Theme.CheckOff);
                GUI.DrawTexture(boxRect, tex);
                Theme.Label.Draw(new Rect(boxRect.xMax + 6f, row.y, row.width - box - 8f, row.height), label, hover, false, false, false);
            }
            bool v = value;
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && hover)
            {
                v = !value;
                Event.current.Use();
            }
            return v;
        }

        private static GUIStyle Bold() => Theme.Bold;

        private static GUIStyle Small() => Theme.Small;
    }
}

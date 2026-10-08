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
            string key = VirtualJoystickSettings.Current.menuKey;
            string title = string.IsNullOrEmpty(key) || key == "None" ? "KBM SLOP" : $"KBM SLOP [{KeyDisplayName(key)}]";
            _rect = GUILayout.Window(WindowId, _rect, DrawContents, title, Theme.Window, GUILayout.Width(WindowWidth), GUILayout.Height(height));
            GUI.skin = saved;
        }

        private const float WindowWidth = 640f;
        private const float TabGap = Theme.Gap;
        private static readonly string[] Tabs = { "GENERAL", "VIRTUAL JOYSTICK", "BINDINGS" };
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
            // Tabs touch, their 1 px borders overlapping; the hovered one's white frame is drawn again on top so its
            // neighbour's border can't cover it.
            int tab = _tab;
            Rect hoveredTab = Rect.zero;
            GUILayout.BeginHorizontal();
            for (int i = 0; i < Tabs.Length; i++)
            {
                if (i > 0)
                    GUILayout.Space(-1f);
                if (GUILayout.Toggle(_tab == i, Tabs[i], Theme.Tab, GUILayout.ExpandWidth(true)) && _tab != i)
                    tab = i;
                Rect r = GUILayoutUtility.GetLastRect();
                if (r.Contains(Event.current.mousePosition))
                    hoveredTab = r;
            }
            GUILayout.Space(Theme.ScrollbarSpace);
            GUILayout.EndHorizontal();
            if (hoveredTab.width > 0f && Event.current.type == EventType.Repaint)
                Outline(hoveredTab, Theme.Menu.Hover);
            if (tab != _tab)
            {
                _tab = tab;
                _rebinding = null;
            }
            GUILayout.Space(Theme.Gap * 2);

            Scroll[shown] = GUILayout.BeginScrollView(Scroll[shown], false, true);
            if (shown == 0)
                DrawSettingsTab(s);
            else if (shown == 1)
                DrawJoystickTab(s);
            else
                DrawBindingsTab(s);
            GUILayout.EndScrollView();

            GUILayout.Space(Theme.Gap * 2);
            if (_rebinding != null)
                GUILayout.Label("Press a key to bind. Esc cancels, Delete or Backspace clears.", Theme.Footer);
            GUILayout.BeginHorizontal();
            // The reset button resets what the tab shows: the settings, or (Bindings tab) only the keys.
            if (shown < 2)
            {
                if (GUILayout.Button("RESET SETTINGS TO DEFAULTS", Theme.Flush))
                {
                    VirtualJoystickSettings.ResetToDefaults();
                    _rebinding = null;
                    KeysChanged = true;
                }
            }
            else if (GUILayout.Button("RESET KEY BINDINGS TO DEFAULTS", Theme.Flush))
            {
                VirtualJoystickSettings.ResetKeysToDefaults();
                _rebinding = null;
                KeysChanged = true;
            }
            GUILayout.Space(TabGap);
            if (GUILayout.Button("CLOSE", Theme.Flush))
                Close();
            GUILayout.Space(Theme.ScrollbarSpace);
            GUILayout.EndHorizontal();

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
            GUILayout.Space(Theme.Panel.margin.bottom); // as far from the graph as sections are from each other
            DrawResponseGraph();
            EndPanel();

            BeginPanel("Look");
            s.overlayOpacity = Slider("Opacity", s.overlayOpacity, 0.1f, 1f, "0%");
            s.stickDotSize = Slider("Dot size", s.stickDotSize, 2f, 12f, "0.0' px'");
            EndPanel();
        }

        // Extra space between two setting rows (checkbox / slider) in a section: the checkbox's gap to its label
        // (6 px to the label, plus the label's 4 px padding).
        private const float PanelRowGap = 10f;
        private static bool _panelFirstRow;

        // Before each setting row in a section: the row gap, except above the first.
        private static void PanelRowSpace()
        {
            if (!_panelFirstRow)
                GUILayout.Space(PanelRowGap);
            _panelFirstRow = false;
        }

        // A section: corner marks, no background; optional title.
        private static void BeginPanel(string title)
        {
            _panelFirstRow = true;
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

        private const float KeyLabelWidth = 300f;

        // One "label | key" row; the key button starts capture. A key that's also bound elsewhere shows in yellow, with
        // a wrapped "Also used by: ..." line under the row naming every clash.
        private static void KeyRow(Bind b, float labelWidth = KeyLabelWidth)
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
            // Lowered so the name's first line sits level with the key's text (the key button's margin and padding are
            // taller than the label's).
            GUILayout.Space(Theme.KeyButton.margin.top + Theme.KeyButton.padding.top - Theme.LabelWrap.padding.top);
            GUILayout.Label(b.Label, Theme.LabelWrap);
            if (hint != null)
                GUILayout.Label(hint, HintStyle()); // what this input does in the aircraft you're sitting in
            GUILayout.EndVertical();
            bool capturing = _rebinding == b;
            // Drawn in a keycap instead of by the button: a bound key's name in capitals, an empty cap with a cross, or
            // while waiting for a key, PRESS A KEY with cycling dots.
            bool bound = !string.IsNullOrEmpty(current) && current != "None";
            string text = capturing ? "Press a key…" : KeyDisplayName(current);
            if (bound)
                text = text.ToUpperInvariant();
            GUIStyle keyStyle = !capturing && clashes.Count > 0 ? Theme.KeyButtonClash : Theme.KeyButton;
            GUIStyle drawStyle = !capturing && clashes.Count > 0 ? Theme.KeyButtonClashCapped : Theme.KeyButtonCapped;
            if (GUILayout.Button(text, drawStyle, GUILayout.ExpandWidth(true)))
                _rebinding = capturing ? null : b;
            if (Event.current.type == EventType.Repaint)
            {
                Rect button = GUILayoutUtility.GetLastRect();
                if (capturing)
                {
                    // Three steps a second: "." "..", "...". Laid out for the full "..." so the text doesn't shift.
                    int dots = 1 + (int)(Time.unscaledTime * 3f) % 3;
                    Keycap(button, "PRESS A KEY" + new string('.', dots), keyStyle, false, "PRESS A KEY...");
                }
                else
                    Keycap(button, bound ? text : null, keyStyle);
            }
            GUILayout.EndHorizontal();
            if (clashes.Count > 0 && !capturing)
                GUILayout.Label("Also used by: " + string.Join(", ", clashes), Theme.WarningText);
        }

        // A bound key's name in a rounded 1 px frame like a keycap, in the colour the button's text would have (green,
        // yellow on a clash, white on hover), centred in the button. The frame is fitted to the letters' ink, not the
        // text box (whose side bearings and line spacing are uneven): KeycapPad of clear space between the ink and the
        // frame on every side (the frame's own pixel not counted), at least as wide as it is tall so short names get a
        // square cap. Vertically it always spans the capital height, so every cap in the list is the same height.
        // No key (text null): an empty square cap, the size of a one-letter one, with a cross corner to corner; hovered,
        // the frame and cross turn white (no fill).
        private const float KeycapPad = 5f;

        // fillOnHover false: hovered, only the frame and text turn white. layoutText: sizes and places the cap and text
        // instead of text (which must start the same), so a changing ending doesn't move it.
        private static void Keycap(Rect button, string text, GUIStyle style, bool fillOnHover = true, string layoutText = null)
        {
            Rect capLine = InkBounds("H"); // baseline and capital height
            Rect ink = text != null ? InkBounds(layoutText ?? text) : Rect.zero;
            if (capLine.height <= 0f || (text != null && ink.width <= 0f))
                return;
            float edge = KeycapPad + 1f;
            float capH = Mathf.Round(capLine.height) + edge * 2f;
            float capW = Mathf.Max(capH, Mathf.Round(ink.width) + edge * 2f);
            bool hover = button.Contains(Event.current.mousePosition);
            Color saved = GUI.color;
            if (text == null)
            {
                var empty = new Rect(Mathf.Round(button.center.x - capH * 0.5f), Mathf.Round(button.center.y - capH * 0.5f), capH, capH);
                GUI.color = hover ? Theme.Menu.Hover : style.normal.textColor;
                Theme.Keycap.Draw(empty, false, false, false, false);
                GUI.DrawTexture(empty, Cross((int)capH));
                GUI.color = saved;
                return;
            }
            var cap = new Rect(Mathf.Round(button.center.x - capW * 0.5f), Mathf.Round(button.center.y - capH * 0.5f), capW, capH);
            // Where the text must be drawn so its ink sits in the middle of the cap, on the capital line.
            float x = Mathf.Round(cap.center.x - (ink.x + ink.width * 0.5f));
            float y = cap.y + edge - capLine.y;
            // Styles so the frame is 9-sliced (corners kept at any size); both tinted by GUI.color. Hovered: filled
            // white, the text in the background colour.
            bool fill = hover && fillOnHover;
            GUI.color = hover ? Theme.Menu.Hover : style.normal.textColor;
            (fill ? Theme.KeycapFill : Theme.Keycap).Draw(cap, false, false, false, false);
            if (fill)
                GUI.color = Theme.Menu.Main;
            Theme.KeyText.Draw(new Rect(x, y, 1000f, 100f), new GUIContent(text), false, false, false, false);
            GUI.color = saved;
        }

        // An empty cap's cross: white (tinted when drawn), 1 px anti-aliased diagonals right into the corners, past the
        // frame's rounding. One texture per size, drawn 1:1.
        private static Texture2D _cross;

        private static Texture2D Cross(int n)
        {
            if (_cross != null && _cross.width == n)
                return _cross;
            if (_cross != null)
                Object.Destroy(_cross);
            _cross = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            const float inset = 0.5f; // the corner pixels' centres
            Vector2 a0 = new Vector2(inset, inset), a1 = new Vector2(n - inset, n - inset);
            Vector2 b0 = new Vector2(inset, n - inset), b1 = new Vector2(n - inset, inset);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float d = Mathf.Min(SegmentDistance(p, a0, a1), SegmentDistance(p, b0, b1));
                    _cross.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(1f - d)));
                }
            _cross.Apply();
            return _cross;
        }

        private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        // The ink of the text in the key font, relative to where an upper-left aligned style draws it (y down), as laid
        // out by Unity's text generator (the one IMGUI draws with). Cached per text.
        private static readonly System.Collections.Generic.Dictionary<string, Rect> InkCache = new System.Collections.Generic.Dictionary<string, Rect>();
        private static TextGenerator _textGen;
        private static Font _inkFont;

        private static Rect InkBounds(string text)
        {
            Font font = Theme.KeyText.font;
            if (font == null)
                return Rect.zero;
            if (font != _inkFont)
            {
                InkCache.Clear();
                _inkFont = font;
            }
            if (InkCache.TryGetValue(text, out Rect r))
                return r;
            if (_textGen == null)
                _textGen = new TextGenerator();
            var settings = new TextGenerationSettings
            {
                font = font, fontSize = Theme.KeyText.fontSize, fontStyle = FontStyle.Normal, color = Color.white,
                lineSpacing = 1f, richText = false, scaleFactor = 1f, textAnchor = TextAnchor.UpperLeft,
                pivot = new Vector2(0f, 1f), generationExtents = new Vector2(1000f, 100f),
                horizontalOverflow = HorizontalWrapMode.Overflow, verticalOverflow = VerticalWrapMode.Overflow,
                generateOutOfBounds = true, updateBounds = false,
            };
            _textGen.Populate(text, settings);
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            var verts = _textGen.verts;
            // Four vertices per glyph; spaces have no area.
            for (int i = 0; i + 3 < verts.Count; i += 4)
            {
                float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
                for (int k = 0; k < 4; k++)
                {
                    Vector3 p = verts[i + k].position;
                    x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x);
                    y0 = Mathf.Min(y0, -p.y); y1 = Mathf.Max(y1, -p.y); // generator's y is up
                }
                if (x1 - x0 < 0.01f || y1 - y0 < 0.01f)
                    continue;
                minX = Mathf.Min(minX, x0); maxX = Mathf.Max(maxX, x1);
                minY = Mathf.Min(minY, y0); maxY = Mathf.Max(maxY, y1);
            }
            r = minX <= maxX ? new Rect(minX, minY, maxX - minX, maxY - minY) : Rect.zero;
            InkCache[text] = r;
            return r;
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
            // Draws the card's body itself (key rows mixed with sliders / checkboxes); default: one key row per bind.
            public System.Action<VirtualJoystickSettings, Bind[]> Body;
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
                new BindCard { Title = "Stick movement", Body = StickMovementBody, Binds = new[]
                {
                    B("Pitch up (stick back)", s => s.pitchUpKey, (s, v) => s.pitchUpKey = v),
                    B("Pitch down (stick forward)", s => s.pitchDownKey, (s, v) => s.pitchDownKey = v),
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
                new BindCard { Title = "Throttle / collective movement",
                    Body = (s, b) => { KeyRows(b, 0, b.Length); s.throttleRate = CardSlider("Throttle speed", s.throttleRate, 0.1f, 3f, "0.00'/s'"); },
                    Binds = new[]
                {
                    B("Throttle up", s => s.throttleUpKey, (s, v) => s.throttleUpKey = v, Cockpit.KeyActions.HintThrottleMove),
                    B("Throttle down", s => s.throttleDownKey, (s, v) => s.throttleDownKey = v, Cockpit.KeyActions.HintThrottleMove),
                    B("Full throttle", s => s.throttleFullKey, (s, v) => s.throttleFullKey = v, Cockpit.KeyActions.HintThrottleMove),
                    B("MIL power (below afterburner)", s => s.throttleMilKey, (s, v) => s.throttleMilKey = v, Cockpit.KeyActions.HintThrottleMove),
                    B("Zero throttle", s => s.throttleZeroKey, (s, v) => s.throttleZeroKey = v, Cockpit.KeyActions.HintThrottleMove),
                } },
                new BindCard { Title = "Trigger / modifier",
                    Body = (s, b) => { KeyRows(b, 0, b.Length); s.throttleTriggerRamp = CardSlider("Ramp-up time", s.throttleTriggerRamp, 0f, 0.8f, "0.0' s'"); },
                    Binds = new[] { B("Trigger", s => s.throttleTriggerKey, (s, v) => s.throttleTriggerKey = v, Cockpit.KeyActions.HintLeftTrigger) } },
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
                new BindCard { Title = "Airbrake (speed brake)", Available = () => Cockpit.KeyActions.HasAirbrake, Binds = new[]
                {
                    B("Hold", s => s.airbrakeHoldKey, (s, v) => s.airbrakeHoldKey = v),
                    B("Toggle", s => s.airbrakeToggleKey, (s, v) => s.airbrakeToggleKey = v),
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

        // Stick movement: pitch, roll and rudder groups, each its keys then its speeds.
        private static void StickMovementBody(VirtualJoystickSettings s, Bind[] b)
        {
            KeyRows(b, 0, 2);
            s.keyboardRatePitch = CardSlider("Pitch Sensitivity", s.keyboardRatePitch, 0.5f, 10f, "0.0'/s'");
            s.keyboardReturnRatePitch = CardToggleSlider("Pitch return to center", ref s.keyboardReturnPitch, s.keyboardReturnRatePitch, 0.5f, 10f, "0.0'/s'");
            GUILayout.Space(GroupGap);
            KeyRows(b, 2, 2);
            s.keyboardRateRoll = CardSlider("Roll Sensitivity", s.keyboardRateRoll, 0.5f, 10f, "0.0'/s'");
            s.keyboardReturnRateRoll = CardToggleSlider("Roll return to center", ref s.keyboardReturnRoll, s.keyboardReturnRateRoll, 0.5f, 10f, "0.0'/s'");
            GUILayout.Space(GroupGap);
            KeyRows(b, 4, 2);
            s.rudderRate = CardSlider("Rudder Sensitivity", s.rudderRate, 0.5f, 10f, "0.0'/s'");
        }

        // Extra space between groups of rows in a card: twice the gap between two rows.
        private const float GroupGap = Theme.Gap * 2;

        private static void KeyRows(Bind[] b, int start, int count)
        {
            for (int i = start; i < start + count; i++)
                KeyRow(b[i]);
        }

        // Vertical padding that gives a slider / checkbox row in a card a key row's height, its text where a key row's sits.
        private static float CardRowPad => Theme.KeyButton.margin.top + Theme.KeyButton.padding.top - Theme.Label.padding.top;

        // A slider in a card: its label in the key names' column, the slider in the keys' column.
        private static float CardSlider(string label, float value, float min, float max, string format, bool enabled = true)
        {
            GUILayout.Space(CardRowPad);
            _panelFirstRow = true; // cards space their rows themselves
            float v = Slider(label, value, min, max, format, KeyLabelWidth, enabled);
            GUILayout.Space(CardRowPad);
            return v;
        }

        // A checkbox and the slider it switches on, in one card row: label in the key names' column, then the (larger)
        // box at the start of the keys' column and the slider after it, grey and fixed while the box is off. The label
        // and the box take the click.
        private static float CardToggleSlider(string label, ref bool on, float value, float min, float max, string format)
        {
            float box = Theme.BigCheckSize;
            const float boxGap = 6f; // box to slider, as a checkbox to its label
            GUILayout.Space(CardRowPad);
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, Theme.Label, GUILayout.Width(KeyLabelWidth));
            Rect labelRect = GUILayoutUtility.GetLastRect();
            Rect boxCol = GUILayoutUtility.GetRect(Theme.Slider.margin.left + box + boxGap, 1f,
                GUILayout.Width(Theme.Slider.margin.left + box + boxGap), GUILayout.Height(1f));
            float v = GUILayout.HorizontalSlider(value, min, max, on ? Theme.Slider : Theme.SliderDisabled,
                on ? Theme.SliderThumb : Theme.SliderThumbDisabled, GUILayout.ExpandWidth(true));
            Rect sliderRect = GUILayoutUtility.GetLastRect();
            if (!on)
                v = value;
            GUILayout.Label(v.ToString(format), on ? Theme.SliderValue : Theme.SliderValueDisabled, GUILayout.Width(64f));
            GUILayout.EndHorizontal();
            GUILayout.Space(CardRowPad);

            if (Event.current.type == EventType.Layout)
                return v;
            // Centred on the slider's line.
            var boxRect = new Rect(boxCol.x + Theme.Slider.margin.left, Mathf.Round(sliderRect.center.y - box * 0.5f), box, box);
            Vector2 mouse = Event.current.mousePosition;
            bool hover = boxRect.Contains(mouse) || labelRect.Contains(mouse);
            if (Event.current.type == EventType.Repaint)
                GUI.DrawTexture(boxRect, on ? (hover ? Theme.BigCheckOnHover : Theme.BigCheckOn) : (hover ? Theme.BigCheckOffHover : Theme.BigCheckOff));
            else if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && hover)
            {
                on = !on;
                Event.current.Use();
            }
            return v;
        }

        // Thumbstick rows share one event; say which way this key pushes it.
        private static string Dir(string dir, string hint) => hint == null ? null : hint.StartsWith("(") ? hint : $"push {dir}: {hint}";

        private static readonly System.Collections.Generic.HashSet<string> OpenCards = new System.Collections.Generic.HashSet<string>();

        // World positions of the aircraft controls whose cards are open: the overlay marks them in the cockpit.
        public static readonly System.Collections.Generic.List<Vector3> HighlightPositions = new System.Collections.Generic.List<Vector3>();

        private static void DrawBindingsTab(VirtualJoystickSettings s)
        {
            string vehicle = Cockpit.KeyActions.VehicleName;
            if (vehicle != null)
            {
                BeginPanel(null);
                if (GUILayout.Button($"Rescan {vehicle} controls", Theme.Flush))
                    Cockpit.KeyActions.Rescan();
                EndPanel();
            }

            foreach (var section in Sections)
            {
                string device = section.Device?.Invoke();
                // More room inside the corner marks than a settings section: above the title, and below the last card
                // as much as between the title and the first card.
                GUILayout.BeginVertical(Theme.Panel);
                GUILayout.Space(SectionTopExtra);
                GUILayout.Label(section.Title + (device != null ? $"   ({device})" : ""), CategoryHeader());
                foreach (var card in section.Cards)
                    DrawCard(s, section.Title + "/" + card.Title, card);
                GUILayout.Space(SectionBottomExtra);
                GUILayout.EndVertical();
            }
        }

        // Bindings sections: extra space above the title, and below the last card so the space under it matches the
        // title's gap to the first card (the title's bottom padding and margin, plus the text's descent).
        private const float SectionTopExtra = Theme.Gap;
        private const float SectionBottomExtra = Theme.Gap;

        // An expanding card: header (title, number of keys bound, availability); key rows when open.
        private static void DrawCard(VirtualJoystickSettings s, string id, BindCard card)
        {
            bool open = OpenCards.Contains(id);
            int bound = CountBound(card.Binds);
            bool missing = Cockpit.KeyActions.VehicleName != null && card.Available != null && !card.Available();
            bool clash = CardHasClash(card);
            string header = card.Title + (bound > 0 ? $"   ({bound} bound)" : "") + (missing ? "   (not in this aircraft)" : "");
            // Framed by a 1 px border; when open the frame holds the header and key rows together. Yellow frame and
            // header text when one of its keys is also bound elsewhere.
            if (open)
                GUILayout.BeginVertical(clash ? Theme.CardOpenClash : Theme.CardOpen);
            GUIStyle headerStyle = open
                ? (clash ? Theme.CardHeaderOpenClash : Theme.CardHeaderOpen)
                : (clash ? Theme.CardHeaderClash : CardHeader());
            bool clicked = GUILayout.Button(header, headerStyle);
            bool headerHover = open && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition);
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
            GUILayout.BeginVertical(Theme.CardBody);
            if (card.Body != null)
                card.Body(s, card.Binds);
            else
                KeyRows(card.Binds, 0, card.Binds.Length);
            GUILayout.EndVertical();
            GUILayout.EndVertical(); // the frame
            // Hovering an open card's header turns its whole frame white, like a closed card's.
            if (headerHover && Event.current.type == EventType.Repaint)
                Outline(GUILayoutUtility.GetLastRect(), Theme.Menu.Hover);
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

            if (Event.current.type != EventType.Repaint)
                return;

            var s = VirtualJoystickSettings.Current;
            // One colour throughout: the outline with quarter ticks, the deadzone edge as a dotted vertical line, the
            // linear reference as a dotted diagonal and the response as a solid 2 px curve.
            Color c = Theme.Menu.Text;

            Outline(g, c);
            const float tick = 4f;
            for (int i = 1; i < 4; i++)
            {
                float tx = Mathf.Round(g.x + g.width * i / 4f), ty = Mathf.Round(g.y + g.height * i / 4f);
                Fill(new Rect(tx, g.yMax - tick, 1f, tick), c);
                Fill(new Rect(g.x, ty, tick, 1f), c);
            }

            float dzX = Mathf.Round(g.x + s.deadzone * g.width);
            if (dzX - g.x >= 1f)
                for (float y = g.y; y < g.yMax; y += 4f)
                    Fill(new Rect(dzX, y, 1f, 2f), c);

            float prevOut = g.yMax;
            int cols = Mathf.RoundToInt(g.width);
            for (int i = 0; i <= cols; i++)
            {
                float a = i / (float)cols;
                float x = g.x + i;
                if (i % 4 == 0)
                    Fill(new Rect(x, Mathf.Round(g.yMax - a * g.height), 1f, 1f), c);
                float yOut = g.yMax - VirtualJoystickBehaviour.AxisResponse(a) * g.height;
                VSpan(x, prevOut, yOut, 2f, c);
                prevOut = yOut;
            }
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

        // Label | slider | value: the slider takes all the width the label and the (right-aligned) value leave.
        // Disabled: drawn in grey and can't be moved.
        private static float Slider(string label, float value, float min, float max, string format, float labelWidth = 260f, bool enabled = true)
        {
            PanelRowSpace();
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, enabled ? Theme.Label : Theme.LabelDisabled, GUILayout.Width(labelWidth));
            float v = GUILayout.HorizontalSlider(value, min, max, enabled ? Theme.Slider : Theme.SliderDisabled,
                enabled ? Theme.SliderThumb : Theme.SliderThumbDisabled, GUILayout.ExpandWidth(true));
            if (!enabled)
                v = value;
            GUILayout.Label(v.ToString(format), enabled ? Theme.SliderValue : Theme.SliderValueDisabled, GUILayout.Width(64f));
            GUILayout.EndHorizontal();
            return v;
        }

        // Checkbox: hollow when off, filled when on. Drawn by hand so it looks exactly like that.
        private static bool Toggle(string label, bool value)
        {
            // The box is as tall as the text's capitals, centred on the text line.
            PanelRowSpace();
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

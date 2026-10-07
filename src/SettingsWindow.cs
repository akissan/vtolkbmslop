using UnityEngine;

namespace MouseStick
{
    // In-game settings window (default F8). Changes apply immediately; they are written to mousestick.json
    // when the window closes. While it is open the overlay is shown as a live preview.
    internal static class SettingsWindow
    {
        private const int WindowId = 0x4D53; // "MS"

        private static Rect _rect = new Rect(40f, 120f, 380f, 10f);

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
            MouseStickSettings.SaveIfDirty();
        }

        public static void Draw()
        {
            if (!IsOpen)
                return;
            _rect.x = Mathf.Clamp(_rect.x, 0f, Mathf.Max(0f, Screen.width - _rect.width));
            _rect.y = Mathf.Clamp(_rect.y, 0f, Mathf.Max(0f, Screen.height - 60f));
            _rect = GUILayout.Window(WindowId, _rect, DrawContents, "Mouse Stick settings");
        }

        private static void DrawContents(int id)
        {
            var s = MouseStickSettings.Current;

            s.enableOnSpawn = Toggle("Enable stick control when entering cockpit", s.enableOnSpawn);

            GUILayout.Space(6f);
            GUILayout.Label("Control area", Bold());
            s.overlaySize = Slider("Size", s.overlaySize, 100f, 900f, "0' px'");
            s.deadzone = Slider("Deadzone", s.deadzone, 0f, 0.3f, "0%");
            s.sensitivity = Slider("Mouse sensitivity", s.sensitivity, 0.25f, 4f, "0.00'x'");
            s.curve = Slider("Centre curve (inverse cubic)", s.curve, 0f, 1f, "0%");
            DrawResponseGraph();
            s.autoCenterRate = Slider("Auto-centre", s.autoCenterRate, 0f, 5f, "0.0'/s'");
            s.circularLimit = Toggle("Round travel limit", s.circularLimit);
            s.invertPitch = Toggle("Invert pitch (mouse up = nose up)", s.invertPitch);

            GUILayout.Space(6f);
            GUILayout.Label("Keyboard", Bold());
            s.keyboardRate = Slider("WASD speed", s.keyboardRate, 0.5f, 10f, "0.0'/s'");
            s.keyboardReturnRatePitch = Slider("W/S return to centre", s.keyboardReturnRatePitch, 0.1f, 20f, "0.0'/s'");
            s.keyboardReturnRateRoll = Slider("A/D return to centre", s.keyboardReturnRateRoll, 0.1f, 20f, "0.0'/s'");
            s.rudderRate = Slider("Rudder (Q/E) speed", s.rudderRate, 0.5f, 10f, "0.0'/s'");

            GUILayout.Space(6f);
            GUILayout.Label("SOI cursor", Bold());
            s.tgpSensitivity = Slider("TGP sensitivity", s.tgpSensitivity, 0.1f, 5f, "0.00'x'");
            s.cursorSensitivity = Slider("Radar/map/ARAD cursor", s.cursorSensitivity, 0.1f, 5f, "0.00'x'");

            GUILayout.Space(6f);
            GUILayout.Label("Overlay", Bold());
            s.overlayOpacity = Slider("Opacity", s.overlayOpacity, 0.1f, 1f, "0%");
            s.clickModeOpacity = Slider($"Clickable mode ({s.clickModeKey}) opacity", s.clickModeOpacity, 0f, 1f, "0%");
            s.leftMouseFiresTrigger = Toggle("LMB fires stick trigger", s.leftMouseFiresTrigger);

            GUILayout.Space(6f);
            GUILayout.Label("Cockpit screens", Bold());
            s.handleScreens = Toggle("Handle screen buttons & touch drag (not FlatScreen 3)", s.handleScreens);
            s.showScreenHitbox = Toggle("Outline the screen hitbox under the cursor", s.showScreenHitbox);
            s.showScreenTooltip = Toggle("Debug tooltip: element name, hitbox source, size", s.showScreenTooltip);

            GUILayout.Space(6f);
            DrawKeybinds(s);

            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset to defaults"))
            {
                MouseStickSettings.ResetToDefaults();
                _rebinding = null;
                KeysChanged = true;
            }
            if (GUILayout.Button("Close"))
                Close();
            GUILayout.EndHorizontal();

            GUILayout.Label($"{s.menuKey} toggles this window.", Small());

            GUI.DragWindow();
        }

        // ------------------------------------------------------------------ keybinds

        private class Bind
        {
            public string Label;
            public System.Func<MouseStickSettings, string> Get;
            public System.Action<MouseStickSettings, string> Set;
        }

        private static readonly Bind[] Binds =
        {
            new Bind { Label = "Stick control (toggle)", Get = s => s.toggleKey, Set = (s, v) => s.toggleKey = v },
            new Bind { Label = "Stick control (toggle, 2nd)", Get = s => s.toggleKey2, Set = (s, v) => s.toggleKey2 = v },
            new Bind { Label = "Stick control (while held)", Get = s => s.clickModeKey, Set = (s, v) => s.clickModeKey = v },
            new Bind { Label = "SOI cursor (toggle)", Get = s => s.tgpModeKey, Set = (s, v) => s.tgpModeKey = v },
            new Bind { Label = "SOI cursor (while held)", Get = s => s.soiHoldKey, Set = (s, v) => s.soiHoldKey = v },
            new Bind { Label = "Settings window", Get = s => s.menuKey, Set = (s, v) => s.menuKey = v },
            new Bind { Label = "Pitch down (stick fwd)", Get = s => s.pitchDownKey, Set = (s, v) => s.pitchDownKey = v },
            new Bind { Label = "Pitch up (stick back)", Get = s => s.pitchUpKey, Set = (s, v) => s.pitchUpKey = v },
            new Bind { Label = "Roll left", Get = s => s.rollLeftKey, Set = (s, v) => s.rollLeftKey = v },
            new Bind { Label = "Roll right", Get = s => s.rollRightKey, Set = (s, v) => s.rollRightKey = v },
            new Bind { Label = "Rudder left", Get = s => s.rudderLeftKey, Set = (s, v) => s.rudderLeftKey = v },
            new Bind { Label = "Rudder right", Get = s => s.rudderRightKey, Set = (s, v) => s.rudderRightKey = v },
        };

        private static bool _showKeybinds;
        private static Bind _rebinding;
        private static KeyCode[] _keyCandidates;

        // True while waiting for a key press; the mod ignores its own shortcuts meanwhile.
        public static bool IsCapturingKey => IsOpen && _rebinding != null;

        // Set when a binding changes; the behaviour re-reads its keys and clears it.
        public static bool KeysChanged;

        private static void DrawKeybinds(MouseStickSettings s)
        {
            if (GUILayout.Button((_showKeybinds ? "▾ " : "▸ ") + "Keybinds", Bold()))
            {
                _showKeybinds = !_showKeybinds;
                _rebinding = null;
            }
            if (!_showKeybinds)
                return;

            foreach (var b in Binds)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(b.Label, GUILayout.Width(170f));
                string current = b.Get(s);
                string text = _rebinding == b ? "Press a key…" : KeyDisplayName(current);
                if (GUILayout.Button(text, GUILayout.Width(185f)))
                    _rebinding = _rebinding == b ? null : b;
                GUILayout.EndHorizontal();
            }
            GUILayout.Label(_rebinding != null
                ? "Press a key to bind. Esc cancels, Delete or Backspace clears."
                : "Click a binding, then press the new key.", Small());
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
            _rebinding.Set(MouseStickSettings.Current, key);
            _rebinding = null;
            MouseStickSettings.MarkDirty();
            KeysChanged = true;
        }

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

            var s = MouseStickSettings.Current;
            Fill(g, new Color(0f, 0f, 0f, 0.35f));

            // Quarter grid.
            Color grid = new Color(1f, 1f, 1f, 0.08f);
            for (int i = 1; i < 4; i++)
            {
                Fill(new Rect(g.x + g.width * i / 4f, g.y, 1f, g.height), grid);
                Fill(new Rect(g.x, g.y + g.height * i / 4f, g.width, 1f), grid);
            }

            // Deadzone band.
            float dzW = s.deadzone * g.width;
            if (dzW >= 1f)
                Fill(new Rect(g.x, g.y, dzW, g.height), new Color(1f, 0.85f, 0.3f, 0.15f));

            // Linear reference diagonal, then the actual response, drawn column by column.
            Color linear = new Color(1f, 1f, 1f, 0.25f);
            Color curve = new Color(0.55f, 1f, 0.6f, 1f);
            float prevLin = g.yMax, prevOut = g.yMax;
            int cols = Mathf.RoundToInt(g.width);
            for (int i = 0; i <= cols; i++)
            {
                float a = i / (float)cols;
                float x = g.x + i;
                float yLin = g.yMax - a * g.height;
                float yOut = g.yMax - MouseStickBehaviour.AxisResponse(a) * g.height;
                VSpan(x, prevLin, yLin, 1f, linear);
                VSpan(x, prevOut, yOut, 2f, curve);
                prevLin = yLin;
                prevOut = yOut;
            }

            Outline(g, new Color(1f, 1f, 1f, 0.35f));
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
            GUILayout.Label(label, GUILayout.Width(170f));
            float v = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(130f));
            GUILayout.Label(v.ToString(format), GUILayout.Width(55f));
            GUILayout.EndHorizontal();
            if (!Mathf.Approximately(v, value))
                MouseStickSettings.MarkDirty();
            return v;
        }

        private static bool Toggle(string label, bool value)
        {
            bool v = GUILayout.Toggle(value, " " + label);
            if (v != value)
                MouseStickSettings.MarkDirty();
            return v;
        }

        private static GUIStyle _bold, _small;

        private static GUIStyle Bold() => _bold ?? (_bold = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });

        private static GUIStyle Small() => _small ?? (_small = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true });
    }
}

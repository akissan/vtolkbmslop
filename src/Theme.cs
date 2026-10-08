using UnityEngine;

namespace VirtualJoystick
{
    // Look of the settings window, after a dark cockpit MFD: near-black base with a slight purple cast, borderless
    // purple-grey panels with hard corners and padding (stepped shades instead of outlines), light grey-green text,
    // a muted blue-green accent and the aircraft HUD's font (VeraMono).
    // Built from scratch, and once more when the HUD font turns up.
    internal static class Theme
    {
        // Every colour of the settings window, and only of it: the overlay, its title and messages and the screen
        // hovers use HudGreen below, so the menu can be recoloured without touching them.
        public static class Menu
        {
            public static readonly Color Main = Hex(0x1B1B21, 0.95f);         // window background
            public static readonly Color Section = Hex(0x26252D, 0.85f);      // section / group panels
            public static readonly Color Control = Hex(0x31313A, 0.95f);      // buttons, card headers and bodies
            public static readonly Color ControlHover = Hex(0x3A3A45, 0.97f);
            public static readonly Color ControlActive = Hex(0x44434F, 1f);
            public static readonly Color KeyBg = Hex(0x141318, 0.95f);        // key binding buttons
            public static readonly Color KeyHover = Hex(0x1D1C23, 0.97f);
            public static readonly Color Text = Hex(0xCCDACB, 1f);            // grey with a little green
            public static readonly Color TextHover = Color.white;             // button text when hovered / pressed
            public static readonly Color Muted = Hex(0x8A9989, 1f);           // small text and hints
            public static readonly Color Accent = Hex(0x6BB88A, 1f);          // selected tab, slider thumb, checked box
            public static readonly Color AccentHover = Color.Lerp(Accent, Color.white, 0.15f);
            public static readonly Color OnAccentText = Color.white;          // text on the accent (selected tab)
            public static readonly Color CheckOffFill = Hex(0x141318, 1f);
            public static readonly Color CheckBorder = Hex(0x6A786B, 1f);
            public static readonly Color CheckOffHoverBorder = Color.Lerp(CheckBorder, Accent, 0.6f);
            public static readonly Color CheckOnBorder = Color.Lerp(Accent, Color.white, 0.3f);
            public static readonly Color CheckOnHoverFill = Color.Lerp(Accent, Color.white, 0.15f);
            public static readonly Color CheckOnHoverBorder = Color.Lerp(Accent, Color.white, 0.45f);
            public static readonly Color Warning = Hex(0xF2C14E, 1f);         // clashing key bindings
            public static readonly Color ScrollTrack = Hex(0x141318, 0.7f);
            public static readonly Color ScrollThumb = Hex(0x4B4956, 1f);

            // Response curve graph (Stick tab).
            public static readonly Color GraphBg = new Color(0f, 0f, 0f, 0.35f);
            public static readonly Color GraphGrid = new Color(1f, 1f, 1f, 0.08f);
            public static readonly Color GraphDeadzone = new Color(1f, 0.85f, 0.3f, 0.15f);
            public static readonly Color GraphLinear = new Color(1f, 1f, 1f, 0.25f);
            public static readonly Color GraphCurve = Accent;
            public static readonly Color GraphOutline = new Color(1f, 1f, 1f, 0.35f);
        }

        // The green of the aircraft HUD and helmet symbology (the game's most used UI green): the overlay title and
        // messages, the screen hover outline and the hovered button's labels. Not used by the settings window.
        public static readonly Color HudGreen = new Color(0.67f, 1f, 0.08f, 1f);

        // The portal MFD buttons' hover (read from the F-45A prefab) is a hard white frame, a 9-sliced sprite with a
        // solid 4/128 px edge: about 1/26 of the button's height, no glow, no fill (the screen hover outline copies
        // the shape, in HUD green).
        // The HUD and helmet HMCS text and symbols (read from the aircraft prefabs): VeraMono in HudGreen at 85% alpha
        // (a few aircraft's HMCS use a bluer (0.641, 1, 0.309)), material mat_HUD-UI with the game's UI/DefaultOverlay2
        // shader (UI/Default with additive "Blend SrcAlpha One", no depth test). Used for the overlay title and
        // messages, drawn the same way.
        public const string HmcsShaderName = "UI/DefaultOverlay2";
        public const float OverlayTextOpacity = 0.85f;

        // 13 rather than 14: the monospaced HUD font runs wider than the default one.
        public const int FontSize = 13;

        // The font of the game's HUD and helmet display text. It's loaded with the game's shared assets, so it may not
        // exist yet at startup: looked up again every couple of seconds until found (null until then).
        private const string HudFontName = "VeraMono";
        private static Font _hudFont;
        private static float _nextHudFontLookup;

        public static Font HudFont
        {
            get
            {
                if (_hudFont == null && Time.unscaledTime >= _nextHudFontLookup)
                {
                    _nextHudFontLookup = Time.unscaledTime + 2f;
                    foreach (var f in Resources.FindObjectsOfTypeAll<Font>())
                        if (f != null && f.name == HudFontName)
                            _hudFont = f;
                }
                return _hudFont;
            }
        }

        private static bool _builtWithHudFont;

        // One spacing unit for every gap: window edges, between tabs, panels, cards, rows and buttons. The scrollbar is
        // one gap wide, with one gap on each side of it (to the content, and the window edge).
        public const int Gap = 6;

        // Width the scrollbar column takes (gap + bar): the tab bar and bottom row leave the same space on the right.
        public const float ScrollbarSpace = Gap * 2;

        private static GUISkin _skin;
        public static GUIStyle Window, Label, Bold, Small, Hint, SectionTitle, Button, CardHeader, CardHeaderOpen, CardBody,
            Tab, Panel, Slider, SliderThumb, KeyButton, Flush, LabelWrap, WarningText;
        public static Texture2D CheckOff, CheckOffHover, CheckOn, CheckOnHover;

        // Checkbox side: the height of a capital letter of Label.
        public static int CheckSize;

        // Call inside OnGUI (needs GUI.skin for the default font).
        public static GUISkin Skin
        {
            get
            {
                // Rebuilt once the HUD font is found, if it wasn't there the first time.
                if (_skin == null || (!_builtWithHudFont && HudFont != null))
                    Build();
                return _skin;
            }
        }

        private static void Build()
        {
            Font font = HudFont;
            _builtWithHudFont = font != null;
            if (font == null)
                font = GUI.skin.font;
            _skin = ScriptableObject.CreateInstance<GUISkin>();
            _skin.hideFlags = HideFlags.HideAndDontSave;
            _skin.font = font;
            // VeraMono has no bold face: Unity fakes one by thickening the glyphs without widening their advance, so
            // the letters run into each other. Bold only for the fallback font.
            FontStyle heavy = _builtWithHudFont ? FontStyle.Normal : FontStyle.Bold;

            Texture2D control = Solid(Menu.Control), controlHover = Solid(Menu.ControlHover), controlActive = Solid(Menu.ControlActive);
            Texture2D accent = Solid(Menu.Accent), accentHover = Solid(Menu.AccentHover);

            Window = new GUIStyle
            {
                font = font, fontSize = FontSize + 1, fontStyle = heavy,
                alignment = TextAnchor.UpperCenter,
                padding = new RectOffset(Gap, Gap, 32, Gap),
                contentOffset = new Vector2(0f, -24f),
            };
            Window.normal.background = Solid(Menu.Main);
            Window.normal.textColor = Menu.Text;
            Window.onNormal.background = Window.normal.background;
            Window.onNormal.textColor = Menu.Text;

            Label = new GUIStyle { font = font, fontSize = FontSize, wordWrap = false, padding = new RectOffset(2, 2, 3, 3), margin = new RectOffset(0, 0, 0, 0) };
            Label.normal.textColor = Menu.Text;

            // Wrapping text that grows downwards instead of clipping; padded so it never touches its container's edges.
            LabelWrap = new GUIStyle(Label) { wordWrap = true, padding = new RectOffset(4, 6, 3, 3) };
            WarningText = new GUIStyle(Label) { fontSize = FontSize - 2, wordWrap = true, padding = new RectOffset(6, 6, 0, 6) };
            WarningText.normal.textColor = Menu.Warning;

            Bold = new GUIStyle(Label) { fontStyle = heavy };
            Small = new GUIStyle(Label) { fontSize = FontSize - 2, wordWrap = true };
            Small.normal.textColor = Menu.Muted;
            Hint = new GUIStyle(Label) { fontSize = FontSize - 3, wordWrap = true, padding = new RectOffset(8, 6, 0, 4) };
            Hint.normal.textColor = Menu.Muted;
            SectionTitle = new GUIStyle(Label) { fontStyle = heavy, fontSize = FontSize + 1, padding = new RectOffset(2, 2, 0, Gap) };
            SectionTitle.normal.textColor = Menu.Text;

            Button = new GUIStyle
            {
                font = font, fontSize = FontSize, alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(10, 10, 6, 6), margin = new RectOffset(0, 0, 0, Gap),
            };
            Button.normal.background = control;
            Button.normal.textColor = Menu.Text;
            Button.hover.background = controlHover;
            Button.hover.textColor = Menu.TextHover;
            Button.active.background = controlActive;
            Button.active.textColor = Menu.TextHover;

            // Key binding buttons: just a darker background.
            KeyButton = new GUIStyle(Button) { margin = new RectOffset(Gap, 0, Gap / 2, Gap / 2) };
            KeyButton.normal.background = Solid(Menu.KeyBg);
            KeyButton.hover.background = Solid(Menu.KeyHover);
            KeyButton.active.background = Solid(Menu.KeyHover);

            // Cards: a header button; when open, the body continues directly below it in the same shade (no gap).
            CardHeader = new GUIStyle(Button) { alignment = TextAnchor.MiddleLeft, padding = new RectOffset(10, 10, 7, 7), wordWrap = true };
            CardHeaderOpen = new GUIStyle(CardHeader) { margin = new RectOffset(0, 0, 0, 0) };
            CardBody = new GUIStyle { padding = new RectOffset(10, 10, Gap, Gap), margin = new RectOffset(0, 0, 0, Gap) };
            CardBody.normal.background = control;

            // Tabs and full-width row buttons have no side margins; the window puts explicit gaps between them.
            Flush = new GUIStyle(Button) { margin = new RectOffset(0, 0, 0, 0) };
            Tab = new GUIStyle(Button) { fontStyle = heavy, padding = new RectOffset(10, 10, 7, 7), margin = new RectOffset(0, 0, 0, 0) };
            Tab.onNormal.background = accent;
            Tab.onNormal.textColor = Menu.OnAccentText;
            Tab.onHover.background = accentHover;
            Tab.onHover.textColor = Menu.OnAccentText;
            Tab.onActive.background = accent;
            Tab.onActive.textColor = Menu.OnAccentText;

            // No side margins: panels span exactly the width of the tab bar and the bottom row.
            Panel = new GUIStyle { padding = new RectOffset(10, 10, 10, 10), margin = new RectOffset(0, 0, 0, Gap) };
            Panel.normal.background = Solid(Menu.Section);

            // A 1 px line in the text colour through the middle of the slider, accent handle its full height. The slider
            // itself is the handle's height (not the line's) so the whole handle takes clicks instead of dragging the window.
            Slider = new GUIStyle { fixedHeight = SliderHeight, margin = new RectOffset(4, 4, 4, 5) };
            Slider.normal.background = HorizontalLine(SliderHeight, Menu.Text);
            SliderThumb = new GUIStyle { fixedWidth = 6f, fixedHeight = SliderHeight };
            SliderThumb.normal.background = accent;
            SliderThumb.hover.background = accentHover;
            SliderThumb.active.background = accentHover;

            // Checkboxes as tall as a capital letter, with a slight 1 px border (drawn 1:1 so it stays 1 px).
            MeasureText(font, FontSize);
            CheckOff = Checkbox(Menu.CheckOffFill, Menu.CheckBorder);
            CheckOffHover = Checkbox(Menu.CheckOffFill, Menu.CheckOffHoverBorder);
            CheckOn = Checkbox(Menu.Accent, Menu.CheckOnBorder);
            CheckOnHover = Checkbox(Menu.CheckOnHoverFill, Menu.CheckOnHoverBorder);

            _skin.window = Window;
            _skin.label = Label;
            _skin.button = Button;
            _skin.box = Panel;
            _skin.toggle = new GUIStyle(Label);
            _skin.horizontalSlider = Slider;
            _skin.horizontalSliderThumb = SliderThumb;
            _skin.textField = new GUIStyle(KeyButton) { alignment = TextAnchor.MiddleLeft };
            _skin.scrollView = new GUIStyle();
            _skin.verticalScrollbar = new GUIStyle { fixedWidth = Gap, margin = new RectOffset(Gap, 0, 0, 0) };
            _skin.verticalScrollbar.normal.background = Solid(Menu.ScrollTrack);
            _skin.verticalScrollbarThumb = new GUIStyle { fixedWidth = Gap };
            _skin.verticalScrollbarThumb.normal.background = Solid(Menu.ScrollThumb);
            _skin.verticalScrollbarUpButton = new GUIStyle();
            _skin.verticalScrollbarDownButton = new GUIStyle();
            _skin.horizontalScrollbar = new GUIStyle();
            _skin.horizontalScrollbarThumb = new GUIStyle();
            _skin.horizontalScrollbarLeftButton = new GUIStyle();
            _skin.horizontalScrollbarRightButton = new GUIStyle();
        }

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 2; y++)
                for (int x = 0; x < 2; x++)
                    t.SetPixel(x, y, c);
            t.Apply();
            return t;
        }

        private const int SliderHeight = 17;

        // Transparent, with a 1 px line across the middle row; drawn at its own height so the line stays 1 px.
        private static Texture2D HorizontalLine(int height, Color c)
        {
            var t = new Texture2D(2, height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < height; y++)
                for (int x = 0; x < 2; x++)
                    t.SetPixel(x, y, y == height / 2 ? c : Color.clear);
            t.Apply();
            return t;
        }

        // Cap height of the font at the given size, from the glyph of 'H'.
        private static void MeasureText(Font font, int size)
        {
            float cap = size * 0.73f; // VeraMono's proportion, if the font can't say
            font.RequestCharactersInTexture("H", size, FontStyle.Normal);
            if (font.GetCharacterInfo('H', out CharacterInfo ci, size, FontStyle.Normal) && ci.maxY > 0)
                cap = ci.maxY;
            CheckSize = Mathf.Max(6, Mathf.RoundToInt(cap));
        }

        // Square with a 1 px border, drawn 1:1 so the border stays thin.
        private static Texture2D Checkbox(Color fill, Color border)
        {
            int n = CheckSize;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    t.SetPixel(x, y, x == 0 || y == 0 || x == n - 1 || y == n - 1 ? border : fill);
            t.Apply();
            return t;
        }

        private static Color Hex(int rgb, float a) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, a);
    }
}

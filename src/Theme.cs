using UnityEngine;

namespace VirtualJoystick
{
    // Look of the settings window, after a dark cockpit MFD: near-black base with a slight green cast, borderless
    // green-grey panels with hard corners and padding (stepped shades instead of outlines), light grey-green text,
    // the aircraft HUD's green as accent and its font (VeraMono).
    // Built from scratch, and once more when the HUD font turns up.
    internal static class Theme
    {
        public static readonly Color Main = Hex(0x181C19, 0.95f);         // window background
        public static readonly Color Section = Hex(0x222823, 0.85f);      // section / group panels
        public static readonly Color Control = Hex(0x2D352E, 0.95f);      // buttons, card headers and bodies
        public static readonly Color ControlHover = Hex(0x364038, 0.97f);
        public static readonly Color ControlActive = Hex(0x3F4A41, 1f);
        public static readonly Color KeyBg = Hex(0x111512, 0.95f);        // key binding buttons: darker, no border
        public static readonly Color KeyHover = Hex(0x1A1F1B, 0.97f);
        public static readonly Color Text = Hex(0xCCDACB, 1f);            // grey with a little green
        public static readonly Color Muted = Hex(0x8A9989, 1f);
        // The green of the aircraft HUD and helmet symbology (the game's most used UI green). Also the screen hover
        // outline, the overlay title and messages.
        public static readonly Color HudGreen = new Color(0.67f, 1f, 0.08f, 1f);
        public static readonly Color Accent = HudGreen;
        public static readonly Color CheckOffFill = Hex(0x111512, 1f);
        public static readonly Color CheckBorder = Hex(0x6A786B, 1f);
        public static readonly Color Warning = Hex(0xF2C14E, 1f);

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

            Texture2D control = Solid(Control), controlHover = Solid(ControlHover), controlActive = Solid(ControlActive);
            Texture2D accent = Solid(Accent), accentHover = Solid(Color.Lerp(Accent, Color.white, 0.15f));
            Color onAccentText = Hex(0x111A05, 1f);

            Window = new GUIStyle
            {
                font = font, fontSize = FontSize + 1, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperCenter,
                padding = new RectOffset(Gap, Gap, 32, Gap),
                contentOffset = new Vector2(0f, -24f),
            };
            Window.normal.background = Solid(Main);
            Window.normal.textColor = Text;
            Window.onNormal.background = Window.normal.background;
            Window.onNormal.textColor = Text;

            Label = new GUIStyle { font = font, fontSize = FontSize, wordWrap = false, padding = new RectOffset(2, 2, 3, 3), margin = new RectOffset(0, 0, 0, 0) };
            Label.normal.textColor = Text;

            // Wrapping text that grows downwards instead of clipping; padded so it never touches its container's edges.
            LabelWrap = new GUIStyle(Label) { wordWrap = true, padding = new RectOffset(4, 6, 3, 3) };
            WarningText = new GUIStyle(Label) { fontSize = FontSize - 2, wordWrap = true, padding = new RectOffset(6, 6, 0, 6) };
            WarningText.normal.textColor = Warning;

            Bold = new GUIStyle(Label) { fontStyle = FontStyle.Bold };
            Small = new GUIStyle(Label) { fontSize = FontSize - 2, wordWrap = true };
            Small.normal.textColor = Muted;
            Hint = new GUIStyle(Label) { fontSize = FontSize - 3, wordWrap = true, padding = new RectOffset(8, 6, 0, 4) };
            Hint.normal.textColor = Muted;
            SectionTitle = new GUIStyle(Label) { fontStyle = FontStyle.Bold, fontSize = FontSize + 1, padding = new RectOffset(2, 2, 0, Gap) };
            SectionTitle.normal.textColor = Text;

            Button = new GUIStyle
            {
                font = font, fontSize = FontSize, alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(10, 10, 6, 6), margin = new RectOffset(0, 0, 0, Gap),
            };
            Button.normal.background = control;
            Button.normal.textColor = Text;
            Button.hover.background = controlHover;
            Button.hover.textColor = Color.white;
            Button.active.background = controlActive;
            Button.active.textColor = Color.white;

            // Key binding buttons: just a darker background.
            KeyButton = new GUIStyle(Button) { margin = new RectOffset(Gap, 0, Gap / 2, Gap / 2) };
            KeyButton.normal.background = Solid(KeyBg);
            KeyButton.hover.background = Solid(KeyHover);
            KeyButton.active.background = Solid(KeyHover);

            // Cards: a header button; when open, the body continues directly below it in the same shade (no gap).
            CardHeader = new GUIStyle(Button) { alignment = TextAnchor.MiddleLeft, padding = new RectOffset(10, 10, 7, 7), wordWrap = true };
            CardHeaderOpen = new GUIStyle(CardHeader) { margin = new RectOffset(0, 0, 0, 0) };
            CardBody = new GUIStyle { padding = new RectOffset(10, 10, Gap, Gap), margin = new RectOffset(0, 0, 0, Gap) };
            CardBody.normal.background = control;

            // Tabs and full-width row buttons have no side margins; the window puts explicit gaps between them.
            Flush = new GUIStyle(Button) { margin = new RectOffset(0, 0, 0, 0) };
            Tab = new GUIStyle(Button) { fontStyle = FontStyle.Bold, padding = new RectOffset(10, 10, 7, 7), margin = new RectOffset(0, 0, 0, 0) };
            Tab.onNormal.background = accent;
            Tab.onNormal.textColor = onAccentText;
            Tab.onHover.background = accentHover;
            Tab.onHover.textColor = onAccentText;
            Tab.onActive.background = accent;
            Tab.onActive.textColor = onAccentText;

            // No side margins: panels span exactly the width of the tab bar and the bottom row.
            Panel = new GUIStyle { padding = new RectOffset(10, 10, 10, 10), margin = new RectOffset(0, 0, 0, Gap) };
            Panel.normal.background = Solid(Section);

            // Thin dark track, green handle overflowing it vertically.
            Slider = new GUIStyle { fixedHeight = 6f, margin = new RectOffset(4, 4, 10, 10) };
            Slider.normal.background = Solid(KeyBg);
            SliderThumb = new GUIStyle { fixedWidth = 10f, overflow = new RectOffset(0, 0, 6, 6) };
            SliderThumb.normal.background = accent;
            SliderThumb.hover.background = accentHover;
            SliderThumb.active.background = accentHover;

            // Checkboxes keep a slight 1 px border (drawn at exactly 16 px so it stays 1 px).
            CheckOff = Checkbox(CheckOffFill, CheckBorder);
            CheckOffHover = Checkbox(CheckOffFill, Color.Lerp(CheckBorder, Accent, 0.6f));
            CheckOn = Checkbox(Accent, Color.Lerp(Accent, Color.white, 0.3f));
            CheckOnHover = Checkbox(Color.Lerp(Accent, Color.white, 0.15f), Color.Lerp(Accent, Color.white, 0.45f));

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
            _skin.verticalScrollbar.normal.background = Solid(Hex(0x111512, 0.7f));
            _skin.verticalScrollbarThumb = new GUIStyle { fixedWidth = Gap };
            _skin.verticalScrollbarThumb.normal.background = Solid(Hex(0x485449, 1f));
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

        // 16 x 16 with a 1 px border, drawn 1:1 so the border stays thin.
        private static Texture2D Checkbox(Color fill, Color border)
        {
            const int n = 16;
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

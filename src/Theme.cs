using UnityEngine;

namespace VirtualJoystick
{
    // Look of the settings window, after a dark cockpit MFD: four opaque colours and nothing else. A dark purple-grey
    // background, bright green for all text, lines and fills, white for whatever is under the cursor and yellow for
    // clashing key bindings. No panels or button backgrounds: buttons, tabs and cards are framed by a 1 px border (a
    // selected tab filled), sections only marked at their corners, and a key binding waiting for a key is
    // filled, its text in the background colour.
    // The aircraft HUD's font (VeraMono).
    // Built from scratch, and once more when the HUD font turns up.
    internal static class Theme
    {
        // Every colour of the settings window, and only of it: the overlay, its title and messages and the screen
        // hovers use HudGreen below, so the menu can be recoloured without touching them.
        public static class Menu
        {
            public static readonly Color Main = Hex(0x17161B, 1f);   // window background (grey, a little purple); also text on a fill
            public static readonly Color Text = Hex(0x74F27E, 1f);   // bright green: text, lines, fills
            public static readonly Color Hover = Color.white;        // hovered / pressed elements
            public static readonly Color Clash = Hex(0xFFE14D, 1f);  // bright yellow: keys also bound elsewhere, and their cards
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

        // Padding on every side of every button (tabs, key bindings and card headers too): square, like the keys.
        public const int ButtonPad = 13;

        // Width the scrollbar column takes (gap + bar): the tab bar and bottom row leave the same space on the right.
        public const float ScrollbarSpace = Gap * 2;

        private static GUISkin _skin;
        public static GUIStyle Window, Label, Bold, Small, Hint, SectionTitle, Button, CardHeader, CardHeaderOpen, CardBody,
            CardOpen, CardHeaderClash, CardHeaderOpenClash, CardOpenClash, KeyButtonClash, Tab, Panel, Slider, SliderThumb, KeyButton, KeyButtonCapture, Flush, LabelWrap, WarningText, Footer, SliderValue;
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

            // IMGUI only uses a style's hover / active / on states when they have a background, so the text-only buttons
            // get one in the window's own colour to make their text colours apply.
            Texture2D none = Solid(Menu.Main);
            Texture2D fill = Solid(Menu.Text), fillHover = Solid(Menu.Hover);

            Window = new GUIStyle
            {
                font = font, fontSize = FontSize + 1, fontStyle = heavy,
                alignment = TextAnchor.UpperLeft, // title on the left, over the tabs' left edge
                padding = new RectOffset(Gap, Gap, 32, Gap),
                contentOffset = new Vector2(0f, -24f),
            };
            Window.normal.background = Solid(Menu.Main);
            Window.normal.textColor = Menu.Text;
            Window.onNormal.background = Window.normal.background;
            Window.onNormal.textColor = Menu.Text;

            Label = new GUIStyle { font = font, fontSize = FontSize, wordWrap = false, padding = new RectOffset(4, 4, 4, 4), margin = new RectOffset(0, 0, 0, 0) };
            Label.normal.textColor = Menu.Text;

            // Wrapping text that grows downwards instead of clipping; padded so it never touches its container's edges.
            LabelWrap = new GUIStyle(Label) { wordWrap = true, padding = new RectOffset(4, 7, 4, 4) };
            WarningText = new GUIStyle(Label) { fontSize = FontSize - 2, wordWrap = true, padding = new RectOffset(7, 7, 1, 7) };
            WarningText.normal.textColor = Menu.Clash;

            Bold = new GUIStyle(Label) { fontStyle = heavy };
            Small = new GUIStyle(Label) { fontSize = FontSize - 2, wordWrap = true };
            Small.normal.textColor = Menu.Text;
            // The hint line at the bottom of the window: as far from the buttons above as from the window's left and
            // bottom edges (which add the window's own Gap).
            Footer = new GUIStyle(Small) { padding = new RectOffset(Gap * 2, Gap * 2, Gap * 3, Gap * 2) };
            SliderValue = new GUIStyle(Label) { alignment = TextAnchor.UpperRight };
            Hint = new GUIStyle(Label) { fontSize = FontSize - 3, wordWrap = true, padding = new RectOffset(9, 7, 1, 5) };
            Hint.normal.textColor = Menu.Text;
            SectionTitle = new GUIStyle(Label) { fontStyle = heavy, fontSize = FontSize + 1, padding = new RectOffset(3, 3, 1, Gap + 1), margin = new RectOffset(0, 0, Gap, Gap) };
            SectionTitle.normal.textColor = Menu.Text;

            // Buttons (tabs and key bindings too): framed by a 1 px border like the cards, white with the text on hover.
            Texture2D buttonFrame = Frame(Menu.Text, 1), buttonFrameHover = Frame(Menu.Hover, 1);
            Button = new GUIStyle
            {
                font = font, fontSize = FontSize, alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(ButtonPad, ButtonPad, ButtonPad, ButtonPad), margin = new RectOffset(0, 0, 0, Gap + 1),
                border = new RectOffset(1, 1, 1, 1),
            };
            Button.normal.background = buttonFrame;
            Button.normal.textColor = Menu.Text;
            Button.hover.background = buttonFrameHover;
            Button.hover.textColor = Menu.Hover;
            Button.active.background = buttonFrameHover;
            Button.active.textColor = Menu.Hover;

            // Key binding buttons: just text, framed only on hover; yellow text when the key is also bound elsewhere
            // (KeyButtonClash); while waiting for a key, filled (KeyButtonCapture).
            KeyButton = new GUIStyle(Button) { margin = new RectOffset(Gap + 1, 0, Gap, Gap) };
            KeyButton.normal.background = null;
            KeyButtonClash = new GUIStyle(KeyButton);
            KeyButtonClash.normal.textColor = Menu.Clash;
            KeyButtonCapture = new GUIStyle(KeyButton);
            KeyButtonCapture.normal.background = fill;
            KeyButtonCapture.normal.textColor = Menu.Main;
            KeyButtonCapture.hover.background = fillHover;
            KeyButtonCapture.hover.textColor = Menu.Main;
            KeyButtonCapture.active.background = fillHover;
            KeyButtonCapture.active.textColor = Menu.Main;

            // Cards: a header button framed by a 1 px border (white on hover). When open, the frame moves out to hold
            // header and body together (CardOpen) and the header itself is unframed. A card with a clashing key has a
            // yellow frame and header text (the ...Clash styles).
            Texture2D cardFrame = Frame(Menu.Text, 1), cardFrameHover = Frame(Menu.Hover, 1), cardFrameClash = Frame(Menu.Clash, 1);
            CardHeader = new GUIStyle(Button)
            {
                alignment = TextAnchor.MiddleLeft, padding = new RectOffset(ButtonPad, ButtonPad, ButtonPad, ButtonPad), wordWrap = true,
                border = new RectOffset(1, 1, 1, 1),
            };
            CardHeader.normal.background = cardFrame;
            CardHeader.hover.background = cardFrameHover;
            CardHeader.active.background = cardFrameHover;
            CardHeaderClash = new GUIStyle(CardHeader);
            CardHeaderClash.normal.background = cardFrameClash;
            CardHeaderClash.normal.textColor = Menu.Clash;
            // Inside the open frame's 1 px, so one less padding keeps the text where it was when closed.
            CardHeaderOpen = new GUIStyle(CardHeader) { padding = new RectOffset(ButtonPad - 1, ButtonPad - 1, ButtonPad - 1, ButtonPad - 1), margin = new RectOffset(0, 0, 0, 0) };
            CardHeaderOpen.normal.background = null;
            CardHeaderOpen.hover.background = none;
            CardHeaderOpen.active.background = none;
            CardHeaderOpenClash = new GUIStyle(CardHeaderOpen);
            CardHeaderOpenClash.normal.textColor = Menu.Clash;
            // Left padding plus the labels' own 4 px lines the key names up with the header text (1 + 12 px in the frame).
            CardBody = new GUIStyle { padding = new RectOffset(8, 12, 0, Gap) };
            // Padded by the frame's 1 px so the header's hover background can't paint over it.
            CardOpen = new GUIStyle { border = new RectOffset(1, 1, 1, 1), padding = new RectOffset(1, 1, 1, 1), margin = new RectOffset(0, 0, 0, Gap + 1) };
            CardOpen.normal.background = cardFrame;
            CardOpenClash = new GUIStyle(CardOpen);
            CardOpenClash.normal.background = cardFrameClash;

            // Tabs and full-width row buttons have no side margins; the window puts explicit gaps between them.
            // Tabs: the selected one filled, its text in the background colour.
            Flush = new GUIStyle(Button) { margin = new RectOffset(0, 0, 0, 0) };
            Tab = new GUIStyle(Button) { fontStyle = heavy, margin = new RectOffset(0, 0, 0, 0) };
            Tab.onNormal.background = fill;
            Tab.onNormal.textColor = Menu.Main;
            Tab.onHover.background = fillHover;
            Tab.onHover.textColor = Menu.Main;
            Tab.onActive.background = fillHover;
            Tab.onActive.textColor = Menu.Main;

            // Sections: no full border, just small 1 px corner marks, with the content padded clear of them. No side
            // margins, so they span the width of the tab bar and the bottom row.
            Panel = new GUIStyle
            {
                padding = new RectOffset(Gap + 1, Gap + 1, Gap + 1, Gap + 1), margin = new RectOffset(0, 0, 0, Gap * 2),
                border = new RectOffset(CornerArm, CornerArm, CornerArm, CornerArm),
            };
            Panel.normal.background = Corners(Menu.Text);

            // A 1 px line through the middle of the slider, the handle its full height. The slider
            // itself is the handle's height (not the line's) so the whole handle takes clicks instead of dragging the window.
            Slider = new GUIStyle { fixedHeight = SliderHeight, margin = new RectOffset(4, 4, 4, 5) };
            Slider.normal.background = HorizontalLine(SliderHeight, Menu.Text);
            SliderThumb = new GUIStyle { fixedWidth = 6f, fixedHeight = SliderHeight };
            SliderThumb.normal.background = fill;
            SliderThumb.hover.background = fillHover;
            SliderThumb.active.background = fillHover;

            // Checkboxes as tall as a capital letter: a 1 px border, filled when on (drawn 1:1 so the border stays 1 px).
            MeasureText(font, FontSize);
            CheckOff = Checkbox(Menu.Main, Menu.Text);
            CheckOffHover = Checkbox(Menu.Main, Menu.Hover);
            CheckOn = Checkbox(Menu.Text, Menu.Text);
            CheckOnHover = Checkbox(Menu.Hover, Menu.Hover);

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
            _skin.verticalScrollbarThumb = new GUIStyle { fixedWidth = Gap };
            _skin.verticalScrollbarThumb.normal.background = fill;
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

        // The window's colour inside a border of the given width; 9-sliced with a style border of the same width so the
        // edge keeps its width at any size.
        private static Texture2D Frame(Color c, int width)
        {
            int n = width * 2 + 1;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    t.SetPixel(x, y, x == width && y == width ? Menu.Main : c);
            t.Apply();
            return t;
        }

        // Length of each arm of a section's corner marks.
        private const int CornerArm = 8;

        // The window's colour with 1 px L-shaped marks in the four corners. 9-sliced with a style border of CornerArm:
        // the corners are drawn as they are and the edges between them (all background) stretch.
        private static Texture2D Corners(Color c)
        {
            int n = CornerArm * 2 + 1;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    bool edgeX = x == 0 || x == n - 1, edgeY = y == 0 || y == n - 1;
                    bool nearX = x < CornerArm || x > n - 1 - CornerArm, nearY = y < CornerArm || y > n - 1 - CornerArm;
                    t.SetPixel(x, y, (edgeY && nearX) || (edgeX && nearY) ? c : Menu.Main);
                }
            t.Apply();
            return t;
        }

        private const int SliderHeight = 17;

        // The window's colour, with a 1 px line across the middle row; drawn at its own height so the line stays 1 px.
        private static Texture2D HorizontalLine(int height, Color c)
        {
            var t = new Texture2D(2, height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < height; y++)
                for (int x = 0; x < 2; x++)
                    t.SetPixel(x, y, y == height / 2 ? c : Menu.Main);
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

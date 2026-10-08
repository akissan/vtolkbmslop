using UnityEngine;

namespace VirtualJoystick
{
    // Look of the settings window: dark grey base, borderless grey panels with hard corners and padding (stepped
    // shades instead of outlines), light grey-blue text, saturated sky-blue accent. Built once from scratch.
    internal static class Theme
    {
        public static readonly Color Main = Hex(0x1C2024, 0.95f);         // window background
        public static readonly Color Section = Hex(0x262B31, 0.85f);      // section / group panels
        public static readonly Color Control = Hex(0x323941, 0.95f);      // buttons, card headers and bodies
        public static readonly Color ControlHover = Hex(0x3B434C, 0.97f);
        public static readonly Color ControlActive = Hex(0x444D57, 1f);
        public static readonly Color KeyBg = Hex(0x14171A, 0.95f);        // key binding buttons: darker, no border
        public static readonly Color KeyHover = Hex(0x1D2126, 0.97f);
        public static readonly Color Text = Hex(0xC9D6E2, 1f);            // grey with a little light blue
        public static readonly Color Muted = Hex(0x8996A3, 1f);
        public static readonly Color Accent = Hex(0x29B6FF, 1f);          // saturated sky blue
        public static readonly Color CheckOffFill = Hex(0x14171A, 1f);
        public static readonly Color CheckBorder = Hex(0x6A747E, 1f);
        public static readonly Color Warning = Hex(0xF2C14E, 1f);

        public const int FontSize = 14;

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
                if (_skin == null)
                    Build();
                return _skin;
            }
        }

        private static void Build()
        {
            Font font = GUI.skin.font;
            _skin = ScriptableObject.CreateInstance<GUISkin>();
            _skin.hideFlags = HideFlags.HideAndDontSave;
            _skin.font = font;

            Texture2D control = Solid(Control), controlHover = Solid(ControlHover), controlActive = Solid(ControlActive);
            Texture2D accent = Solid(Accent), accentHover = Solid(Color.Lerp(Accent, Color.white, 0.15f));
            Color onAccentText = Hex(0x0B1A24, 1f);

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

            // Thin dark track, blue handle overflowing it vertically.
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
            _skin.verticalScrollbar.normal.background = Solid(Hex(0x14171A, 0.7f));
            _skin.verticalScrollbarThumb = new GUIStyle { fixedWidth = Gap };
            _skin.verticalScrollbarThumb.normal.background = Solid(Hex(0x4A535D, 1f));
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

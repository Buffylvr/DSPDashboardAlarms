using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DashboardAlarms
{
    /// <summary>
    /// An IMGUI skin themed after the game's UI: the game's own font, dark translucent navy panels with a
    /// title band, cyan accents and borders, flat buttons with hover/pressed states, game-style
    /// checkboxes and dark input fields. Built lazily inside OnGUI (GUI.skin is only valid there).
    /// </summary>
    internal static class GameSkin
    {
        // palette (taken from the look of the game's panels)
        public static readonly Color Text = new Color(0.86f, 0.92f, 0.96f, 1f);
        public static readonly Color TextDim = new Color(0.62f, 0.70f, 0.76f, 1f);
        public static readonly Color Accent = new Color(0.38f, 0.84f, 1f, 1f);
        private static readonly Color Panel = new Color(0.06f, 0.11f, 0.16f, 0.94f);
        private static readonly Color TitleBand = new Color(0.11f, 0.19f, 0.26f, 0.97f);
        private static readonly Color Edge = new Color(0.38f, 0.84f, 1f, 0.35f);
        private static readonly Color ButtonFill = new Color(0.16f, 0.25f, 0.33f, 0.95f);
        private static readonly Color ButtonHover = new Color(0.22f, 0.35f, 0.45f, 0.98f);
        private static readonly Color ButtonPressed = new Color(0.12f, 0.42f, 0.56f, 1f);
        private static readonly Color SelectedFill = new Color(0.14f, 0.50f, 0.68f, 1f);
        private static readonly Color FieldFill = new Color(0.03f, 0.07f, 0.10f, 0.95f);
        private static readonly Color FieldEdge = new Color(0.30f, 0.40f, 0.48f, 1f);

        private static GUISkin skin;
        private static readonly List<Texture2D> textures = new List<Texture2D>();

        public static GUIStyle SelectedButton { get; private set; }
        public static GUIStyle Check { get; private set; }
        public static GUIStyle DimLabel { get; private set; }
        public static GUIStyle Header { get; private set; }
        public static Texture2D CheckOn { get; private set; }
        public static Texture2D CheckOff { get; private set; }
        public static Texture2D BannerFill { get; private set; }
        public static Font GameFont { get; private set; }

        public static GUISkin Skin
        {
            get
            {
                if (skin == null)
                {
                    Build();
                }
                return skin;
            }
        }

        private static void Build()
        {
            skin = UnityEngine.Object.Instantiate(GUI.skin);
            skin.hideFlags = HideFlags.HideAndDontSave;
            GameFont = FindGameFont();
            if (GameFont != null)
            {
                skin.font = GameFont;
            }
            skin.settings.cursorColor = Accent;
            skin.settings.selectionColor = new Color(Accent.r, Accent.g, Accent.b, 0.35f);

            // window: 1px cyan-tinted edge, a title band with a cyan rule under it, dark body
            const int band = 26;
            GUIStyle win = skin.window;
            Texture2D winTex = Make(16, 40, (x, top, w, h) =>
            {
                if (x == 0 || x == w - 1 || top == 0 || top == h - 1)
                {
                    return Edge;
                }
                if (top < band)
                {
                    return TitleBand;
                }
                if (top == band)
                {
                    return new Color(Accent.r, Accent.g, Accent.b, 0.7f);
                }
                return Panel;
            });
            SetAll(win, winTex, Accent);
            win.border = new RectOffset(2, 2, band + 2, 2);
            win.padding = new RectOffset(12, 12, band + 10, 12);
            win.contentOffset = new Vector2(0, -(band + 4));
            win.alignment = TextAnchor.UpperCenter;
            win.fontSize = 15;
            win.fontStyle = FontStyle.Bold;

            // labels
            skin.label.normal.textColor = Text;
            skin.label.fontSize = 14;
            DimLabel = new GUIStyle(skin.label) { wordWrap = true, fontSize = 13 };
            DimLabel.normal.textColor = TextDim;
            Header = new GUIStyle(skin.label) { fontStyle = FontStyle.Bold, fontSize = 14, margin = new RectOffset(4, 4, 6, 2) };
            Header.normal.textColor = Accent;

            // buttons
            GUIStyle button = skin.button;
            button.normal.background = Boxed(ButtonFill, Edge);
            button.hover.background = Boxed(ButtonHover, new Color(Accent.r, Accent.g, Accent.b, 0.7f));
            button.active.background = Boxed(ButtonPressed, Accent);
            button.focused.background = button.normal.background;
            button.onNormal.background = button.normal.background;
            button.onHover.background = button.hover.background;
            button.onActive.background = button.active.background;
            button.normal.textColor = Text;
            button.hover.textColor = Color.white;
            button.active.textColor = Color.white;
            button.focused.textColor = Text;
            button.onNormal.textColor = Text;
            button.onHover.textColor = Color.white;
            button.onActive.textColor = Color.white;
            button.border = new RectOffset(2, 2, 2, 2);
            button.padding = new RectOffset(10, 10, 5, 5);
            button.fontSize = 14;
            button.alignment = TextAnchor.MiddleCenter;

            SelectedButton = new GUIStyle(button) { fontStyle = FontStyle.Bold };
            Texture2D selected = Boxed(SelectedFill, Accent);
            SelectedButton.normal.background = selected;
            SelectedButton.hover.background = Boxed(new Color(0.18f, 0.58f, 0.78f, 1f), Accent);
            SelectedButton.active.background = selected;
            SelectedButton.normal.textColor = Color.white;
            SelectedButton.hover.textColor = Color.white;

            // text fields
            GUIStyle field = skin.textField;
            Texture2D fieldTex = Boxed(FieldFill, FieldEdge);
            field.normal.background = fieldTex;
            field.hover.background = Boxed(FieldFill, new Color(Accent.r, Accent.g, Accent.b, 0.6f));
            field.focused.background = Boxed(FieldFill, Accent);
            field.active.background = field.focused.background;
            field.onNormal.background = fieldTex;
            field.normal.textColor = Text;
            field.hover.textColor = Text;
            field.focused.textColor = Color.white;
            field.border = new RectOffset(2, 2, 2, 2);
            field.padding = new RectOffset(6, 6, 4, 4);
            field.fontSize = 14;

            // box (used tinted for the banner, so it is white and takes GUI.backgroundColor)
            BannerFill = Boxed(Color.white, new Color(1f, 0.55f, 0.5f, 1f));
            SetAll(skin.box, BannerFill, Text);
            skin.box.border = new RectOffset(2, 2, 2, 2);

            // checkboxes: drawn as a label with a box image in front of the text
            CheckOff = Make(16, 16, (x, top, w, h) => Edge1(x, top, w, h) ? Accent : FieldFill);
            CheckOn = Make(16, 16, (x, top, w, h) =>
                Edge1(x, top, w, h) ? Accent : (x >= 4 && x <= 11 && top >= 4 && top <= 11 ? Accent : FieldFill));
            Check = new GUIStyle(skin.label)
            {
                imagePosition = ImagePosition.ImageLeft,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(0, 4, 3, 3),
            };
            Check.hover.textColor = Color.white;

            // scrollbars: thin, flat, no arrow buttons
            skin.verticalScrollbar.normal.background = Make(4, 4, (x, top, w, h) => FieldFill);
            skin.verticalScrollbar.fixedWidth = 10;
            skin.verticalScrollbar.border = new RectOffset(0, 0, 0, 0);
            skin.verticalScrollbarThumb.normal.background = Boxed(ButtonHover, Edge);
            skin.verticalScrollbarThumb.fixedWidth = 10;
            skin.verticalScrollbarThumb.border = new RectOffset(2, 2, 2, 2);
            skin.verticalScrollbarUpButton.fixedHeight = 0;
            skin.verticalScrollbarDownButton.fixedHeight = 0;
            skin.scrollView.normal.background = null;
        }

        /// <summary>A game-style checkbox: returns the new value.</summary>
        public static bool Toggle(bool value, string label, params GUILayoutOption[] options)
        {
            var content = new GUIContent(string.IsNullOrEmpty(label) ? "" : "  " + label, value ? CheckOn : CheckOff);
            return GUILayout.Button(content, Check, options) ? !value : value;
        }

        /// <summary>The font most used by the game's UI (its own font), or null to keep the default.</summary>
        private static Font FindGameFont()
        {
            try
            {
                if (UIRoot.instance == null)
                {
                    return null;
                }
                var counts = new Dictionary<Font, int>();
                foreach (Text t in UIRoot.instance.GetComponentsInChildren<Text>(true))
                {
                    if (t.font == null || t.font.name == "Arial")
                    {
                        continue;
                    }
                    counts.TryGetValue(t.font, out int n);
                    counts[t.font] = n + 1;
                }
                Font best = null;
                int bestN = 0;
                foreach (KeyValuePair<Font, int> kv in counts)
                {
                    if (kv.Value > bestN)
                    {
                        best = kv.Key;
                        bestN = kv.Value;
                    }
                }
                if (best != null)
                {
                    Plugin.Log?.LogInfo($"Alarm windows use the game font '{best.name}'");
                }
                return best;
            }
            catch (Exception e)
            {
                Plugin.Log?.LogWarning("Could not find the game's UI font: " + e.Message);
                return null;
            }
        }

        private static bool Edge1(int x, int top, int w, int h) => x == 0 || x == w - 1 || top == 0 || top == h - 1;

        private static void SetAll(GUIStyle s, Texture2D tex, Color text)
        {
            s.normal.background = tex;
            s.hover.background = tex;
            s.active.background = tex;
            s.focused.background = tex;
            s.onNormal.background = tex;
            s.onHover.background = tex;
            s.onActive.background = tex;
            s.onFocused.background = tex;
            s.normal.textColor = text;
            s.hover.textColor = text;
            s.active.textColor = text;
            s.focused.textColor = text;
            s.onNormal.textColor = text;
            s.onHover.textColor = text;
            s.onActive.textColor = text;
            s.onFocused.textColor = text;
        }

        /// <summary>8x8 fill with a 1px edge (used 9-sliced with border 2).</summary>
        private static Texture2D Boxed(Color fill, Color edge) => Make(8, 8, (x, top, w, h) => Edge1(x, top, w, h) ? edge : fill);

        /// <summary>Makes a texture from a colour function of (x, row from top, width, height).</summary>
        private static Texture2D Make(int w, int h, Func<int, int, int, int, Color> colorAt)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var pixels = new Color[w * h];
            for (int top = 0; top < h; top++)
            {
                for (int x = 0; x < w; x++)
                {
                    pixels[(h - 1 - top) * w + x] = colorAt(x, top, w, h); // texture rows start at the bottom
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            textures.Add(tex);
            return tex;
        }
    }
}

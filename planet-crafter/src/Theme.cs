// GamesOMG Teleport 0.3.0: how the panel looks (2026-11-19: more appealing, branded in a small way
// with our colours and logo; ChatGPT's review: "strong typography, generous rows, clear selection states,
// and a small gear-G signature. Use orange for the main action and blue for focus").
//   · brand orange #f08000 for the main action, brand blue #3be3ff for focus, exactly (0.2.x used a near-orange #FF9E33)
//   · a dark panel; background opacity is a setting, text and focus stay solid at any opacity
//   · Manrope (OFL, the site's own font) for every word; a glyph Manrope lacks falls back to the game's font per label
//   · one small G Emblem mark in the header; icons drawn as tinted textures, so nothing depends on a font having ✓ or ★
//   · comfortable rows 44 px at 1080p, compact 32; everything scales with the screen height and the Text size setting
// 0.3.2 (player feedback 2026-11-21 08:19: "the font is hard to read now", "should we focus on keeping it more compact?"): the game's
//   own IMGUI font again, as 0.2.6 drew it (GUI.skin's font, bold by FontStyle), not Manrope; Manrope is no longer embedded.
//   Compact (the default now) also tightens buttons, fields and tabs, not only the rows.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GamesOMG.PlanetCrafterTeleport
{
    internal enum Icon
    {
        Check, ChevronRight, ChevronDown, Star, StarFilled, Gear, Close, Return, Clipboard, Save, Map, Trash, Search, Undo,
        Pencil, Go, Clock, Warn, Found, Pad, Keyboard, Folder, Help,
    }

    internal sealed class Theme
    {
        public static readonly Color Orange = Hex(0xf08000), Blue = Hex(0x3be3ff);
        // 0.3.3 (player feedback 2026-11-21 13:00, "the font background is almost the same color as the background"): two text tiers,
        // Ink and a lighter Muted; Faint is for icons only now. Every text style measures >= 4.5:1 (WCAG) on its real plate with
        // the panel at the 85 % floor over a white scene, the worst case (lowest: Small on a chosen chip, 5.18); CHANGELOG 0.3.3.
        public static readonly Color Ink = Hex(0xeef1f5), Muted = Hex(0xbdc6d2), Faint = Hex(0x8f99a8);
        public static readonly Color Panel = Hex(0x0e1117), Surface = Hex(0x171b23), Raised = Hex(0x20262f), Line = Hex(0x2b3240);
        public static readonly Color Good = Hex(0x4fd08a), Bad = Hex(0xff6b5e), BadText = Hex(0xff8a80), OrangeDeep = Hex(0xb85f00);
        public static readonly Color Note = Hex(0xfacc15);   // 0.3.2: a row's note and its marker
        public static readonly Dictionary<string, Color> SpotColour = new Dictionary<string, Color>
        {
            ["orange"] = Orange, ["red"] = Hex(0xef4444), ["yellow"] = Hex(0xfacc15), ["green"] = Hex(0x22c55e),
            ["cyan"] = Hex(0x22d3ee), ["blue"] = Hex(0x3b82f6), ["purple"] = Hex(0xa855f7), ["white"] = Hex(0xf1f5f9),
        };

        public static Color Hex(int rgb) => new Color((rgb >> 16 & 255) / 255f, (rgb >> 8 & 255) / 255f, (rgb & 255) / 255f);
        public static Color A(Color c, float a) => new Color(c.r, c.g, c.b, a);

        private readonly string cacheDir;
        private readonly Action<string> log;
        private Texture2D icons, mark, white;
        private readonly Dictionary<int, Texture2D> solids = new Dictionary<int, Texture2D>();

        public Theme(string cacheDir, Action<string> log) { this.cacheDir = cacheDir; this.log = log ?? (_ => { }); }

        public bool FontLoaded => false;   // 0.3.2: no font of our own
        public float S { get; private set; } = 1;   // scale: screen height x the Text size setting
        public float Row { get; private set; } = 32;
        public float ButtonH { get; private set; } = 32;
        public float ButtonSubH { get; private set; } = 42;
        public float TabH { get; private set; } = 30;
        public bool Compact { get; private set; }
        public float Opacity { get; private set; } = .96f;
        // 0.3.3: the lowest background opacity. Below it, a bright scene shows through enough that even white text drops under
        // 4.5:1 (0.3.2's 60 % floor: 3.99 for Ink, 1.04 for the headings)
        public static readonly int MinOpacity = 85;

        // ── embedded files ──
        private static byte[] Resource(string name)
        {
            using (var stream = typeof(Theme).Assembly.GetManifestResourceStream(name))
            {
                if (stream == null) return null;
                var bytes = new byte[stream.Length];
                int read = 0;
                while (read < bytes.Length) { int k = stream.Read(bytes, read, bytes.Length - read); if (k <= 0) break; read += k; }
                return bytes;
            }
        }

        // 0.3.2: every label uses the game's own IMGUI font (GUI.skin's), the one 0.2.6 used; a strong style is bold by
        // FontStyle. FontFor stays so every caller keeps one place to ask; it answers null = the skin's font.
        public Font FontFor(string text, bool strong) => null;

        public Texture2D Solid(Color c)
        {
            int key = Mathf.RoundToInt(c.r * 255) << 24 | Mathf.RoundToInt(c.g * 255) << 16 | Mathf.RoundToInt(c.b * 255) << 8 | Mathf.RoundToInt(c.a * 255);
            if (solids.TryGetValue(key, out var t) && t != null) return t;
            t = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return solids[key] = t;
        }

        private Texture2D Png(string name)
        {
            var bytes = Resource(name);
            if (bytes == null) return null;
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            return t.LoadImage(bytes) ? t : null;
        }

        public Texture2D Mark => mark != null ? mark : (mark = Png("mark.png"));

        public void DrawIcon(Rect r, Icon icon, Color tint)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (icons == null) icons = Png("icons.png");
            if (icons == null) return;
            int n = Enum.GetValues(typeof(Icon)).Length;
            var old = GUI.color;
            GUI.color = tint;
            GUI.DrawTextureWithTexCoords(r, icons, new Rect((int)icon / (float)n, 0, 1f / n, 1));
            GUI.color = old;
        }

        public void Fill(Rect r, Color c)
        {
            if (Event.current.type != EventType.Repaint) return;
            GUI.DrawTexture(r, white != null ? white : (white = Solid(Color.white)), ScaleMode.StretchToFill, true, 0, c, 0, 0);
        }

        // a rounded plate (buttons, rows, chips): Unity's DrawTexture draws the corner radius itself
        public void Plate(Rect r, Color c, float radius)
        {
            if (Event.current.type != EventType.Repaint || c.a <= 0) return;
            GUI.DrawTexture(r, white != null ? white : (white = Solid(Color.white)), ScaleMode.StretchToFill, true, 0, c, Vector4.zero, radius);
        }

        // a solid rounded outline (focus): never faded by the background opacity
        public void Outline(Rect r, Color c, float w, float radius = 0)
        {
            if (Event.current.type != EventType.Repaint) return;
            GUI.DrawTexture(r, white != null ? white : (white = Solid(Color.white)), ScaleMode.StretchToFill, true, 0, c, new Vector4(w, w, w, w), radius);
        }

        // ── styles, rebuilt when the scale or a look setting changes ──
        public GUIStyle Title, Sub, Text, TextStrong, Small, SmallWrap, Head, Meta, Button, Primary, Ghost, Tab, TabOn, Field, Placeholder,
            RowName, RowNameMuted, NoteText, Status, StatusBad, Prompt, Key, Danger, Wrap;
        private float builtFor = -1;
        private bool builtCompact;

        public void Build(float textSizePercent, bool compact, float opacityPercent)
        {
            float s = Mathf.Clamp(Screen.height / 1080f, .7f, 3f) * Mathf.Clamp(textSizePercent, 70, 200) / 100f;
            Opacity = Mathf.Clamp(opacityPercent, MinOpacity, 100) / 100f;
            if (Title != null && Mathf.Approximately(s, builtFor) && compact == builtCompact) return;
            builtFor = s;
            builtCompact = compact;
            S = s;
            Compact = compact;
            Row = Mathf.Round((compact ? 32 : 44) * s);
            ButtonH = Mathf.Round((compact ? 32 : 38) * s);
            ButtonSubH = Mathf.Round((compact ? 42 : 46) * s);
            TabH = Mathf.Round((compact ? 30 : 36) * s);
            Title = Style(19, Ink, true);
            Sub = Style(12.5f, Muted, false);
            Text = Style(14.5f, Ink, false);
            TextStrong = Style(14.5f, Ink, true);
            Small = Style(12.5f, Muted, false);
            SmallWrap = Style(12.5f, Muted, false, true);
            Wrap = Style(14f, Ink, false, true);
            Head = Style(11.5f, Muted, true);
            Head.margin = new RectOffset(0, 0, Px(10), Px(3));
            Meta = Style(12f, Muted, false);
            RowName = Style(15f, Ink, false);   // 0.3.2: regular weight, as 0.2.6's rows
            RowNameMuted = Style(15f, Muted, false);   // 0.3.3: was Faint, 3.1:1
            NoteText = Style(12.5f, Note, false);
            Status = Style(13f, Good, false, true);
            StatusBad = Style(13f, BadText, false, true);
            Prompt = Style(12f, Muted, false);
            Key = Style(11f, Ink, true);
            Key.alignment = TextAnchor.MiddleCenter;
            Key.normal.background = Solid(Raised);
            Key.border = new RectOffset(0, 0, 0, 0);
            Key.padding = new RectOffset(Px(6), Px(6), Px(1), Px(1));
            Button = Btn(14f, Ink, Raised, Hex(0x2a313c), false);
            Primary = Btn(14.5f, Hex(0x14100a), Orange, Hex(0xff9a2e), true);
            Danger = Btn(14f, Hex(0x1a0d0b), Bad, Hex(0xff8a80), true);
            Ghost = Btn(13.5f, Muted, null, Raised, false);
            Tab = Btn(14f, Muted, null, Raised, true);
            TabOn = Btn(14f, Ink, Surface, Surface, true);
            Field = Style(14.5f, Ink, false);
            Field.normal.background = Field.hover.background = Solid(Surface);
            Field.focused.background = Field.active.background = Solid(Raised);
            Field.padding = new RectOffset(Px(10), Px(10), 0, 0);
            Field.alignment = TextAnchor.MiddleLeft;
            Field.fixedHeight = Mathf.Round((compact ? 30 : 36) * s);
            Field.clipping = TextClipping.Clip;
            Placeholder = Style(14.5f, Muted, false);
            Placeholder.padding = Field.padding;
            Placeholder.alignment = TextAnchor.MiddleLeft;
        }

        public int Px(float v) => Mathf.RoundToInt(v * S);

        private GUIStyle Style(float size, Color colour, bool strong, bool wrap = false)
        {
            var g = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(8, Mathf.RoundToInt(size * S)), fontStyle = strong ? FontStyle.Bold : FontStyle.Normal, wordWrap = wrap, richText = false,
                padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0), clipping = TextClipping.Clip,
            };
            g.normal.textColor = g.hover.textColor = g.active.textColor = g.focused.textColor = colour;
            g.normal.background = g.hover.background = g.active.background = g.focused.background = null;
            return g;
        }

        private GUIStyle Btn(float size, Color text, Color? bg, Color hover, bool strong)
        {
            var g = Style(size, text, strong);
            g.alignment = TextAnchor.MiddleCenter;
            g.normal.background = bg.HasValue ? Solid(bg.Value) : null;
            g.hover.background = g.active.background = Solid(hover);
            g.padding = new RectOffset(Px(10), Px(10), 0, 0);
            g.fixedHeight = ButtonH;
            return g;
        }
    }
}

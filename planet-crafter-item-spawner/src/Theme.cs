// GamesOMG Item Spawner: how the panel looks. The same look as GamesOMG Teleport 0.3.2 (the game's own IMGUI font, the dark plates,
// the gear-G mark and icon strip, orange for the main action, blue for focus, compact sizes that scale with the screen
// height), with two changes from the start: the panel is opaque, and every text colour is one Palette.Pairs proves ≥ 4.5:1.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace GamesOMG.PlanetCrafterSpawn
{
    // the icon strip's order (data/icons.png, shared with GamesOMG Teleport): never reorder
    internal enum Icon
    {
        Check, ChevronRight, ChevronDown, Star, StarFilled, Gear, Close, Return, Clipboard, Save, Map, Trash, Search, Undo,
        Pencil, Go, Clock, Warn, Found, Pad, Keyboard, Folder, Help,
    }

    internal sealed class Theme
    {
        public static Color C(int rgb) => new Color((rgb >> 16 & 255) / 255f, (rgb >> 8 & 255) / 255f, (rgb & 255) / 255f);
        public static Color A(Color c, float a) => new Color(c.r, c.g, c.b, a);

        public static readonly Color Orange = C(Palette.Orange), Blue = C(Palette.Blue), Ink = C(Palette.Ink), Muted = C(Palette.Muted);
        public static readonly Color Panel = C(Palette.Panel), Surface = C(Palette.Surface), Raised = C(Palette.Raised), Hover = C(Palette.Hover);
        public static readonly Color Line = C(Palette.Line), Good = C(Palette.Good), Bad = C(Palette.Bad), Selected = C(Palette.Selected);

        private Texture2D icons, mark, white;
        private readonly Dictionary<int, Texture2D> solids = new Dictionary<int, Texture2D>();

        public float S { get; private set; } = 1;
        public float ButtonH { get; private set; } = 32;
        public float TabH { get; private set; } = 30;
        public float CellW { get; private set; } = 76;      // a grid cell's width
        public float CellH { get; private set; } = 92;     // icon + two lines of name
        public float IconSize { get; private set; } = 48;

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

        public Texture2D Solid(Color c)
        {
            int key = Mathf.RoundToInt(c.r * 255) << 24 | Mathf.RoundToInt(c.g * 255) << 16 | Mathf.RoundToInt(c.b * 255) << 8 | Mathf.RoundToInt(c.a * 255);
            if (solids.TryGetValue(key, out var t) && t != null) return t;
            t = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return solids[key] = t;
        }

        private static Texture2D Png(string name)
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

        // an item's own icon (the game's Sprite, often packed in an atlas), fitted into r keeping its shape
        public static void DrawSprite(Rect r, Sprite sprite)
        {
            if (sprite == null || Event.current.type != EventType.Repaint) return;
            var tex = sprite.texture;
            if (tex == null) return;
            Rect px;
            try { px = sprite.textureRect; } catch (Exception) { px = sprite.rect; }   // tight-packed sprites refuse textureRect
            if (px.width <= 0 || px.height <= 0) return;
            float k = Mathf.Min(r.width / px.width, r.height / px.height);
            var fit = new Rect(r.x + (r.width - px.width * k) / 2, r.y + (r.height - px.height * k) / 2, px.width * k, px.height * k);
            GUI.DrawTextureWithTexCoords(fit, tex, new Rect(px.x / tex.width, px.y / tex.height, px.width / tex.width, px.height / tex.height));
        }

        public void Fill(Rect r, Color c)
        {
            if (Event.current.type != EventType.Repaint) return;
            GUI.DrawTexture(r, white != null ? white : (white = Solid(Color.white)), ScaleMode.StretchToFill, true, 0, c, 0, 0);
        }

        public void Plate(Rect r, Color c, float radius)
        {
            if (Event.current.type != EventType.Repaint || c.a <= 0) return;
            GUI.DrawTexture(r, white != null ? white : (white = Solid(Color.white)), ScaleMode.StretchToFill, true, 0, c, Vector4.zero, radius);
        }

        public void Outline(Rect r, Color c, float w, float radius = 0)
        {
            if (Event.current.type != EventType.Repaint) return;
            GUI.DrawTexture(r, white != null ? white : (white = Solid(Color.white)), ScaleMode.StretchToFill, true, 0, c, new Vector4(w, w, w, w), radius);
        }

        // ── styles (Palette.Pairs names every one, with the plates it sits on) ──
        public GUIStyle Title, Sub, Text, TextStrong, Small, Head, Cell, Status, StatusBad, Prompt, Key, Button, Primary, Tab, TabOn, Field, Placeholder, Info;
        private float builtFor = -1;

        public void Build(float textSizePercent)
        {
            float s = Mathf.Clamp(Screen.height / 1080f, .7f, 3f) * Mathf.Clamp(textSizePercent, 70, 200) / 100f;
            if (Title != null && Mathf.Approximately(s, builtFor)) return;
            builtFor = s;
            S = s;
            ButtonH = Mathf.Round(32 * s);
            TabH = Mathf.Round(30 * s);
            CellW = Mathf.Round(76 * s);
            CellH = Mathf.Round(90 * s);
            IconSize = Mathf.Round(46 * s);
            Title = Style(19, Ink, true);
            Sub = Style(12.5f, Muted, false);
            Text = Style(14.5f, Ink, false);
            TextStrong = Style(15f, Ink, true);
            Small = Style(12.5f, Muted, false);
            Info = Style(12.5f, Ink, false, true);    // 0.2.0: the selected item's stat line (the description is Small)
            Head = Style(11.5f, Muted, true);
            Cell = Style(11.5f, Ink, false, true);
            Cell.alignment = TextAnchor.UpperCenter;
            Status = Style(13f, Good, false, true);
            StatusBad = Style(13f, Bad, false, true);
            Prompt = Style(12f, Muted, false);
            Key = Style(11f, Ink, true);
            Key.alignment = TextAnchor.MiddleCenter;
            Key.padding = new RectOffset(Px(6), Px(6), Px(1), Px(1));
            Button = Btn(14f, Ink, Raised, Hover, false);
            Primary = Btn(14.5f, C(Palette.OnOrange), Orange, C(Palette.OrangeHover), true);
            Tab = Btn(13.5f, Muted, null, Hover, true);
            TabOn = Btn(13.5f, Ink, Surface, Surface, true);
            TabOn.fixedHeight = Tab.fixedHeight = TabH;
            Field = Style(14.5f, Ink, false);
            Field.normal.background = Field.hover.background = Solid(Surface);
            Field.focused.background = Field.active.background = Solid(Raised);
            Field.padding = new RectOffset(Px(34), Px(10), 0, 0);   // room for the search glyph
            Field.alignment = TextAnchor.MiddleLeft;
            Field.fixedHeight = Mathf.Round(32 * s);
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

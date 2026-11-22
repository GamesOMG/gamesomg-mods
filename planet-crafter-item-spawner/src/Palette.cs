// GamesOMG Item Spawner: the colours, as numbers, and every text style on every plate it is drawn on. Unity-free, so the tests
// measure the WCAG contrast of each pair as it ships (a lesson from the teleport panel: grey text on a dark plate was hard
// to read). The floor is 4.5:1 for every pair, from the first build.
//   · the panel is OPAQUE (no background-opacity setting): a see-through panel puts the game's sky behind the text, and no
//     colour pair can promise a ratio over a scene it can't see
//   · the teleport panel's Faint (#6f7a89, 3.5:1 on Raised) is not used for any text here; it draws lines only
//   · same brand colours and plates as GamesOMG Teleport: orange #f08000 for the main action, blue #3be3ff for focus
using System;
using System.Collections.Generic;

namespace GamesOMG.PlanetCrafterSpawn
{
    internal static class Palette
    {
        public const int Orange = 0xf08000, OrangeHover = 0xff9a2e, Blue = 0x3be3ff;
        public const int Ink = 0xeef1f5, Muted = 0xa3adbb, Line = 0x2b3240;
        public const int Panel = 0x0e1117, Surface = 0x171b23, Raised = 0x20262f, Hover = 0x2a313c;
        public const int Good = 0x4fd08a, Bad = 0xff6b5e, OnOrange = 0x14100a;
        public const float SelectedAlpha = .22f;   // the selected cell: orange at 22 % over Surface

        public static int Selected => Blend(Orange, SelectedAlpha, Surface);

        public const double Floor = 4.5;

        // style, text colour, plate. Theme.Build makes exactly these styles (tests/contrast_test.py checks both ways),
        // and the panel draws each only on the plates listed here.
        public static readonly string[][] Pairs =
        {
            new[] { "Title", "Ink", "Panel" },
            new[] { "Sub", "Muted", "Panel" },
            new[] { "Text", "Ink", "Panel" }, new[] { "Text", "Ink", "Surface" },
            new[] { "TextStrong", "Ink", "Surface" },
            new[] { "Small", "Muted", "Panel" }, new[] { "Small", "Muted", "Surface" },
            new[] { "Info", "Ink", "Surface" },
            new[] { "Head", "Muted", "Panel" },
            new[] { "Cell", "Ink", "Surface" }, new[] { "Cell", "Ink", "Hover" }, new[] { "Cell", "Ink", "Selected" },
            new[] { "Status", "Good", "Surface" }, new[] { "Status", "Good", "Panel" },
            new[] { "StatusBad", "Bad", "Surface" }, new[] { "StatusBad", "Bad", "Panel" },
            new[] { "Prompt", "Muted", "Panel" },
            new[] { "Key", "Ink", "Raised" },
            new[] { "Button", "Ink", "Raised" }, new[] { "Button", "Ink", "Hover" },
            new[] { "Primary", "OnOrange", "Orange" }, new[] { "Primary", "OnOrange", "OrangeHover" },
            new[] { "Tab", "Muted", "Panel" }, new[] { "Tab", "Muted", "Hover" },
            new[] { "TabOn", "Ink", "Surface" },
            new[] { "Field", "Ink", "Surface" }, new[] { "Field", "Ink", "Raised" },
            new[] { "Placeholder", "Muted", "Surface" }, new[] { "Placeholder", "Muted", "Raised" },
        };

        public static int Named(string name)
        {
            switch (name)
            {
                case "Orange": return Orange;
                case "OrangeHover": return OrangeHover;
                case "Blue": return Blue;
                case "Ink": return Ink;
                case "Muted": return Muted;
                case "Line": return Line;
                case "Panel": return Panel;
                case "Surface": return Surface;
                case "Raised": return Raised;
                case "Hover": return Hover;
                case "Good": return Good;
                case "Bad": return Bad;
                case "OnOrange": return OnOrange;
                case "Selected": return Selected;
                default: throw new ArgumentException("no colour " + name);
            }
        }

        // WCAG 2.x relative luminance and contrast ratio
        public static double Luminance(int rgb)
        {
            double Lin(int c) { double s = c / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
            return 0.2126 * Lin(rgb >> 16 & 255) + 0.7152 * Lin(rgb >> 8 & 255) + 0.0722 * Lin(rgb & 255);
        }

        public static double Ratio(int a, int b)
        {
            double la = Luminance(a), lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        public static int Blend(int fg, float alpha, int bg)
        {
            int ch(int shift) => (int)Math.Round((fg >> shift & 255) * alpha + (bg >> shift & 255) * (1 - alpha));
            return ch(16) << 16 | ch(8) << 8 | ch(0);
        }

        // every pair with its ratio, worst first (the report's table)
        public static List<KeyValuePair<string, double>> Table()
        {
            var rows = new List<KeyValuePair<string, double>>();
            foreach (var p in Pairs)
                rows.Add(new KeyValuePair<string, double>(p[0] + ": " + p[1] + " on " + p[2], Ratio(Named(p[1]), Named(p[2]))));
            rows.Sort((x, y) => x.Value.CompareTo(y.Value));
            return rows;
        }
    }
}

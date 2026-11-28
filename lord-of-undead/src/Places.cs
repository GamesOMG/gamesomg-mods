// Places: every pin of the GamesOMG Lord of Undead map that has a landing, embedded in the DLL (data/places.tsv, written
// by the site's tools/lord-of-undead-map/tp/landings.py). Pure C#: tests/ reads the same file.
// Search: what F7's box finds while you type. Every word you type must start a word of the place's name, its layer, the
// place it is near or its region, in any order; the best matches first. Lower-case invariant, accents kept.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace GamesOMG.LordOfUndeadTeleport
{
    internal sealed class Place
    {
        public string Id, Layer, LayerName, Name, Near, Region;
        public float X, Y, Z;             // the landing
        public float PX, PY, PZ;          // the pin itself (the chest, the ore, the camp)
        public int Order;                // file order: the map's layer order, then name
        internal string[] NameWords, OtherWords;

        public string Title => Near != null && !string.Equals(Near, Name, StringComparison.OrdinalIgnoreCase)
            ? Name + " · near " + Near : Name;
        public TpTarget Target => new TpTarget(X, Y, Z);
    }

    internal static class Places
    {
        public static List<Place> Parse(TextReader r)
        {
            var list = new List<Place>();
            string line;
            int n = 0;
            while ((line = r.ReadLine()) != null)
            {
                if (line.Length == 0 || line[0] == '#') continue;
                var c = line.Split('\t');
                if (c.Length < 9) throw new FormatException("places.tsv line " + (n + 1) + ": " + c.Length + " columns, want 9");
                var p = new Place
                {
                    Id = c[0], Layer = c[1], LayerName = c[2], Name = c[3],
                    Near = c[4].Length > 0 ? c[4] : null, Region = c[5].Length > 0 ? c[5] : null,
                    X = F(c[6]), Y = F(c[7]), Z = F(c[8]), Order = n++,
                };
                if (c.Length >= 12) { p.PX = F(c[9]); p.PY = F(c[10]); p.PZ = F(c[11]); }
                else { p.PX = p.X; p.PY = p.Y; p.PZ = p.Z; }
                p.NameWords = Words(p.Name);
                p.OtherWords = Words(p.LayerName + " " + (p.Near ?? "") + " " + (p.Region ?? ""));
                list.Add(p);
            }
            return list;
        }

        static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

        internal static string[] Words(string s)
        {
            var w = new List<string>();
            var cur = new System.Text.StringBuilder();
            foreach (var ch in (s ?? "").ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(ch)) cur.Append(ch);
                else if (cur.Length > 0) { w.Add(cur.ToString()); cur.Clear(); }
            }
            if (cur.Length > 0) w.Add(cur.ToString());
            return w.ToArray();
        }

        /// <summary>The places matching what was typed, best first; empty query = none (the panel shows its hint).</summary>
        public static List<Place> Search(IEnumerable<Place> all, string query, int max = 200)
        {
            var q = Words(query);
            if (q.Length == 0) return new List<Place>();
            var scored = new List<(int score, Place p)>();
            foreach (var p in all)
            {
                int score = 0;
                bool ok = true;
                foreach (var t in q)
                {
                    int s = Best(p.NameWords, t, 3);
                    if (s == 0) s = Best(p.OtherWords, t, 1);
                    if (s == 0) { ok = false; break; }
                    score += s;
                }
                if (!ok) continue;
                // the whole name typed (or its start) ranks first
                string joined = string.Join(" ", q), name = string.Join(" ", p.NameWords);
                if (name == joined) score += 20;
                else if (name.StartsWith(joined, StringComparison.Ordinal)) score += 10;
                scored.Add((score, p));
            }
            return scored.OrderByDescending(t => t.score).ThenBy(t => t.p.Order).Take(max).Select(t => t.p).ToList();
        }

        // a word that IS the typed word scores 2x weight, one it starts scores 1x weight; 0 = no match
        static int Best(string[] words, string t, int weight)
        {
            int best = 0;
            foreach (var w in words)
            {
                if (w == t) return 2 * weight;
                if (w.StartsWith(t, StringComparison.Ordinal)) best = weight;
            }
            return best;
        }

        /// <summary>"1.2 km north-east" / "340 m south": how far and which way a place is from where you stand.</summary>
        public static string Away(float fromX, float fromZ, float toX, float toZ)
        {
            double dx = toX - fromX, dz = toZ - fromZ, d = Math.Sqrt(dx * dx + dz * dz);
            string dist = d >= 1000 ? (d / 1000).ToString("0.0", CultureInfo.InvariantCulture) + " km" : ((int)Math.Round(d / 10) * 10) + " m";
            if (d < 15) return "here";
            // the game's own map: +Z up (north), +X right (east)
            double deg = (Math.Atan2(dx, dz) * 180 / Math.PI + 360) % 360;
            string[] dirs = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
            return dist + " " + dirs[(int)Math.Round(deg / 45) % 8];
        }
    }
}

// GamesOMG Teleport 0.3.3 (player feedback 2026-11-21 13:00, in game on 0.3.2: "now it lists a ton of distances as being above and
// below you, with not much distance, before it was just distance away"): the grey line beside a place's name. Unity-free,
// so the tests run it.
//   · 0.2.6 said only how far. 0.3.0 added the height at |dy| >= 3 m, which is most rows (measured over the nearest 30
//     places from 400 landings per world: Prime 68 %, Humble 70 %, Toxicity 68 %), and 0.3.2's narrow rows then dropped the
//     layer name first, so "26 m below you" was often what was left.
//   · 0.3.3: distance only, unless the height changes what you'd do: the place is mostly straight up or down from you
//     (|dy| >= 20 m AND at least half the flat distance), or it is a cave / underground place (any |dy| >= 3 m).
//     Same sample: Prime 10 %, Humble 22 %, Toxicity 17 % of rows (20 m alone would be 25 / 32 / 31 %).
//   · a one-line (compact) row that doesn't fit drops the height first, then the layer name; the distance stays.
using System;

namespace GamesOMG.PlanetCrafterTeleport
{
    internal static class RowMeta
    {
        public const float BigGap = 20f;     // metres
        public const float Steep = .5f;      // the gap is at least this share of the flat distance
        public const float CaveGap = 3f;     // a cave place says above/below from 3 m, as 0.3.0 did for every place
        public const string Sep = " · ";

        // does this row say "N m above/below you"?
        public static bool ShowsHeight(float flat, float dy, bool underground)
        {
            float a = Math.Abs(dy);
            if (underground) return a >= CaveGap;
            return a >= BigGap && a >= flat * Steep;
        }

        // a cave or underground place, by the map's own words for it (the place's name or its layer's): the POI packs carry
        // no depth flag, and a landing's height alone can't tell a cave from a valley
        public static bool Underground(string name, string layer) => Says(name) || Says(layer);
        private static bool Says(string s) =>
            !string.IsNullOrEmpty(s) && (s.IndexOf("cave", StringComparison.OrdinalIgnoreCase) >= 0 || s.IndexOf("underground", StringComparison.OrdinalIgnoreCase) >= 0);

        // the compact row's fit: the whole line if it fits; else without the tail (height / "lands on the ground"); then the
        // leading segments (the layer name) go one at a time; the last segment (the distance) always stays
        public static string Fit(string lead, string tail, Func<string, float> width, float max)
        {
            string full = Join(lead, tail);
            if (width(full) <= max || string.IsNullOrEmpty(lead)) return full;
            string line = lead;
            int cut;
            while (width(line) > max && (cut = line.IndexOf(Sep, StringComparison.Ordinal)) >= 0) line = line.Substring(cut + Sep.Length);
            return line;
        }

        public static string Join(string a, string b) =>
            string.IsNullOrEmpty(a) ? b ?? "" : string.IsNullOrEmpty(b) ? a : a + Sep + b;
    }
}

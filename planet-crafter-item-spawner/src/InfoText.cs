// GamesOMG Item Spawner 0.2.0: the selected item's info line, as text (a player picking a seed wants its percentage).
// Unity-free, so the tests run it as it ships: ItemInfo.cs reads every
// number and name from the game and hands them here; nothing in this file knows a value of its own.
//   · a multiplier (seeds, larvae, eggs): the game's label, the percent as the game itself shows it ("600 %") and the same as
//     × (the building's output ×6; a building whose contents add up to 0 % counts as ×1, WorldObject.GetUnitGeneration),
//     and the buildings it counts in, one name per family
//   · a fuse: the boost per fuse (they add up, MachineOptimizer.ApplyMultiplierToWorldObjects), then each optimizer's reach
//   · food, water, oxygen, toxins: the gauge and the amount (PlayerGaugesHandler.Eat / Drink / Breath / Purify)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace GamesOMG.PlanetCrafterSpawn
{
    // a building an item counts in: the game's id, its name in the player's language, its place in the game's own list
    internal sealed class Place
    {
        public string Id = "", Name = "";
        public int Order;
    }

    // an optimizer tier: its name, its range in metres, how many machines each fuse reaches
    internal sealed class Tier
    {
        public string Name = "";
        public float Range;
        public int Machines;
    }

    internal static class InfoText
    {
        // the game's own threshold: a building multiplier under this counts as ×1 (WorldObject.GetUnitGeneration)
        public const float Epsilon = 1.1920929E-07f;

        // the tier marks the game's names carry, in its 13 languages (build 26411416): "T2 Tree Spreader", "Végétube t1",
        // "Pflanzenröhre St 1", "Wegemoduł P1", "Bitki Tüpü K1", "Vegetubo G1", "Флоростенд T1", "2级花卉播种机", "2級樹木播種機"
        private static readonly Regex LeadingTier = new Regex(@"^\s*(?:[TtKkGgPpТт]\s?\d+\s+|\d+\s*[级級]\s*)", RegexOptions.CultureInvariant);
        private static readonly Regex TrailingTier = new Regex(@"\s+(?:[TtKkGgPpТт]|St\.?)\s?\d+\s*$", RegexOptions.CultureInvariant);

        public static string Untiered(string name)
        {
            if (string.IsNullOrEmpty(name)) return name ?? "";
            string s = TrailingTier.Replace(LeadingTier.Replace(name, ""), "").Trim();
            return s.Length == 0 ? name.Trim() : s;
        }

        // the id without its tier number: "Vegetube2" → "Vegetube", "TreeSpreader0" → "TreeSpreader"
        public static string Stem(string id) => (id ?? "").TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');

        private static int TierNumber(string id)
        {
            string s = id ?? "";
            int i = s.Length;
            while (i > 0 && char.IsDigit(s[i - 1])) i--;
            return i < s.Length && int.TryParse(s.Substring(i), NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : 0;
        }

        // one name per family: buildings that share an id stem are tiers of one building, named by the lowest tier with its tier
        // mark taken off; the same name twice (T3 Vegetube is the VegetubeOutside family) is said once. The game's order.
        public static List<string> Families(IEnumerable<Place> places)
        {
            var lowest = new Dictionary<string, Place>(StringComparer.Ordinal);
            var first = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var p in places)
            {
                if (p == null) continue;
                string stem = Stem(p.Id);
                if (!lowest.TryGetValue(stem, out var was) || TierNumber(p.Id) < TierNumber(was.Id)) lowest[stem] = p;
                if (!first.TryGetValue(stem, out int o) || p.Order < o) first[stem] = p.Order;
            }
            var stems = new List<string>(lowest.Keys);
            stems.Sort((a, b) => first[a].CompareTo(first[b]));
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in stems)
            {
                string name = Untiered(lowest[s].Name);
                if (name.Length > 0 && seen.Add(Search.Fold(name))) names.Add(name);
            }
            return names;
        }

        // a percent as the game prints it: whole numbers plainly, anything else with the language's decimal mark
        public static string Percent(float value) => Math.Abs(value - Math.Round(value)) < 0.001 ? Math.Round(value).ToString("0", CultureInfo.InvariantCulture) : L.Number(value);

        public static string Join(params string[] sentences)
        {
            var parts = new List<string>();
            foreach (var s in sentences) if (!string.IsNullOrEmpty(s)) parts.Add(s);
            return string.Join(L.T("sentence.sep"), parts.ToArray());
        }

        // elsewhere: 0 nowhere else takes it, 1 a pot does (seeds), 2 another building does
        public static string Multiplier(string label, float percent, IList<string> families, bool addUp, int elsewhere)
        {
            if (percent / 100f < Epsilon) return L.T("info.multNone", label);
            string line = L.T("info.mult", label, Percent(percent), L.Number(percent / 100.0), string.Join(L.T("list.sep"), families));
            return Join(line, addUp ? L.T("info.addUp") : null, elsewhere == 1 ? L.T("info.pot") : elsewhere == 2 ? L.T("info.elsewhere") : null);
        }

        public static string Fuse(string label, float percent, IList<Tier> tiers)
        {
            string line = L.T("info.fuse", label, Percent(percent));
            if (tiers == null || tiers.Count == 0) return line;
            var reach = new List<string>();
            foreach (var t in tiers) reach.Add(L.T("info.fuseTier", t.Machines, Percent(t.Range), t.Name));
            return Join(line, L.T("info.fuseReach", string.Join(" · ", reach.ToArray())));
        }

        // kind: "info.food", "info.water", "info.oxygen", "info.toxic"
        public static string Gauge(string kind, int value) => L.T(kind, value);

        // the game's description as one plain line: no rich-text tags, no line breaks, nothing when it only repeats the name
        public static string Description(string text, string name)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string s = Regex.Replace(text, @"<br\s*/?>", " ", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"<[^>]{1,40}>", "");
            s = Regex.Replace(s, @"\s+", " ").Trim();
            if (s.Length == 0 || Search.Fold(s) == Search.Fold(name)) return "";
            return s;
        }
    }
}

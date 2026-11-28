// GamesOMG Item Spawner: finding an item by the name a player knows (most players never see the game's internal ids). Unity-free, so the tests
// run it as it ships.
//   · every item has three names to match: the game's own name in the player's language, its English name (most guides are
//     English) and the internal id ("Iron", "Alloy", "OxygenCapsule1")
//   · folded before comparing: case, accents (é → e, ß → ss, ø → o …), and -, _ , . as spaces; ids are also split at
//     their capitals ("OxygenCapsule1" → "oxygen capsule 1")
//   · every word typed must appear in one of the names, anywhere (partial); the best match is first, so Enter takes it
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GamesOMG.PlanetCrafterSpawn
{
    internal sealed class ItemEntry
    {
        public string Id = "", Name = "", English = "";
        public string FoldName = "", FoldEnglish = "", FoldId = "";
        public int Order;       // the game's own list order, the tie-break of last resort
        public object Group;    // the game's Group (Unity side only)
        // the game gives it no name in any language (build 26411416: the growing and hatched states seeds and eggs turn into
        // inside planters and aquariums, Tree5Growable / Fish1Hatched, and some Blueprint… chips). Its game would show the raw
        // id; here it reads as the id split into words, is left out of browsing, and comes up only when a search matches its id
        public bool Unnamed;
        // 0.3.0: one of the things the game hands out for a base's looks (Decor.cs): an effigy or a poster (the game files
        // those as buildings: Placeable, put down from the backpack) or a spacesuit skin
        public bool Decoration, Placeable;

        public static ItemEntry Make(string id, string name, string english, int order, object group = null, bool unnamed = false)
        {
            id = id ?? "";
            if (unnamed) name = english = Search.Words(id);
            name = string.IsNullOrEmpty(name) ? id : name;
            english = string.IsNullOrEmpty(english) ? name : english;
            return new ItemEntry
            {
                Id = id, Name = name, English = english, Order = order, Group = group, Unnamed = unnamed,
                FoldName = Search.Fold(name), FoldEnglish = Search.Fold(english), FoldId = Search.Fold(Search.SplitId(id)),
            };
        }
    }

    internal static class Search
    {
        // lower case, no accents, separators as single spaces, trimmed
        public static string Fold(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string d = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(d.Length);
            bool space = true;
            foreach (char raw in d)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(raw) == UnicodeCategory.NonSpacingMark) continue;
                string c = Special(char.ToLowerInvariant(raw));
                foreach (char ch in c)
                {
                    bool sep = char.IsWhiteSpace(ch) || ch == '-' || ch == '_' || ch == '.' || ch == ',' || ch == '/' || ch == '(' || ch == ')' || ch == '\'';
                    if (sep) { if (!space) { sb.Append(' '); space = true; } continue; }
                    sb.Append(ch);
                    space = false;
                }
            }
            if (sb.Length > 0 && sb[sb.Length - 1] == ' ') sb.Length--;
            return sb.ToString();
        }

        // letters FormD does not split into a base letter and a mark
        private static string Special(char c)
        {
            switch (c)
            {
                case 'ß': return "ss";
                case 'æ': return "ae";
                case 'œ': return "oe";
                case 'ø': return "o";
                case 'ł': return "l";
                case 'đ': return "d";
                case 'ð': return "d";
                case 'þ': return "th";
                case 'ı': return "i";
                default: return c.ToString();
            }
        }

        // "OxygenCapsule1" → "Oxygen Capsule 1", "RedPowder2" → "Red Powder 2"; ids are camel case with numbers on the end
        public static string SplitId(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            var sb = new StringBuilder(id.Length + 8);
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                if (i > 0)
                {
                    char p = id[i - 1];
                    bool lowerToUpper = char.IsUpper(c) && char.IsLower(p);
                    bool letterToDigit = char.IsDigit(c) && char.IsLetter(p);
                    bool digitToLetter = char.IsLetter(c) && char.IsDigit(p);
                    if (lowerToUpper || letterToDigit || digitToLetter) sb.Append(' ');
                }
                sb.Append(c);
            }
            // keep the id itself matchable as one word too ("oxygencapsule1")
            return sb + " " + id;
        }

        // an id as words, for an item the game never names: "BlueprintAutocrafter" → "Blueprint Autocrafter"
        public static string Words(string id)
        {
            string s = SplitId(id);
            int cut = s.LastIndexOf(' ');
            return cut > 0 ? s.Substring(0, cut) : s;
        }

        // how well one item answers the query: 0 = not at all, higher = better. Every word must be found in some name.
        public static int Score(ItemEntry item, string foldedQuery)
        {
            if (item == null) return 0;
            if (string.IsNullOrEmpty(foldedQuery)) return item.Unnamed ? 0 : 1;
            if (item.Unnamed)
            {
                // only its id answers (Filter puts it after every named item), scored like a name so "pod 9 x b" is xB, not xA
                int sum = 0;
                foreach (string w in foldedQuery.Split(' '))
                {
                    if (w.Length == 0) continue;
                    int i = Word(item.FoldId, w);
                    if (i == 0) return 0;
                    sum += i;
                }
                return item.FoldName == foldedQuery || item.FoldId.EndsWith(" " + foldedQuery, StringComparison.Ordinal) ? sum + 100 : sum;
            }
            string[] words = foldedQuery.Split(' ');
            int total = 0;
            foreach (string w in words)
            {
                if (w.Length == 0) continue;
                int n = Word(item.FoldName, w), e = Word(item.FoldEnglish, w), i = Word(item.FoldId, w);
                if (n == 0 && e == 0 && i == 0) return 0;
                // the player's own language first, then English, then the id
                total += Math.Max(n > 0 ? n + 2 : 0, Math.Max(e > 0 ? e + 1 : 0, i));
            }
            // the whole query as the whole name: "iron" is Iron before Iron Plate
            if (item.FoldName == foldedQuery) total += 100;
            else if (item.FoldEnglish == foldedQuery) total += 90;
            else if (item.FoldName.StartsWith(foldedQuery, StringComparison.Ordinal)) total += 40;
            else if (item.FoldEnglish.StartsWith(foldedQuery, StringComparison.Ordinal)) total += 35;
            return total;
        }

        // one word against one folded name: 30 = a whole word, 20 = the start of a word, 10 = inside a word, 0 = absent
        private static int Word(string name, string w)
        {
            int at = name.IndexOf(w, StringComparison.Ordinal);
            if (at < 0) return 0;
            int best = 10;
            while (at >= 0)
            {
                bool start = at == 0 || name[at - 1] == ' ';
                bool end = at + w.Length == name.Length || name[at + w.Length] == ' ';
                int s = start && end ? 30 : start ? 20 : 10;
                if (s > best) best = s;
                at = name.IndexOf(w, at + 1, StringComparison.Ordinal);
            }
            return best;
        }

        // the items that match, best first; ties: the shorter name, then the game's own order
        public static List<ItemEntry> Filter(IList<ItemEntry> all, string query)
        {
            string q = Fold(query);
            var hits = new List<KeyValuePair<int, ItemEntry>>();
            foreach (var item in all)
            {
                int s = Score(item, q);
                if (s > 0) hits.Add(new KeyValuePair<int, ItemEntry>(s, item));
            }
            if (q.Length == 0) hits.Sort((a, b) => string.Compare(a.Value.FoldName, b.Value.FoldName, StringComparison.Ordinal) is int c && c != 0 ? c : a.Value.Order.CompareTo(b.Value.Order));
            else hits.Sort((a, b) =>
            {
                int c = a.Value.Unnamed.CompareTo(b.Value.Unnamed);   // every named item before any unnamed one
                if (c != 0) return c;
                c = b.Key.CompareTo(a.Key);
                if (c != 0) return c;
                c = a.Value.Name.Length.CompareTo(b.Value.Name.Length);
                if (c != 0) return c;
                c = string.Compare(a.Value.FoldName, b.Value.FoldName, StringComparison.Ordinal);
                return c != 0 ? c : a.Value.Order.CompareTo(b.Value.Order);
            });
            var list = new List<ItemEntry>(hits.Count);
            foreach (var h in hits) list.Add(h.Value);
            return list;
        }
    }
}

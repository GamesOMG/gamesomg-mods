// GamesOMG Teleport 0.2.4: the map's found ticks, both ways, in the map's own format. The GamesOMG map shares them as
// #foundset=<key>.<hash>.<bits> (assets/js/gomg-sets.js): one bit per pin of trackers.json's gomg_pc_found row, in the
// order the map's own reader adds them; hash = FNV-1a 32 over those ids joined by "\n" (UTF-16 code units), base 36; bits
// = "z" + base64url(deflate-raw(the bytes)) when that is shorter, else "b" + base64url(the bytes), trailing zero bytes cut.
// The ids, their order, their planets and the hash are packed by build-foundset.mjs, which runs gomg-sets.js itself, into
// data/foundset-pc.json.z. A link whose hash is not ours was made over other pins: refused, never guessed.
// Only that row's layers travel (keys, chips, cards, golden crates and the other collectibles); a crate or ore tick has no bit.
// No Unity here: the WSL tests run it as it runs in game.
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace GamesOMG.PlanetCrafterTeleport
{
    internal sealed class FoundSet
    {
        public string Key = "", Url = "", Map = "", Hash = "";
        public readonly List<string> Worlds = new List<string>();
        public readonly List<string> Ids = new List<string>();
        public readonly List<int> W = new List<int>();   // each id's planet, an index into Worlds
        public const string Origin = "https://gamesomg.com";

        private static readonly Regex Link = new Regex(@"(?:^|[#&?\s])foundset=([^&\s#]+)", RegexOptions.CultureInvariant);
        private static readonly Regex Parts = new Regex(@"^(.+)\.([0-9a-z]{1,8})\.([zb])([A-Za-z0-9_-]*)$", RegexOptions.CultureInvariant);

        public static bool LooksLikeLink(string text) => text != null && text.IndexOf("foundset=", StringComparison.Ordinal) >= 0;

        public static FoundSet Read(byte[] deflated)
        {
            var o = MiniJson.O(MiniJson.Parse(Encoding.UTF8.GetString(SpotSet.Inflate(deflated, 1 << 22))));
            if (o == null || MiniJson.N(MiniJson.Get(o, "v")) != 1) throw new FormatException("not a v1 found set");
            var f = new FoundSet
            {
                Key = MiniJson.S(MiniJson.Get(o, "key")), Url = MiniJson.S(MiniJson.Get(o, "url")),
                Map = MiniJson.S(MiniJson.Get(o, "map")), Hash = MiniJson.S(MiniJson.Get(o, "hash")),
            };
            foreach (var w in MiniJson.A(MiniJson.Get(o, "worlds")) ?? new List<object>()) f.Worlds.Add(MiniJson.S(w));
            foreach (var id in MiniJson.A(MiniJson.Get(o, "ids")) ?? new List<object>()) f.Ids.Add(MiniJson.S(id));
            foreach (var w in MiniJson.A(MiniJson.Get(o, "w")) ?? new List<object>()) f.W.Add((int)MiniJson.N(w));
            if (f.Ids.Count == 0 || f.W.Count != f.Ids.Count || f.W.Exists(i => i < 0 || i >= f.Worlds.Count)) throw new FormatException("a found set without its planets");
            if (HashOf(f.Ids) != f.Hash) throw new FormatException("the packed hash is not the hash of the packed ids");
            return f;
        }

        // gomg-sets.js hash(): FNV-1a 32 over the ids joined by "\n", one step per UTF-16 code unit, base 36
        public static string HashOf(List<string> ids)
        {
            uint h = 0x811c9dc5;
            string s = string.Join("\n", ids.ToArray());
            for (int i = 0; i < s.Length; i++) { h ^= s[i]; h = unchecked(h * 0x01000193); }
            return Base36(h);
        }

        private static string Base36(uint v)
        {
            const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
            if (v == 0) return "0";
            var sb = new StringBuilder();
            while (v > 0) { sb.Insert(0, digits[(int)(v % 36)]); v /= 36; }
            return sb.ToString();
        }

        // the ticks a link carries, as indexes into Ids; false with a reason the panel shows
        public bool TryRead(string text, out List<int> ticked, out string reason)
        {
            ticked = new List<int>();
            reason = null;
            var m = text == null ? null : Link.Match(text.Trim());
            if (m == null || !m.Success) { reason = L.T("found.notLink"); return false; }
            string raw = m.Groups[1].Value;
            try { raw = Uri.UnescapeDataString(raw); } catch (Exception) { }
            var p = Parts.Match(raw);
            if (!p.Success) { reason = L.T("found.damaged"); return false; }
            if (p.Groups[1].Value != Key) { reason = L.T("found.otherList"); return false; }
            if (p.Groups[2].Value != Hash) { reason = L.T("found.otherHash"); return false; }
            byte[] bytes;
            try
            {
                bytes = SpotSet.Unbase64Url(p.Groups[4].Value);
                if (p.Groups[3].Value == "z") bytes = SpotSet.Inflate(bytes, 1 << 18);
            }
            catch (Exception) { reason = L.T("found.damaged"); return false; }
            if (bytes.Length > (Ids.Count + 7) / 8) { reason = L.T("found.damaged"); return false; }
            for (int i = 0; i < Ids.Count; i++) if ((i >> 3) < bytes.Length && (bytes[i >> 3] & (1 << (i & 7))) != 0) ticked.Add(i);
            return true;
        }

        // gomg-sets.js code(): the text after "#foundset=" for the ticks `on` says yes to
        public string CodeOf(Func<int, bool> on)
        {
            var bytes = new byte[(Ids.Count + 7) / 8];
            int last = 0;
            for (int i = 0; i < Ids.Count; i++) if (on(i)) { bytes[i >> 3] |= (byte)(1 << (i & 7)); last = (i >> 3) + 1; }
            var cut = new byte[last];
            Array.Copy(bytes, cut, last);
            string plain = "b" + SpotSet.Base64Url(cut);
            string best = plain;
            if (last > 0)
            {
                string z = "z" + SpotSet.Base64Url(SpotSet.Deflate(cut));
                if (z.Length < plain.Length) best = z;
            }
            return Key + "." + Hash + "." + best;
        }

        public string LinkOf(Func<int, bool> on) => Origin + Url + "#foundset=" + CodeOf(on);
    }
}

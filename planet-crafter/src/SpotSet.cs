// GamesOMG Teleport 0.2.0: reads the GamesOMG map's "Share all" link (#spotset=), the one format the map already writes
// (assets/js/map-spots.js packSet/unpackSet, D134). <code> is "z" + base64url(deflate-raw(JSON)), or "j" + base64url(JSON)
// from a browser without CompressionStream. The JSON is [1, from, {d: drawings}?, [x, y, name, note, view, colour, pin,
// group, timer…], …]. On the Planet Crafter map x, y are the game's X and Z in metres and view is the world id ("prime").
// No Unity here: the WSL tests run it as it runs in game.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace GamesOMG.PlanetCrafterTeleport
{
    internal sealed class MapSpot
    {
        public float X, Z;
        public string Name = "Spot", Note = "", World = "", Colour = "", Pin = "", Group = "";
    }

    internal static class SpotSet
    {
        // map-spots.js's own limits, so a link reads the same here as on the map
        public const int Max = 200, NameMax = 60, NoteMax = 120, GroupMax = 30, InflateMax = 1 << 19;
        public static readonly string[] Colours = { "orange", "red", "yellow", "green", "cyan", "blue", "purple", "white" };
        private static readonly Regex Code = new Regex(@"(?:^|[#&?\s])spotset=([zj][A-Za-z0-9_-]+)", RegexOptions.CultureInvariant);

        public static bool LooksLikeLink(string text) => text != null && text.IndexOf("spotset=", StringComparison.Ordinal) >= 0;

        public static bool TryRead(string text, out List<MapSpot> spots, out string from, out string reason)
        {
            spots = new List<MapSpot>();
            from = "";
            reason = null;
            var m = text == null ? null : Code.Match(text.Trim());
            if (m == null || !m.Success) { reason = L.T("link.notSpots"); return false; }
            string code = m.Groups[1].Value;
            byte[] bytes;
            try { bytes = Unbase64Url(code.Substring(1)); }
            catch (FormatException) { reason = L.T("link.damaged"); return false; }
            if (code[0] == 'z')
            {
                try { bytes = Inflate(bytes); }
                catch (Exception) { reason = L.T("link.damaged"); return false; }
            }
            List<object> a;
            try { a = MiniJson.A(MiniJson.Parse(Encoding.UTF8.GetString(bytes))); }
            catch (FormatException) { reason = L.T("link.damaged"); return false; }
            if (a == null || a.Count < 2 || MiniJson.N(a[0]) != 1) { reason = L.T("link.newer"); return false; }
            from = Clean(MiniJson.S(a[1]), 30);
            for (int i = 2; i < a.Count && spots.Count < Max; i++)
            {
                var r = MiniJson.A(a[i]);   // an object here is the map's drawings: not for the game
                if (r == null || r.Count < 2) continue;
                double x = MiniJson.N(r[0]), z = MiniJson.N(r[1]);
                if (double.IsNaN(x) || double.IsNaN(z) || double.IsInfinity(x) || double.IsInfinity(z)) continue;
                if (Math.Abs(x) > 1000000 || Math.Abs(z) > 1000000) continue;
                var spot = new MapSpot
                {
                    X = (float)x,
                    Z = (float)z,
                    Name = Field(r, 2, NameMax),
                    Note = Field(r, 3, NoteMax),
                    World = Field(r, 4, 80).ToLowerInvariant(),
                    Colour = Field(r, 5, 12),
                    Pin = Field(r, 6, 64),
                    Group = Clean(Field(r, 7, 200), GroupMax),
                };
                if (spot.Name.Length == 0) spot.Name = L.T("link.spot");
                if (Array.IndexOf(Colours, spot.Colour) < 0) spot.Colour = "";
                spots.Add(spot);
            }
            if (spots.Count == 0) { reason = L.T("link.noSpots"); return false; }
            return true;
        }

        private static string Field(List<object> r, int i, int max)
        {
            string s = i < r.Count ? MiniJson.S(r[i]) : "";
            return s.Length > max ? s.Substring(0, max) : s;
        }

        // map-spots.js cleanGroup: whitespace runs to one space, trimmed, cut to the cap
        public static string Clean(string s, int max)
        {
            s = Regex.Replace(s ?? "", @"\s+", " ").Trim();
            return s.Length > max ? s.Substring(0, max).Trim() : s;
        }

        public static byte[] Unbase64Url(string t)
        {
            string b = t.Replace('-', '+').Replace('_', '/');
            b += new string('=', (4 - b.Length % 4) % 4);
            return Convert.FromBase64String(b);
        }

        public static string Base64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        // raw deflate (the browser's 'deflate-raw'); in game this is Mono's DeflateStream over MonoPosixHelper, which ships
        // in the game's MonoBleedingEdge/EmbedRuntime. A crafted link cannot unpack past InflateMax.
        public static byte[] Inflate(byte[] z, int max = InflateMax)
        {
            using (var input = new DeflateStream(new MemoryStream(z), CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                var buffer = new byte[8192];
                int n;
                while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (output.Length + n > max) throw new InvalidDataException("too large");
                    output.Write(buffer, 0, n);
                }
                return output.ToArray();
            }
        }

        public static byte[] Deflate(byte[] raw)
        {
            using (var output = new MemoryStream())
            {
                using (var z = new DeflateStream(output, CompressionMode.Compress, true)) z.Write(raw, 0, raw.Length);
                return output.ToArray();
            }
        }
    }
}

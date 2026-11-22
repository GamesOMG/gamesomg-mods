// ChatGPT C56: strict, standalone clipboard protocol shared with the parser proof.
// 0.1.2: the same rules, now also saying why a line was refused (the paste box shows it).
using System;
using System.Globalization;

namespace GamesOMG.PlanetCrafterTeleport
{
    internal readonly struct TeleportRequest
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;
        public readonly bool AutomaticHeight;
        // 0.2.5: a POI landing out on the far ring (16-30 m, the last resort of the landing search) says how far it set you
        // down from the place: ", 24 m from it". Null for every other landing and for a copied line.
        public readonly string Away;

        public TeleportRequest(float x, float y, float z, bool automaticHeight, string away = null)
        {
            X = x;
            Y = y;
            Z = z;
            AutomaticHeight = automaticHeight;
            Away = away;
        }

        public static bool TryParse(string text, out TeleportRequest request) => Parse(text, out request, out _);

        // Null when the line is a teleport; otherwise one short sentence a player can act on.
        public static string Explain(string text)
        {
            Parse(text, out _, out var reason);
            return reason;
        }

        public static bool Parse(string text, out TeleportRequest request, out string reason)
        {
            request = default;
            reason = null;
            if (text == null || text.Trim().Length == 0) { reason = L.T("tp.pasteFirst"); return false; }
            if (text.Length > 256) { reason = L.T("tp.tooLong"); return false; }
            text = text.Trim();
            if (text.IndexOf('\n') >= 0 || text.IndexOf('\r') >= 0) { reason = L.T("tp.oneLine"); return false; }
            // Any whitespace between tokens (a copied non-breaking space included), still one line.
            var parts = text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (!string.Equals(parts[0], "/tp", StringComparison.Ordinal))
            {
                reason = L.T("tp.startsWith");
                return false;
            }
            if (parts.Length != 4) { reason = L.T("tp.threeValues"); return false; }
            if (parts[1] == "~" || parts[3] == "~") { reason = L.T("tp.tildeHeight"); return false; }
            if (!Coordinate(parts[1], out var x, ref reason) | !Coordinate(parts[3], out var z, ref reason)) return false;
            bool automatic = parts[2] == "~";
            float y = 0;
            if (!automatic && !Coordinate(parts[2], out y, ref reason)) return false;
            request = new TeleportRequest(x, y, z, automatic);
            return true;
        }

        private static bool Coordinate(string value, out float number, ref string reason)
        {
            const NumberStyles style = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
            if (!float.TryParse(value, style, CultureInfo.InvariantCulture, out number)
                || float.IsNaN(number) || float.IsInfinity(number))
            {
                reason = reason ?? L.T("tp.plainNumbers");
                return false;
            }
            if (Math.Abs(number) > 1000000)
            {
                reason = reason ?? L.T("tp.outside");
                return false;
            }
            return true;
        }
    }
}

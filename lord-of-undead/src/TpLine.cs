// TpLine: reads a teleport line. Pure C#, no game types, so tests/ runs it without the game.
// The map's Copy Teleport Command button copies "/tp x y z" (world metres, y = where your feet go). Also read:
//   "/tp x z" and "/tp x ~ z"  (no height: the mod finds the ground from above)
//   with or without the slash, commas or spaces between, decimals with a point, anywhere in a pasted line.
using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace GamesOMG.LordOfUndeadTeleport
{
    internal readonly struct TpTarget
    {
        public readonly float X, Z;
        public readonly float? Y;          // null = "~" or not given: find the ground from above
        public TpTarget(float x, float? y, float z) { X = x; Y = y; Z = z; }
        public override string ToString() =>
            "/tp " + F(X) + " " + (Y.HasValue ? F(Y.Value) : "~") + " " + F(Z);
        internal static string F(float v) => Math.Round(v).ToString("0", CultureInfo.InvariantCulture);
    }

    internal static class TpLine
    {
        const string N = @"-?\d{1,6}(?:\.\d+)?";
        static readonly Regex Three = new Regex(@"(?i)(?:^|[^a-z0-9])/?tp\s+(" + N + @")[\s,;]+(" + N + @"|~)[\s,;]+(" + N + @")(?![\d.])",
            RegexOptions.CultureInvariant);
        static readonly Regex Two = new Regex(@"(?i)(?:^|[^a-z0-9])/?tp\s+(" + N + @")[\s,;]+(" + N + @")(?![\d.]|[\s,;]+(?:" + N + "|~))",
            RegexOptions.CultureInvariant);
        // far past any world the game ships (its play area is about 2.3 x 1.4 km): a number this size is not a position
        public const float Limit = 50000f;

        public static bool TryParse(string text, out TpTarget target)
        {
            target = default;
            if (string.IsNullOrEmpty(text) || text.Length > 2000) return false;
            var m = Three.Match(text);
            if (m.Success)
            {
                if (!Num(m.Groups[1].Value, out var x) || !Num(m.Groups[3].Value, out var z)) return false;
                float? y = null;
                if (m.Groups[2].Value != "~")
                {
                    if (!Num(m.Groups[2].Value, out var yy)) return false;
                    y = yy;
                }
                target = new TpTarget(x, y, z);
                return true;
            }
            m = Two.Match(text);
            if (m.Success && Num(m.Groups[1].Value, out var x2) && Num(m.Groups[2].Value, out var z2))
            {
                target = new TpTarget(x2, null, z2);
                return true;
            }
            return false;
        }

        // does the text look like someone MEANT a /tp line (so a refusal says why, instead of opening the search)?
        public static bool LooksLikeTp(string text) =>
            !string.IsNullOrEmpty(text) && Regex.IsMatch(text, @"(?i)(?:^|\s)/?tp(?:\s|$)");

        static bool Num(string s, out float v)
        {
            if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return false;
            return !float.IsNaN(v) && !float.IsInfinity(v) && Math.Abs(v) <= Limit;
        }
    }
}

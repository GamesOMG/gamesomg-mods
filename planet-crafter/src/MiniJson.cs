// GamesOMG Teleport 0.2.0: a small JSON reader and writer with no Unity or library dependency, so the places store, the
// map's spot-set links and the bundled POI lists read the same way in the game and in the WSL tests, and the pattern
// copies to another game's mod as one file. Objects are Dictionary<string, object>, arrays List<object>, numbers double.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GamesOMG.PlanetCrafterTeleport
{
    internal static class MiniJson
    {
        public static object Parse(string text)
        {
            int i = 0;
            var value = Value(text, ref i, 0);
            Space(text, ref i);
            if (i != text.Length) throw new FormatException("trailing text at " + i);
            return value;
        }

        private static void Space(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
        }

        private static object Value(string s, ref int i, int depth)
        {
            if (depth > 64) throw new FormatException("nested too deep");
            Space(s, ref i);
            if (i >= s.Length) throw new FormatException("unexpected end");
            char c = s[i];
            if (c == '{')
            {
                var o = new Dictionary<string, object>(StringComparer.Ordinal);
                i++; Space(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return o; }
                while (true)
                {
                    Space(s, ref i);
                    if (i >= s.Length || s[i] != '"') throw new FormatException("expected a key at " + i);
                    string key = Str(s, ref i);
                    Space(s, ref i);
                    if (i >= s.Length || s[i] != ':') throw new FormatException("expected : at " + i);
                    i++;
                    o[key] = Value(s, ref i, depth + 1);
                    Space(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == '}') { i++; return o; }
                    throw new FormatException("expected , or } at " + i);
                }
            }
            if (c == '[')
            {
                var a = new List<object>();
                i++; Space(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return a; }
                while (true)
                {
                    a.Add(Value(s, ref i, depth + 1));
                    Space(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == ']') { i++; return a; }
                    throw new FormatException("expected , or ] at " + i);
                }
            }
            if (c == '"') return Str(s, ref i);
            if (Word(s, ref i, "true")) return true;
            if (Word(s, ref i, "false")) return false;
            if (Word(s, ref i, "null")) return null;
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (start == i) throw new FormatException("unexpected '" + c + "' at " + i);
            if (!double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
                throw new FormatException("bad number at " + start);
            return n;
        }

        private static bool Word(string s, ref int i, string word)
        {
            if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) return false;
            i += word.Length;
            return true;
        }

        private static string Str(string s, ref int i)
        {
            var b = new StringBuilder();
            i++; // opening quote
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return b.ToString();
                if (c != '\\') { b.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case '"': b.Append('"'); break;
                    case '\\': b.Append('\\'); break;
                    case '/': b.Append('/'); break;
                    case 'b': b.Append('\b'); break;
                    case 'f': b.Append('\f'); break;
                    case 'n': b.Append('\n'); break;
                    case 'r': b.Append('\r'); break;
                    case 't': b.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("bad \\u escape");
                        b.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: throw new FormatException("bad escape \\" + e);
                }
            }
            throw new FormatException("unterminated string");
        }

        public static string Write(object value) => Write(value, 2);

        // decimals: how many places a number keeps (the places file keeps 2; the landing survey's floors keep 4)
        public static string Write(object value, int decimals)
        {
            var b = new StringBuilder();
            Write(b, value, decimals);
            return b.ToString();
        }

        private static void Write(StringBuilder b, object v, int decimals)
        {
            switch (v)
            {
                case null: b.Append("null"); return;
                case string s: Quote(b, s); return;
                case bool t: b.Append(t ? "true" : "false"); return;
                case double d: b.Append(Num(d, decimals)); return;
                case float f: b.Append(Num(f, decimals)); return;
                case int n: b.Append(n.ToString(CultureInfo.InvariantCulture)); return;
                case long l: b.Append(l.ToString(CultureInfo.InvariantCulture)); return;
                case IDictionary<string, object> o:
                    b.Append('{');
                    bool first = true;
                    foreach (var kv in o)
                    {
                        if (!first) b.Append(',');
                        first = false;
                        Quote(b, kv.Key); b.Append(':'); Write(b, kv.Value, decimals);
                    }
                    b.Append('}');
                    return;
                case System.Collections.IEnumerable list:
                    b.Append('[');
                    bool head = true;
                    foreach (var item in list)
                    {
                        if (!head) b.Append(',');
                        head = false;
                        Write(b, item, decimals);
                    }
                    b.Append(']');
                    return;
                default: throw new ArgumentException("cannot write " + v.GetType().Name);
            }
        }

        private static string Num(double d, int decimals)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return "null";
            return Math.Round(d, decimals).ToString("0." + new string('#', Math.Max(1, decimals)), CultureInfo.InvariantCulture);
        }

        private static void Quote(StringBuilder b, string s)
        {
            b.Append('"');
            foreach (char c in s)
            {
                if (c == '"') b.Append("\\\"");
                else if (c == '\\') b.Append("\\\\");
                else if (c < ' ') b.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else b.Append(c);
            }
            b.Append('"');
        }

        // Readers that never throw on a missing or wrong-typed field.
        public static string S(object o, string fallback = "") => o as string ?? fallback;
        public static double N(object o, double fallback = double.NaN) => o is double d ? d : fallback;
        public static List<object> A(object o) => o as List<object>;
        public static Dictionary<string, object> O(object o) => o as Dictionary<string, object>;
        public static object Get(Dictionary<string, object> o, string key) => o != null && o.TryGetValue(key, out var v) ? v : null;
    }
}

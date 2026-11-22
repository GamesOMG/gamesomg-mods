// GamesOMG Teleport 0.3.0: Copy diagnostics (Help/About), for a bug report. Versions, settings that matter, and the last
// few refusals or failures by their message id. Never the clipboard, never a place name, never a position: refusals are
// kept as ids (Strings.cs keys), not their filled-in text. No Unity here: the tests check what it leaves out.
using System;
using System.Collections.Generic;
using System.Text;

namespace GamesOMG.PlanetCrafterTeleport
{
    internal sealed class Diagnostics
    {
        public const int Keep = 8;
        private readonly List<string> recent = new List<string>();

        // a refusal or failure the player saw: its message id only ("land.noRoom"), with the time since start
        public void Note(string messageId, float atSeconds)
        {
            if (string.IsNullOrEmpty(messageId) || !L.Has(messageId)) return;
            recent.Add(messageId + " @" + Math.Round(atSeconds) + "s");
            if (recent.Count > Keep) recent.RemoveAt(0);
        }

        public IReadOnlyList<string> Recent => recent;

        // facts: name -> value, each a version, a setting or a count (the caller passes only those)
        public string Text(IList<KeyValuePair<string, string>> facts)
        {
            var sb = new StringBuilder();
            sb.Append("GamesOMG Teleport diagnostics\n");
            foreach (var kv in facts) sb.Append(kv.Key).Append(": ").Append(Clean(kv.Value)).Append('\n');
            sb.Append("Recent refusals/failures: ").Append(recent.Count == 0 ? "none" : string.Join(", ", recent.ToArray())).Append('\n');
            sb.Append("(No clipboard text, place names or positions are included.)\n");
            return sb.ToString();
        }

        private static string Clean(string value)
        {
            value = (value ?? "").Replace('\n', ' ').Replace('\r', ' ');
            return value.Length > 200 ? value.Substring(0, 200) : value;
        }
    }
}

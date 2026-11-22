// GamesOMG Item Spawner: a controller chord as text ("<Gamepad>/leftShoulder + <Gamepad>/rightShoulder"), Unity-free (tests run it).
// A chord's buttons each do something alone in the game's play map; what matters is that pressing them together does
// nothing the player would notice. Allowed alone: LB = the game's SecretDevKey (it does nothing in play, build 26411416) and
// RB = AccessibilityKey (a held modifier, read only while building or clicking in an inventory). Any other play action on a
// chord button is a clash, said once in the log and in a notice.
using System.Collections.Generic;

namespace GamesOMG.PlanetCrafterSpawn
{
    internal static class Chord
    {
        public static readonly HashSet<string> Harmless = new HashSet<string> { "SecretDevKey", "AccessibilityKey" };

        public static List<string> Parts(string chord)
        {
            var parts = new List<string>();
            foreach (var p in (chord ?? "").Split('+'))
                if (!KeyCheck.Empty(p)) parts.Add(p.Trim());
            return parts;
        }

        public static string Label(string chord, bool playStation)
        {
            var parts = Parts(chord);
            if (parts.Count == 0) return L.T("key.none");
            var labels = new List<string>();
            foreach (var p in parts) labels.Add(KeyCheck.Label(p, playStation));
            return string.Join(" + ", labels);
        }

        // the game's play actions a chord would also fire: one button alone = every play action on it; a chord of two or
        // more = the play actions on its buttons other than the harmless ones
        public static List<string> Clashes(string chord, IList<GameBinding> game)
        {
            var found = new List<string>();
            var parts = Parts(chord);
            if (parts.Count == 0 || game == null) return found;
            foreach (var g in game)
            {
                if (g.Map != KeyCheck.PlayMap) continue;
                if (parts.Count > 1 && Harmless.Contains(g.Action)) continue;
                foreach (var p in parts)
                    if (KeyCheck.Overlap(p, g.Path) && !found.Contains(g.Action)) found.Add(g.Action);
            }
            return found;
        }
    }
}

// GamesOMG Teleport 0.3.0: key and button clashes, and how a binding reads on screen. Two checks (the brief: "Detect
// duplicate bindings within the mod and against the game's own bindings (read them from the game code)"):
//   · inside the mod: two of our actions on one key or button;
//   · against the game: the game's own gameplay bindings ("PlayerMovable", the map that is live while you play and our
//     keys listen), read at run time from its input asset with the player's own rebinds (Plugin), or from build 26411416's
//     serialized PlayerActions (tests/bindings-all.json) in the tests.
// Other mods can still clash; we say so instead of claiming to see them. No Unity here.
using System;
using System.Collections.Generic;

namespace GamesOMG.PlanetCrafterTeleport
{
    internal sealed class GameBinding
    {
        public string Map = "", Action = "", Path = "";
    }

    internal sealed class Clash
    {
        public string Ours = "", With = "", Path = "";
        public bool Game;   // false: two of our own actions
    }

    internal static class KeyCheck
    {
        public const string PlayMap = "PlayerMovable";

        // "<Keyboard>/F7" -> "<keyboard>/f7"; "<XInputController>/buttonSouth" stays its own device
        public static string Norm(string path) => (path ?? "").Trim().Replace(" ", "").ToLowerInvariant();

        public static bool Empty(string path) => Norm(path).Length == 0;

        // one control path covers another when it is the same control or a parent of it ("<gamepad>/dpad" covers
        // "<gamepad>/dpad/up"); a binding on any gamepad ("<gamepad>/…") meets one on a named pad's same control
        public static bool Overlap(string a, string b)
        {
            a = Norm(a); b = Norm(b);
            if (a.Length == 0 || b.Length == 0 || a.IndexOf('{') >= 0 || b.IndexOf('{') >= 0 || a.IndexOf('/') < 0 || b.IndexOf('/') < 0) return false;
            if (a == b || b.StartsWith(a + "/", StringComparison.Ordinal) || a.StartsWith(b + "/", StringComparison.Ordinal)) return true;
            string ac = a.Substring(a.IndexOf('/')), bc = b.Substring(b.IndexOf('/'));
            bool pads = IsPad(a) && IsPad(b);
            return pads && (ac == bc || bc.StartsWith(ac + "/", StringComparison.Ordinal) || ac.StartsWith(bc + "/", StringComparison.Ordinal));
        }

        public static bool IsPad(string path)
        {
            string p = Norm(path);
            return p.StartsWith("<gamepad>", StringComparison.Ordinal) || p.StartsWith("<xinputcontroller>", StringComparison.Ordinal)
                || p.StartsWith("<dualshockgamepad>", StringComparison.Ordinal) || p.StartsWith("<dualsensegamepad>", StringComparison.Ordinal)
                || p.StartsWith("<switchprocontrollerhid>", StringComparison.Ordinal);
        }

        // ours: action name -> path (an empty path is "not set" and never clashes)
        public static List<Clash> Find(IList<KeyValuePair<string, string>> ours, IList<GameBinding> game)
        {
            var found = new List<Clash>();
            for (int i = 0; i < ours.Count; i++)
            {
                if (Empty(ours[i].Value)) continue;
                for (int j = i + 1; j < ours.Count; j++)
                    if (Overlap(ours[i].Value, ours[j].Value))
                    {
                        found.Add(new Clash { Ours = ours[i].Key, With = ours[j].Key, Path = ours[i].Value });
                        found.Add(new Clash { Ours = ours[j].Key, With = ours[i].Key, Path = ours[j].Value });
                    }
                if (game == null) continue;
                var named = new HashSet<string>(StringComparer.Ordinal);
                foreach (var g in game)
                    if (g.Map == PlayMap && Overlap(ours[i].Value, g.Path) && named.Add(g.Action))
                        found.Add(new Clash { Ours = ours[i].Key, With = g.Action, Path = g.Path, Game = true });
            }
            return found;
        }

        // 0.3.3 (the 0.3.0 sitting: F8 was accepted as the Places key; the changelog promised a refusal): may `action` take
        // `path`? null = yes; else the clash that stops it: one of our other actions first, then the game's own play binding
        // (with the player's rebinds, read live). An empty path ("not set") always may.
        public static Clash Refuse(string action, string path, IList<KeyValuePair<string, string>> ours, IList<GameBinding> game)
        {
            if (Empty(path)) return null;
            var mine = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>(action, path) };
            foreach (var kv in ours) if (kv.Key != action) mine.Add(kv);
            Clash gameClash = null;
            foreach (var c in Find(mine, game))
            {
                if (c.Ours != action) continue;
                if (!c.Game) return c;
                if (gameClash == null) gameClash = c;
            }
            return gameClash;
        }

        // the game's own action names, as a player reads them ("ShowFeedback" -> "Show feedback")
        public static string Words(string action)
        {
            if (string.IsNullOrEmpty(action)) return "";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < action.Length; i++)
            {
                char c = action[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(action[i - 1])) sb.Append(' ').Append(char.ToLowerInvariant(c));
                else sb.Append(c);
            }
            return sb.ToString();
        }

        // how a binding reads in a prompt: "F7", "Num 5", "A" / "Cross", "View" / "Share"
        public static string Label(string path, bool playStation = false)
        {
            string p = Norm(path);
            if (p.Length == 0) return L.T("key.none");
            int slash = p.IndexOf('/');
            string control = slash >= 0 ? p.Substring(slash + 1) : p;
            if (IsPad(p))
            {
                switch (control)
                {
                    case "buttonsouth": return playStation ? "Cross" : "A";
                    case "buttoneast": return playStation ? "Circle" : "B";
                    case "buttonwest": return playStation ? "Square" : "X";
                    case "buttonnorth": return playStation ? "Triangle" : "Y";
                    case "leftshoulder": return playStation ? "L1" : "LB";
                    case "rightshoulder": return playStation ? "R1" : "RB";
                    case "lefttrigger": return playStation ? "L2" : "LT";
                    case "righttrigger": return playStation ? "R2" : "RT";
                    case "leftstickpress": return playStation ? "L3" : "LS";
                    case "rightstickpress": return playStation ? "R3" : "RS";
                    case "select": return playStation ? "Share" : "View";
                    case "start": return playStation ? "Options" : "Menu";
                    case "dpad/up": return L.T("key.dpadUp");
                    case "dpad/down": return L.T("key.dpadDown");
                    case "dpad/left": return L.T("key.dpadLeft");
                    case "dpad/right": return L.T("key.dpadRight");
                }
                return control;
            }
            switch (control)
            {
                case "escape": return "Esc";
                case "enter": return "Enter";
                case "numpadenter": return "Num Enter";
                case "backquote": return "`";
                case "space": return L.T("key.space");
                case "tab": return "Tab";
                case "backspace": return "Backspace";
                case "leftshift": case "shift": return "Shift";
                case "leftctrl": case "ctrl": return "Ctrl";
                case "leftalt": case "alt": return "Alt";
                case "pageup": return "Page Up";
                case "pagedown": return "Page Down";
                case "insert": return "Insert";
                case "delete": return "Delete";
                case "home": return "Home";
                case "end": return "End";
            }
            if (control.StartsWith("numpad", StringComparison.Ordinal) && control.Length == 7) return "Num " + control.Substring(6);
            if (control.StartsWith("digit", StringComparison.Ordinal) && control.Length == 6) return control.Substring(5);
            return control.ToUpperInvariant();
        }
    }
}

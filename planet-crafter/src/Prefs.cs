// GamesOMG Teleport 0.3.0: the settings screen's model (ChatGPT's review 2026-11-19 § "The settings screen"). Every setting
// is a BepInEx config entry (the same .cfg file a player can still edit); this only lists them by section with their
// defaults, so each section's "Restore defaults" resets exactly that section. A preference reset NEVER touches places,
// found marks or progress: Prefs holds no reference to them (tests: a reset leaves the places file byte for byte). No Unity
// or BepInEx here: the plugin wires Get/Set to its ConfigEntry objects, the tests to a dictionary.
using System;
using System.Collections.Generic;

namespace GamesOMG.PlanetCrafterTeleport
{
    internal sealed class Pref
    {
        public string Section = "", Key = "", Default = "";
        public Func<string> Get;
        public Action<string> Set;
        public string Id => Section + "." + Key;
    }

    internal sealed class Prefs
    {
        public const string Controls = "Controls", Appearance = "Appearance", Behaviour = "Behaviour", State = "State";
        // the sections the screen resets, one button each; State (the first-run tip seen) is not a preference
        public static readonly string[] Resettable = { Controls, Appearance, Behaviour };

        public readonly List<Pref> All = new List<Pref>();

        public Pref Add(string section, string key, string def, Func<string> get, Action<string> set)
        {
            var p = new Pref { Section = section, Key = key, Default = def ?? "", Get = get, Set = set };
            All.Add(p);
            return p;
        }

        public Pref Find(string id) => All.Find(p => p.Id == id);

        public bool IsDefault(string section)
        {
            foreach (var p in All) if (p.Section == section && p.Get() != p.Default) return false;
            return true;
        }

        // one section back to its defaults; how many values changed
        public int Reset(string section)
        {
            if (Array.IndexOf(Resettable, section) < 0) return 0;
            int changed = 0;
            foreach (var p in All)
            {
                if (p.Section != section || p.Get() == p.Default) continue;
                p.Set(p.Default);
                changed++;
            }
            return changed;
        }

        // for an export that asked for settings: every resettable section, by id
        public Dictionary<string, string> Snapshot()
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var p in All) if (Array.IndexOf(Resettable, p.Section) >= 0) d[p.Id] = p.Get();
            return d;
        }

        // ── 0.3.2: moving a .cfg onto new defaults. BepInEx writes every default into the file on the first run, so a
        // changed default never reaches anyone who already ran the mod. Each step lists the settings whose default changed:
        // a value still on the OLD default (never changed, as far as the file can tell) moves to the new one; any other
        // value is the player's choice and stays. State.ConfigVersion records the last step taken, so each runs once. ──
        public const int ConfigVersion = 2;

        private static readonly (int Step, string Id, string Was, string Now)[] Moves =
        {
            // 0.3.2 (player feedback 2026-11-21 08:19): compact rows, and one click teleports again as in 0.2.x
            (2, Appearance + ".Rows", "Comfortable", "Compact"),
            (2, Behaviour + ".RowClick", "Select", "Teleport"),
        };

        // the lines for the log, one per setting moved; nothing when the file is already at this version
        public static List<string> Migrate(int from, Func<string, string> get, Action<string, string> set)
        {
            var said = new List<string>();
            foreach (var m in Moves)
            {
                if (from >= m.Step) continue;
                string now = get(m.Id);
                if (now != m.Was) continue;
                set(m.Id, m.Now);
                said.Add(m.Id + " " + m.Was + " -> " + m.Now + " (the old default; settings version " + from + " -> " + m.Step + ")");
            }
            return said;
        }

        // an import that asked for its settings: only ids this version knows; unknown ones are left out, never invented
        public int Apply(IDictionary<string, string> values)
        {
            if (values == null) return 0;
            int changed = 0;
            foreach (var kv in values)
            {
                var p = Find(kv.Key);
                if (p == null || Array.IndexOf(Resettable, p.Section) < 0 || p.Get() == kv.Value) continue;
                p.Set(kv.Value ?? "");
                changed++;
            }
            return changed;
        }
    }
}

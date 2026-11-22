// GamesOMG Teleport 0.3.2 (player feedback 2026-11-21 08:19, "we can do it now" 08:33): the Unity-free half. Notes on places (kept
// per save beside the found marks, schema 4, the v3 file backed up first), and the .cfg moving onto the new defaults
// (compact rows, one click teleports) only where it still held 0.3.0/0.3.1's untouched default.
using System;
using System.Collections.Generic;
using System.IO;
using GamesOMG.PlanetCrafterTeleport;

internal static class Polish032
{
    public static List<string> Run()
    {
        var fails = new List<string>();
        void Expect(bool ok, string name) { if (!ok) fails.Add(name); }
        Func<string, bool> allThere = _ => true;
        var clock = new DateTime(2026, 11, 21, 9, 0, 0);
        Func<DateTime> tick = () => { clock = clock.AddSeconds(1); return clock; };
        string dir = Path.Combine(Path.GetTempPath(), "c56-032-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // ── N. notes ──
            var s = new PlaceStore();
            s.Use("Survival-3", 7u, allThere);
            var home = s.Save("prime", "Home", 1, 2, 3);
            s.For("prime").Visited.Add("w2");
            Expect(s.SetNote("prime", "w1", "  come   back\twith the card ") == "come back with the card", "N1 a note is one clean line");
            Expect(s.SetNote("prime", "w2", "locked") == "locked" && s.For("prime").Visited.Contains("w2"), "N2 a note on a found place keeps it found");
            PlaceStore.SetNote(home, "my base");
            s.For("humble").Visited.Add("w9");
            string json = s.ToJson();
            var back = PlaceStore.FromJson(json);
            back.Use("Survival-3", 7u, allThere);
            Expect(back.NoteOf("prime", "w1") == "come back with the card" && back.NoteOf("prime", "w2") == "locked", "N3 map-place notes survive the file");
            Expect(back.For("prime").Saved.Find(p => p.Name == "Home")?.Note == "my base", "N4 a place's own note survives the file");
            Expect(back.Unreadable == 0 && json.Contains("\"v\":4"), "N5 written as schema 4, read back whole");
            Expect(!back.For("prime").Visited.Contains("w1"), "N6 a note never marks a place found");
            Expect(s.SetNote("prime", "w1", "   ") == "" && s.NoteOf("prime", "w1") == "" && !s.ToJson().Contains("come back"), "N7 saving an empty box deletes the note");
            s.For("prime").Visited.Remove("w2");
            Expect(s.NoteOf("prime", "w2") == "locked", "N8 unmarking found keeps the note too");
            Expect(s.SetNote("prime", "w3", new string('x', 200)).Length == PlaceStore.NoteMax, "N9 a note is cut to its length");
            var onlyNote = new PlaceStore();
            onlyNote.Use("Custom-1", 3u, allThere);
            onlyNote.SetNote("prime", "w4", "later");
            Expect(PlaceStore.FromJson(onlyNote.ToJson()).SaveNames.Contains("Custom-1"), "N10 a save holding only a note is still written");
            var bad = PlaceStore.FromJson("{\"v\":4,\"saves\":{\"A\":{\"planets\":{\"prime\":{\"saved\":[],\"recent\":[],\"visited\":[],\"notes\":{\"w1\":5,\"w2\":\"ok\"}}}}}}");
            bad.Use("A", 0u, allThere);
            Expect(bad.Unreadable == 1 && bad.NoteOf("prime", "w2") == "ok", "N11 a note that is not text is counted unreadable, never dropped quietly");

            // ── M. a 0.3.1 (v3) file: backed up byte for byte, then written as v4 with its notes ──
            string v3 = "{\"v\":3,\"saves\":{\"Survival-3\":{\"seed\":7,\"planets\":{\"prime\":{\"saved\":[{\"n\":\"Home\",\"x\":1,\"y\":2,\"z\":3,\"fav\":true}],\"recent\":[],\"visited\":[\"w5\"]}}}}}";
            string places = Path.Combine(dir, "places.json");
            File.WriteAllText(places, v3);
            var lib = new Library(places, Path.Combine(dir, "data"), new Prefs(), null, tick);
            lib.Bind("Survival-3", 7u, allThere, out _);
            var taken = new Backups(lib.BackupsDir).List();
            Expect(taken.Count == 1 && taken[0].Reason == "before-migration" && File.ReadAllText(taken[0].Path) == v3, "M1 the v3 file is backed up byte for byte before the first v4 write");
            lib.Store.SetNote("prime", "w5", "come back later");
            Expect(lib.Save() == null && File.ReadAllText(places).Contains("\"notes\":{\"w5\":\"come back later\"}"), "M2 then the note is written beside the found marks");
            var again = new Library(places, Path.Combine(dir, "data"), new Prefs(), null, tick);
            again.Bind("Survival-3", 7u, allThere, out _);
            Expect(again.Store.NoteOf("prime", "w5") == "come back later" && again.Store.For("prime").Visited.Contains("w5")
                && again.Store.For("prime").Saved[0].Fav, "M3 notes, found marks and favourites all come back");

            // ── T. export / import carry notes; a merge keeps yours ──
            var mine = new PlaceStore();
            mine.Use("Survival-3", 7u, allThere);
            mine.SetNote("prime", "w1", "mine");
            var theirs = new PlaceStore();
            theirs.Use("Other", 9u, allThere);
            theirs.SetNote("prime", "w1", "theirs");
            theirs.SetNote("prime", "w2", "new one");
            var file = new ExportFile { Store = theirs };
            var plan = Transfer.Plan(file, "Other", mine, "Survival-3");
            Expect(plan.NewNotes == 1, "T1 the preview counts only the notes you don't have");
            Transfer.Apply(plan, mine, false);
            Expect(mine.NoteOf("prime", "w1") == "mine" && mine.NoteOf("prime", "w2") == "new one", "T2 a merge adds their notes and keeps yours");
            var rep = new PlaceStore();
            rep.Use("Survival-3", 7u, allThere);
            Transfer.Apply(Transfer.Plan(file, "Other", rep, "Survival-3"), rep, true);
            Expect(rep.NoteOf("prime", "w1") == "theirs", "T3 a replace takes their notes");

            // ── P. the .cfg onto 0.3.2's defaults: only values still on the old default, once ──
            Dictionary<string, string> Cfg(string rows, string click) => new Dictionary<string, string>
            {
                ["Appearance.Rows"] = rows, ["Behaviour.RowClick"] = click, ["Appearance.TextSize"] = "100",
            };
            var his = Cfg("Comfortable", "Select");   // what 0.3.0/0.3.1 wrote on their first run
            var lines = Prefs.Migrate(0, k => his.TryGetValue(k, out var v) ? v : null, (k, v) => his[k] = v);
            Expect(his["Appearance.Rows"] == "Compact" && his["Behaviour.RowClick"] == "Teleport" && lines.Count == 2, "P1 the old defaults move to compact and one-click teleport");
            Expect(his["Appearance.TextSize"] == "100", "P2 nothing else is touched");
            var chose = Cfg("Compact", "Select");
            chose["Behaviour.RowClick"] = "Teleport";
            chose["Appearance.Rows"] = "Comfortable";
            var later = Prefs.Migrate(Prefs.ConfigVersion, k => chose[k], (k, v) => chose[k] = v);
            Expect(later.Count == 0 && chose["Appearance.Rows"] == "Comfortable", "P3 a file already at this version is never moved again (a choice made after stays)");
            var fresh = Cfg("Compact", "Teleport");
            Expect(Prefs.Migrate(0, k => fresh[k], (k, v) => fresh[k] = v).Count == 0, "P4 a new install is already on the new defaults");
            var custom = Cfg("Compact", "Select");
            Prefs.Migrate(0, k => custom[k], (k, v) => custom[k] = v);
            Expect(custom["Behaviour.RowClick"] == "Teleport" && custom["Appearance.Rows"] == "Compact", "P5 each setting moves on its own");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (Exception) { }
        }
        return fails;
    }
}

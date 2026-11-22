// GamesOMG Teleport 0.3.0 (the look, controls, settings and data pass, 2026-11-19, before the first public release): the
// Unity-free half, end to end on real temp files. Soft checks: one run lists every failure, so tests/mutate.mjs can plant a
// bug in src/ and show the check that catches it going red (each mutation names the check it expects to fail).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GamesOMG.PlanetCrafterTeleport;

internal static class Polish030
{
    public static List<string> Run(Func<string, PoiWorld> pack)
    {
        var fails = new List<string>();
        int n = 0;
        void Expect(bool ok, string name) { n++; if (!ok) fails.Add(name); }
        string Temp() { string d = Path.Combine(Path.GetTempPath(), "c56-030-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d); return d; }
        Func<string, bool> allThere = _ => true;
        var clock = new DateTime(2026, 11, 19, 20, 0, 0);
        Func<DateTime> tick = () => { clock = clock.AddSeconds(1); return clock; };
        var dirs = new List<string>();

        try
        {
            // ── A. the store: favourites, rename, Delete with Undo, scoped clears, automatic names, a named Return spot ──
            var s = new PlaceStore();
            s.Use("Custom-1", 11u, allThere);
            var home = s.Save("prime", "Home", 1, 2, 3);
            var lake = s.Save("prime", "Lake", 10, 20, 30);
            s.Save("prime", "Ridge", 100, 50, 300);
            home.Fav = true;
            s.For("prime").Visited.Add("w1");
            s.For("humble").Visited.Add("w2");
            s.AddRecent("prime", "Trip", 5, 5, 5);
            s.Teleported("prime", 7, 8, 9, "Iridium base");
            var back = PlaceStore.FromJson(s.ToJson());
            back.Use("Custom-1", 11u, allThere);
            Expect(s.ToJson().Contains("\"v\":" + PlaceStore.FileVersion) && back.ReadVersion == PlaceStore.FileVersion, "A1 the file is the current schema and says so");
            Expect(back.For("prime").Saved.Find(p => p.Name == "Home")?.Fav == true && back.For("prime").Saved.Find(p => p.Name == "Lake")?.Fav == false,
                "A2 a favourite survives the file, and only it");
            Expect(back.Back("prime")?.Name == "Iridium base", "A3 the Return spot keeps its name in the file");
            Expect(s.Rename("prime", lake, "  Home ") == "Home 2" && lake.Name == "Home 2" && s.Rename("prime", lake, "   ") == null && lake.Name == "Home 2",
                "A4 rename: a taken name gets a number, an empty one changes nothing");
            Expect(s.Rename("prime", lake, "Home 2") == "Home 2", "A5 renaming to its own name is not a clash with itself");
            var gone = s.Delete("prime", lake);
            Expect(gone != null && !s.For("prime").Saved.Contains(lake) && gone.Index == 1, "A6 delete takes it out and remembers where it was");
            Expect(s.Undo(gone) && s.For("prime").Saved.IndexOf(lake) == 1, "A7 Undo puts it back in the same place");
            gone = s.Delete("prime", lake);
            s.Save("prime", "Home 2", 0, 0, 0);
            Expect(!s.Undo(gone) && s.For("prime").Saved.Count(p => p.Name == "Home 2") == 1, "A8 Undo refuses when its name was taken meanwhile, changing nothing");
            var r = s.For("prime").Recent[0];
            var goneRecent = s.Delete("prime", r);
            Expect(goneRecent != null && goneRecent.Recent && s.Undo(goneRecent) && s.For("prime").Recent[0] == r, "A9 a Recently travelled row deletes and undoes too");
            Expect(s.ClearFound("prime") == 1 && s.For("prime").Visited.Count == 0 && s.For("humble").Visited.Count == 1,
                "A10 clearing found marks on Prime leaves Humble's");
            s.Use("Custom-3", 33u, allThere);
            s.For("prime").Visited.Add("w9");
            s.Use("Custom-1", 11u, allThere);
            Expect(s.ClearSaved("prime") == 3 && s.ClearRecent("prime") == 1 && s.For("prime").Saved.Count == 0, "A11 clearing saved places and Recent counts what went");
            s.Use("Custom-3", 33u, allThere);
            Expect(s.For("prime").Visited.Contains("w9"), "A12 a clear in one save leaves another save's data");
            var prime = pack("prime");
            string near = PlaceStore.AutoName("prime", 785, 795, (x, z) => prime.Closest(x, z, 60) is Poi p ? prime.NameOf(p) : null);
            string far = PlaceStore.AutoName("prime", 99999, -5, (x, z) => prime.Closest(x, z, 60) is Poi p ? prime.NameOf(p) : null);
            Expect(near.StartsWith("Near ") && far == "Prime 99999, -5", "A13 Save here's automatic name: the nearest map place, else the planet and spot (" + near + " / " + far + ")");
            var tree = new PlacesTree(prime.StartLayers()) { ByName = true };
            var az = tree.Rows(prime, "crate", 0, 0, new HashSet<string>(), false).Where(x => x.Kind == RowKind.Place).Select(x => prime.NameOf(x.Poi)).ToList();
            Expect(az.Count > 2 && az.SequenceEqual(az.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)), "A14 Sort A-Z orders a search by name");

            // ── B. rotating backups ──
            string bd = Temp(); dirs.Add(bd);
            var b = new Backups(Path.Combine(bd, "backups"), tick);
            string firstPath = null;
            for (int i = 0; i < Backups.Keep + 3; i++) { Expect(b.Take("{\"i\":" + i + "}", "before-clear", out var p) == null, "B1 a backup writes"); if (i == 0) firstPath = p; }
            var list = b.List();
            Expect(list.Count == Backups.Keep, "B2 rotation keeps exactly the newest " + Backups.Keep + " (" + list.Count + ")");
            Expect(File.ReadAllText(list[0].Path) == "{\"i\":" + (Backups.Keep + 2) + "}" && !File.Exists(firstPath), "B3 the newest stays first, the oldest went");
            Expect(list.TrueForAll(x => x.Reason == "before-clear"), "B4 each says why it was taken");
            var same = new Backups(Path.Combine(bd, "same"), () => clock);
            same.Take("a", "x", out var p1); same.Take("b", "x", out var p2);
            Expect(p1 != p2 && same.List().Count == 2 && File.ReadAllText(same.List()[0].Path) == "b", "B5 two in one second both stay, newest first");
            Expect(Backups.Slug("Before import: “Frog eggs” base") == "before-import-frog-eggs-base" && Backups.Slug("") == "manual", "B6 a reason becomes a safe file name");

            // ── C. the library: migration, bulk clears, restore, settings resets ──
            string v1 = "{\"v\":1,\"planets\":{\"prime\":{\"saved\":[{\"n\":\"home\",\"x\":1,\"y\":2,\"z\":3}],\"recent\":[],\"visited\":[\"w9\"]}}}";
            string v2 = "{\"v\":2,\"saves\":{\"Custom-1\":{\"seed\":11,\"planets\":{\"prime\":{\"saved\":[{\"n\":\"base\",\"x\":4,\"y\":5,\"z\":6}],\"recent\":[],\"visited\":[\"w5\"]}},\"back\":{\"w\":\"prime\",\"x\":1,\"y\":2,\"z\":3}}}}";
            foreach (var (label, text, save) in new[] { ("0.2.4/0.2.5", v1, "Custom-1"), ("0.2.6", v2, "Custom-1") })
            {
                string d = Temp(); dirs.Add(d);
                string places = Path.Combine(d, "places.json");
                File.WriteAllText(places, text);
                var lib = new Library(places, Path.Combine(d, "data"), new Prefs(), null, tick);
                lib.Bind(save, 11u, allThere, out var bindFailed);
                var taken = new Backups(lib.BackupsDir).List();
                Expect(taken.Count == 1 && taken[0].Reason == "before-migration" && File.ReadAllText(taken[0].Path) == text, "C1 " + label + " data is backed up byte for byte before the first write over it");
                Expect(lib.Save() == null && File.ReadAllText(places).Contains("\"v\":" + PlaceStore.FileVersion), "C2 " + label + " then saves as the current schema");
                var again = new Library(places, Path.Combine(d, "data"), new Prefs(), null, tick);
                again.Bind(save, 11u, allThere, out _);
                var pp = again.Store.For("prime");
                Expect(pp.Saved.Count == 1 && pp.Visited.Count == 1 && (label == "0.2.6" ? again.Store.Back("prime") != null : true), "C3 " + label + " places, found marks and Return all came through");
                Expect(new Backups(again.BackupsDir).List().Count == 1, "C4 " + label + " a schema-3 file is not backed up again on every load");
            }
            {
                string d = Temp(); dirs.Add(d);
                string places = Path.Combine(d, "places.json");
                File.WriteAllText(places, v2);
                Directory.CreateDirectory(Path.Combine(d, "data"));
                File.WriteAllText(Path.Combine(d, "data", "backups"), "a file where the backups folder should be");
                var lib = new Library(places, Path.Combine(d, "data"), new Prefs(), null, tick);
                string note = lib.TakeLoadNote();
                lib.Store.Save("prime", "new", 0, 0, 0);
                Expect(note != null && lib.Save() != null && File.ReadAllText(places) == v2, "C5 when the migration backup can't be taken, the old file is never written over");
            }
            // bulk clears back up first, and refuse when they can't
            string cd = Temp(); dirs.Add(cd);
            string cplaces = Path.Combine(cd, "places.json");
            var prefsDict = new Dictionary<string, string>();
            var prefs = new Prefs();
            foreach (var (sec, key, def) in new[] { (Prefs.Controls, "PanelKey", "<Keyboard>/f3"), (Prefs.Controls, "PadPanel", "<Gamepad>/select"), (Prefs.Appearance, "TextSize", "100"),
                (Prefs.Appearance, "Side", "Right"), (Prefs.Behaviour, "RowClick", "Select"), (Prefs.Behaviour, "Sounds", "false"), (Prefs.State, "FirstRunShown", "false") })
            {
                string id = sec + "." + key;
                prefsDict[id] = def;
                prefs.Add(sec, key, def, () => prefsDict[id], v => prefsDict[id] = v);
            }
            var clib = new Library(cplaces, Path.Combine(cd, "data"), prefs, null, tick);
            clib.Bind("Custom-1", 11u, allThere, out _);
            clib.Store.Save("prime", "Home", 1, 2, 3).Fav = true;
            clib.Store.Save("prime", "Lake", 10, 20, 30);
            clib.Store.For("prime").Visited.UnionWith(new[] { "w1", "w2" });
            clib.Store.AddRecent("prime", "Trip", 5, 5, 5);
            Expect(clib.Save() == null, "C6 a fresh library saves");
            // settings resets never touch places (the brief: "no preference reset ever touches places or progress")
            string before = File.ReadAllBytes(cplaces).Length + ":" + File.ReadAllText(cplaces);
            string storeBefore = clib.Store.ToJson();
            prefsDict["Controls.PanelKey"] = "<Keyboard>/f4"; prefsDict["Appearance.TextSize"] = "140"; prefsDict["Behaviour.Sounds"] = "true"; prefsDict["State.FirstRunShown"] = "true";
            int ra = clib.ResetPrefs(Prefs.Appearance);
            Expect(ra == 1 && prefsDict["Appearance.TextSize"] == "100" && prefsDict["Controls.PanelKey"] == "<Keyboard>/f4" && prefsDict["Behaviour.Sounds"] == "true",
                "C7 restore appearance defaults resets appearance only");
            int rc = clib.ResetPrefs(Prefs.Controls), rb = clib.ResetPrefs(Prefs.Behaviour), rs = clib.ResetPrefs(Prefs.State);
            Expect(rc == 1 && rb == 1 && rs == 0 && prefsDict["Controls.PanelKey"] == "<Keyboard>/f3" && prefsDict["Behaviour.Sounds"] == "false" && prefsDict["State.FirstRunShown"] == "true",
                "C8 each section resets on its own; the first-run state is not a preference");
            clib.Save();
            Expect(File.ReadAllBytes(cplaces).Length + ":" + File.ReadAllText(cplaces) == before && clib.Store.ToJson() == storeBefore,
                "C9 a settings reset leaves the places file and the places themselves untouched");
            // a bulk clear: backup first, the scope only
            int bk0 = clib.ListBackups().Count;
            Expect(clib.Clear(Library.Bulk.Found, "prime", out int cleared) == null && cleared == 2 && clib.Store.For("prime").Visited.Count == 0
                && clib.Store.For("prime").Saved.Count == 2, "C10 clear found marks on Prime clears those and nothing else");
            var kept = clib.ListBackups();
            Expect(kept.Count == bk0 + 1 && kept[0].Reason == "before-clear-found" && File.ReadAllText(kept[0].Path).Contains("\"w1\""), "C11 the clear took a backup first, holding the marks");
            Expect(clib.Clear(Library.Bulk.Found, "prime", out int none) == null && none == 0 && clib.ListBackups().Count == bk0 + 1, "C12 clearing nothing takes no backup");
            // Delete with Undo is saved straight away both ways
            var lakeP = clib.Store.For("prime").Saved.Find(p => p.Name == "Lake");
            var del = clib.Delete("prime", lakeP, out var delFail);
            Expect(del != null && delFail == null && !File.ReadAllText(cplaces).Contains("Lake"), "C13 a delete is saved at once");
            Expect(clib.Undo(del, out var undoFail) && undoFail == null && File.ReadAllText(cplaces).Contains("Lake"), "C14 and its Undo too");
            // restore: the current state is backed up first, a broken backup changes nothing, the save in play stays bound
            Expect(clib.Restore(kept[0], out var restored) == null && clib.Store.For("prime").Visited.Contains("w1") && clib.Store.Current == "Custom-1",
                "C15 restore brings the backup back, still on the save in play");
            Expect(clib.ListBackups()[0].Reason == "before-restore", "C16 restore backed up what was there first");
            string brokenPath = Path.Combine(clib.BackupsDir, "places-20990101-000000-broken.json");
            File.WriteAllText(brokenPath, "{\"v\":3,\"saves\":{broken");
            string pre = File.ReadAllText(cplaces);
            var brokenInfo = clib.ListBackups().Find(x => x.Path == brokenPath);
            Expect(brokenInfo != null && clib.Restore(brokenInfo, out _) != null && File.ReadAllText(cplaces) == pre && clib.Store.For("prime").Visited.Contains("w1"),
                "C17 a backup that won't read changes nothing");
            File.Delete(brokenPath);
            // a clear refuses when its backup can't be written
            string blockDir = clib.BackupsDir;
            foreach (var f in Directory.GetFiles(blockDir)) File.Delete(f);
            Directory.Delete(blockDir);
            File.WriteAllText(blockDir, "blocked");
            int foundNow = clib.Store.For("prime").Visited.Count;
            Expect(clib.Clear(Library.Bulk.Found, "prime", out int refused) != null && refused == 0 && clib.Store.For("prime").Visited.Count == foundNow,
                "C18 no backup, no clear");
            File.Delete(blockDir);

            // ── D. export / import ──
            string xd = Temp(); dirs.Add(xd);
            var src = new PlaceStore();
            src.Use("Old PC save", 77u, allThere);
            src.Save("prime", "Home", 1, 2, 3).Fav = true;                 // = yours: a duplicate (fav added)
            src.Save("prime", "Lake", 500, 20, 500);                       // ≠ yours: a conflict
            src.Save("prime", "Cave mouth", 797, 150, 1099);               // new
            var auto = src.Save("humble", "Dune", 4, 0, 6); auto.AutoHeight = true;
            src.For("prime").Visited.UnionWith(new[] { "w1", "w7" });
            src.Use("Second", 88u, allThere);
            src.Save("selenea", "Elsewhere", 9, 9, 9);
            string exported = Transfer.Export(src, "0.3.0", new DateTime(2026, 11, 19, 21, 0, 0, DateTimeKind.Utc), null);
            var file = Transfer.Read(exported);
            Expect(file.Saves.Count == 2 && file.Saves.Contains("Old PC save") && file.Settings == null && file.Mod == "0.3.0", "D1 an export holds every save, its mod version, and no settings unless asked");
            var fp = file.Store.SaveOf("Old PC save");
            Expect(fp.Planets["prime"].Saved.Find(p => p.Name == "Cave mouth")?.Y == 150 && fp.Planets["humble"].Saved[0].AutoHeight && fp.Planets["prime"].Visited.Contains("w7")
                && fp.Planets["prime"].Saved.Find(p => p.Name == "Home").Fav, "D2 real heights, ~ heights, world ids, found ids and favourites all travel");
            var withSettings = Transfer.Read(Transfer.Export(src, "0.3.0", DateTime.UtcNow, prefs.Snapshot()));
            Expect(withSettings.Settings != null && withSettings.Settings.ContainsKey("Controls.PanelKey") && !withSettings.Settings.ContainsKey("State.FirstRunShown"),
                "D3 settings travel only when asked, and only preferences");
            // malformed: every one refused, nothing changes
            string preImport = File.ReadAllText(cplaces), storePre = clib.Store.ToJson();
            foreach (var bad in new[] { "", "not json", "{\"format\":\"something-else\",\"schema\":1,\"game\":\"planet-crafter\",\"store\":{\"saves\":{}}}",
                exported.Replace("\"planet-crafter\"", "\"grounded-2\""), exported.Replace("\"schema\":1", "\"schema\":99"), exported.Substring(0, exported.Length / 2),
                "{\"format\":\"gamesomg-teleport-export\",\"schema\":1,\"game\":\"planet-crafter\"}",
                exported.Replace("\"saves\":{", "\"saves\":{\"broken\":7,") })
            {
                string path = Path.Combine(xd, "bad.json");
                File.WriteAllText(path, bad);
                bool refusedIt = false;
                try { var f = clib.ReadImport(path); var plan = clib.Preview(f, f.Saves[0], "Custom-1"); refusedIt = bad.Contains("\"broken\":7") && plan != null; }
                catch (FormatException e) { refusedIt = e.Message.Contains("Nothing changed") || e.Message.Contains("not in the export"); }
                Expect(refusedIt && File.ReadAllText(cplaces) == preImport && clib.Store.ToJson() == storePre, "D4 a malformed import changes nothing: " + (bad.Length > 40 ? bad.Substring(0, 40) : bad));
            }
            // the plan: additions, duplicates, conflicts, into the save the player picked
            var planX = clib.Preview(file, "Old PC save", "Custom-1");
            Expect(planX.Additions.Count == 2 && planX.Additions.Exists(a => a.Value.Name == "Cave mouth") && planX.Additions.Exists(a => a.Key == "humble"),
                "D5 the preview lists what's new");
            Expect(planX.Duplicates.Count == 1 && planX.Duplicates[0].Value.Name == "Home" && planX.NewFavs == 0, "D6 the same name at the same spot is a duplicate");
            Expect(planX.Conflicts.Count == 1 && planX.Conflicts[0].Name == "Lake" && planX.Conflicts[0].Yours.X == 10, "D7 the same name somewhere else is a conflict, with both");
            Expect(planX.NewFound == 1 && planX.SourceWorlds.Contains("prime") && planX.SourceWorlds.Contains("humble") && planX.DestSave == "Custom-1", "D8 new found marks counted; source worlds and the destination named");
            // merge into ANOTHER save than the one in play: only that one changes
            var other = clib.Preview(file, "Old PC save", "Custom-9 (copy)");
            int otherBk = clib.ListBackups().Count;
            Expect(clib.Import(other, false, file, false, out _) == null, "D9 a merge into another save applies");
            Expect(clib.ListBackups().Count == otherBk + 1 && clib.ListBackups()[0].Reason == "before-import", "D10 an import takes a backup first");
            var c9 = clib.Store.SaveOf("Custom-9 (copy)");
            Expect(c9 != null && c9.Planets["prime"].Saved.Count == 3 && clib.Store.Current == "Custom-1" && clib.Store.For("prime").Saved.Count == 2,
                "D11 the destination is the save the player picked, never quietly the one loaded");
            // merge into the save in play: conflicts keep yours, marks union, nothing deleted
            Expect(clib.Import(planX, false, file, false, out _) == null, "D12 a merge applies");
            var cp = clib.Store.For("prime");
            Expect(cp.Saved.Find(p => p.Name == "Lake")?.X == 10 && cp.Saved.Exists(p => p.Name == "Cave mouth") && cp.Saved.Count == 3 && clib.Store.For("humble").Saved.Count == 1,
                "D13 merge adds the new and keeps yours where names clash");
            Expect(cp.Visited.SetEquals(new[] { "w1", "w2", "w7" }) && cp.Saved.Find(p => p.Name == "Home").Fav, "D14 merge unions found marks; a favourite stays one");
            Expect(cp.Recent.Count == 1 && cp.Recent[0].Name == "Trip", "D15 merge leaves your Recently travelled as it was");
            // replace: only when chosen, and then exactly theirs
            var planR = clib.Preview(file, "Second", "Custom-1");
            Expect(planR.DestHasData && planR.TheirPlaces == 1, "D16 a replace preview says what is there and what comes in");
            Expect(clib.Import(planR, true, file, false, out _) == null && clib.Store.For("prime").Saved.Count == 0 && clib.Store.For("selenea").Saved[0].Name == "Elsewhere"
                && clib.ListBackups()[0].Reason == "before-import-replace", "D17 replace makes the save exactly the source, after a backup");
            Expect(clib.Store.SaveOf("Custom-9 (copy)").Planets["prime"].Saved.Count == 3, "D18 replace leaves every other save alone");
            // settings from a file: only when asked
            prefsDict["Controls.PanelKey"] = "<Keyboard>/f3";
            var fileS = Transfer.Read(Transfer.Export(src, "0.3.0", DateTime.UtcNow, new Dictionary<string, string> { ["Controls.PanelKey"] = "<Keyboard>/f6", ["Bogus.Key"] = "x" }));
            clib.Import(clib.Preview(fileS, "Old PC save", "Custom-1"), false, fileS, false, out int notApplied);
            Expect(notApplied == 0 && prefsDict["Controls.PanelKey"] == "<Keyboard>/f3", "D19 an import never takes the file's settings unless asked");
            clib.Import(clib.Preview(fileS, "Old PC save", "Custom-1"), false, fileS, true, out int applied);
            Expect(applied == 1 && prefsDict["Controls.PanelKey"] == "<Keyboard>/f6" && !prefsDict.ContainsKey("Bogus.Key"), "D20 asked: it takes the known settings, never invents one");
            // the export file on disk, and the import list
            Expect(clib.Export("0.3.0", false, out var outPath) == null && File.Exists(outPath) && clib.ImportCandidates().Contains(outPath)
                && Transfer.Read(File.ReadAllText(outPath)).Saves.Contains("Custom-1"), "D21 Export writes a file Import lists and reads back");

            // ── F. keys: clashes inside the mod and with the game's own bindings (build 26411416) ──
            var game = new List<GameBinding>();
            var bj = MiniJson.O(MiniJson.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "bindings-all.json"))));
            foreach (var o in MiniJson.A(bj["bindings"])) { var g = MiniJson.O(o); game.Add(new GameBinding { Map = MiniJson.S(g["map"]), Action = MiniJson.S(g["action"]), Path = MiniJson.S(g["path"]) }); }
            var defaults = new List<KeyValuePair<string, string>> { new("Places", "<Keyboard>/f3"), new("Teleport", "<Keyboard>/f7"), new("Return", "<Keyboard>/f10"), new("Places (controller)", "<Gamepad>/select"), new("Return (controller)", "") };
            Expect(game.Count > 100 && KeyCheck.Find(defaults, game).Count == 0, "F1 the defaults clash with nothing in the game's play bindings or each other");
            var f8 = KeyCheck.Find(new List<KeyValuePair<string, string>> { new("Places", "<Keyboard>/F8") }, game);
            Expect(f8.Count == 1 && f8[0].Game && f8[0].With == "ShowFeedback" && KeyCheck.Words(f8[0].With) == "Show feedback", "F2 F8 is the game's Show feedback");
            var north = KeyCheck.Find(new List<KeyValuePair<string, string>> { new("Places (controller)", "<Gamepad>/buttonNorth") }, game);
            Expect(north.Count == 1 && north[0].With == "OpenChatWindow", "F3 Y on a controller is the game's chat");
            var dup = KeyCheck.Find(new List<KeyValuePair<string, string>> { new("Places", "<Keyboard>/f6"), new("Teleport", "<keyboard>/F6") }, game);
            Expect(dup.Count == 2 && !dup[0].Game && dup[0].With == "Teleport", "F4 two of our own on one key is caught, both ways");
            Expect(KeyCheck.Overlap("<Gamepad>/dpad", "<Gamepad>/dpad/up") && KeyCheck.Overlap("<XInputController>/select", "<Gamepad>/select") && !KeyCheck.Overlap("<Gamepad>/leftStick", "<Gamepad>/leftStickPress"),
                "F5 a parent control and a named pad overlap; a stick and its click do not");
            var dpadUp = KeyCheck.Find(new List<KeyValuePair<string, string>> { new("Return (controller)", "<Gamepad>/dpad/up") }, game);
            Expect(dpadUp.Exists(c => c.With == "OpenInventory"), "F6 D-pad up is the game's inventory");
            Expect(KeyCheck.Label("<Keyboard>/f7") == "F7" && KeyCheck.Label("<Gamepad>/select") == "View" && KeyCheck.Label("<Gamepad>/select", true) == "Share"
                && KeyCheck.Label("<Gamepad>/buttonSouth", true) == "Cross" && KeyCheck.Label("") == "not set" && KeyCheck.Label("<Keyboard>/numpad5") == "Num 5",
                "F7 bindings read as players know them, Xbox and PlayStation");

            // ── G. focus without a mouse ──
            var nav = new Nav();
            void Frame(params string[] ids) { nav.Begin(); foreach (var id in ids) nav.Add(id); nav.End(); }
            Frame("act:clip", "act:save", "row:a", "row:b", "row:c");
            nav.Move(1);
            Expect(nav.Focus == "act:clip" && nav.Visible, "G1 the first key shows focus before it moves it");
            nav.Move(1); nav.Move(1);
            Frame("act:clip", "act:save", "row:new", "row:a", "row:b", "row:c");
            Expect(nav.Focus == "row:a", "G2 focus stays on the same control when the list above it changes");
            Frame("act:clip", "act:save", "row:new", "row:b", "row:c");
            Expect(nav.Focus == "row:b", "G3 a focused row that went away hands focus to the one now in its place");
            nav.Push("dlg:ok");
            Frame("dlg:ok", "dlg:cancel");
            Expect(nav.Focus == "dlg:ok", "G4 a dialog takes focus");
            nav.Pop();
            Frame("act:clip", "act:save", "row:new", "row:b", "row:c");
            Expect(nav.Focus == "row:b", "G5 and gives it back to the same row");
            nav.Last(); nav.Move(1);
            Expect(nav.Focus == "row:c", "G6 the end of the list does not wrap to the top");
            nav.Place("row:c", 900, 42);
            Expect(nav.ScrollFor(0, 400) == 542 && nav.ScrollFor(600, 400) == 600 && Math.Abs(nav.ScrollFor(950, 400) - 900) < .01, "G7 the scroll brings the focused row into view, no further than needed");
            nav.NextArea(1, id => id.Substring(0, id.IndexOf(':')));
            Expect(nav.Focus == "act:clip", "G8 Tab past the last area comes round to the first");

            // ── H. diagnostics leave out the clipboard and your places ──
            var diag = new Diagnostics();
            diag.Note("land.noRoom", 12.4f);
            diag.Note("/tp 785 31 795", 13f);
            diag.Note("Iridium base", 14f);
            string diagText = diag.Text(new List<KeyValuePair<string, string>> { new("Mod", "0.3.0"), new("Game", "2.104") });
            Expect(diagText.Contains("land.noRoom") && !diagText.Contains("785") && !diagText.Contains("Iridium") && diag.Recent.Count == 1, "H1 diagnostics keep message ids only, never a line, a name or a spot");

            // ── I. the string table ──
            Expect(L.N("import.added", 1, "Prime 1") == "Added 1 place from the map (Prime 1)." && L.N("import.added", 2, "Prime 2") == "Added 2 places from the map (Prime 2).", "I1 one and many read right");
            Expect(L.T("no.such.id") == "no.such.id" && L.Has("land.noRoom") && L.Has("import.added"), "I2 a missing id shows itself, never a blank");
        }
        catch (Exception e) { fails.Add("threw " + e.GetType().Name + ": " + e.Message + " " + e.StackTrace); }
        finally { foreach (var d in dirs) try { Directory.Delete(d, true); } catch (Exception) { } }
        Console.WriteLine("0.3.0 checks: " + (n - fails.Count) + "/" + n + " passed");
        return fails;
    }
}

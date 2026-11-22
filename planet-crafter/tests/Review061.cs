// GamesOMG Teleport 0.3.0: the Codex C61 release review's findings that 0.3.0 closes (REVIEW.md on codex/c61-mod-review,
// 2026-11-19), each as checks that were RED on the code it reviewed and are green on the fix. Same soft style as
// Polish030: one run lists every failure, and tests/mutate.mjs plants each bug back and names the check that must catch it.
//   R4  a places file this version can't fully read is never silently replaced
//   R5  a new world under a reused save name starts clean; the old world's data is kept aside
//   R7  the landing check sees the floors the game switches on only near the player (src/DestinationWake.cs)
//   R8  closing our panel never switches gameplay back on under a game window (src/InputHold.cs, against HoldStandIns.cs)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Logging;
using GamesOMG.PlanetCrafterTeleport;
using SpaceCraft;
using UnityEngine.InputSystem;

internal static class Review061
{
    public static List<string> Run()
    {
        var fails = new List<string>();
        int n = 0;
        void Expect(bool ok, string name) { n++; if (!ok) fails.Add(name); }
        var dirs = new List<string>();
        string Temp() { string d = Path.Combine(Path.GetTempPath(), "c56-r061-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(d); dirs.Add(d); return d; }
        Func<string, bool> allThere = _ => true;
        var clock = new DateTime(2026, 11, 19, 21, 0, 0);
        Func<DateTime> tick = () => { clock = clock.AddSeconds(1); return clock; };
        string Places(string text) { string p = Path.Combine(Temp(), "places.json"); File.WriteAllText(p, text); return p; }

        try
        {
            // ── R4. the places file: not ours, partly readable, or from a newer version (F4) ──
            foreach (var text in new[] { "[]", "null", "\"text\"", "5", "{\"v\":\"x\",\"saves\":{}}", "{\"v\":2.5,\"saves\":{}}", "{\"v\":0,\"planets\":{}}" })
            {
                string p = Places(text);
                var file = new PlaceFile(p, null);
                var loaded = file.Load();
                bool kept = File.Exists(p + ".bad") && File.ReadAllText(p + ".bad") == text;
                loaded.Save("prime", "new", 1, 2, 3);
                bool wrote = file.Save(loaded) == null;
                Expect(file.Note != null && kept && wrote && File.ReadAllText(p + ".bad") == text, "R4a a file that is not a places file is kept aside, said on screen, then replaced (" + text + ")");
            }
            foreach (var text in new[] { "{\"v\":2,\"saves\":[]}", "{\"v\":3}", "{\"v\":3,\"saves\":\"Custom-1\"}" })
            {
                string p = Places(text);
                var lib = new Library(p, Path.Combine(Path.GetDirectoryName(p), "data"), new Prefs(), null, tick);
                string note = lib.TakeLoadNote();
                Expect(note != null && File.Exists(p + ".bad") && File.ReadAllText(p + ".bad") == text, "R4b a known version without its saves is kept aside and said, never read as empty (" + text + ")");
            }
            {
                string future = "{\"v\":99,\"saves\":{\"Custom-1\":{\"seed\":5,\"planets\":{\"prime\":{\"saved\":[{\"n\":\"keep me\",\"x\":1,\"y\":2,\"z\":3}]}}}},\"newThing\":{\"x\":1}}";
                string p = Places(future);
                var lib = new Library(p, Path.Combine(Path.GetDirectoryName(p), "data"), new Prefs(), null, tick);
                string note = lib.TakeLoadNote();
                lib.Bind("Custom-1", 5u, allThere, out _);
                bool readable = lib.Store.For("prime").Saved.Any(x => x.Name == "keep me");
                lib.Store.Save("prime", "today", 4, 5, 6);
                string refused = lib.Save();
                Expect(note != null && readable && refused != null && File.ReadAllText(p) == future && !File.Exists(p + ".bad"),
                    "R4c a newer version's file is used but never written over this session, and the screen says why");
            }
            {
                string partial = "{\"v\":3,\"saves\":{\"Custom-1\":{\"seed\":5,\"planets\":{"
                    + "\"prime\":{\"saved\":[{\"n\":\"good\",\"x\":1,\"y\":2,\"z\":3},{\"n\":\"letters\",\"x\":\"abc\",\"y\":2,\"z\":3},"
                    + "{\"n\":\"infinite\",\"x\":1,\"y\":1e400,\"z\":3},{\"n\":\"too far\",\"x\":5000000,\"y\":2,\"z\":3}],\"visited\":\"w1\"},"
                    + "\"humble\":7}}}}";
                string p = Places(partial);
                var lib = new Library(p, Path.Combine(Path.GetDirectoryName(p), "data"), new Prefs(), null, tick);
                string note = lib.TakeLoadNote();
                lib.Bind("Custom-1", 5u, allThere, out _);
                var prime = lib.Store.For("prime");
                bool loadedGood = prime.Saved.Count == 1 && prime.Saved[0].Name == "good";
                prime.Saved.Add(new Place { Name = "added", X = 9, Y = 9, Z = 9 });
                Expect(note != null && note.Contains("5 entries") && loadedGood && File.ReadAllText(p + ".bad") == partial && lib.Save() == null
                    && File.ReadAllText(p + ".bad") == partial && File.ReadAllText(p).Contains("added"),
                    "R4d entries that can't be read are counted and said, the whole file is kept aside first, the rest loads and saves");
            }
            {
                string p = Places("{broken");
                File.WriteAllText(p + ".bad", "an earlier copy");
                var file = new PlaceFile(p, null);
                file.Load();
                string firstNote = file.Note;
                var again = new PlaceFile(p, null);
                again.Load();
                Expect(File.ReadAllText(p + ".bad") == "an earlier copy" && File.ReadAllText(p + ".bad2") == "{broken" && !File.Exists(p + ".bad3")
                    && firstNote != null && firstNote.Contains("places.json.bad2") && again.Note.Contains("places.json.bad2"),
                    "R4e an earlier copy is never replaced, and the same broken file is not copied twice");
            }
            {
                string v1 = "{\"v\":1,\"planets\":{\"prime\":{\"saved\":[{\"n\":\"home\",\"x\":1,\"y\":2,\"z\":3}],\"recent\":[],\"visited\":[\"w9\"]}}}";
                string v2 = "{\"v\":2,\"saves\":{\"Custom-1\":{\"seed\":11,\"planets\":{\"prime\":{\"saved\":[{\"n\":\"base\",\"x\":4,\"y\":5,\"z\":6},{\"n\":\"spot\",\"x\":4,\"z\":6,\"auto\":true}],\"recent\":[],\"visited\":[\"w5\"]}},\"back\":{\"w\":\"prime\",\"x\":1,\"y\":2,\"z\":3}}}}";
                string v3empty = "{\"v\":3,\"saves\":{}}";
                string noVersion = "{\"planets\":{\"prime\":{\"saved\":[{\"n\":\"old\",\"x\":1,\"y\":2,\"z\":3}]}}}";
                bool allClean = true;
                foreach (var text in new[] { v1, v2, v3empty, noVersion, "" })
                {
                    string p = Places(text);
                    var file = new PlaceFile(p, null);
                    var st = file.Load();
                    allClean &= file.Note == null && st.Unreadable == 0 && !File.Exists(p + ".bad");
                }
                Expect(allClean, "R4f every shape this mod ever wrote still reads whole, with nothing kept aside and nothing said");
            }
            {
                var rows = string.Join(",", Enumerable.Range(0, PlaceStore.SavedMax + 3).Select(i => "{\"n\":\"p" + i + "\",\"x\":" + i + ",\"y\":1,\"z\":1}"));
                string many = "{\"v\":3,\"saves\":{\"Custom-1\":{\"planets\":{\"prime\":{\"saved\":[" + rows + "]}}}}}";
                string p = Places(many);
                var file = new PlaceFile(p, null);
                var st = file.Load();
                st.Use("Custom-1", 0u, allThere);
                Expect(st.For("prime").Saved.Count == PlaceStore.SavedMax && st.Unreadable == 3 && file.Note != null && File.Exists(p + ".bad"),
                    "R4g reading keeps the list's own limit: rows past it are counted and the file kept, not read in and later cut");
            }
            {
                string d = Temp();
                string p = Path.Combine(d, "places.json");
                var lib = new Library(p, Path.Combine(d, "data"), new Prefs(), null, tick);
                lib.Bind("Custom-1", 5u, allThere, out _);
                lib.Store.Save("prime", "mine", 1, 2, 3);
                lib.Save();
                string newer = Path.Combine(d, "newer-backup.json");
                File.WriteAllText(newer, "{\"v\":99,\"saves\":{\"Custom-1\":{\"planets\":{}}}}");
                string said = lib.Restore(new BackupInfo { Path = newer, Reason = "before-import" }, out _);
                Expect(said == L.T("lib.backupNewer") && lib.Store.For("prime").Saved.Count == 1 && File.ReadAllText(p).Contains("mine"),
                    "R4h a backup from a newer version is never restored over what you have");
            }

            // ── R5. a reused save name with a new world (F5) ──
            {
                var s = new PlaceStore();
                s.Use("Survival-1", 111u, allThere);
                s.Save("prime", "old base", 1, 2, 3);
                s.For("prime").Visited.Add("old-pin");
                s.Teleported("prime", 4, 5, 6);
                string said = s.Use("Survival-1", 222u, allThere);
                string aside = L.T("store.asideName", "Survival-1", 111u);
                var kept = s.SaveOf(aside);
                Expect(s.For("prime").Saved.Count == 0 && s.For("prime").Visited.Count == 0 && s.Back("prime") == null && said != null && said.Contains(aside),
                    "R5a the same save name with a new world seed starts clean, and the screen says where the old world's places went");
                Expect(kept != null && kept.Seed == 111u && kept.Planets["prime"].Saved[0].Name == "old base" && kept.Planets["prime"].Visited.Contains("old-pin") && kept.Back != null
                    && s.SaveNames.Contains(aside), "R5b the old world's places, found marks and Return are kept whole under their own name");
                Expect(s.Use("Survival-1", 222u, allThere) == null && s.SaveOf(aside) != null, "R5c the same world again changes nothing");
                s.Use("Survival-1", 333u, allThere);
                Expect(s.SaveOf(L.T("store.asideName", "Survival-1", 222u)) != null && s.SaveOf(aside) != null, "R5d a third world keeps both earlier ones");
            }
            {
                // a record written before seeds were kept takes the seed it first sees: data written by 0.2.6 never splits
                var s = PlaceStore.FromJson("{\"v\":2,\"saves\":{\"Survival-1\":{\"planets\":{\"prime\":{\"saved\":[{\"n\":\"base\",\"x\":1,\"y\":2,\"z\":3}]}}}}}");
                string said = s.Use("Survival-1", 777u, allThere);
                Expect(said == null && s.For("prime").Saved.Count == 1 && s.SaveOf("Survival-1").Seed == 777u, "R5e a record with no seed yet keeps its places and takes this seed");
            }
            {
                // renamed outside the game onto a name a deleted world used: the old name's world goes aside, the renamed one follows
                var s = new PlaceStore();
                s.Use("Survival-1", 9u, allThere);
                s.Save("prime", "deleted world's", 1, 1, 1);
                s.Use("My base", 5u, allThere);
                s.Save("prime", "renamed world's", 2, 2, 2);
                string said = s.Use("Survival-1", 5u, name => name != "My base");
                Expect(s.For("prime").Saved.Count == 1 && s.For("prime").Saved[0].Name == "renamed world's" && s.SaveOf(L.T("store.asideName", "Survival-1", 9u)) != null
                    && said != null && said.Contains("My base") && said.Contains(L.T("store.asideName", "Survival-1", 9u)),
                    "R5f a rename onto a reused name still follows, the old world is kept, and the screen says both");
            }
            {
                // through the file: the split is written at once, so a crash before the next change loses nothing
                string d = Temp();
                string p = Path.Combine(d, "places.json");
                var lib = new Library(p, Path.Combine(d, "data"), new Prefs(), null, tick);
                lib.Bind("Survival-1", 111u, allThere, out _);
                lib.Store.Save("prime", "first world", 1, 2, 3);
                lib.Save();
                var next = new Library(p, Path.Combine(d, "data"), new Prefs(), null, tick);
                string said = next.Bind("Survival-1", 222u, allThere, out var failed);
                string onDisk = File.ReadAllText(p);
                Expect(said != null && failed == null && onDisk.Contains(L.T("store.asideName", "Survival-1", 111u)) && onDisk.Contains("first world")
                    && next.Store.For("prime").Saved.Count == 0, "R5g the split reaches the file straight away");
            }

            // ── R7. floors the game switches on only near the player are on for the landing check (F7) ──
            {
                var at = new TeleportRequest(100, 20, 100, false);
                var near = new Far(130, 20, 100, 50);            // 30 m away: on once you stand there
                var far = new Far(160, 20, 100, 50);             // 60 m: stays off
                var shortReach = new Far(100, 20, 115, 10);      // 15 m, but this one only switches on within 10 m
                var alreadyOn = new Far(105, 20, 100, 50) { On = true };
                var lostGroup = new Far(101, 20, 100, 50) { Root = false };   // its network group is off: the game leaves it
                var wake = new DestinationWake();
                int woke = wake.Wake(new IFarSwitch[] { near, far, shortReach, alreadyOn, lostGroup, null }, at);
                Expect(near.On && !far.On && woke == 1 && near.Switched == 1, "R7a the check sees a floor within reach of the destination switched on, and nothing past it");
                Expect(!shortReach.On, "R7f each switch's own reach is used, not a fixed one");
                Expect(!lostGroup.On && lostGroup.Switched == 0, "R7d a switch whose network group is off is left alone, as the game leaves it");
                int undone = wake.Undo();
                Expect(!near.On && alreadyOn.On && alreadyOn.Switched == 0 && undone == 1 && wake.Count == 0,
                    "R7c a refused teleport switches off again exactly what it switched on, and nothing that was already on");
                var auto = new TeleportRequest(100, 0, 100, true);
                var platform = new Far(100, 65, 100, 50);       // a base platform 65 m above the ground under the spot
                var wake2 = new DestinationWake();
                wake2.Wake(new IFarSwitch[] { platform }, auto);
                Expect(platform.On, "R7b with no height given, every height counts: a platform above the spot is on for the check");
                wake2.Keep();
                Expect(wake2.Undo() == 0 && platform.On, "R7e a teleport that lands leaves the woken floors to the game's own switch");
            }

            // ── R8. the input hold against the game's own window policy (F8) ──
            Hold Setup()
            {
                var h = new Hold();
                h.Move = new InputAction("Move"); h.Jump = new InputAction("Jump"); h.Inventory = new InputAction("OpenInventory");
                h.Chat = new InputAction("OpenChatWindow"); h.Submit = new InputAction("Submit"); h.Cancel = new InputAction("Cancel");
                h.Other = new InputAction("OtherAssetAction");
                var asset = new InputActionAsset(new InputActionMap("PlayerMovable", h.Move, h.Jump, h.Inventory, h.Chat), new InputActionMap("UI", h.Submit, h.Cancel));
                h.Pad = new WindowsGamepadHandler { InputMap = asset };
                h.Pad.GameplayInputsActiveInUI.Add(h.Chat);
                h.Pad.Start();
                h.Windows = new WindowsHandler { Pad = h.Pad };
                h.Other.Enable();
                h.Player = new PlayerMainController();
                // the player's own input asset: a second one, outside the game's window policy
                h.Player.Components[typeof(PlayerInputDispatcher)] = new PlayerInputDispatcher { inputMap = new InputActionAsset(new InputActionMap("Other", h.Other)) };
                Managers.Items.Clear();
                Managers.Items[typeof(WindowsHandler)] = h.Windows;
                Managers.Items[typeof(WindowsGamepadHandler)] = h.Pad;
                UnityEngine.Cursor.lockState = UnityEngine.CursorLockMode.Locked;
                h.Log = new ManualLogSource();
                h.It = new InputHold(h.Log);
                return h;
            }
            {
                var h = Setup();
                bool began = h.It.Begin(h.Player);
                bool allOff = !h.Move.enabled && !h.Jump.enabled && !h.Inventory.enabled && !h.Chat.enabled && !h.Other.enabled;
                h.Windows.OpenAndReturnUi(DataConfig.UiType.Inventory);   // a game window takes over while our panel is open
                string why = h.It.Check();
                h.It.End(why);
                Expect(began && allOff && why != null && !h.Move.enabled && !h.Jump.enabled && !h.Inventory.enabled && h.Submit.enabled && h.Chat.enabled
                    && h.Windows.GetOpenedUi() == DataConfig.UiType.Inventory,
                    "R8a closing our panel under a game window leaves the game's play map off and its UI on, as the game set them");
                Expect(h.Other.enabled, "R8b an action outside the game's window maps comes back on even then");
                h.Windows.CloseAllWindows();   // the player closes the inventory: the game's policy puts play back
                Expect(h.Move.enabled && h.Jump.enabled && h.Inventory.enabled && h.Chat.enabled && !h.Submit.enabled && !h.Cancel.enabled && h.Other.enabled,
                    "R8c and when the game's window closes, every key is where it was before our panel opened");
            }
            {
                var h = Setup();
                h.It.Begin(h.Player);
                h.It.End("closed");
                Expect(h.Move.enabled && h.Jump.enabled && h.Inventory.enabled && h.Chat.enabled && !h.Submit.enabled && h.Other.enabled
                    && h.Windows.GetOpenedUi() == DataConfig.UiType.Null && UnityEngine.Cursor.lockState == UnityEngine.CursorLockMode.Locked,
                    "R8d a normal close puts every key, the marker and the cursor back");
            }
            {
                var h = Setup();
                h.Inventory.ThrowOnDisable = true;   // the third action refuses to switch off
                bool began, threw = false;
                try { began = h.It.Begin(h.Player); }
                catch (Exception) { began = false; threw = true; }
                Expect(!threw && !began && !h.It.Active && h.Windows.GetOpenedUi() == DataConfig.UiType.Null && h.Move.enabled && h.Jump.enabled
                    && UnityEngine.Cursor.lockState == UnityEngine.CursorLockMode.Locked,
                    "R8e a hold that fails part way is undone: no marker, no key left off, the cursor as it was");
            }
        }
        catch (Exception e) { fails.Add("threw " + e.GetType().Name + ": " + e.Message + " " + e.StackTrace); }
        finally { foreach (var d in dirs) try { Directory.Delete(d, true); } catch (Exception) { } }
        Console.WriteLine("C61 review checks: " + (n - fails.Count) + "/" + n + " passed");
        return fails;
    }

    // one of the game's distance switches (PerformancerDisableIfFarAway), as DestinationWake sees it
    private sealed class Far : IFarSwitch
    {
        public bool On, Root = true;
        public int Switched;
        private readonly float x, y, z, reach;
        public Far(float x, float y, float z, float reach) { this.x = x; this.y = y; this.z = z; this.reach = reach; }
        public bool ActiveSelf => On;
        public void SetActive(bool on) { On = on; Switched++; }
        public float X => x;
        public float Y => y;
        public float Z => z;
        public float Reach => reach;
        public bool RootActive => Root;
    }

    private sealed class Hold
    {
        public InputAction Move, Jump, Inventory, Chat, Submit, Cancel, Other;
        public WindowsGamepadHandler Pad;
        public WindowsHandler Windows;
        public PlayerMainController Player;
        public ManualLogSource Log;
        public InputHold It;
    }
}

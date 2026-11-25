// GamesOMG Teleport 0.3.0: the Places panel, rebuilt for the look, controls, settings and data pass (2026-11-19:
// more appealing, easier controls, a settings screen to reset, export and save, before the first public release;
// ChatGPT's review the spec). "Pick a place. Go there. Get back." The hierarchy, top to bottom:
//   header           G Emblem mark · Places · planet and save · Settings · Close
//   main actions     From clipboard · Save here · Return to <the spot you left>
//   navigation       My places | Map places
//   search, filters  search · Hide found · Sort (Nearest / A-Z)
//   rows             name first; distance and height second. My places: favourites, your places, Recently travelled.
//                    Map places: the 0.2.3 tree, each layer's places opening right under it (kept)
//   selected place   Teleport · Favourite / Save to My places · Mark found · Rename · Show on map · Delete
//   footer           what just happened (with Undo), and the keys or buttons for the device in your hands
// The Settings screen (Controls / Appearance / Behaviour / Data / Help) is the same panel's other view. Every control is
// reachable without a mouse (Nav + PadInput). While it is open the player is held as the game holds them in its own text
// box (InputHold): the world is NOT paused, and in co-op the game goes on around you (Help says so).
// 0.3.2 (player feedback 2026-11-21 08:19, "we can do it now" 08:33): narrower (360 at 1080p, 0.2.6 was 380, 0.3.0 420), compact by
// default, one click teleports again (right-click selects), and every place row has a + for a short note of your own.
using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using SpaceCraft;
using UnityEngine;

namespace GamesOMG.PlanetCrafterTeleport
{
    // what the panel needs from the plugin
    internal interface IPanelHost
    {
        void Go(TeleportRequest request, string label);   // closes the panel, then the checked teleport path
        void GoBack();
        bool Busy { get; }
        string Binding(string action);
        string SetBinding(string action, string path);    // null when set, else why not (another of our actions, or 0.3.3 the game's own)
        List<GameBinding> GameBindings();
        Look Look { get; }
        string Version { get; }
        string Diagnostics();
        void Sound(bool ok);
        void ReplayFirstRun();
        List<string> GameSaves();
        void Opened(string why);                          // for the log
    }

    // the Appearance and Behaviour settings the panel reads and the settings screen changes (backed by the .cfg)
    internal sealed class Look
    {
        public Func<int> TextSize, Opacity;
        public Func<bool> Left, Compact, ReducedMotion, QuickTeleport, CloseAfter, Sounds;
        public Action<string, string> Set;   // pref id ("Appearance.TextSize"), value
    }

    internal sealed class Panel
    {
        public enum Tab { Mine, Places }
        private enum View { Places, Settings }
        private enum Section { Controls, Appearance, Behaviour, Data, Help }
        private enum Dlg { None, Paste, Rename, Confirm, ImportList, ImportPreview, Backups, Capture }

        public const string ActPlaces = "Places", ActTeleport = "Teleport", ActReturn = "Return", ActPadPlaces = "PadPlaces", ActPadReturn = "PadReturn";
        public static readonly string[] Actions = { ActPlaces, ActTeleport, ActReturn, ActPadPlaces, ActPadReturn };
        public const string MapUrl = "https://gamesomg.com/planet-crafter-map/";

        private readonly ManualLogSource log;
        private readonly InputHold hold;
        private readonly IPanelHost host;
        private readonly Theme theme;
        private readonly Nav nav = new Nav();
        private readonly PadInput input = new PadInput();
        public readonly Library Lib;

        private string world = "", planetName = "";
        private Tab tab = Tab.Mine;
        private View view = View.Places;
        private Section section = Section.Controls;
        private Dlg dlg = Dlg.None;
        private Vector2 scroll, setScroll, dlgScroll;
        private float listViewHeight = 300, setViewHeight = 300;
        private float slide;
        private Action pending;
        private readonly Dictionary<string, Action> acts = new Dictionary<string, Action>(StringComparer.Ordinal);
        private readonly Dictionary<string, Action> lefts = new Dictionary<string, Action>(StringComparer.Ordinal);
        private readonly Dictionary<string, Action> rights = new Dictionary<string, Action>(StringComparer.Ordinal);
        private bool layoutPass, inScroll;
        private float innerWidth = 380;
        private string focusField, focusedNow = "";
        private string lastClickId;
        private float lastClickAt;

        // selection: one of your places, or one of the map's
        private Place selPlace;
        private Poi selPoi;

        // what just happened
        private string status;
        private bool statusBad, statusShown;
        private float statusUntil;
        private Removed undo;
        private Action undoLook;   // an Appearance change can be taken back in one press

        // text
        private string search = "", rename = "", paste = "", pasteWhy;
        private string searchUsed = "";   // the search as this frame's Layout saw it: typing mid-frame must not change the row count
        // dialogs
        private string confirmTitle, confirmBody, confirmYes;
        private Action confirmAct;
        private bool confirmDanger;
        private string captureFor;
        private float captureUntil;
        private List<string> importFiles = new List<string>();
        private ExportFile importFile;
        private string importPath;
        private List<string> srcSaves = new List<string>(), dstSaves = new List<string>();
        private int srcAt, dstAt;
        private ImportPlan plan;
        private bool importSettings, exportSettings;
        private List<BackupInfo> backupList = new List<BackupInfo>();

        // 0.2.1 Map places: the map's POIs per planet, from the packs embedded in the DLL (build-pois.mjs)
        private readonly Dictionary<string, PoiWorld> pois = new Dictionary<string, PoiWorld>(StringComparer.Ordinal);
        // 0.2.3: the tree per planet; what is ticked and open stays for the game session
        private readonly Dictionary<string, PlacesTree> trees = new Dictionary<string, PlacesTree>(StringComparer.Ordinal);
        private bool hideFound, byName;
        private List<TreeRow> rows = new List<TreeRow>();
        // 0.2.4: the map's found list (#foundset=), both ways
        private FoundSet foundSet;
        private bool foundTried;
        // 0.3.2 notes: the row whose note box is open (a place listed twice opens one box), its place, the text being typed
        private string noteRow;
        private object noteItem;
        private string noteText = "";
        // 0.3.2 (first in-game sitting, 11-21 09:18): keyboard focus dropped from Update did not stick: after typing in a field,
        // Esc left it focused, so Enter, F3 and Esc kept acting as typing. The drop is now also made inside OnGUI.
        private bool dropFocus;

        public Panel(ManualLogSource log, Library lib, IPanelHost host, string cacheDir)
        {
            this.log = log;
            Lib = lib;
            this.host = host;
            theme = new Theme(cacheDir, line => log.LogInfo(line));
            hold = new InputHold(log);
        }

        public bool Open { get; private set; }
        public PlaceStore Store => Lib.Store;
        public string TakeLoadNote() => Lib.TakeLoadNote();
        public bool UsingPad => input.UsingPad;
        public bool PlayStation => input.PlayStation;
        public Theme Theme => theme;

        // writes the places file; a failure is a line on screen
        public bool SaveStore()
        {
            string failed = Lib.Save();
            if (failed == null) return true;
            Say(failed, true);
            if (!Open) host.Sound(false);
            return false;
        }

        // ── open / close ──
        public bool Show(PlayerMainController player, PlanetData planet, string prefill, string note, bool noteBad)
        {
            if (Lib.Store == null || !hold.Begin(player)) return false;
            Open = true;
            slide = host.Look.ReducedMotion() ? 1 : 0;
            planetName = planet.id;
            world = PlaceStore.WorldOf(planet.id);
            if (selPoi != null && PoisFor(world)?.ById.ContainsKey(selPoi.Id) != true) selPoi = null;
            if (selPlace != null && !Store.For(world).Saved.Contains(selPlace) && !Store.For(world).Recent.Contains(selPlace)) selPlace = null;
            if (prefill != null)
            {
                paste = prefill;
                pasteWhy = note;
                OpenDialog(Dlg.Paste, "d:pastefield");
                note = null;
            }
            if (note != null) Say(note, noteBad);
            if (string.IsNullOrEmpty(nav.Focus) || dlg == Dlg.None && view == View.Places && !nav.Focus.StartsWith("r", StringComparison.Ordinal)) nav.Set("a:clip");
            nav.Visible = input.UsingPad;
            host.Opened("Panel opened on " + world + ": " + Store.For(world).Saved.Count + " places, " + Store.For(world).Recent.Count + " recent.");
            return true;
        }

        public void Hide(string why)
        {
            if (!Open) return;
            Open = false;
            pending = null;
            focusField = null;
            noteRow = null; noteItem = null;
            if (dlg == Dlg.Capture) dlg = Dlg.None;
            hold.End(why);
            log.LogInfo("Panel closed (" + why + ").");
        }

        // a refusal after the panel closed for a teleport: back open on the same place, with the reason (the selection kept)
        public void Refused(string why) => Say(why, true);

        // ── every frame while open; closeKey = the Places key, or the clipboard key, pressed this frame ──
        public void Update(bool closeKey)
        {
            string lost = hold.Check();
            if (lost != null) { Hide(lost); return; }
            slide = host.Look.ReducedMotion() ? 1 : Mathf.MoveTowards(slide, 1f, Time.unscaledDeltaTime * 7f);
            if (dlg == Dlg.Capture) { Capture(); return; }
            bool typing = GUIUtility.keyboardControl != 0 && focusedNow.Length > 0;
            // while you type, a key bound to Places (it could be a letter) types; Esc leaves the field first
            if (closeKey && !typing) { Hide("the key again"); return; }
            var cmd = input.Read(typing);
            if (cmd != Cmd.None) Command(cmd, typing);
            var act = pending;
            pending = null;
            act?.Invoke();
            if (nav.Moved)
            {
                nav.Moved = false;
                float m = theme.Px(8);
                if (dlg == Dlg.None && view == View.Places) scroll.y = nav.ScrollFor(scroll.y, listViewHeight, m);
                else if (dlg == Dlg.None) setScroll.y = nav.ScrollFor(setScroll.y, setViewHeight, m);
                else dlgScroll.y = nav.ScrollFor(dlgScroll.y, setViewHeight, m);
                FocusMoved();
            }
        }

        private void Command(Cmd cmd, bool typing)
        {
            switch (cmd)
            {
                case Cmd.Back: Back(typing); return;
                case Cmd.Activate:
                    if (typing && focusedNow == "f:search") { GUIUtility.keyboardControl = 0; dropFocus = true; nav.Move(1); return; }
                    if (typing && focusedNow == "d:renamefield") { pending = DoRename; return; }
                    if (typing && focusedNow == "d:pastefield") { pending = SubmitPaste; return; }
                    if (typing && focusedNow == "ne:field") { string typed = noteText; pending = () => SaveNote(typed); return; }
                    if (!nav.Visible && !typing) { nav.Move(0); return; }   // the first press shows where focus is
                    var a = Act(nav.Focus);
                    if (a != null) pending = a; else nav.Move(0);
                    return;
                case Cmd.Up: LeaveField(); nav.Move(-1); return;
                case Cmd.Down: LeaveField(); nav.Move(1); return;
                case Cmd.PageUp: LeaveField(); for (int i = 0; i < 8; i++) nav.Move(-1); return;
                case Cmd.PageDown: LeaveField(); for (int i = 0; i < 8; i++) nav.Move(1); return;
                case Cmd.Home: nav.First(); return;
                case Cmd.End: nav.Last(); return;
                case Cmd.Next: LeaveField(); nav.Move(1); return;
                case Cmd.Prev: LeaveField(); nav.Move(-1); return;
                case Cmd.Left:
                    if (lefts.TryGetValue(nav.Focus, out var l) && nav.Visible) pending = l; else nav.Move(-1);
                    return;
                case Cmd.Right:
                    if (rights.TryGetValue(nav.Focus, out var r) && nav.Visible) pending = r; else nav.Move(1);
                    return;
                case Cmd.TabLeft: case Cmd.TabRight:
                    if (dlg != Dlg.None) return;
                    int step = cmd == Cmd.TabRight ? 1 : -1;
                    if (view == View.Places) { SetTab(tab == Tab.Mine ? Tab.Places : Tab.Mine); nav.Set(tab == Tab.Mine ? "t:mine" : "t:places", true); }
                    else { section = (Section)(((int)section + step + 5) % 5); setScroll = Vector2.zero; nav.Set("set:" + section, true); }
                    return;
                case Cmd.SaveHere: if (dlg == Dlg.None && view == View.Places) pending = SaveHere; return;
                case Cmd.Actions: if (dlg == Dlg.None && view == View.Places) { SelectFocused(); if (HasSelection) nav.Set("s:go", true); } return;
            }
        }

        private Action Act(string id) => id != null && acts.TryGetValue(id, out var a) ? a : null;

        private void LeaveField()
        {
            if (GUIUtility.keyboardControl != 0) GUIUtility.keyboardControl = 0;
            dropFocus = true;
        }

        // Esc / B: cancel the edit or dialog first, then step back out of Settings, then close
        private void Back(bool typing)
        {
            if (noteRow != null) { CancelNote(); return; }   // 0.3.2: Esc / B closes the note box first, nothing saved
            if (typing) { GUIUtility.keyboardControl = 0; focusField = null; dropFocus = true; return; }
            if (dlg != Dlg.None) { CloseDialog(); return; }
            if (view == View.Settings) { view = View.Places; nav.Set("h:settings", nav.Visible); return; }
            Hide(input.UsingPad ? "B" : "Esc");
        }

        // focus moved by a key: a row becomes the selection; a text field takes the typing (a controller can't type)
        private void FocusMoved()
        {
            string f = nav.Focus;
            if (f.StartsWith("f:", StringComparison.Ordinal) || f == "d:renamefield" || f == "d:pastefield" || f == "ne:field")
            {
                if (!input.UsingPad) focusField = f;
            }
            else LeaveField();
            if (dlg == Dlg.None && view == View.Places) SelectFocused();
        }

        private readonly Dictionary<string, object> rowItems = new Dictionary<string, object>(StringComparer.Ordinal);

        private void SelectFocused()
        {
            if (!rowItems.TryGetValue(nav.Focus, out var item)) return;
            if (item is Place p) { selPlace = p; selPoi = null; }
            else if (item is Poi q) { selPoi = q; selPlace = null; }
        }

        private bool HasSelection => selPlace != null || selPoi != null;

        private void Say(string text, bool bad, Removed undoable = null, Action undoAppearance = null)
        {
            status = text;
            statusBad = bad;
            statusUntil = Time.realtimeSinceStartup + (undoable != null || undoAppearance != null ? 12f : 8f);
            undo = undoable;
            undoLook = undoAppearance;
        }

        private void SetTab(Tab t)
        {
            if (tab == t) return;
            tab = t;
            scroll = Vector2.zero;
        }

        // ── main actions ──
        private void FromClipboard()
        {
            string clip = null;
            try { clip = GUIUtility.systemCopyBuffer; }
            catch (Exception error) { log.LogWarning("Clipboard unavailable: " + error.GetType().Name); }
            string text = (clip ?? "").Trim();
            if (TeleportRequest.TryParse(text, out var request)) { host.Go(request, Coords(request)); return; }
            if (text.Length <= 60000 && (SpotSet.LooksLikeLink(text) || FoundSet.LooksLikeLink(text))) { ImportLink(text); return; }
            bool near = text.Length > 0 && text.Length <= 300 && text.IndexOf('\n') < 0 && text.StartsWith("/tp", StringComparison.Ordinal);
            paste = near ? text : "";
            pasteWhy = text.Length == 0 ? L.T("paste.empty") : near ? TeleportRequest.Explain(text) : L.T("paste.notOne");
            OpenDialog(Dlg.Paste, "d:pastefield");
        }

        private static string Coords(TeleportRequest r) =>
            Mathf.RoundToInt(r.X) + ", " + (r.AutomaticHeight ? "~" : Mathf.RoundToInt(r.Y).ToString()) + ", " + Mathf.RoundToInt(r.Z);

        private void SubmitPaste()
        {
            string text = (paste ?? "").Trim();
            if (SpotSet.LooksLikeLink(text) || FoundSet.LooksLikeLink(text)) { CloseDialog(); ImportLink(text); return; }
            if (!TeleportRequest.Parse(text, out var request, out var reason)) { pasteWhy = reason; host.Sound(false); return; }
            CloseDialog();
            host.Go(request, Coords(request));
        }

        public void SaveHere()
        {
            var player = hold.Player;
            if (player == null || !player) return;
            Vector3 feet = Feet(player);
            var pw = PoisFor(world);
            string name = PlaceStore.AutoName(planetName, feet.x, feet.z, (x, z) => pw?.Closest(x, z, 60) is Poi p ? pw.NameOf(p) : null);
            var place = Store.Save(world, name, feet.x, feet.y, feet.z);
            if (place == null) { Say(L.T("save.full"), true); host.Sound(false); return; }
            selPlace = place; selPoi = null;
            SetTab(Tab.Mine);
            if (SaveStore()) { Say(L.T("save.done", place.Name), false); host.Sound(true); }
            nav.Set(RowId(place, "m"), nav.Visible);
            log.LogInfo("Panel: saved a place on " + world + ".");
        }

        // where the player stands, as a numeric /tp height takes it: the capsule's bottom. Save here and Return record it.
        public static Vector3 Feet(PlayerMainController player)
        {
            var body = player.GetComponent<CharacterController>();
            Vector3 pos = player.transform.position;
            if (body == null) return pos;
            Vector3 centre = player.transform.TransformPoint(body.center);
            return new Vector3(pos.x, centre.y - body.height * .5f * Mathf.Abs(player.transform.lossyScale.y), pos.z);
        }

        // the name of where someone stands, for "Return to …": the place you teleported to while you're within 30 m of where
        // it landed you, else one of your places within 30 m, else a map place within 30 m (PlaceStore.NameForSpot)
        public string NameForSpot(string w, float x, float z)
        {
            var pw = PoisFor(w);
            return Store.NameForSpot(w, x, z, (px, pz) => pw?.Closest(px, pz, PlaceStore.SpotNameReach) is Poi poi ? pw.NameOf(poi) : null);
        }

        // ── a map link pasted: spots (#spotset=), found marks (#foundset=), or both (the map's Send to game) ──
        private void ImportLink(string text)
        {
            var said = new List<string>();
            bool done = false, failed = false;
            if (SpotSet.LooksLikeLink(text))
            {
                if (ImportSpots(text, out string s)) done = true; else failed = true;
                said.Add(s);
            }
            if (FoundSet.LooksLikeLink(text))
            {
                if (ImportFound(text, out string f)) done = true; else failed = true;
                said.Add(f);
            }
            Say(string.Join(" ", said.ToArray()), failed && !done);
            host.Sound(!failed || done);
        }

        private bool ImportFound(string text, out string said)
        {
            var fs = Found();
            if (fs == null) { said = L.T("found.cantRead"); return false; }
            if (!fs.TryRead(text, out var ticked, out var reason)) { said = reason; log.LogInfo("Panel: a found-set link refused."); return false; }
            int added = 0;
            var byWorld = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (int i in ticked)
            {
                string w = fs.Worlds[fs.W[i]];
                if (!Store.For(w).Visited.Add(fs.Ids[i])) continue;   // merge only: a mark here is never taken away
                added++;
                byWorld[w] = byWorld.TryGetValue(w, out int n) ? n + 1 : 1;
            }
            string failed = added > 0 ? Lib.Save() : null;
            var parts = new List<string>();
            foreach (var kv in byWorld) parts.Add(Title(kv.Key) + " " + kv.Value);
            said = ticked.Count == 0 ? L.T("found.none")
                : added == 0 ? L.T("found.allAlready")
                : L.N("found.added", added, string.Join(", ", parts.ToArray())) + (ticked.Count > added ? " " + L.T("found.someAlready", ticked.Count - added) : "");
            if (failed != null) said += " " + failed;
            log.LogInfo("Panel: imported a found-set link: " + added + " marked, " + (ticked.Count - added) + " already.");
            return failed == null;
        }

        private bool ImportSpots(string text, out string said)
        {
            if (!SpotSet.TryRead(text, out var spots, out var from, out var reason)) { said = reason; return false; }
            var result = Store.Import(spots, world, (w, pin) => PoisFor(w) is PoiWorld pw && pw.ById.TryGetValue(pin, out var poi) && poi.HasLanding
                ? new[] { poi.LX, poi.LY, poi.LZ } : null);
            string failed = result.Added > 0 ? Lib.Save() : null;
            said = PlaceStore.Describe(result, Title);
            if (from.Length > 0 && result.Added > 0) said = L.T("import.from", from) + " " + said;
            if (failed != null) said += " " + failed;
            log.LogInfo("Panel: imported a spot-set (" + spots.Count + " spots): " + result.Added + " added, " + result.Skipped + " skipped.");
            return failed == null;
        }

        // Copy for the map: the found marks on the map's Found list, every planet, as the map's own #foundset= link
        private void CopyForMap()
        {
            var fs = Found();
            if (fs == null) { Say(L.T("found.cantWrite"), true); return; }
            int n = 0;
            string link = fs.LinkOf(i => { bool on = Store.For(fs.Worlds[fs.W[i]]).Visited.Contains(fs.Ids[i]); if (on) n++; return on; });
            try { GUIUtility.systemCopyBuffer = link; }
            catch (Exception error) { log.LogWarning("Clipboard unavailable: " + error.GetType().Name); Say(L.T("clip.refused"), true); return; }
            Say(n == 0 ? L.T("found.copiedNone") : L.N("found.copied", n), false);
            host.Sound(true);
            log.LogInfo("Panel: copied a found-set link for the map (" + n + " marks).");
        }

        public FoundSet Found()
        {
            if (foundTried) return foundSet;
            foundTried = true;
            try
            {
                using (var stream = typeof(Panel).Assembly.GetManifestResourceStream("foundset-pc.json.z"))
                {
                    if (stream == null) return null;
                    var bytes = new byte[stream.Length];
                    int read = 0;
                    while (read < bytes.Length) { int k = stream.Read(bytes, read, bytes.Length - read); if (k <= 0) break; read += k; }
                    foundSet = FoundSet.Read(bytes);
                    log.LogInfo("Found list: " + foundSet.Ids.Count + " pins over " + foundSet.Worlds.Count + " planets (map " + foundSet.Map.Substring(0, 9) + ", hash " + foundSet.Hash + ").");
                }
            }
            catch (Exception error) { log.LogWarning("Found list unreadable: " + error.GetType().Name); }
            return foundSet;
        }

        public PoiWorld PoisFor(string w)
        {
            w = PlaceStore.WorldOf(w);
            if (pois.TryGetValue(w, out var cached)) return cached;
            PoiWorld loaded = null;
            try
            {
                using (var stream = typeof(Panel).Assembly.GetManifestResourceStream("pois-" + w + ".json.z"))
                {
                    if (stream != null)
                    {
                        var bytes = new byte[stream.Length];
                        int read = 0;
                        while (read < bytes.Length) { int n = stream.Read(bytes, read, bytes.Length - read); if (n <= 0) break; read += n; }
                        loaded = PoiWorld.Read(bytes);
                        log.LogInfo("Places: " + w + " has " + loaded.Pins.Count + " POIs (map " + loaded.Map.Substring(0, 9) + ", build " + loaded.Build + ").");
                    }
                }
            }
            catch (Exception error) { log.LogWarning("Places list for " + w + " unreadable: " + error.GetType().Name); }
            return pois[w] = loaded;
        }

        private PlacesTree TreeFor(PoiWorld pw)
        {
            if (!trees.TryGetValue(world, out var t)) trees[world] = t = new PlacesTree(pw.StartLayers());
            t.ByName = byName;
            return t;
        }

        private static string Title(string w) => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w.Substring(1);
        private string SaveLabel => Store.Current.Length == 0 ? L.T("save.unknown") : Store.Current.StartsWith("co-op ", StringComparison.Ordinal) ? L.T("save.coop") : Store.Current;

        // ── the selected place's actions ──
        private void Teleport()
        {
            if (host.Busy) { Say(L.T("tp.preparing"), true); return; }
            if (selPlace != null) host.Go(selPlace.Request, selPlace.Name);
            else if (selPoi != null && selPoi.HasLanding) host.Go(selPoi.Landing, PoisFor(world).NameOf(selPoi));
            else if (selPoi != null) { Say(L.T("poi.noLanding"), true); host.Sound(false); }
        }

        private void ToggleFav()
        {
            if (selPlace != null)
            {
                selPlace.Fav = !selPlace.Fav;
                if (SaveStore()) Say(L.T(selPlace.Fav ? "fav.on" : "fav.off", selPlace.Name), false);
                return;
            }
            if (selPoi == null) return;
            // a map place to My places, as a favourite, landing on its checked landing
            var pw = PoisFor(world);
            string name = pw.NameOf(selPoi);
            var existing = Store.For(world).Saved.Find(p => p.Pin == selPoi.Id);
            if (existing != null) { existing.Fav = true; if (SaveStore()) Say(L.T("fav.on", existing.Name), false); return; }
            if (!selPoi.HasLanding) { Say(L.T("poi.noLanding"), true); return; }
            var place = Store.Save(world, name, selPoi.LX, selPoi.LY, selPoi.LZ);
            if (place == null) { Say(L.T("save.full"), true); return; }
            place.Pin = selPoi.Id;
            place.Fav = true;
            if (SaveStore()) { Say(L.T("fav.saved", place.Name), false); host.Sound(true); }
        }

        // "Mark found": by hand, for a collectible. Never set by arriving somewhere (ChatGPT R4, held).
        private void ToggleFound()
        {
            if (selPoi == null || !PoisFor(world).Cats[selPoi.Cat].Findable) return;
            var v = Store.For(world).Visited;
            bool on = !v.Remove(selPoi.Id);
            if (on) v.Add(selPoi.Id);
            if (SaveStore()) Say(L.T(on ? "found.marked" : "found.unmarked", PoisFor(world).NameOf(selPoi)), false);
        }

        private void ShowOnMap()
        {
            string url;
            if (selPoi != null) url = MapUrl + "#pin=" + selPoi.Id;
            else if (selPlace != null && !string.IsNullOrEmpty(selPlace.Pin) && PoisFor(world)?.ById.ContainsKey(selPlace.Pin) == true) url = MapUrl + "#pin=" + selPlace.Pin;
            else if (selPlace != null) url = MapUrl + "#spot=" + Math.Round(selPlace.X) + "," + Math.Round(selPlace.Z) + "," + Uri.EscapeDataString(selPlace.Name) + ",," + world;
            else
            {
                var player = hold.Player;
                if (player == null || !player) return;
                var p = player.transform.position;
                url = MapUrl + "#spot=" + Math.Round(p.x) + "," + Math.Round(p.z) + "," + Uri.EscapeDataString(L.T("map.youWereHere")) + ",," + world;
            }
            try { Application.OpenURL(url); Say(L.T("map.opened"), false); }
            catch (Exception error) { log.LogWarning("Browser did not open: " + error.GetType().Name); Say(L.T("map.failed"), true); }
        }

        private void DeleteSelected()
        {
            if (selPlace == null) return;
            var gone = Lib.Delete(world, selPlace, out var failed);
            if (gone == null) return;
            Say(failed ?? L.T("delete.done", gone.Place.Name), failed != null, failed == null ? gone : null);
            selPlace = null;
        }

        private void Undo()
        {
            if (undoLook != null) { var a = undoLook; undoLook = null; a(); Say(L.T("look.undone"), false); return; }
            if (undo == null) return;
            var r = undo;
            undo = null;
            if (Lib.Undo(r, out var failed)) { selPlace = r.Place; Say(failed ?? L.T("undo.done", r.Place.Name), failed != null); }
            else Say(L.T("undo.taken", r.Place.Name), true);
        }

        private void DoRename()
        {
            if (selPlace == null) { CloseDialog(); return; }
            string got = Store.Rename(world, selPlace, rename);
            if (got == null) { Say(L.T("rename.empty"), true); return; }
            CloseDialog();
            if (SaveStore()) Say(L.T("rename.done", got), false);
        }

        // ── 0.3.2 notes (player feedback 2026-11-21: "a little + to the list items that allows adding a small note ... I have a way of
        // knowing a certain one I already tried needs me to come back later"). One line; the place keeps its spot in its
        // list (a note never moves it to Found) and marking it found keeps the note. Map places' notes sit beside the found
        // marks in this save's places file, your own places' on the place. ──
        private string NoteOf(object item) => item is Place pl ? pl.Note : item is Poi po ? Store.NoteOf(world, po.Id) : "";

        private string NameOf(object item) => item is Place pl ? pl.Name : item is Poi po && PoisFor(world) is PoiWorld pw ? pw.NameOf(po) : "";

        private void OpenNote(string rowId, object item)
        {
            if (item is Place pl) { selPlace = pl; selPoi = null; } else if (item is Poi po) { selPoi = po; selPlace = null; }
            noteRow = rowId;
            noteItem = item;
            noteText = NoteOf(item);
            // a keyboard types; a controller cannot, so its focus starts on the first ready-made note
            string at = input.UsingPad ? "ne:p0" : "ne:field";
            nav.Set(at, nav.Visible || input.UsingPad);
            if (!input.UsingPad) focusField = at;
        }

        private void CancelNote()
        {
            if (noteRow == null) return;
            string row = noteRow;
            noteRow = null;
            noteItem = null;
            GUIUtility.keyboardControl = 0;
            focusField = null;
            dropFocus = true;
            nav.Set(row, nav.Visible);
        }

        // Enter, a ready-made note, or Delete; an empty box deletes the note
        private void SaveNote(string text)
        {
            var item = noteItem;
            string row = noteRow;
            if (item == null) return;
            string had = NoteOf(item);
            string kept = item is Place pl ? PlaceStore.SetNote(pl, text) : item is Poi po ? Store.SetNote(world, po.Id, text) : had;
            noteRow = null;
            noteItem = null;
            GUIUtility.keyboardControl = 0;
            focusField = null;
            dropFocus = true;
            nav.Set(row, nav.Visible);
            if (kept == had) return;
            if (SaveStore()) Say(kept.Length == 0 ? L.T("note.deleted", NameOf(item)) : L.T("note.saved", NameOf(item)), false);
            log.LogInfo("Panel: a note " + (kept.Length == 0 ? "deleted" : had.Length == 0 ? "added" : "changed") + " on " + world + ".");
        }

        // ── dialogs ──
        private void OpenDialog(Dlg d, string focus)
        {
            if (dlg == Dlg.None) nav.Push(focus); else nav.Set(focus);
            dlg = d;
            dlgScroll = Vector2.zero;
            if (focus == "d:pastefield" || focus == "d:renamefield") { if (!input.UsingPad) focusField = focus; }
            nav.Visible = nav.Visible || input.UsingPad;
        }

        private void CloseDialog()
        {
            if (dlg == Dlg.None) return;
            dlg = Dlg.None;
            focusField = null;
            GUIUtility.keyboardControl = 0;
            dropFocus = true;
            nav.Pop();
        }

        private void Confirm(string title, string body, string yes, bool danger, Action act)
        {
            confirmTitle = title; confirmBody = body; confirmYes = yes; confirmDanger = danger; confirmAct = act;
            OpenDialog(Dlg.Confirm, danger ? "d:no" : "d:yes");
        }

        private void Capture()
        {
            string path = PadInput.Captured(out bool cancelled);
            if (cancelled || Time.realtimeSinceStartup > captureUntil) { CloseDialog(); Say(L.T("bind.cancelled"), false); return; }
            if (path == null) return;
            bool pad = KeyCheck.IsPad(path), wantPad = captureFor == ActPadPlaces || captureFor == ActPadReturn;
            if (pad != wantPad) { Say(L.T(wantPad ? "bind.wantButton" : "bind.wantKey"), true); return; }
            string refused = host.SetBinding(captureFor, path);
            CloseDialog();
            if (refused != null) { Say(refused, true); host.Sound(false); return; }
            Say(L.T("bind.set", ActionName(captureFor), KeyCheck.Label(path, input.PlayStation)), false);
        }

        private static string ActionName(string action) => L.T("act." + action);

        // ══ drawing ══════════════════════════════════════════════════════════════════════════════════════════════════
        public void OnGUI()
        {
            if (!Open) return;
            var e = Event.current;
            layoutPass = e.type == EventType.Layout;
            if (dropFocus) { GUI.FocusControl(null); GUIUtility.keyboardControl = 0; if (e.type == EventType.Repaint) dropFocus = false; }
            var look = host.Look;
            theme.Build(look.TextSize(), look.Compact(), look.Opacity());
            // 0.3.3: Drawn holds this frame's rects only. Never cleared, it kept a row's rect from mid slide-in (or from another
            // text size) and the sitting's off-screen check judged that (0.3.2 sitting: "rowrt:… is off screen sideways")
            if (e.type == EventType.Repaint) Drawn.Clear();
            focusedNow = GUI.GetNameOfFocusedControl() ?? "";
            if (layoutPass)
            {
                nav.Begin(); acts.Clear(); lefts.Clear(); rights.Clear(); rowItems.Clear();
                statusShown = !string.IsNullOrEmpty(status) && Time.realtimeSinceStartup < statusUntil;
                searchUsed = search ?? "";
            }
            float s = theme.S, margin = theme.Px(16);
            float width = Mathf.Min(Mathf.Round(360 * s), Screen.width - margin * 2);   // 0.3.2: 360 (0.2.6 380, 0.3.0 420)
            float eased = 1f - (1f - slide) * (1f - slide);
            float hidden = (1 - eased) * (width + margin);
            float x = look.Left() ? margin - hidden : Screen.width - width - margin + hidden;
            var area = new Rect(x, margin, width, Screen.height - margin * 2);
            theme.Plate(area, Theme.A(Theme.Panel, theme.Opacity), theme.Px(14));
            theme.Fill(new Rect(area.x + theme.Px(14), area.y, area.width - theme.Px(28), Mathf.Max(2, theme.Px(2.5f))), Theme.Orange);
            var inner = new Rect(area.x + theme.Px(16), area.y + theme.Px(14), area.width - theme.Px(32), area.height - theme.Px(28));
            innerWidth = inner.width;
            GUILayout.BeginArea(inner);
            Header();
            if (dlg != Dlg.None) DrawDialog();
            else if (view == View.Settings) DrawSettings();
            else DrawPlaces();
            Footer();
            GUILayout.EndArea();
            if (layoutPass) nav.End();
            if (focusField != null && e.type == EventType.Repaint)
            {
                GUI.FocusControl(focusField);
                focusField = null;
            }
        }

        // ── small building blocks: every control registers for keyboard/controller focus and draws its own focus ring ──
        private void Reg(string id, Action act, Action left = null, Action right = null)
        {
            if (!layoutPass) return;
            nav.Add(id);
            if (act != null) acts[id] = act;
            if (left != null) lefts[id] = left;
            if (right != null) rights[id] = right;
        }

        // where each control was drawn in the last repaint, in screen pixels (top-left origin): the diagnostics, and the unattended in-game
        // sitting's real mouse clicks (sitting/), read it; the panel itself never does
        internal readonly Dictionary<string, Rect> Drawn = new Dictionary<string, Rect>(StringComparer.Ordinal);

        private bool Hit(string id, Rect r, Action act)
        {
            if (Event.current.type == EventType.Repaint) Drawn[id] = GUIUtility.GUIToScreenRect(r);
            if (GUI.Button(r, GUIContent.none, GUIStyle.none))
            {
                nav.Set(id);
                if (act != null) pending = act;
                return true;
            }
            return false;
        }

        private void Ring(string id, Rect r, float radius)
        {
            if (Event.current.type == EventType.Repaint && inScroll) nav.Place(id, r.y, r.height);
            if (nav.Visible && nav.Is(id)) theme.Outline(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), Theme.Blue, Mathf.Max(2, theme.Px(2)), radius + 2);
        }

        private void Text(Rect r, string text, GUIStyle style, bool strong = false)
        {
            style.font = theme.FontFor(text, strong);
            GUI.Label(r, text, style);
        }

        private void LayoutText(string text, GUIStyle style, bool strong = false, params GUILayoutOption[] opts)
        {
            style.font = theme.FontFor(text, strong);
            GUILayout.Label(text, style, opts);
        }

        // a button: kind 0 = plain, 1 = primary (orange), 2 = danger, 3 = ghost (no plate until hover)
        private bool Button(string id, string label, Icon? icon, int kind, Action act, float width = -1, bool enabled = true, string sub = null)
        {
            float h = sub != null ? theme.ButtonSubH : theme.ButtonH;
            var r = width > 0 ? GUILayoutUtility.GetRect(width, h, GUILayout.Width(width)) : GUILayoutUtility.GetRect(10, h, GUILayout.ExpandWidth(true));
            if (enabled) Reg(id, act);
            bool hover = enabled && r.Contains(Event.current.mousePosition);
            Color plate = kind == 1 ? (enabled ? (hover ? Theme.Hex(0xff9a2e) : Theme.Orange) : Theme.Raised)   // 0.3.3: a disabled primary reads as any disabled button
                : kind == 2 ? (hover ? Theme.Hex(0xff8a80) : Theme.A(Theme.Bad, .9f))
                : kind == 3 ? (hover ? Theme.Raised : Theme.A(Theme.Raised, 0f))
                : (hover ? Theme.Hex(0x2a313c) : Theme.Raised);
            theme.Plate(r, plate, theme.Px(8));
            Color ink = !enabled ? Theme.Muted : kind == 1 || kind == 2 ? Theme.Hex(0x15100a) : Theme.Ink;
            var style = kind == 1 || kind == 2 ? theme.TextStrong : theme.Text;
            var old = style.normal.textColor;
            style.normal.textColor = ink;
            float iconSize = theme.Px(18), pad = theme.Px(10);
            style.font = theme.FontFor(label, kind == 1);
            var size = style.CalcSize(new GUIContent(label));
            float total = Mathf.Min(r.width - pad * 2, size.x + (icon.HasValue ? iconSize + theme.Px(8) : 0));
            float cx = r.x + (r.width - total) / 2;
            if (icon.HasValue)
            {
                theme.DrawIcon(new Rect(cx, r.y + (sub != null ? theme.Px(theme.Compact ? 4 : 7) : (r.height - iconSize) / 2), iconSize, iconSize), icon.Value, ink);
                cx += iconSize + theme.Px(8);
            }
            var tr = new Rect(cx, sub != null ? r.y + theme.Px(theme.Compact ? 2 : 4) : r.y, r.xMax - pad - cx, sub != null ? theme.Px(22) : r.height);
            style.alignment = TextAnchor.MiddleLeft;
            GUI.Label(tr, label, style);
            style.normal.textColor = old;
            if (sub != null)
            {
                var sr = new Rect(r.x + pad, r.y + theme.Px(theme.Compact ? 23 : 25), r.width - pad * 2, theme.Px(16));
                theme.Small.alignment = TextAnchor.MiddleCenter;
                Text(sr, sub, theme.Small);
                theme.Small.alignment = TextAnchor.UpperLeft;
            }
            Ring(id, r, theme.Px(8));
            return enabled && Hit(id, r, act);
        }

        private void IconButton(string id, Icon icon, string tip, Action act)
        {
            float sz = theme.Px(34);
            var r = GUILayoutUtility.GetRect(sz, sz, GUILayout.Width(sz), GUILayout.Height(sz));
            Reg(id, act);
            bool hover = r.Contains(Event.current.mousePosition);
            theme.Plate(r, hover ? Theme.Raised : Theme.A(Theme.Raised, .0f), theme.Px(8));
            float i = theme.Px(18);
            theme.DrawIcon(new Rect(r.x + (sz - i) / 2, r.y + (sz - i) / 2, i, i), icon, hover ? Theme.Ink : Theme.Muted);
            GUI.Label(r, new GUIContent("", tip), GUIStyle.none);
            Ring(id, r, theme.Px(8));
            Hit(id, r, act);
        }

        private void Heading(string text)
        {
            GUILayout.Space(theme.Px(8));
            LayoutText(text.ToUpperInvariant(), theme.Head, true);
            GUILayout.Space(theme.Px(2));
        }

        // a text field with a placeholder; registered for focus (a controller is told it needs a keyboard)
        private string Field(string id, string value, int max, string placeholder, float width = -1)
        {
            var r = width > 0 ? GUILayoutUtility.GetRect(width, theme.Field.fixedHeight, GUILayout.Width(width)) : GUILayoutUtility.GetRect(10, theme.Field.fixedHeight, GUILayout.ExpandWidth(true));
            return FieldAt(r, id, value, max, placeholder);
        }

        private string FieldAt(Rect r, string id, string value, int max, string placeholder)
        {
            Reg(id, () => { if (!input.UsingPad) focusField = id; else Say(L.T("type.needKeyboard"), false); });
            theme.Plate(r, focusedNow == id ? Theme.Raised : Theme.Surface, theme.Px(8));
            var style = theme.Field;
            style.normal.background = style.hover.background = style.focused.background = style.active.background = null;
            style.font = theme.FontFor(value, false);
            GUI.SetNextControlName(id);
            string got = GUI.TextField(r, value ?? "", max, style);
            if (string.IsNullOrEmpty(got) && focusedNow != id && Event.current.type == EventType.Repaint) Text(r, placeholder, theme.Placeholder);
            if (focusedNow == id) theme.Outline(r, Theme.A(Theme.Blue, .9f), Mathf.Max(1, theme.Px(1.5f)), theme.Px(8));
            Ring(id, r, theme.Px(8));
            return got;
        }

        // ── the header ──
        private void Header()
        {
            GUILayout.BeginHorizontal();
            float m = theme.Px(32);
            var mr = GUILayoutUtility.GetRect(m, m, GUILayout.Width(m), GUILayout.Height(m));
            if (theme.Mark != null && Event.current.type == EventType.Repaint) GUI.DrawTexture(mr, theme.Mark, ScaleMode.ScaleToFit, true);
            GUILayout.Space(theme.Px(10));
            GUILayout.BeginVertical();
            LayoutText(view == View.Settings ? L.T("set.title") : L.T("panel.title"), theme.Title, true);
            LayoutText(Title(planetName) + " · " + SaveLabel, theme.Sub);
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            if (view == View.Settings) IconButton("h:settings", Icon.Close, L.T("set.back"), () => { view = View.Places; CloseDialog(); });
            else IconButton("h:settings", Icon.Gear, L.T("set.open"), () => { view = View.Settings; setScroll = Vector2.zero; CloseDialog(); nav.Set("set:" + section, nav.Visible); });
            GUILayout.Space(theme.Px(4));
            IconButton("h:close", Icon.Close, L.T("panel.close"), () => Hide("Close"));
            GUILayout.EndHorizontal();
            GUILayout.Space(theme.Px(12));
        }

        // ── the Places view ──
        private void DrawPlaces()
        {
            // main actions
            GUILayout.BeginHorizontal();
            Button("a:clip", L.T("act.fromClipboard"), Icon.Clipboard, 0, FromClipboard);
            GUILayout.Space(theme.Px(8));
            Button("a:save", L.T("act.saveHere"), Icon.Save, 0, SaveHere);
            GUILayout.EndHorizontal();
            GUILayout.Space(theme.Px(8));
            var back = Store.Back(world);
            string to = back == null ? null : back.Name.Length > 0 ? back.Name : Mathf.RoundToInt(back.X) + ", " + Mathf.RoundToInt(back.Z);
            Button("a:return", back == null ? L.T("act.return") : L.T("act.returnTo", to), Icon.Return, 0, host.GoBack, -1, back != null,
                back == null ? L.T("return.none") : L.T("return.swaps", KeyCheck.Label(host.Binding(ActReturn))));
            GUILayout.Space(theme.Px(12));

            // My places | Map places
            GUILayout.BeginHorizontal();
            TabButton("t:mine", L.T("tab.mine"), tab == Tab.Mine, () => SetTab(Tab.Mine));
            GUILayout.Space(theme.Px(4));
            TabButton("t:places", L.T("tab.map"), tab == Tab.Places, () => SetTab(Tab.Places));
            GUILayout.EndHorizontal();
            GUILayout.Space(theme.Px(8));

            // search and filters
            GUILayout.BeginHorizontal();
            search = Field("f:search", search, 60, tab == Tab.Places ? L.T("search.map") : L.T("search.mine"));
            GUILayout.Space(theme.Px(6));
            if (tab == Tab.Places)
            {
                Chip("f:hide", hideFound ? Icon.Check : (Icon?)null, L.T("filter.hideFound"), hideFound, () => hideFound = !hideFound);
                GUILayout.Space(theme.Px(6));
            }
            Chip("f:sort", null, byName ? L.T("sort.az") : L.T("sort.near"), false, () => byName = !byName);
            GUILayout.EndHorizontal();
            GUILayout.Space(theme.Px(6));

            // rows
            inScroll = true;
            if (Event.current.type == EventType.Repaint) nav.ClearPlaces();
            scroll = GUILayout.BeginScrollView(scroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none, GUILayout.ExpandHeight(true));
            if (tab == Tab.Mine) DrawMine(); else DrawMap();
            GUILayout.EndScrollView();
            inScroll = false;
            if (Event.current.type == EventType.Repaint) listViewHeight = GUILayoutUtility.GetLastRect().height;

            SelectedActions();
        }

        private void TabButton(string id, string label, bool on, Action act)
        {
            var r = GUILayoutUtility.GetRect(10, theme.TabH, GUILayout.ExpandWidth(true));
            Reg(id, act, () => SetTab(Tab.Mine), () => SetTab(Tab.Places));
            bool hover = r.Contains(Event.current.mousePosition);
            theme.Plate(r, on ? Theme.Surface : hover ? Theme.A(Theme.Raised, .6f) : Theme.A(Theme.Raised, 0), theme.Px(8));
            if (on) theme.Fill(new Rect(r.x + theme.Px(12), r.yMax - theme.Px(3), r.width - theme.Px(24), theme.Px(3)), Theme.Orange);
            var st = on ? theme.TextStrong : theme.Text;
            var old = st.normal.textColor;
            st.normal.textColor = on ? Theme.Ink : Theme.Muted;
            st.alignment = TextAnchor.MiddleCenter;
            Text(r, label, st, on);
            st.alignment = TextAnchor.MiddleLeft;
            st.normal.textColor = old;
            Ring(id, r, theme.Px(8));
            Hit(id, r, act);
        }

        private void Chip(string id, Icon? icon, string label, bool on, Action act)
        {
            var st = theme.Small;
            st.font = theme.FontFor(label, false);
            float w = st.CalcSize(new GUIContent(label)).x + theme.Px(icon.HasValue ? 40 : 22);
            var r = GUILayoutUtility.GetRect(w, theme.Field.fixedHeight, GUILayout.Width(w));
            Reg(id, act);
            bool hover = r.Contains(Event.current.mousePosition);
            theme.Plate(r, on ? Theme.A(Theme.Orange, .22f) : hover ? Theme.Raised : Theme.Surface, theme.Px(8));
            float x = r.x + theme.Px(11);
            if (icon.HasValue)
            {
                float i = theme.Px(14);
                theme.DrawIcon(new Rect(x, r.y + (r.height - i) / 2, i, i), icon.Value, on ? Theme.Orange : Theme.Muted);
                x += i + theme.Px(5);
            }
            var old = st.normal.textColor;
            st.normal.textColor = on ? Theme.Ink : Theme.Muted;
            st.alignment = TextAnchor.MiddleLeft;
            GUI.Label(new Rect(x, r.y, r.xMax - x, r.height), label, st);
            st.normal.textColor = old;
            st.alignment = TextAnchor.UpperLeft;
            Ring(id, r, theme.Px(8));
            Hit(id, r, act);
        }

        private static string RowId(object item, string kind) => "r" + kind + ":" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(item).ToString("x");

        private Vector3 Me()
        {
            var player = hold.Player;
            return player != null && player ? Feet(player) : Vector3.zero;
        }

        // 0.3.3: the line is "layer · distance", and the tail (the height, or "lands on the ground") comes back apart, so a
        // compact row that doesn't fit drops the tail first (RowMeta). The height shows only by RowMeta.ShowsHeight.
        private string Meta(float x, float z, float? y, Vector3 me, string first, bool underground, out string tail)
        {
            tail = null;
            string lead = first ?? "";
            if (me == Vector3.zero) return lead;
            float d = Mathf.Sqrt((x - me.x) * (x - me.x) + (z - me.z) * (z - me.z));
            lead = RowMeta.Join(lead, Distance(d));
            if (y.HasValue)
            {
                float dy = y.Value - me.y;
                if (RowMeta.ShowsHeight(d, dy, underground)) tail = L.T(dy > 0 ? "meta.above" : "meta.below", Mathf.RoundToInt(Mathf.Abs(dy)));
            }
            else tail = L.T("meta.ground");
            return lead;
        }

        private static string Distance(float metres) =>
            metres < 1000 ? Mathf.RoundToInt(metres) + " m" : (metres / 1000f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " km";

        private bool Matches(string name) => searchUsed.Trim().Length == 0 || name.IndexOf(searchUsed.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;

        private void DrawMine()
        {
            var here = Store.For(world);
            var me = Me();
            var list = here.Saved.FindAll(p => Matches(p.Name));
            if (byName) list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            else if (me != Vector3.zero) list.Sort((a, b) => a.Distance2D(me.x, me.z).CompareTo(b.Distance2D(me.x, me.z)));
            var favs = list.FindAll(p => p.Fav);
            var rest = list.FindAll(p => !p.Fav);
            if (here.Saved.Count == 0)
            {
                GUILayout.Space(theme.Px(6));
                LayoutText(L.T("mine.empty", KeyCheck.Label(host.Binding(ActPlaces))), theme.SmallWrap);
            }
            else if (list.Count == 0) LayoutText(L.T("mine.noMatch"), theme.SmallWrap);
            if (favs.Count > 0)
            {
                Heading(L.T("head.favourites"));
                foreach (var p in favs) PlaceRow(p, me, false);
            }
            if (rest.Count > 0)
            {
                Heading(L.T("head.mine"));
                // ungrouped first, then each map group under its own small label, in the order it first appears
                var groups = new List<string> { "" };
                foreach (var p in rest) if (!groups.Contains(p.Group)) groups.Add(p.Group);
                foreach (var g in groups)
                {
                    bool any = false;
                    foreach (var p in rest)
                    {
                        if (p.Group != g) continue;
                        if (!any && g.Length > 0) LayoutText(g, theme.Small);
                        any = true;
                        PlaceRow(p, me, false);
                    }
                }
            }
            var recent = here.Recent.FindAll(p => Matches(p.Name));
            if (recent.Count > 0)
            {
                Heading(L.T("head.recent"));
                foreach (var p in recent) PlaceRow(p, me, true);
            }
        }

        private void PlaceRow(Place p, Vector3 me, bool recent)
        {
            string id = RowId(p, recent ? "t" : "m");
            string meta = Meta(p.X, p.Z, p.AutoHeight ? (float?)null : p.Y, me, null, RowMeta.Underground(p.Name, null), out string tail);
            Row(id, p, p.Name, meta,
                recent ? Icon.Clock : p.Fav ? Icon.StarFilled : (Icon?)null, recent ? Theme.Faint : p.Fav ? Theme.Orange : SpotTint(p),
                selPlace == p, false, true, 0, () => { host.Go(p.Request, p.Name); }, noteable: !recent, metaTail: tail);   // Recently travelled rolls over: no notes
        }

        private static Color SpotTint(Place p) => Theme.SpotColour.TryGetValue(p.Colour, out var c) ? c : Theme.Orange;

        // one row: [icon or found box] name / meta [+]. Click teleports (0.3.2 default, as 0.2.x) or selects ("Selects it");
        // right-click always selects; a second click or Enter/A teleports. A found box is its own control (Mark found by
        // hand), never set by arriving. 0.3.2: the + opens a one-line note box under the name (Right / D-pad right too); a
        // noted row shows its note under the name in yellow, with a yellow edge, so it stands out while you scroll.
        private void Row(string id, object item, string name, string meta, Icon? icon, Color iconTint, bool selected, bool muted, bool canGo,
            float indent, Action go, Action left = null, Action right = null, string foundId = null, bool found = false, bool noteable = true, string metaTail = null)
        {
            float h = theme.Row;
            string note = noteable ? NoteOf(item) ?? "" : "";
            bool editing = noteable && noteRow == id;
            bool hasNote = note.Length > 0 && !editing;
            float chipH = theme.Px(26);
            float total = h + (editing ? theme.Field.fixedHeight + chipH + theme.Px(12) : hasNote ? theme.Px(13) : 0);
            var r = GUILayoutUtility.GetRect(10, total, GUILayout.ExpandWidth(true));
            var ev = Event.current;
            if (layoutPass) rowItems[id] = item;
            Action primary = canGo ? go : null;
            Action openNote = noteable ? () => OpenNote(id, item) : (Action)null;
            Reg(id, primary ?? (() => { }), left, right ?? openNote);
            bool hover = r.Contains(ev.mousePosition);
            if (selected) { theme.Plate(r, Theme.A(Theme.Orange, .16f), theme.Px(8)); theme.Fill(new Rect(r.x, r.y + theme.Px(6), theme.Px(3), r.height - theme.Px(12)), Theme.Orange); }
            else if (hover) theme.Plate(r, Theme.A(Theme.Raised, .8f), theme.Px(8));
            else if (hasNote) theme.Plate(r, Theme.A(Theme.Note, .07f), theme.Px(8));
            if (hasNote && !selected) theme.Fill(new Rect(r.x, r.y + theme.Px(5), theme.Px(3), r.height - theme.Px(10)), Theme.Note);
            float rightEdge = noteable ? r.xMax - theme.Px(30) : r.xMax;   // the + sits beyond this
            float x = r.x + theme.Px(10) + indent;
            float iz = theme.Px(16);
            if (foundId != null)
            {
                // the found box: a check icon in a ring, muted text beside it; never colour alone
                var box = new Rect(x - theme.Px(4), r.y + (h - theme.Px(26)) / 2, theme.Px(26), theme.Px(26));
                string fid = "x" + id;
                Reg(fid, () => { selPoi = item as Poi; selPlace = null; ToggleFound(); });
                theme.Plate(box, found ? Theme.A(Theme.Good, .22f) : box.Contains(ev.mousePosition) ? Theme.Raised : Theme.Surface, theme.Px(6));
                if (found) theme.DrawIcon(new Rect(box.x + theme.Px(5), box.y + theme.Px(5), theme.Px(16), theme.Px(16)), Icon.Check, Theme.Good);
                Ring(fid, box, theme.Px(6));
                if (GUI.Button(box, GUIContent.none, GUIStyle.none)) { nav.Set(fid); var target = item as Poi; pending = () => { selPoi = target; selPlace = null; ToggleFound(); }; }
                x += theme.Px(28);
            }
            else if (icon.HasValue)
            {
                theme.DrawIcon(new Rect(x, r.y + (h - iz) / 2, iz, iz), icon.Value, iconTint);
                x += iz + theme.Px(10);
            }
            else
            {
                float d = theme.Px(9);
                theme.Plate(new Rect(x + (iz - d) / 2, r.y + (h - d) / 2, d, d), iconTint, d / 2);
                x += iz + theme.Px(10);
            }
            // 0.3.1: a found place is not dimmed; it has moved to the Found drawer, and its green tick says so
            var ns = muted ? theme.RowNameMuted : theme.RowName;
            bool twoLines = !theme.Compact && !string.IsNullOrEmpty(meta);
            if (twoLines)
            {
                Text(new Rect(x, r.y + theme.Px(4), rightEdge - x - theme.Px(8), theme.Px(22)), name, ns, !muted);
                Text(new Rect(x, r.y + theme.Px(23), rightEdge - x - theme.Px(8), theme.Px(17)), RowMeta.Join(meta, metaTail), theme.Meta);
            }
            else
            {
                float mw = 0, maxMeta = (rightEdge - x) * .55f;
                if (!string.IsNullOrEmpty(meta))
                {
                    theme.Meta.font = theme.FontFor(meta, false);
                    // 0.3.3: "Storage crates · 115 m · 26 m below you" loses the height first, then "Storage crates · "
                    meta = RowMeta.Fit(meta, metaTail, m => theme.Meta.CalcSize(new GUIContent(m)).x, maxMeta);
                    mw = Mathf.Min(theme.Meta.CalcSize(new GUIContent(meta)).x, maxMeta);
                }
                ns.alignment = TextAnchor.MiddleLeft;
                Text(new Rect(x, r.y, rightEdge - x - mw - theme.Px(12), h), name, ns, !muted);
                ns.alignment = TextAnchor.UpperLeft;
                if (mw > 0) { theme.Meta.alignment = TextAnchor.MiddleRight; Text(new Rect(rightEdge - mw - theme.Px(4), r.y, mw, h), meta, theme.Meta); theme.Meta.alignment = TextAnchor.UpperLeft; }
            }
            if (nav.Visible && nav.Is(id) && canGo && ev.type == EventType.Repaint)
            {
                // the focused row says what Enter / A does
                string hint = input.UsingPad ? KeyCheck.Label("<Gamepad>/buttonSouth", input.PlayStation) : "Enter";
                var hs = theme.Key;
                hs.font = theme.FontFor(hint, true);
                float hw = hs.CalcSize(new GUIContent(hint)).x + theme.Px(4);
                var hr = new Rect(rightEdge - hw - theme.Px(4), r.y + theme.Px(3), hw, theme.Px(16));
                theme.Plate(hr, Theme.A(Theme.Blue, .25f), theme.Px(4));
                GUI.Label(hr, hint, hs);
            }
            Ring(id, new Rect(r.x, r.y, r.width, h), theme.Px(8));
            if (noteable)
            {
                // the + (a pencil once the place has a note): its own small button at the row's right end
                float pz = theme.Px(24);
                var pr = new Rect(r.xMax - theme.Px(28), r.y + (h - pz) / 2, pz, pz);
                string pid = "p" + id;
                bool ph = pr.Contains(ev.mousePosition);
                theme.Plate(pr, ph || editing ? Theme.Raised : Theme.A(Theme.Raised, .45f), theme.Px(6));
                Color pc = ph || editing ? Theme.Ink : note.Length > 0 ? Theme.Note : Theme.Muted;
                if (note.Length > 0) theme.DrawIcon(new Rect(pr.x + theme.Px(5), pr.y + theme.Px(5), theme.Px(14), theme.Px(14)), Icon.Pencil, pc);
                else
                {
                    float arm = theme.Px(12), bar = Mathf.Max(2, theme.Px(2));
                    theme.Fill(new Rect(pr.center.x - arm / 2, pr.center.y - bar / 2, arm, bar), pc);
                    theme.Fill(new Rect(pr.center.x - bar / 2, pr.center.y - arm / 2, bar, arm), pc);
                }
                if (ev.type == EventType.Repaint) Drawn[pid] = GUIUtility.GUIToScreenRect(pr);
                if (GUI.Button(pr, GUIContent.none, GUIStyle.none)) { nav.Set(id); pending = editing ? CancelNote : openNote; }
            }
            if (hasNote)
            {
                // the note, under the name: click it to change it
                var nr = new Rect(x, r.y + h - theme.Px(5), rightEdge - x - theme.Px(4), theme.Px(16));
                theme.DrawIcon(new Rect(nr.x, nr.y + theme.Px(2), theme.Px(12), theme.Px(12)), Icon.Pencil, Theme.Note);
                theme.NoteText.alignment = TextAnchor.MiddleLeft;
                Text(new Rect(nr.x + theme.Px(16), nr.y, nr.width - theme.Px(16), nr.height), note, theme.NoteText);
                if (GUI.Button(nr, GUIContent.none, GUIStyle.none)) { nav.Set(id); pending = openNote; }
            }
            if (editing) NoteBox(new Rect(x - theme.Px(4), r.y + h, r.xMax - x - theme.Px(4), total - h), note);
            var hitRect = foundId != null ? new Rect(r.x + theme.Px(36) + indent, r.y, rightEdge - r.x - theme.Px(36) - indent, h) : new Rect(r.x, r.y, rightEdge - r.x, h);
            var it = item;
            // right-click: select only, whatever a click does
            if (ev.type == EventType.MouseDown && ev.button == 1 && hitRect.Contains(ev.mousePosition))
            {
                nav.Set(id);
                pending = () => { if (it is Place pl) { selPlace = pl; selPoi = null; } else if (it is Poi po) { selPoi = po; selPlace = null; } };
                ev.Use();
            }
            if (ev.type == EventType.Repaint) Drawn["row" + id] = GUIUtility.GUIToScreenRect(hitRect);
            if (GUI.Button(hitRect, GUIContent.none, GUIStyle.none))
            {
                nav.Set(id);
                bool again = lastClickId == id && Time.realtimeSinceStartup - lastClickAt < .4f;
                lastClickId = id;
                lastClickAt = Time.realtimeSinceStartup;
                pending = () =>
                {
                    if (it is Place pl) { selPlace = pl; selPoi = null; } else if (it is Poi po) { selPoi = po; selPlace = null; }
                    if (canGo && (again || host.Look.QuickTeleport())) go();
                };
            }
        }

        // 0.3.2: the note box under a row: one line (Enter saves, Esc cancels, empty deletes), then two ready-made notes a
        // controller can pick (it can't type), and Delete when there is a note
        private void NoteBox(Rect r, string had)
        {
            var fr = new Rect(r.x, r.y + theme.Px(2), r.width, theme.Field.fixedHeight);
            noteText = FieldAt(fr, "ne:field", noteText, PlaceStore.NoteMax, L.T("note.placeholder"));
            float y = fr.yMax + theme.Px(6), cx = r.x, ch = theme.Px(26);
            var presets = new[] { L.T("note.preset.later"), L.T("note.preset.locked") };
            for (int i = 0; i < presets.Length; i++)
            {
                string text = presets[i];
                cx += MiniButton("ne:p" + i, cx, y, ch, text, false, () => SaveNote(text)) + theme.Px(6);
            }
            if (had.Length > 0) MiniButton("ne:del", cx, y, ch, L.T("note.delete"), true, () => SaveNote(""));
        }

        private float MiniButton(string id, float x, float y, float h, string label, bool danger, Action act)
        {
            var st = theme.Small;
            st.font = theme.FontFor(label, false);
            float w = st.CalcSize(new GUIContent(label)).x + theme.Px(16);
            var r = new Rect(x, y, w, h);
            Reg(id, act);
            bool hover = r.Contains(Event.current.mousePosition);
            theme.Plate(r, hover ? Theme.Raised : Theme.Surface, theme.Px(6));
            var old = st.normal.textColor;
            st.normal.textColor = danger ? Theme.BadText : Theme.Ink;
            st.alignment = TextAnchor.MiddleCenter;
            GUI.Label(r, label, st);
            st.alignment = TextAnchor.UpperLeft;
            st.normal.textColor = old;
            Ring(id, r, theme.Px(6));
            Hit(id, r, act);
            return w;
        }

        // a group / layer header in the Map places tree: a chevron, the name, a count; Left folds, Right opens
        private void FoldRow(string id, string label, bool open, string count, float indent, Action toggle, bool strong, Action tick = null, bool on = false)
        {
            float h = Mathf.Round((theme.Compact ? 30 : 36) * theme.S);
            var r = GUILayoutUtility.GetRect(10, h, GUILayout.ExpandWidth(true));
            Reg(id, toggle, open ? toggle : null, open ? null : toggle);
            bool hover = r.Contains(Event.current.mousePosition);
            if (hover) theme.Plate(r, Theme.A(Theme.Raised, .7f), theme.Px(8));
            float x = r.x + theme.Px(8) + indent;
            if (tick != null)
            {
                // the layer's "on" box (feeds Nearest); its own control
                var box = new Rect(x, r.y + (h - theme.Px(22)) / 2, theme.Px(22), theme.Px(22));
                string tid = "o" + id;
                Reg(tid, tick);
                theme.Plate(box, on ? Theme.A(Theme.Orange, .3f) : Theme.Surface, theme.Px(5));
                if (on) theme.DrawIcon(new Rect(box.x + theme.Px(4), box.y + theme.Px(4), theme.Px(14), theme.Px(14)), Icon.Check, Theme.Orange);
                Ring(tid, box, theme.Px(5));
                if (GUI.Button(box, GUIContent.none, GUIStyle.none)) { nav.Set(tid); pending = tick; }
                x += theme.Px(30);
            }
            float iz = theme.Px(14);
            theme.DrawIcon(new Rect(x, r.y + (h - iz) / 2, iz, iz), open ? Icon.ChevronDown : Icon.ChevronRight, Theme.Muted);
            x += iz + theme.Px(8);
            var st = strong ? theme.Head : theme.Text;
            st.alignment = TextAnchor.MiddleLeft;
            float cw = 0;
            if (!string.IsNullOrEmpty(count)) { theme.Meta.font = theme.FontFor(count, false); cw = theme.Meta.CalcSize(new GUIContent(count)).x; }
            Text(new Rect(x, r.y, r.xMax - x - cw - theme.Px(16), h), strong ? label.ToUpperInvariant() : label, st, strong);
            st.alignment = TextAnchor.UpperLeft;
            if (cw > 0) { theme.Meta.alignment = TextAnchor.MiddleRight; Text(new Rect(r.xMax - cw - theme.Px(10), r.y, cw, h), count, theme.Meta); theme.Meta.alignment = TextAnchor.UpperLeft; }
            Ring(id, r, theme.Px(8));
            var hit = tick != null ? new Rect(r.x + theme.Px(38) + indent, r.y, r.width - theme.Px(38) - indent, r.height) : r;
            Hit(id, hit, toggle);
        }

        // 0.2.3 the tree: Nearest on top, then each group folds and each layer opens its own places right under it
        private void DrawMap()
        {
            var pw = PoisFor(world);
            if (pw == null) { LayoutText(L.T("map.noData"), theme.SmallWrap); return; }
            var tree = TreeFor(pw);
            var here = Store.For(world);
            var me = Me();
            if (layoutPass) rows = tree.Rows(pw, searchUsed, me.x, me.z, here.Visited, hideFound);
            foreach (var r in rows)
            {
                switch (r.Kind)
                {
                    case RowKind.Nearest:
                        FoldRow("n:near", L.T("tree.nearest"), r.Open, r.Count == 0 ? "" : L.N("tree.onLayers", r.Count), 0, () => tree.NearestOpen = !tree.NearestOpen, true);
                        break;
                    case RowKind.Found:
                        // 0.3.1 the Found drawer at the bottom: closed until opened; open by itself while a search has found matches
                        GUILayout.Space(theme.Px(10));
                        FoldRow("n:found", L.T("tree.found"), r.Open, searchUsed.Trim().Length > 0 ? L.T("tree.matching", r.Count) : r.Count.ToString(), 0, tree.ToggleDrawer, true);
                        break;
                    case RowKind.Group:
                    {
                        string g = r.Group;
                        if (r.InFound) FoldRow("fg:" + g, g.Length > 0 ? g : L.T("tree.other"), r.Open, r.Count.ToString(), theme.Px(12), () => tree.ToggleFoundGroup(g), true);
                        else FoldRow("g:" + g, g.Length > 0 ? g : L.T("tree.other"), r.Open, r.Visited > 0 ? L.T("tree.foundOf", r.Visited, r.Count) : "", 0, () => tree.ToggleGroup(g), true);
                        break;
                    }
                    case RowKind.Layer:
                    {
                        int k = r.Cat;
                        var c = pw.Cats[k];
                        if (r.InFound) FoldRow("fl:" + c.Key, c.Name, r.Open, r.Count.ToString(), theme.Px(24), () => tree.ToggleFoundLayer(pw, k), false);
                        else FoldRow("l:" + c.Key, c.Name, r.Open, c.Findable && r.Visited > 0 ? L.T("tree.foundOf", r.Visited, r.Count) : r.Count.ToString(),
                            theme.Px(12), () => tree.ToggleLayer(pw, k), false, () => tree.ToggleOn(pw, k), r.On);
                        break;
                    }
                    case RowKind.Place:
                    {
                        var p = r.Poi;
                        var cat = pw.Cats[p.Cat];
                        bool found = here.Visited.Contains(p.Id);
                        string name = pw.NameOf(p);
                        string tail = null;
                        string meta = p.HasLanding ? Meta(p.LX, p.LZ, p.LY, me, r.Depth >= 2 ? null : cat.Name, RowMeta.Underground(name, cat.Name), out tail) : L.T("poi.noLandingShort");
                        string kind = r.InFound ? "d" : r.Depth == 0 ? "f" : r.Depth == 1 ? "n" : "l";
                        Row(RowId(p, kind), p, name, meta, null, Theme.Faint, selPoi == p, !p.HasLanding, p.HasLanding,
                            theme.Px(r.Depth == 3 ? 42 : r.Depth == 2 ? 30 : r.Depth == 1 ? 12 : 0), () => host.Go(p.Landing, name), null, null, cat.Findable ? p.Id : null, found, metaTail: tail);
                        break;
                    }
                    case RowKind.More:
                    {
                        int k = r.Cat;
                        GUILayout.BeginHorizontal();
                        GUILayout.Space(theme.Px(r.InFound ? 42 : 30));
                        if (r.InFound) Button("fm:" + pw.Cats[k].Key, L.T("tree.more", Math.Min(PlacesTree.LayerMore, r.Left), r.Left), null, 3, () => tree.ShowMoreFound(pw, k));
                        else Button("m:" + pw.Cats[k].Key, L.T("tree.more", Math.Min(PlacesTree.LayerMore, r.Left), r.Left), null, 3, () => tree.ShowMore(pw, k));
                        GUILayout.EndHorizontal();
                        break;
                    }
                    default:
                        GUILayout.BeginHorizontal();
                        GUILayout.Space(theme.Px(r.Depth > 0 ? 30 : 4));
                        LayoutText(r.Text, theme.SmallWrap);
                        GUILayout.EndHorizontal();
                        break;
                }
            }
            if (Found() != null)
            {
                GUILayout.Space(theme.Px(10));
                Button("m:copyformap", L.T("found.copyForMap"), Icon.Map, 3, CopyForMap, -1, true, L.T("found.copyForMapSub"));
            }
        }

        // ── the selected place's actions, below the list ──
        private void SelectedActions()
        {
            if (!HasSelection) return;
            var pw = PoisFor(world);
            string name = selPlace != null ? selPlace.Name : pw != null ? pw.NameOf(selPoi) : "";
            GUILayout.Space(theme.Px(8));
            theme.Fill(GUILayoutUtility.GetRect(10, 1, GUILayout.ExpandWidth(true)), Theme.Line);
            GUILayout.Space(theme.Px(8));
            LayoutText(name, theme.TextStrong, true);
            GUILayout.Space(theme.Px(6));
            bool canGo = selPlace != null || selPoi != null && selPoi.HasLanding;
            GUILayout.BeginHorizontal();
            Button("s:go", host.Busy ? L.T("tp.preparing") : L.T("act.teleport"), Icon.Go, 1, Teleport, -1, canGo && !host.Busy);
            GUILayout.EndHorizontal();
            GUILayout.Space(theme.Px(6));
            var more = new List<ActionButton>();
            if (selPlace != null)
            {
                if (!IsRecent(selPlace))
                {
                    more.Add(new ActionButton("s:fav", selPlace.Fav ? L.T("act.unfav") : L.T("act.fav"), selPlace.Fav ? Icon.StarFilled : Icon.Star, ToggleFav));
                    more.Add(new ActionButton("s:rename", L.T("act.rename"), Icon.Pencil, () => { rename = selPlace.Name; OpenDialog(Dlg.Rename, "d:renamefield"); }));
                }
            }
            else
            {
                if (pw != null && pw.Cats[selPoi.Cat].Findable)
                {
                    bool f = Store.For(world).Visited.Contains(selPoi.Id);
                    more.Add(new ActionButton("s:found", f ? L.T("act.unmark") : L.T("act.markFound"), Icon.Found, ToggleFound));
                }
                more.Add(new ActionButton("s:fav", L.T("act.saveToMine"), Icon.Star, ToggleFav, selPoi.HasLanding));
            }
            more.Add(new ActionButton("s:map", L.T("act.showOnMap"), Icon.Map, ShowOnMap));
            if (selPlace != null) more.Add(new ActionButton("s:delete", L.T("act.delete"), Icon.Trash, DeleteSelected));
            FlowButtons(more);
        }

        private sealed class ActionButton
        {
            public readonly string Id, Label;
            public readonly Icon Icon;
            public readonly Action Act;
            public readonly bool Enabled;
            public ActionButton(string id, string label, Icon icon, Action act, bool enabled = true) { Id = id; Label = label; Icon = icon; Act = act; Enabled = enabled; }
        }

        // 0.3.2: buttons side by side while their whole labels fit, the rest on the next line; the spare width shared out
        private void FlowButtons(List<ActionButton> list)
        {
            float gap = theme.Px(6), maxW = innerWidth;
            var need = new List<float>();
            foreach (var b in list)
            {
                theme.Text.font = theme.FontFor(b.Label, false);
                need.Add(theme.Text.CalcSize(new GUIContent(b.Label)).x + theme.Px(18) + theme.Px(8) + theme.Px(24));
            }
            int at = 0;
            while (at < list.Count)
            {
                int end = at;
                float used = need[at];
                while (end + 1 < list.Count && used + gap + need[end + 1] <= maxW) { end++; used += gap + need[end]; }
                float spare = (maxW - used) / (end - at + 1);
                GUILayout.BeginHorizontal();
                for (int i = at; i <= end; i++)
                {
                    var b = list[i];
                    Button(b.Id, b.Label, b.Icon, 0, b.Act, Mathf.Floor(need[i] + spare), b.Enabled);
                    if (i < end) GUILayout.Space(gap);
                }
                GUILayout.EndHorizontal();
                if (end + 1 < list.Count) GUILayout.Space(gap);
                at = end + 1;
            }
        }

        private bool IsRecent(Place p) => Store.For(world).Recent.Contains(p);

        // ── the footer: what just happened, and the keys or buttons for the device in use ──
        private void Footer()
        {
            GUILayout.Space(theme.Px(8));
            if (statusShown)
            {
                GUILayout.BeginHorizontal();
                var st = statusBad ? theme.StatusBad : theme.Status;
                LayoutText(status, st, false, GUILayout.ExpandWidth(true));
                if (undo != null || undoLook != null) { GUILayout.Space(theme.Px(6)); Button("u:undo", L.T("act.undo"), Icon.Undo, 0, Undo, theme.Px(96)); }
                GUILayout.EndHorizontal();
                GUILayout.Space(theme.Px(6));
            }
            Prompts();
        }

        private void Prompts()
        {
            var keys = new List<KeyValuePair<string, string>>();
            bool pad = input.UsingPad, ps = input.PlayStation;
            string A = KeyCheck.Label("<Gamepad>/buttonSouth", ps), B = KeyCheck.Label("<Gamepad>/buttonEast", ps);
            if (dlg == Dlg.Capture) keys.Add(new KeyValuePair<string, string>(pad ? L.T("prompt.wait") : "Esc", L.T("prompt.cancel")));
            else if (noteRow != null && dlg == Dlg.None && view == View.Places)
            {
                // 0.3.2: the note box
                keys.Add(new KeyValuePair<string, string>(pad ? A : "Enter", pad ? L.T("prompt.choose") : L.T("prompt.save")));
                keys.Add(new KeyValuePair<string, string>(pad ? B : "Esc", L.T("prompt.cancel")));
            }
            else if (dlg != Dlg.None)
            {
                keys.Add(new KeyValuePair<string, string>(pad ? A : "Enter", L.T("prompt.choose")));
                keys.Add(new KeyValuePair<string, string>(pad ? B : "Esc", L.T("prompt.cancel")));
            }
            else if (pad)
            {
                keys.Add(new KeyValuePair<string, string>(A, view == View.Places ? L.T("prompt.teleport") : L.T("prompt.choose")));
                keys.Add(new KeyValuePair<string, string>(B, view == View.Places ? L.T("prompt.close") : L.T("prompt.back")));
                keys.Add(new KeyValuePair<string, string>(KeyCheck.Label("<Gamepad>/leftShoulder", ps) + "/" + KeyCheck.Label("<Gamepad>/rightShoulder", ps), view == View.Places ? L.T("prompt.tabs") : L.T("prompt.sections")));
                if (view == View.Places)
                {
                    keys.Add(new KeyValuePair<string, string>("D-pad →", L.T("prompt.note")));
                    keys.Add(new KeyValuePair<string, string>(KeyCheck.Label("<Gamepad>/buttonWest", ps), L.T("act.saveHere")));
                    keys.Add(new KeyValuePair<string, string>(KeyCheck.Label("<Gamepad>/buttonNorth", ps), L.T("prompt.actions")));
                }
            }
            else
            {
                keys.Add(new KeyValuePair<string, string>("Enter", view == View.Places ? L.T("prompt.teleport") : L.T("prompt.choose")));
                keys.Add(new KeyValuePair<string, string>("↑↓", L.T("prompt.move")));
                if (view == View.Places) keys.Add(new KeyValuePair<string, string>("→", L.T("prompt.note")));
                // 0.3.2: one chip for both ways out (compact: 0.3.1 gave the Places key its own)
                keys.Add(new KeyValuePair<string, string>(view == View.Places ? "Esc / " + KeyCheck.Label(host.Binding(ActPlaces)) : "Esc", view == View.Places ? L.T("prompt.close") : L.T("prompt.back")));
                if (view == View.Places && host.Look.QuickTeleport()) keys.Add(new KeyValuePair<string, string>(L.T("prompt.rightClick"), L.T("prompt.select")));
            }
            // chips that wrap onto a second line when the panel is narrow
            float maxW = innerWidth, lineW = 0;
            GUILayout.BeginHorizontal();
            foreach (var kv in keys)
            {
                var ks = theme.Key;
                ks.font = theme.FontFor(kv.Key, true);
                float kw = ks.CalcSize(new GUIContent(kv.Key)).x + theme.Px(4);
                theme.Prompt.font = theme.FontFor(kv.Value, false);
                float lw = theme.Prompt.CalcSize(new GUIContent(kv.Value)).x;
                float w = kw + theme.Px(5) + lw + theme.Px(12);
                if (lineW > 0 && lineW + w > maxW) { GUILayout.FlexibleSpace(); GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); lineW = 0; }
                var kr = GUILayoutUtility.GetRect(kw, theme.Px(18), GUILayout.Width(kw));
                theme.Plate(kr, Theme.Raised, theme.Px(4));
                GUI.Label(kr, kv.Key, ks);
                GUILayout.Space(theme.Px(5));
                var lr = GUILayoutUtility.GetRect(lw, theme.Px(18), GUILayout.Width(lw));
                theme.Prompt.alignment = TextAnchor.MiddleLeft;
                GUI.Label(lr, kv.Value, theme.Prompt);
                GUILayout.Space(theme.Px(12));
                lineW += w;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        // ══ Settings ═════════════════════════════════════════════════════════════════════════════════════════════════
        private void DrawSettings()
        {
            // the sections: Controls / Appearance / Behaviour / Data / Help, wrapping onto two lines when narrow
            GUILayout.BeginHorizontal();
            int i = 0;
            foreach (Section sec in Enum.GetValues(typeof(Section)))
            {
                if (i++ == 3) { GUILayout.EndHorizontal(); GUILayout.Space(theme.Px(4)); GUILayout.BeginHorizontal(); }
                var sc = sec;
                SectionTab("set:" + sec, L.T("sec." + sec), section == sec, () => { section = sc; setScroll = Vector2.zero; });
                GUILayout.Space(theme.Px(4));
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(theme.Px(10));
            inScroll = true;
            if (Event.current.type == EventType.Repaint) nav.ClearPlaces();
            setScroll = GUILayout.BeginScrollView(setScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none, GUILayout.ExpandHeight(true));
            switch (section)
            {
                case Section.Controls: DrawControls(); break;
                case Section.Appearance: DrawAppearance(); break;
                case Section.Behaviour: DrawBehaviour(); break;
                case Section.Data: DrawData(); break;
                default: DrawHelp(); break;
            }
            GUILayout.Space(theme.Px(8));
            GUILayout.EndScrollView();
            inScroll = false;
            if (Event.current.type == EventType.Repaint) setViewHeight = GUILayoutUtility.GetLastRect().height;
        }

        private void SectionTab(string id, string label, bool on, Action act)
        {
            var st = on ? theme.TextStrong : theme.Text;
            st.font = theme.FontFor(label, on);
            float w = st.CalcSize(new GUIContent(label)).x + theme.Px(20);
            var r = GUILayoutUtility.GetRect(w, theme.Px(32), GUILayout.MinWidth(w), GUILayout.ExpandWidth(true));
            Reg(id, act, () => { section = (Section)(((int)section + 4) % 5); nav.Set("set:" + section, true); }, () => { section = (Section)(((int)section + 1) % 5); nav.Set("set:" + section, true); });
            bool hover = r.Contains(Event.current.mousePosition);
            theme.Plate(r, on ? Theme.Surface : hover ? Theme.A(Theme.Raised, .6f) : Theme.A(Theme.Raised, 0), theme.Px(8));
            if (on) theme.Fill(new Rect(r.x + theme.Px(10), r.yMax - theme.Px(3), r.width - theme.Px(20), theme.Px(3)), Theme.Orange);
            var old = st.normal.textColor;
            st.normal.textColor = on ? Theme.Ink : Theme.Muted;
            st.alignment = TextAnchor.MiddleCenter;
            GUI.Label(r, label, st);
            st.alignment = TextAnchor.UpperLeft;
            st.normal.textColor = old;
            Ring(id, r, theme.Px(8));
            Hit(id, r, act);
        }

        // a setting row: its name (and a line under it), then a value control on the right
        private void SettingLabel(string name, string note)
        {
            GUILayout.BeginVertical();
            LayoutText(name, theme.TextStrong, true);
            if (!string.IsNullOrEmpty(note)) LayoutText(note, theme.SmallWrap);
            GUILayout.EndVertical();
        }

        // a choice between a few values, as a segmented control: Left/Right step it
        private void Segmented(string id, string[] labels, int at, Action<int> set)
        {
            GUILayout.BeginHorizontal();
            for (int i = 0; i < labels.Length; i++)
            {
                int k = i;
                var r = GUILayoutUtility.GetRect(10, theme.Px(34), GUILayout.ExpandWidth(true));
                string cid = id + ":" + i;
                Reg(cid, () => set(k), () => set(Math.Max(0, at - 1)), () => set(Math.Min(labels.Length - 1, at + 1)));
                bool on = i == at, hover = r.Contains(Event.current.mousePosition);
                theme.Plate(r, on ? Theme.A(Theme.Orange, .9f) : hover ? Theme.Raised : Theme.Surface, theme.Px(8));
                var st = on ? theme.TextStrong : theme.Text;
                var old = st.normal.textColor;
                st.normal.textColor = on ? Theme.Hex(0x15100a) : Theme.Ink;
                st.alignment = TextAnchor.MiddleCenter;
                Text(r, labels[i], st, on);
                st.alignment = TextAnchor.UpperLeft;
                st.normal.textColor = old;
                Ring(cid, r, theme.Px(8));
                Hit(cid, r, () => set(k));
                if (i < labels.Length - 1) GUILayout.Space(theme.Px(4));
            }
            GUILayout.EndHorizontal();
        }

        private void Stepper(string id, string value, Action down, Action up)
        {
            GUILayout.BeginHorizontal();
            Button(id + ":down", "−", null, 0, down, theme.Px(44));
            GUILayout.Space(theme.Px(6));
            var r = GUILayoutUtility.GetRect(theme.Px(70), theme.Px(38), GUILayout.ExpandWidth(true));
            Reg(id, null, down, up);
            theme.Plate(r, Theme.Surface, theme.Px(8));
            theme.TextStrong.alignment = TextAnchor.MiddleCenter;
            Text(r, value, theme.TextStrong, true);
            theme.TextStrong.alignment = TextAnchor.UpperLeft;
            Ring(id, r, theme.Px(8));
            GUILayout.Space(theme.Px(6));
            Button(id + ":up", "+", null, 0, up, theme.Px(44));
            GUILayout.EndHorizontal();
        }

        private void Toggle(string id, string name, string note, bool on, Action flip)
        {
            GUILayout.BeginHorizontal();
            SettingLabel(name, note);
            GUILayout.Space(theme.Px(8));
            var r = GUILayoutUtility.GetRect(theme.Px(52), theme.Px(30), GUILayout.Width(theme.Px(52)));
            Reg(id, flip, on ? flip : null, on ? null : flip);
            theme.Plate(r, on ? Theme.Orange : Theme.Raised, r.height / 2);
            float k = r.height - theme.Px(6);
            theme.Plate(new Rect(on ? r.xMax - k - theme.Px(3) : r.x + theme.Px(3), r.y + theme.Px(3), k, k), on ? Theme.Hex(0x15100a) : Theme.Muted, k / 2);
            Ring(id, r, r.height / 2);
            Hit(id, r, flip);
            GUILayout.EndHorizontal();
            GUILayout.Space(theme.Px(10));
        }

        private void Gap() => GUILayout.Space(theme.Px(12));

        private void DrawControls()
        {
            LayoutText(L.T("controls.intro"), theme.SmallWrap);
            Gap();
            var ours = new List<KeyValuePair<string, string>>();
            foreach (var a in Actions) ours.Add(new KeyValuePair<string, string>(a, host.Binding(a)));
            var clashes = KeyCheck.Find(ours, host.GameBindings());
            foreach (var a in Actions)
            {
                string action = a;
                bool pad = a == ActPadPlaces || a == ActPadReturn;
                GUILayout.BeginHorizontal();
                SettingLabel(ActionName(a), L.T("act." + a + ".note"));
                GUILayout.Space(theme.Px(8));
                var key = theme.Key;
                string label = KeyCheck.Label(host.Binding(a), input.PlayStation);
                key.font = theme.FontFor(label, true);
                float kw = Mathf.Max(theme.Px(54), key.CalcSize(new GUIContent(label)).x + theme.Px(16));
                var kr = GUILayoutUtility.GetRect(kw, theme.Px(30), GUILayout.Width(kw));
                theme.Plate(kr, Theme.Raised, theme.Px(6));
                GUI.Label(kr, label, key);
                GUILayout.EndHorizontal();
                GUILayout.Space(theme.Px(4));
                GUILayout.BeginHorizontal();
                Button("c:" + a, L.T("bind.change"), pad ? Icon.Pad : Icon.Keyboard, 0, () =>
                {
                    captureFor = action;
                    captureUntil = Time.realtimeSinceStartup + 8f;
                    OpenDialog(Dlg.Capture, "d:capture");
                });
                if (a != ActPlaces && a != ActTeleport)
                {
                    GUILayout.Space(theme.Px(6));
                    Button("c:" + a + ":clear", L.T("bind.clear"), Icon.Close, 0, () => { host.SetBinding(action, ""); Say(L.T("bind.cleared", ActionName(action)), false); }, -1, !KeyCheck.Empty(host.Binding(a)));
                }
                GUILayout.EndHorizontal();
                foreach (var c in clashes)
                    if (c.Ours == a)
                    {
                        GUILayout.Space(theme.Px(4));
                        GUILayout.BeginHorizontal();
                        var ir = GUILayoutUtility.GetRect(theme.Px(16), theme.Px(16), GUILayout.Width(theme.Px(16)));
                        theme.DrawIcon(ir, Icon.Warn, Theme.Orange);
                        GUILayout.Space(theme.Px(6));
                        LayoutText(c.Game ? L.T("clash.game", KeyCheck.Words(c.With)) : L.T("clash.ours", ActionName(c.With)), theme.SmallWrap);
                        GUILayout.EndHorizontal();
                    }
                Gap();
            }
            LayoutText(L.T("controls.otherMods"), theme.SmallWrap);
            Gap();
            Button("set:resetControls", L.T("reset.controls"), Icon.Undo, 0, () =>
                Confirm(L.T("reset.controls"), L.T("reset.controlsBody"), L.T("reset.yes"), false, () => { int n = Lib.ResetPrefs(Prefs.Controls); Say(L.N("reset.done", n, L.T("sec.Controls")), false); }));
        }

        private void DrawAppearance()
        {
            var look = host.Look;
            LayoutText(L.T("look.intro"), theme.SmallWrap);
            Gap();
            int size = look.TextSize();
            SettingLabel(L.T("look.size"), L.T("look.sizeNote"));
            GUILayout.Space(theme.Px(6));
            Stepper("v:size", size + "%", () => LookChange("Appearance.TextSize", Math.Max(70, size - 10).ToString(), size.ToString()),
                () => LookChange("Appearance.TextSize", Math.Min(160, size + 10).ToString(), size.ToString()));
            Gap();
            SettingLabel(L.T("look.side"), null);
            GUILayout.Space(theme.Px(6));
            bool left = look.Left();
            Segmented("v:side", new[] { L.T("look.left"), L.T("look.right") }, left ? 0 : 1, k => LookChange("Appearance.Side", k == 0 ? "Left" : "Right", left ? "Left" : "Right"));
            Gap();
            SettingLabel(L.T("look.rows"), L.T("look.rowsNote"));
            GUILayout.Space(theme.Px(6));
            bool compact = look.Compact();
            Segmented("v:rows", new[] { L.T("look.comfortable"), L.T("look.compact") }, compact ? 1 : 0, k => LookChange("Appearance.Rows", k == 1 ? "Compact" : "Comfortable", compact ? "Compact" : "Comfortable"));
            Gap();
            int op = look.Opacity();
            SettingLabel(L.T("look.bg"), L.T("look.bgNote"));
            GUILayout.Space(theme.Px(6));
            Stepper("v:bg", op + "%", () => LookChange("Appearance.BackgroundOpacity", Math.Max(Theme.MinOpacity, op - 5).ToString(), op.ToString()),
                () => LookChange("Appearance.BackgroundOpacity", Math.Min(100, op + 5).ToString(), op.ToString()));
            Gap();
            bool rm = look.ReducedMotion();
            Toggle("v:motion", L.T("look.motion"), L.T("look.motionNote"), rm, () => LookChange("Appearance.ReducedMotion", rm ? "false" : "true", rm ? "true" : "false"));
            Button("set:resetLook", L.T("reset.look"), Icon.Undo, 0, () => { int n = Lib.ResetPrefs(Prefs.Appearance); Say(L.N("reset.done", n, L.T("sec.Appearance")), false); });
        }

        // an appearance change applies at once; the status line offers Undo (the "easy way back" when text gets too big)
        private void LookChange(string id, string value, string was)
        {
            if (value == was) return;
            host.Look.Set(id, value);
            Say(L.T("look.changed"), false, null, () => host.Look.Set(id, was));
        }

        private void DrawBehaviour()
        {
            var look = host.Look;
            bool quick = look.QuickTeleport();
            SettingLabel(L.T("beh.click"), L.T("beh.clickNote"));
            GUILayout.Space(theme.Px(6));
            Segmented("b:click", new[] { L.T("beh.select"), L.T("beh.quick") }, quick ? 1 : 0, k => look.Set("Behaviour.RowClick", k == 1 ? "Teleport" : "Select"));
            Gap();
            bool close = look.CloseAfter();
            Toggle("b:close", L.T("beh.close"), L.T("beh.closeNote"), close, () => look.Set("Behaviour.CloseAfterTeleport", close ? "false" : "true"));
            bool snd = look.Sounds();
            Toggle("b:sound", L.T("beh.sounds"), L.T("beh.soundsNote"), snd, () => { look.Set("Behaviour.Sounds", snd ? "false" : "true"); if (!snd) host.Sound(true); });
            Button("b:tip", L.T("beh.tip"), Icon.Help, 0, () => { host.ReplayFirstRun(); Hide("first-run tip"); });
            Gap();
            Button("set:resetBeh", L.T("reset.beh"), Icon.Undo, 0, () => { int n = Lib.ResetPrefs(Prefs.Behaviour); Say(L.N("reset.done", n, L.T("sec.Behaviour")), false); });
        }

        private void DrawData()
        {
            string planet = Title(planetName), save = SaveLabel;
            LayoutText(L.T("data.intro"), theme.SmallWrap);
            Heading(L.T("data.backupHead"));
            Toggle("d:withSettings", L.T("data.withSettings"), L.T("data.withSettingsNote"), exportSettings, () => exportSettings = !exportSettings);
            Button("d:export", L.T("data.export"), Icon.Save, 1, () =>
            {
                string failed = Lib.Export(host.Version, exportSettings, out var path);
                if (failed != null) { Say(failed, true); host.Sound(false); return; }
                Say(L.T("data.exported", Path.GetFileName(path)), false);
                host.Sound(true);
            });
            GUILayout.Space(theme.Px(6));
            Button("d:import", L.T("data.import"), Icon.Folder, 0, () => { importFiles = Lib.ImportCandidates(); OpenDialog(Dlg.ImportList, importFiles.Count > 0 ? "d:file:0" : "d:openfolder"); });
            GUILayout.Space(theme.Px(6));
            Button("d:restore", L.T("data.restore"), Icon.Undo, 0, () => { backupList = Lib.ListBackups(); OpenDialog(Dlg.Backups, backupList.Count > 0 ? "d:bk:0" : "d:cancel"); });
            GUILayout.Space(theme.Px(6));
            Button("d:folder", L.T("data.openFolder"), Icon.Folder, 3, () => OpenFolder(Lib.DataDir));
            Heading(L.T("data.linksHead"));
            LayoutText(L.T("data.linksNote"), theme.SmallWrap);
            GUILayout.Space(theme.Px(6));
            Button("d:copyformap", L.T("found.copyForMap"), Icon.Map, 0, CopyForMap);
            GUILayout.Space(theme.Px(6));
            Button("d:paste", L.T("data.pasteLink"), Icon.Clipboard, 0, () => { paste = ""; pasteWhy = L.T("paste.linkHow"); OpenDialog(Dlg.Paste, "d:pastefield"); });
            Heading(L.T("data.clearHead", planet, save));
            Bulk("d:clearRecent", Library.Bulk.Recent, L.T("data.clearRecent", save, planet), L.T("data.clearRecentBody", save, planet));
            Bulk("d:clearFound", Library.Bulk.Found, L.T("data.clearFound", save, planet), L.T("data.clearFoundBody", save, planet));
            Bulk("d:clearSaved", Library.Bulk.Saved, L.T("data.clearSaved", save, planet), L.T("data.clearSavedBody", save, planet));
        }

        private void Bulk(string id, Library.Bulk what, string label, string body)
        {
            int n = Lib.CountOf(what, world);
            Button(id, label, Icon.Trash, 0, () => Confirm(label, L.N("data.clearCount", n) + " " + body, L.T("data.clearYes"), true, () =>
            {
                string failed = Lib.Clear(what, world, out int cleared);
                if (failed != null) { Say(failed, true); host.Sound(false); return; }
                if (what == Library.Bulk.Saved || what == Library.Bulk.Recent) selPlace = null;
                Say(L.N("data.cleared", cleared), false);
            }), -1, n > 0, n == 0 ? L.T("data.nothing") : L.N("data.clearCount", n));
            GUILayout.Space(theme.Px(6));
        }

        private void OpenFolder(string dir)
        {
            try { Directory.CreateDirectory(dir); Application.OpenURL(new Uri(dir).AbsoluteUri); Say(L.T("data.folderOpened"), false); }
            catch (Exception error) { log.LogWarning("Folder did not open: " + error.GetType().Name); Say(L.T("data.folderFailed", dir), true); }
        }

        private void DrawHelp()
        {
            Heading(L.T("help.keysHead"));
            foreach (var a in Actions)
            {
                GUILayout.BeginHorizontal();
                LayoutText(ActionName(a), theme.Text, false, GUILayout.ExpandWidth(true));
                LayoutText(KeyCheck.Label(host.Binding(a), input.PlayStation), theme.TextStrong, true);
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(theme.Px(4));
            LayoutText(L.T("help.panelKeys"), theme.SmallWrap);
            Heading(L.T("help.howHead"));
            LayoutText(L.T("help.how", KeyCheck.Label(host.Binding(ActTeleport))), theme.Wrap);
            GUILayout.Space(theme.Px(6));
            LayoutText(L.T("help.startModded"), theme.Wrap);
            Heading(L.T("help.honestHead"));
            foreach (var k in new[] { "help.notPaused", "help.samePlanet", "help.tilde", "help.transfer", "help.found", "help.landing", "help.noNetwork" })
            {
                LayoutText("• " + L.T(k), theme.SmallWrap);
                GUILayout.Space(theme.Px(3));
            }
            Heading(L.T("help.aboutHead"));
            LayoutText(L.T("help.version", host.Version, "26411416 (v2.104)"), theme.SmallWrap);
            LayoutText(L.T("help.data", Lib.PlacesPath), theme.SmallWrap);
            LayoutText(L.T("help.backups", Lib.BackupsDir), theme.SmallWrap);
            GUILayout.Space(theme.Px(8));
            Button("h:diag", L.T("help.copyDiag"), Icon.Clipboard, 0, () =>
            {
                try { GUIUtility.systemCopyBuffer = host.Diagnostics(); Say(L.T("help.diagCopied"), false); }
                catch (Exception) { Say(L.T("clip.refused"), true); }
            });
            GUILayout.Space(theme.Px(6));
            Button("h:site", L.T("help.site"), Icon.Map, 0, () => { try { Application.OpenURL(MapUrl); Say(L.T("map.opened"), false); } catch (Exception) { Say(L.T("map.failed"), true); } });
            GUILayout.Space(theme.Px(6));
            LayoutText(L.T("help.credit"), theme.SmallWrap);
        }

        // ══ dialogs ══════════════════════════════════════════════════════════════════════════════════════════════════
        private void DrawDialog()
        {
            inScroll = true;
            if (Event.current.type == EventType.Repaint) nav.ClearPlaces();
            dlgScroll = GUILayout.BeginScrollView(dlgScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none, GUILayout.ExpandHeight(true));
            switch (dlg)
            {
                case Dlg.Paste: DrawPaste(); break;
                case Dlg.Rename: DrawRename(); break;
                case Dlg.Confirm: DrawConfirm(); break;
                case Dlg.ImportList: DrawImportList(); break;
                case Dlg.ImportPreview: DrawImportPreview(); break;
                case Dlg.Backups: DrawBackups(); break;
                case Dlg.Capture: DrawCapture(); break;
            }
            GUILayout.EndScrollView();
            inScroll = false;
            if (Event.current.type == EventType.Repaint) setViewHeight = GUILayoutUtility.GetLastRect().height;
        }

        private void DialogTitle(string text, string body)
        {
            LayoutText(text, theme.Title, true);
            if (!string.IsNullOrEmpty(body)) { GUILayout.Space(theme.Px(6)); LayoutText(body, theme.Wrap); }
            GUILayout.Space(theme.Px(12));
        }

        private void TypingNote()
        {
            if (!input.UsingPad) return;
            GUILayout.Space(theme.Px(6));
            LayoutText(L.T("type.needKeyboard"), theme.SmallWrap);
        }

        private void DrawPaste()
        {
            DialogTitle(L.T("paste.title"), L.T("paste.body", KeyCheck.Label(host.Binding(ActTeleport))));
            paste = Field("d:pastefield", paste, 60000, "/tp 785 31 795");
            if (!string.IsNullOrEmpty(pasteWhy)) { GUILayout.Space(theme.Px(6)); LayoutText(pasteWhy, theme.StatusBad); }
            TypingNote();
            GUILayout.Space(theme.Px(10));
            GUILayout.BeginHorizontal();
            Button("d:go", L.T("paste.go"), Icon.Go, 1, SubmitPaste);
            GUILayout.Space(theme.Px(6));
            Button("d:cancel", L.T("dlg.cancel"), null, 0, CloseDialog);
            GUILayout.EndHorizontal();
        }

        private void DrawRename()
        {
            DialogTitle(L.T("rename.title"), null);
            rename = Field("d:renamefield", rename, PlaceStore.NameMax, L.T("rename.placeholder"));
            TypingNote();
            GUILayout.Space(theme.Px(10));
            GUILayout.BeginHorizontal();
            Button("d:ok", L.T("rename.save"), Icon.Check, 1, DoRename);
            GUILayout.Space(theme.Px(6));
            Button("d:cancel", L.T("dlg.cancel"), null, 0, CloseDialog);
            GUILayout.EndHorizontal();
        }

        private void DrawConfirm()
        {
            DialogTitle(confirmTitle, confirmBody);
            LayoutText(L.T("dlg.backupFirst"), theme.SmallWrap);
            GUILayout.Space(theme.Px(10));
            GUILayout.BeginHorizontal();
            Button("d:yes", confirmYes, confirmDanger ? Icon.Trash : Icon.Check, confirmDanger ? 2 : 1, () => { var a = confirmAct; CloseDialog(); a?.Invoke(); });
            GUILayout.Space(theme.Px(6));
            Button("d:no", L.T("dlg.cancel"), null, 0, CloseDialog);
            GUILayout.EndHorizontal();
        }

        private void DrawCapture()
        {
            bool pad = captureFor == ActPadPlaces || captureFor == ActPadReturn;
            DialogTitle(L.T("bind.title", ActionName(captureFor)), L.T(pad ? "bind.pressButton" : "bind.pressKey"));
            float left = Mathf.Max(0, captureUntil - Time.realtimeSinceStartup);
            LayoutText(L.T("bind.waiting", Mathf.CeilToInt(left)), theme.SmallWrap);
            Reg("d:capture", null);
        }

        private void DrawImportList()
        {
            DialogTitle(L.T("imp.title"), L.T("imp.body", Lib.ExportsDir));
            if (importFiles.Count == 0) LayoutText(L.T("imp.none"), theme.SmallWrap);
            for (int i = 0; i < importFiles.Count && i < 30; i++)
            {
                string path = importFiles[i];
                string when = "";
                try { when = File.GetLastWriteTime(path).ToString("d MMM yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture); } catch (Exception) { }
                Button("d:file:" + i, Path.GetFileName(path), Icon.Folder, 0, () => PickImport(path), -1, true, when);
                GUILayout.Space(theme.Px(4));
            }
            GUILayout.Space(theme.Px(8));
            GUILayout.BeginHorizontal();
            Button("d:openfolder", L.T("data.openFolder"), Icon.Folder, 0, () => OpenFolder(Lib.ExportsDir));
            GUILayout.Space(theme.Px(6));
            Button("d:cancel", L.T("dlg.cancel"), null, 0, CloseDialog);
            GUILayout.EndHorizontal();
        }

        private void PickImport(string path)
        {
            try { importFile = Lib.ReadImport(path); }
            catch (FormatException error) { Say(error.Message, true); host.Sound(false); return; }
            importPath = path;
            srcSaves = importFile.Saves;
            srcAt = 0;
            // where it goes: the save you're in first (named as such), then every other save we know of; never chosen silently
            dstSaves = new List<string> { Store.Current };
            foreach (var s in Store.SaveNames) if (!dstSaves.Contains(s)) dstSaves.Add(s);
            foreach (var s in host.GameSaves()) if (!dstSaves.Contains(s)) dstSaves.Add(s);
            dstAt = 0;
            importSettings = false;
            RePlan();
            dlg = Dlg.ImportPreview;
            nav.Set("d:src", nav.Visible);
        }

        private void RePlan()
        {
            try { plan = Lib.Preview(importFile, srcSaves[srcAt], dstSaves[dstAt]); }
            catch (FormatException error) { plan = null; Say(error.Message, true); }
        }

        private string SaveName(string s) => s == Store.Current ? L.T("imp.playing", s.Length == 0 ? L.T("save.unknown") : s) : s;

        private void Cycle(string id, string label, List<string> items, int at, Action<int> set, bool dest)
        {
            LayoutText(label, theme.Small);
            GUILayout.Space(theme.Px(4));
            GUILayout.BeginHorizontal();
            Button(id + ":prev", "‹", null, 0, () => set((at + items.Count - 1) % items.Count), theme.Px(40), items.Count > 1);
            GUILayout.Space(theme.Px(6));
            var r = GUILayoutUtility.GetRect(10, theme.Px(38), GUILayout.ExpandWidth(true));
            Reg(id, null, () => set((at + items.Count - 1) % items.Count), () => set((at + 1) % items.Count));
            theme.Plate(r, Theme.Surface, theme.Px(8));
            string shown = dest ? SaveName(items[at]) : items[at];
            theme.TextStrong.alignment = TextAnchor.MiddleCenter;
            Text(r, shown, theme.TextStrong, true);
            theme.TextStrong.alignment = TextAnchor.UpperLeft;
            Ring(id, r, theme.Px(8));
            GUILayout.Space(theme.Px(6));
            Button(id + ":next", "›", null, 0, () => set((at + 1) % items.Count), theme.Px(40), items.Count > 1);
            GUILayout.EndHorizontal();
            GUILayout.Space(theme.Px(8));
        }

        private void DrawImportPreview()
        {
            DialogTitle(L.T("prev.title"), L.T("prev.from", Path.GetFileName(importPath), importFile.Made.Length >= 10 ? importFile.Made.Substring(0, 10) : importFile.Made, importFile.Mod));
            Cycle("d:src", L.T("prev.source"), srcSaves, srcAt, k => { srcAt = k; RePlan(); }, false);
            Cycle("d:dst", L.T("prev.dest"), dstSaves, dstAt, k => { dstAt = k; RePlan(); }, true);
            if (plan == null) return;
            var worlds = new List<string>();
            foreach (var w in plan.SourceWorlds) worlds.Add(Title(w));
            LayoutText(L.T("prev.worlds", worlds.Count == 0 ? "—" : string.Join(", ", worlds.ToArray())), theme.SmallWrap);
            GUILayout.Space(theme.Px(6));
            Line(Icon.Save, L.N("prev.add", plan.Additions.Count), Theme.Good, Names(plan.Additions));
            Line(Icon.Check, L.N("prev.dup", plan.Duplicates.Count), Theme.Muted, null);
            if (plan.Conflicts.Count > 0)
            {
                var names = new List<string>();
                foreach (var c in plan.Conflicts) names.Add(c.Name);
                Line(Icon.Warn, L.N("prev.conflict", plan.Conflicts.Count), Theme.Orange, string.Join(", ", names.GetRange(0, Math.Min(6, names.Count)).ToArray()) + (names.Count > 6 ? "…" : ""));
            }
            if (plan.NoRoom.Count > 0) Line(Icon.Warn, L.N("prev.noRoom", plan.NoRoom.Count), Theme.Bad, null);
            Line(Icon.Found, L.N("prev.found", plan.NewFound), Theme.Good, null);
            if (plan.NewFavs > 0) Line(Icon.StarFilled, L.N("prev.favs", plan.NewFavs), Theme.Orange, null);
            if (plan.NewNotes > 0) Line(Icon.Pencil, L.N("prev.notes", plan.NewNotes), Theme.Note, null);
            if (importFile.Settings != null)
            {
                GUILayout.Space(theme.Px(6));
                Toggle("d:impSettings", L.T("prev.settings"), L.T("prev.settingsNote"), importSettings, () => importSettings = !importSettings);
            }
            GUILayout.Space(theme.Px(10));
            string dest = SaveName(dstSaves[dstAt]);
            Button("d:merge", L.T("prev.merge", dest), Icon.Check, 1, () =>
            {
                string failed = Lib.Import(plan, false, importFile, importSettings, out int changed);
                CloseDialog();
                if (failed != null) { Say(failed, true); host.Sound(false); return; }
                Say(L.N("prev.merged", plan.Additions.Count, dest) + (changed > 0 ? " " + L.N("prev.settingsTaken", changed) : ""), false);
                host.Sound(true);
            });
            GUILayout.Space(theme.Px(6));
            Button("d:replace", L.T("prev.replace", dest), Icon.Warn, 0, () =>
            {
                var p = plan; var f = importFile; bool st = importSettings;
                Confirm(L.T("prev.replaceTitle", dest), L.T("prev.replaceBody", dest, p.SourceSave, p.TheirPlaces, p.TheirFound), L.T("prev.replaceYes"), true, () =>
                {
                    string failed = Lib.Import(p, true, f, st, out _);
                    if (failed != null) { Say(failed, true); host.Sound(false); return; }
                    selPlace = null;
                    Say(L.T("prev.replaced", dest), false);
                    host.Sound(true);
                });
            });
            GUILayout.Space(theme.Px(6));
            Button("d:cancel", L.T("dlg.cancel"), null, 0, CloseDialog);
        }

        private static string Names(List<KeyValuePair<string, Place>> list)
        {
            if (list.Count == 0) return null;
            var names = new List<string>();
            for (int i = 0; i < list.Count && i < 6; i++) names.Add(list[i].Value.Name);
            return string.Join(", ", names.ToArray()) + (list.Count > 6 ? "…" : "");
        }

        private void Line(Icon icon, string text, Color tint, string detail)
        {
            GUILayout.BeginHorizontal();
            var r = GUILayoutUtility.GetRect(theme.Px(16), theme.Px(20), GUILayout.Width(theme.Px(16)));
            theme.DrawIcon(new Rect(r.x, r.y + theme.Px(2), theme.Px(16), theme.Px(16)), icon, tint);
            GUILayout.Space(theme.Px(8));
            GUILayout.BeginVertical();
            LayoutText(text, theme.Text);
            if (!string.IsNullOrEmpty(detail)) LayoutText(detail, theme.SmallWrap);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.Space(theme.Px(4));
        }

        private void DrawBackups()
        {
            DialogTitle(L.T("bk.title"), L.T("bk.body", Backups.Keep));
            if (backupList.Count == 0) LayoutText(L.T("bk.none"), theme.SmallWrap);
            for (int i = 0; i < backupList.Count; i++)
            {
                var b = backupList[i];
                string when = b.When.ToString("d MMM yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
                Button("d:bk:" + i, when, Icon.Clock, 0, () => Confirm(L.T("bk.confirm", when), L.T("bk.confirmBody", Reason(b.Reason)), L.T("bk.yes"), false, () =>
                {
                    string failed = Lib.Restore(b, out var summary);
                    if (failed != null) { Say(failed, true); host.Sound(false); return; }
                    selPlace = null; selPoi = null;
                    Say(L.T("bk.done", when, summary), false);
                    host.Sound(true);
                }), -1, true, Reason(b.Reason));
                GUILayout.Space(theme.Px(4));
            }
            GUILayout.Space(theme.Px(8));
            Button("d:cancel", L.T("dlg.cancel"), null, 0, CloseDialog);
        }

        private static string Reason(string slug)
        {
            string id = "reason." + slug;
            return L.Has(id) ? L.T(id) : slug.Replace('-', ' ');
        }
    }
}

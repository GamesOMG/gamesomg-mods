// GamesOMG Teleport 0.2.0: your places per planet, kept in the mod's own JSON beside its config. No Unity here, so the
// WSL tests drive it exactly as the panel does. Planets are keyed by the map's world id: the game's PlanetData.id
// lower-cased ("Prime" -> "prime"), the same key the map's save reader (pc-save.js) and its spot-set links use.
// 0.2.6 (ChatGPT's release review, 2026-11-19): kept per SAVE, then per planet. The key is the game's own name for the
// save (SavedDataHandler.GetCurrentSaveFileName, the file name its load list shows). Nothing in a save both survives a
// rename and tells a copy from its original (a copy keeps the world seed), so the seed is kept beside it only to follow
// a save renamed outside the game. A 0.2.4/0.2.5 file (one store for every save) goes to the first save loaded, once.
using System;
using System.Collections.Generic;

namespace GamesOMG.PlanetCrafterTeleport
{
    internal sealed class Place
    {
        public string Name = "", Colour = "", Group = "", Pin = "";
        public float X, Y, Z;
        public bool AutoHeight;   // no height known (a map spot): the mod finds the ground from above, as /tp x ~ z
        public bool Fav;          // 0.3.0: a favourite, listed first in My places
        public string Note = "";  // 0.3.2: the player's own short note on it ("" = none)

        public TeleportRequest Request => new TeleportRequest(X, Y, Z, AutoHeight);
        public float Distance2D(float x, float z) => (float)Math.Sqrt((X - x) * (X - x) + (Z - z) * (Z - z));
    }

    internal sealed class PlanetPlaces
    {
        public readonly List<Place> Saved = new List<Place>();
        public readonly List<Place> Recent = new List<Place>();
        public readonly HashSet<string> Visited = new HashSet<string>(StringComparer.Ordinal);   // POI pin ids (0.2.x Places)
        // 0.3.2 (player feedback 2026-11-21: "a little + ... that allows adding a small note ... a certain one I already tried needs me to
        // come back later"): the player's note on a map place, by pin id, beside its found mark. A note never moves a place
        // to Found and marking it found keeps the note; the two are independent.
        public readonly Dictionary<string, string> Notes = new Dictionary<string, string>(StringComparer.Ordinal);

        public bool Empty => Saved.Count == 0 && Recent.Count == 0 && Visited.Count == 0 && Notes.Count == 0;

        // every note on this planet: the map places' and your own places'
        public int NoteCount
        {
            get { int n = Notes.Count; foreach (var p in Saved) if (p.Note.Length > 0) n++; return n; }
        }
    }

    internal sealed class ImportResult
    {
        public int Added, Skipped;
        public readonly Dictionary<string, int> AddedByWorld = new Dictionary<string, int>(StringComparer.Ordinal);
        // 0.2.6: what was skipped, by name: already on that planet's list, or no room left on it
        public readonly List<string> SkippedNames = new List<string>(), NoRoomNames = new List<string>();
    }

    // 0.2.6 Back (2026-11-19): where you stood before the last teleport, on one planet
    internal sealed class BackSpot
    {
        public string World = "", Name = "";   // 0.3.0: Name = what stood there ("Iridium base"), "" when nothing did
        public float X, Y, Z;

        public TeleportRequest Request => new TeleportRequest(X, Y, Z, false);
    }

    // 0.3.0 Delete with Undo: what one delete took out, and where it sat, so Undo puts it back exactly there
    internal sealed class Removed
    {
        public string World = "";
        public Place Place;
        public bool Recent;
        public int Index;
    }

    internal sealed class SaveData
    {
        public uint Seed;
        public readonly Dictionary<string, PlanetPlaces> Planets = new Dictionary<string, PlanetPlaces>(StringComparer.Ordinal);
        public BackSpot Back;

        public bool Empty
        {
            get
            {
                if (Back != null) return false;
                foreach (var p in Planets.Values) if (!p.Empty) return false;
                return true;
            }
        }
    }

    internal sealed class PlaceStore
    {
        public const int RecentMax = 5, SavedMax = 500, NameMax = 60, NamesShown = 5, NoteMax = 80;
        // 0.3.0: the file's schema. 1 = 0.2.4/0.2.5 (one list per planet), 2 = 0.2.6 (per save), 3 = 0.3.0 (favourites, a
        // named Return spot), 4 = 0.3.2 (notes). A file older than this is backed up before the first write over it (Library).
        public const int FileVersion = 4;

        // the "v" the file said when it was read (0 = no file read)
        public int ReadVersion { get; private set; }
        private readonly Dictionary<string, SaveData> saves = new Dictionary<string, SaveData>(StringComparer.Ordinal);
        private Dictionary<string, PlanetPlaces> legacy;   // a 0.2.4/0.2.5 file's planets, till the first save is loaded

        public static string WorldOf(string planetId) => (planetId ?? "").Trim().ToLowerInvariant();

        // the save in play: "" until the mod has seen one (a store nobody bound, as 0.2.5 and the older tests used it)
        public string Current { get; private set; } = "";

        private SaveData Data
        {
            get
            {
                if (!saves.TryGetValue(Current, out var d)) saves[Current] = d = new SaveData();
                return d;
            }
        }

        // The save in play, each time the player is in a world. Returns a line for the screen, or null: the old file moved
        // onto this save (once), or this save's places followed it from a name it had before (renamed outside the game:
        // the same world seed, and saveExists says the old name's file is gone). A copy keeps the seed AND its original
        // stays, so a copy starts clean.
        // 0.3.0 (Codex C61 F5): the game gives a deleted world's file name to the next new world (SaveFilesCreateNew.
        // SetNameOfNewSave takes the first free "<mode>-<n>"; CreateNewFile rolls a new seed). So the same name with a
        // different seed is a different world: the old world's places, found marks and Return move aside under their own
        // name (kept, and exportable from Settings > Data), and this world starts clean. A record with no seed yet (written
        // before seeds were kept) takes this one. Guests in co-op are keyed by the host world's seed alone.
        public string Use(string save, uint seed, Func<string, bool> saveExists)
        {
            save = (save ?? "").Trim();
            string said = null;
            if (seed != 0 && saves.TryGetValue(save, out var had) && had.Seed != 0 && had.Seed != seed)
            {
                string aside = L.T("store.asideName", save, had.Seed);
                for (int n = 2; saves.ContainsKey(aside); n++) aside = L.T("store.asideName", save, had.Seed) + " " + n;
                saves[aside] = had;
                saves.Remove(save);
                said = L.T("store.newWorld", save, aside);
            }
            if (!saves.ContainsKey(save) && seed != 0 && saveExists != null)
            {
                string from = null;
                int matches = 0;
                foreach (var kv in saves)
                    if (kv.Key.Length > 0 && kv.Key != save && kv.Value.Seed == seed && !saveExists(kv.Key)) { from = kv.Key; matches++; }
                if (matches == 1)
                {
                    saves[save] = saves[from];
                    saves.Remove(from);
                    said = (said != null ? said + " " : "") + L.T("store.followedRename", from);
                }
            }
            Current = save;
            var data = Data;
            if (seed != 0) data.Seed = seed;
            if (legacy != null)
            {
                foreach (var kv in legacy) MergeInto(data, kv.Key, kv.Value);
                legacy = null;
                said = L.T("store.legacyMoved", save.Length > 0 ? save : L.T("store.thisGame"));
            }
            return said;
        }

        private static void MergeInto(SaveData data, string world, PlanetPlaces from)
        {
            if (!data.Planets.TryGetValue(world, out var into)) { data.Planets[world] = from; return; }
            foreach (var p in from.Saved) if (!into.Saved.Exists(q => SameName(q, p.Name)) && into.Saved.Count < SavedMax) into.Saved.Add(p);
            into.Visited.UnionWith(from.Visited);
            foreach (var kv in from.Notes) if (!into.Notes.ContainsKey(kv.Key)) into.Notes[kv.Key] = kv.Value;
        }

        public PlanetPlaces For(string world)
        {
            world = WorldOf(world);
            var planets = Data.Planets;
            if (!planets.TryGetValue(world, out var p)) planets[world] = p = new PlanetPlaces();
            return p;
        }

        public IEnumerable<string> Worlds => Data.Planets.Keys;

        // ── Back: one spot per save. Every successful teleport records where you stood, Back's own too, so a second Back
        // returns: one step that swaps, not a history. A spot on another planet is cleared, never offered. ──
        public void Teleported(string world, float x, float y, float z, string name = null) =>
            Data.Back = new BackSpot { World = WorldOf(world), X = (float)Math.Round(x, 2), Y = (float)Math.Round(y, 2), Z = (float)Math.Round(z, 2),
                Name = SpotSet.Clean(name ?? "", NameMax) };

        public BackSpot Back(string world)
        {
            var data = Data;
            if (data.Back != null && data.Back.World != WorldOf(world)) data.Back = null;
            return data.Back;
        }

        private static bool SameName(Place p, string name) => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase);

        // "Save here": a name the list doesn't have yet (a repeat gets " 2", " 3"…), the standing height as a real Y
        public Place Save(string world, string name, float x, float y, float z)
        {
            var list = For(world).Saved;
            if (list.Count >= SavedMax) return null;
            name = SpotSet.Clean(name, NameMax);
            if (name.Length == 0) name = L.T("place.default", list.Count + 1);
            string unique = name;
            for (int n = 2; list.Exists(p => SameName(p, unique)); n++) unique = name + " " + n;
            var place = new Place { Name = unique, X = Round(x), Y = Round(y), Z = Round(z) };
            list.Add(place);
            return place;
        }

        public bool Remove(string world, Place place) => Delete(world, place) != null;

        // ── 0.3.0: one place out, with Undo; renames; favourites; bulk clears that each name what they cover ──
        public Removed Delete(string world, Place place)
        {
            var here = For(world);
            int i = here.Saved.IndexOf(place);
            if (i >= 0) { here.Saved.RemoveAt(i); return new Removed { World = WorldOf(world), Place = place, Index = i }; }
            i = here.Recent.IndexOf(place);
            if (i >= 0) { here.Recent.RemoveAt(i); return new Removed { World = WorldOf(world), Place = place, Index = i, Recent = true }; }
            return null;
        }

        // puts a deleted place back where it was; false when its name is taken again meanwhile (nothing changes then)
        public bool Undo(Removed removed)
        {
            if (removed == null || removed.Place == null) return false;
            var list = removed.Recent ? For(removed.World).Recent : For(removed.World).Saved;
            if (list.Contains(removed.Place)) return true;
            if (!removed.Recent && (list.Exists(p => SameName(p, removed.Place.Name)) || list.Count >= SavedMax)) return false;
            list.Insert(Math.Min(Math.Max(removed.Index, 0), list.Count), removed.Place);
            return true;
        }

        // the name it got (a repeat gets " 2"), or null when the new name is empty (nothing changes)
        public string Rename(string world, Place place, string name)
        {
            name = SpotSet.Clean(name, NameMax);
            if (name.Length == 0 || place == null) return null;
            var list = For(world).Saved;
            string unique = name;
            for (int n = 2; list.Exists(p => p != place && SameName(p, unique)); n++) unique = name + " " + n;
            place.Name = unique;
            return unique;
        }

        // 0.3.2 notes: a map place's by its pin id, your own place's on the place itself. Saving "" (after trimming) deletes
        // it. Returns the note as kept ("" = none now).
        public string SetNote(string world, string pin, string note)
        {
            note = SpotSet.Clean(note, NoteMax);
            if (string.IsNullOrEmpty(pin)) return "";
            var notes = For(world).Notes;
            if (note.Length == 0) notes.Remove(pin); else notes[pin] = note;
            return note;
        }

        public static string SetNote(Place place, string note)
        {
            if (place == null) return "";
            return place.Note = SpotSet.Clean(note, NoteMax);
        }

        public string NoteOf(string world, string pin) =>
            !string.IsNullOrEmpty(pin) && For(world).Notes.TryGetValue(pin, out var n) ? n : "";

        public int ClearFound(string world) { var v = For(world).Visited; int n = v.Count; v.Clear(); return n; }
        public int ClearRecent(string world) { var r = For(world).Recent; int n = r.Count; r.Clear(); return n; }
        public int ClearSaved(string world) { var s = For(world).Saved; int n = s.Count; s.Clear(); return n; }

        // Save here with no name typed (0.3.0, "typing optional"): the nearest map place within reach names it ("Near Golden
        // crate"), else the planet and where you stand ("Prime 785, 795"); a repeat gets " 2" from Save
        public static string AutoName(string world, float x, float z, Func<float, float, string> nearestPoi)
        {
            string near = nearestPoi?.Invoke(x, z);
            if (!string.IsNullOrEmpty(near)) return L.T("place.autoNear", near);
            string w = WorldOf(world);
            return (w.Length > 0 ? char.ToUpperInvariant(w[0]) + w.Substring(1) + " " : "") + (int)Math.Round(x) + ", " + (int)Math.Round(z);
        }

        // ── "Return to …": the name of where you stand when you teleport away ──
        // (in game on 0.3.3, 2026-11-24: Floating Rocks, then Offline drone, and Return said "Return to Osmium"): the place
        // you teleported to names it while you're still within 30 m of where it landed you, even when a vein sits closer.
        // Else one of your places within 30 m, else a map place within 30 m (mapNear), else "". The landing is kept in
        // memory only, per save and planet: a restart falls back to the nearest-name rule.
        public const float SpotNameReach = 30f;
        private string arrivedSave, arrivedWorld, arrivedName;
        private float arrivedX, arrivedZ;

        // each completed move, Return's too; a move with no name (a pasted /tp line) forgets the last one
        public void Arrived(string world, string name, float x, float z)
        {
            name = SpotSet.Clean(name ?? "", NameMax);
            if (name.Length == 0) { arrivedName = null; return; }
            arrivedSave = Current; arrivedWorld = WorldOf(world); arrivedName = name; arrivedX = x; arrivedZ = z;
        }

        public string NameForSpot(string world, float x, float z, Func<float, float, string> mapNear)
        {
            if (arrivedName != null && arrivedSave == Current && arrivedWorld == WorldOf(world)
                && Math.Sqrt((arrivedX - x) * (arrivedX - x) + (arrivedZ - z) * (arrivedZ - z)) <= SpotNameReach)
                return arrivedName;
            Place best = null;
            float bestD = SpotNameReach;
            foreach (var p in For(world).Saved) { float d = p.Distance2D(x, z); if (d <= bestD) { best = p; bestD = d; } }
            if (best != null) return best.Name;
            return mapNear?.Invoke(x, z) ?? "";
        }

        // ── 0.3.0 export/import: whole saves, by the game's own save name ──
        public List<string> SaveNames
        {
            get
            {
                var names = new List<string>();
                foreach (var kv in saves) if (!kv.Value.Empty || kv.Key == Current) names.Add(kv.Key);
                return names;
            }
        }
        public SaveData SaveOf(string save) => saves.TryGetValue(save ?? "", out var d) ? d : null;
        public void PutSave(string save, SaveData data) { saves[save ?? ""] = data ?? new SaveData(); }
        public bool HasLegacy => legacy != null;

        // the last few teleports, newest first; one within 3 m of another, or of the same name, replaces it
        public void AddRecent(string world, string name, float x, float y, float z)
        {
            var list = For(world).Recent;
            list.RemoveAll(p => SameName(p, name) || p.Distance2D(x, z) < 3f && Math.Abs(p.Y - y) < 3f);
            list.Insert(0, new Place { Name = SpotSet.Clean(name, NameMax), X = Round(x), Y = Round(y), Z = Round(z) });
            if (list.Count > RecentMax) list.RemoveRange(RecentMax, list.Count - RecentMax);
        }

        // a map spot-set: each spot to its own world's list, by name; a name that world already has is skipped. landingOf
        // gives a pin's checked landing (x, y, z), or null: a spot saved on a pin lands there, beside the POI, not on it.
        // Everything goes into the save in play.
        public ImportResult Import(IEnumerable<MapSpot> spots, string fallbackWorld, Func<string, string, float[]> landingOf = null)
        {
            var result = new ImportResult();
            foreach (var s in spots)
            {
                string world = WorldOf(string.IsNullOrEmpty(s.World) ? fallbackWorld : s.World);
                var list = For(world).Saved;
                if (list.Exists(p => SameName(p, s.Name))) { result.Skipped++; result.SkippedNames.Add(s.Name); continue; }
                if (list.Count >= SavedMax) { result.Skipped++; result.NoRoomNames.Add(s.Name); continue; }
                var landing = !string.IsNullOrEmpty(s.Pin) && landingOf != null ? landingOf(world, s.Pin) : null;
                list.Add(landing != null
                    ? new Place { Name = s.Name, X = landing[0], Y = landing[1], Z = landing[2], Colour = s.Colour, Group = s.Group, Pin = s.Pin }
                    : new Place { Name = s.Name, X = Round(s.X), Z = Round(s.Z), AutoHeight = true, Colour = s.Colour, Group = s.Group, Pin = s.Pin });
                result.Added++;
                result.AddedByWorld.TryGetValue(world, out int n);
                result.AddedByWorld[world] = n + 1;
            }
            return result;
        }

        // the line an import shows, naming what it skipped (0.2.6; 0.2.5 said only "N already here")
        public static string Describe(ImportResult result, Func<string, string> title)
        {
            var parts = new List<string>();
            foreach (var kv in result.AddedByWorld) parts.Add(title(kv.Key) + " " + kv.Value);
            string said = result.Added == 0
                ? (result.NoRoomNames.Count == 0 ? L.T("import.nothingNew") : L.T("import.nothingAdded"))
                : L.N("import.added", result.Added, string.Join(", ", parts.ToArray()));
            if (result.Added > 0 && result.SkippedNames.Count > 0) said += " " + L.T("import.alreadyHere", Names(result.SkippedNames));
            if (result.NoRoomNames.Count > 0) said += " " + L.T("import.noRoom", Names(result.NoRoomNames));
            return said;
        }

        private static string Names(List<string> names)
        {
            var shown = names.Count <= NamesShown ? names : names.GetRange(0, NamesShown - 1);
            string list = string.Join(", ", shown.ToArray());
            return names.Count > shown.Count ? L.T("names.andMore", list, names.Count - shown.Count) : list;
        }

        private static float Round(float v) => (float)Math.Round(v, 1);

        // ── the file (0.2.6): {"v":2,"saves":{"<save>":{"seed":n,"planets":{"prime":{"saved":[…],"recent":[…],
        // "visited":[…]}},"back":{"w":"prime","x":…,"y":…,"z":…}}}}, plus "legacy":{planets} while a 0.2.5 file waits.
        // 0.2.4/0.2.5 wrote {"v":1,"planets":{…}}. 0.3.2 (v4): a planet's "notes":{"<pin>":"…"} and a place row's "note". ──
        public string ToJson()
        {
            var root = new Dictionary<string, object>(StringComparer.Ordinal) { ["v"] = FileVersion };
            var all = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var kv in saves)
            {
                if (kv.Value.Empty) continue;
                var o = new Dictionary<string, object>(StringComparer.Ordinal) { ["planets"] = PlanetsJson(kv.Value.Planets) };
                if (kv.Value.Seed != 0) o["seed"] = (double)kv.Value.Seed;
                var b = kv.Value.Back;
                if (b != null)
                {
                    var bo = new Dictionary<string, object>(StringComparer.Ordinal) { ["w"] = b.World, ["x"] = b.X, ["y"] = b.Y, ["z"] = b.Z };
                    if (b.Name.Length > 0) bo["n"] = b.Name;
                    o["back"] = bo;
                }
                all[kv.Key] = o;
            }
            root["saves"] = all;
            if (legacy != null) root["legacy"] = PlanetsJson(legacy);
            return MiniJson.Write(root);
        }

        private static Dictionary<string, object> PlanetsJson(Dictionary<string, PlanetPlaces> planets)
        {
            var all = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var kv in planets)
            {
                if (kv.Value.Empty) continue;
                var visited = new List<object>();
                foreach (var id in kv.Value.Visited) visited.Add(id);
                visited.Sort((a, b) => string.CompareOrdinal((string)a, (string)b));
                var planet = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["saved"] = Rows(kv.Value.Saved),
                    ["recent"] = Rows(kv.Value.Recent),
                    ["visited"] = visited,
                };
                // 0.3.2: {"<pin>": "<note>"}, written only when there is one
                if (kv.Value.Notes.Count > 0)
                {
                    var notes = new Dictionary<string, object>(StringComparer.Ordinal);
                    foreach (var n in kv.Value.Notes) notes[n.Key] = n.Value;
                    planet["notes"] = notes;
                }
                all[kv.Key] = planet;
            }
            return all;
        }

        private static List<object> Rows(List<Place> list)
        {
            var rows = new List<object>();
            foreach (var p in list)
            {
                var o = new Dictionary<string, object>(StringComparer.Ordinal) { ["n"] = p.Name, ["x"] = p.X, ["z"] = p.Z };
                if (p.AutoHeight) o["auto"] = true; else o["y"] = p.Y;
                if (p.Colour.Length > 0) o["c"] = p.Colour;
                if (p.Group.Length > 0) o["g"] = p.Group;
                if (p.Pin.Length > 0) o["pin"] = p.Pin;
                if (p.Fav) o["fav"] = true;
                if (p.Note.Length > 0) o["note"] = p.Note;
                rows.Add(o);
            }
            return rows;
        }

        // ── reading (0.3.0, Codex C61 F4): only shapes this version wrote are read. A file that is not one (not an object, a
        // "v" that is not a whole number, a known "v" without its "saves") throws, and PlaceFile keeps it aside. A newer "v"
        // reads what it can and PlaceFile writes nothing over it. Anything inside that can't be read (a row with no usable
        // spot, a list that isn't one, more rows than a list holds) is counted in Unreadable, never dropped quietly:
        // PlaceFile keeps the whole file aside and says how many before the first write could lose them. ──
        public const double CoordMax = 1000000;   // the same bound /tp's parser keeps (TeleportRequest)

        // entries the last FromJson could not read (0 = it read everything)
        public int Unreadable { get; private set; }

        public static PlaceStore FromJson(string json)
        {
            var store = new PlaceStore();
            if (string.IsNullOrWhiteSpace(json)) return store;
            var root = MiniJson.O(MiniJson.Parse(json));
            if (root == null) throw new FormatException("the places file is not a JSON object");
            object vRaw = MiniJson.Get(root, "v");
            double v = vRaw == null ? 1 : MiniJson.N(vRaw);
            if (double.IsNaN(v) || v < 1 || v != Math.Floor(v) || v > int.MaxValue) throw new FormatException("the places file's version is not a whole number");
            store.ReadVersion = (int)v;
            object savesRaw = MiniJson.Get(root, "saves");
            var saves = MiniJson.O(savesRaw);
            if (saves == null && store.ReadVersion >= 2 && store.ReadVersion <= FileVersion)
                throw new FormatException("a version " + store.ReadVersion + " places file without its saves");
            if (saves != null)
            {
                foreach (var kv in saves)
                {
                    var o = MiniJson.O(kv.Value);
                    if (o == null) { store.Unreadable++; continue; }
                    var data = new SaveData();
                    store.ReadPlanets(MiniJson.Get(o, "planets"), data.Planets);
                    object seedRaw = MiniJson.Get(o, "seed");
                    double seed = MiniJson.N(seedRaw);
                    if (!double.IsNaN(seed) && seed > 0 && seed <= uint.MaxValue && seed == Math.Floor(seed)) data.Seed = (uint)seed;
                    else if (seedRaw != null) store.Unreadable++;
                    object backRaw = MiniJson.Get(o, "back");
                    if (backRaw != null)
                    {
                        var b = MiniJson.O(backRaw);
                        double bx = MiniJson.N(MiniJson.Get(b, "x")), by = MiniJson.N(MiniJson.Get(b, "y")), bz = MiniJson.N(MiniJson.Get(b, "z"));
                        if (b != null && Usable(bx) && Usable(by) && Usable(bz))
                            data.Back = new BackSpot { World = WorldOf(MiniJson.S(MiniJson.Get(b, "w"))), X = (float)bx, Y = (float)by, Z = (float)bz,
                                Name = SpotSet.Clean(MiniJson.S(MiniJson.Get(b, "n")), NameMax) };
                        else store.Unreadable++;
                    }
                    store.saves[kv.Key] = data;
                }
                object waiting = MiniJson.Get(root, "legacy");
                if (waiting != null) store.ReadPlanets(waiting, store.legacy = new Dictionary<string, PlanetPlaces>(StringComparer.Ordinal));
                return store;
            }
            if (savesRaw != null) store.Unreadable++;   // a "saves" that is not an object (a v1 file's, or a newer file's)
            // a 0.2.4/0.2.5 file: one list per planet for every save; it waits for the first save loaded (Use)
            var old = new Dictionary<string, PlanetPlaces>(StringComparer.Ordinal);
            store.ReadPlanets(MiniJson.Get(root, "planets"), old);
            foreach (var p in old.Values)
                if (!p.Empty) { store.legacy = old; break; }
            return store;
        }

        private static bool Usable(double v) => !double.IsNaN(v) && !double.IsInfinity(v) && Math.Abs(v) <= CoordMax;

        private void ReadPlanets(object raw, Dictionary<string, PlanetPlaces> into)
        {
            if (raw == null) return;
            var all = MiniJson.O(raw);
            if (all == null) { Unreadable++; return; }
            foreach (var kv in all)
            {
                var o = MiniJson.O(kv.Value);
                if (o == null) { Unreadable++; continue; }
                string world = WorldOf(kv.Key);
                if (!into.TryGetValue(world, out var p)) into[world] = p = new PlanetPlaces();
                Read(MiniJson.Get(o, "saved"), p.Saved, SavedMax);
                Read(MiniJson.Get(o, "recent"), p.Recent, RecentMax);
                object visitedRaw = MiniJson.Get(o, "visited");
                var visited = MiniJson.A(visitedRaw);
                if (visited != null) { foreach (var id in visited) if (id is string s && s.Length > 0) p.Visited.Add(s); else Unreadable++; }
                else if (visitedRaw != null) Unreadable++;
                object notesRaw = MiniJson.Get(o, "notes");
                var notes = MiniJson.O(notesRaw);
                if (notes != null)
                {
                    foreach (var n in notes)
                    {
                        string text = n.Value is string t ? SpotSet.Clean(t, NoteMax) : null;
                        if (n.Key.Length > 0 && !string.IsNullOrEmpty(text)) p.Notes[n.Key] = text; else Unreadable++;
                    }
                }
                else if (notesRaw != null) Unreadable++;
            }
        }

        private void Read(object raw, List<Place> into, int max)
        {
            if (raw == null) return;
            var rows = MiniJson.A(raw);
            if (rows == null) { Unreadable++; return; }
            foreach (var r in rows)
            {
                var o = MiniJson.O(r);
                if (o == null) { Unreadable++; continue; }
                double x = MiniJson.N(MiniJson.Get(o, "x")), z = MiniJson.N(MiniJson.Get(o, "z")), y = MiniJson.N(MiniJson.Get(o, "y"));
                bool auto = MiniJson.Get(o, "auto") is bool b && b;
                if (!Usable(x) || !Usable(z) || (!auto && !Usable(y)) || into.Count >= max) { Unreadable++; continue; }
                into.Add(new Place
                {
                    Name = SpotSet.Clean(MiniJson.S(MiniJson.Get(o, "n")), NameMax),
                    X = (float)x, Y = auto ? 0 : (float)y, Z = (float)z, AutoHeight = auto,
                    Colour = MiniJson.S(MiniJson.Get(o, "c")), Group = MiniJson.S(MiniJson.Get(o, "g")), Pin = MiniJson.S(MiniJson.Get(o, "pin")),
                    Fav = MiniJson.Get(o, "fav") is bool fav && fav,
                    Note = SpotSet.Clean(MiniJson.S(MiniJson.Get(o, "note")), NoteMax),
                });
            }
        }
    }
}

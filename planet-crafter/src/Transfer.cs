// GamesOMG Teleport 0.3.0: a whole export of the mod's places, and its import (ChatGPT's review 2026-11-19, the
// brief: "a full versioned export/import"). Three distinct promises, said in the panel too:
//   Save here                  remember where I am
//   Export / Import a file     move my places to another PC, install or save (this file)
//   Copy for the map / a link  share a few places, transfer supported found marks (map links: transfer, not sync)
// The file holds every save's places with their real heights, world ids, found POI ids, favourites, Recently travelled,
// the Return spot and its schema; settings only when asked. An import shows a preview first (source world/save, the
// destination save the player picks, additions, duplicates, conflicts), merges by default, replaces only when chosen, and
// a file that fails to read changes nothing. No Unity here.
using System;
using System.Collections.Generic;

namespace GamesOMG.PlanetCrafterTeleport
{
    internal sealed class ExportFile
    {
        public const string Format = "gamesomg-teleport-export", Game = "planet-crafter";
        public const int Schema = 1;
        public string Mod = "", Made = "";
        public PlaceStore Store;
        public Dictionary<string, string> Settings;   // "Section.Key" -> value, or null when the file has none

        public List<string> Saves => Store.SaveNames;
    }

    internal sealed class ImportConflict
    {
        public string World = "", Name = "";
        public Place Yours, Theirs;
    }

    internal sealed class ImportPlan
    {
        public string SourceSave = "", DestSave = "";
        public readonly List<string> SourceWorlds = new List<string>();
        public readonly List<KeyValuePair<string, Place>> Additions = new List<KeyValuePair<string, Place>>();   // world, place
        public readonly List<KeyValuePair<string, Place>> Duplicates = new List<KeyValuePair<string, Place>>();
        public readonly List<ImportConflict> Conflicts = new List<ImportConflict>();
        public readonly List<KeyValuePair<string, Place>> NoRoom = new List<KeyValuePair<string, Place>>();
        public int NewFound, NewFavs, NewNotes;   // NewNotes: 0.3.2, map-place notes yours lacks
        public int TheirPlaces, TheirFound;   // the whole source save, for Replace's line
        public bool DestHasData;
        internal SaveData Source;
    }

    internal static class Transfer
    {
        // positions closer than this (flat and up) are the same place; a same-name place further off is a conflict
        public const float SameSpot = 1f;

        public static string Export(PlaceStore store, string modVersion, DateTime madeUtc, IDictionary<string, string> settings)
        {
            var root = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["format"] = ExportFile.Format,
                ["schema"] = ExportFile.Schema,
                ["game"] = ExportFile.Game,
                ["mod"] = modVersion ?? "",
                ["made"] = madeUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
                ["store"] = MiniJson.Parse(store.ToJson()),
            };
            if (settings != null)
            {
                var o = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var kv in settings) o[kv.Key] = kv.Value;
                root["settings"] = o;
            }
            return MiniJson.Write(root);
        }

        // throws FormatException with a line for the screen; nothing anywhere has changed when it does
        public static ExportFile Read(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new FormatException(L.T("xfer.empty"));
            Dictionary<string, object> root;
            try { root = MiniJson.O(MiniJson.Parse(json)); }
            catch (FormatException) { throw new FormatException(L.T("xfer.damaged")); }
            if (root == null || MiniJson.S(MiniJson.Get(root, "format")) != ExportFile.Format) throw new FormatException(L.T("xfer.notOurs"));
            if (MiniJson.S(MiniJson.Get(root, "game")) != ExportFile.Game) throw new FormatException(L.T("xfer.otherGame"));
            double schema = MiniJson.N(MiniJson.Get(root, "schema"));
            if (double.IsNaN(schema) || schema < 1) throw new FormatException(L.T("xfer.damaged"));
            if (schema > ExportFile.Schema) throw new FormatException(L.T("xfer.newer"));
            var inner = MiniJson.O(MiniJson.Get(root, "store"));
            if (inner == null || MiniJson.O(MiniJson.Get(inner, "saves")) == null) throw new FormatException(L.T("xfer.damaged"));
            PlaceStore store;
            try { store = PlaceStore.FromJson(MiniJson.Write(inner)); }
            catch (Exception) { throw new FormatException(L.T("xfer.damaged")); }
            var file = new ExportFile { Mod = MiniJson.S(MiniJson.Get(root, "mod")), Made = MiniJson.S(MiniJson.Get(root, "made")), Store = store };
            var settings = MiniJson.O(MiniJson.Get(root, "settings"));
            if (settings != null)
            {
                file.Settings = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var kv in settings) if (kv.Value is string v) file.Settings[kv.Key] = v;
            }
            if (file.Saves.Count == 0) throw new FormatException(L.T("xfer.noSaves"));
            return file;
        }

        private static bool SameName(Place a, string name) => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase);

        private static bool SameSpotAs(Place a, Place b) =>
            a.AutoHeight == b.AutoHeight && Math.Abs(a.X - b.X) <= SameSpot && Math.Abs(a.Z - b.Z) <= SameSpot && (a.AutoHeight || Math.Abs(a.Y - b.Y) <= SameSpot);

        // what an import of one of the file's saves into one of yours would do. Changes nothing.
        public static ImportPlan Plan(ExportFile file, string sourceSave, PlaceStore into, string destSave)
        {
            var source = file.Store.SaveOf(sourceSave) ?? throw new FormatException(L.T("xfer.noSuchSave"));
            var plan = new ImportPlan { SourceSave = sourceSave ?? "", DestSave = destSave ?? "", Source = source };
            var dest = into.SaveOf(destSave);
            plan.DestHasData = dest != null && !dest.Empty;
            foreach (var kv in source.Planets)
            {
                if (kv.Value.Empty) continue;
                plan.SourceWorlds.Add(kv.Key);
                plan.TheirPlaces += kv.Value.Saved.Count;
                plan.TheirFound += kv.Value.Visited.Count;
                PlanetPlaces mine = null;
                dest?.Planets.TryGetValue(kv.Key, out mine);
                int room = PlaceStore.SavedMax - (mine?.Saved.Count ?? 0);
                foreach (var theirs in kv.Value.Saved)
                {
                    var yours = mine?.Saved.Find(p => SameName(p, theirs.Name));
                    if (yours == null && plan.Additions.Exists(a => a.Key == kv.Key && SameName(a.Value, theirs.Name))) yours = theirs;   // a repeat inside the file
                    if (yours == null)
                    {
                        if (room-- > 0) plan.Additions.Add(new KeyValuePair<string, Place>(kv.Key, theirs));
                        else plan.NoRoom.Add(new KeyValuePair<string, Place>(kv.Key, theirs));
                    }
                    else if (SameSpotAs(yours, theirs))
                    {
                        plan.Duplicates.Add(new KeyValuePair<string, Place>(kv.Key, theirs));
                        if (theirs.Fav && !yours.Fav && yours != theirs) plan.NewFavs++;
                    }
                    else plan.Conflicts.Add(new ImportConflict { World = kv.Key, Name = theirs.Name, Yours = yours, Theirs = theirs });
                }
                foreach (var id in kv.Value.Visited) if (mine == null || !mine.Visited.Contains(id)) plan.NewFound++;
                foreach (var n in kv.Value.Notes) if (mine == null || !mine.Notes.ContainsKey(n.Key)) plan.NewNotes++;
            }
            return plan;
        }

        // Merge (the default): add what's new, union the found marks, a favourite stays a favourite; conflicts keep yours.
        // Replace (only when chosen): the destination save becomes exactly the source save. Recently travelled and the Return
        // spot are history: a merge leaves yours, a replace takes theirs.
        public static void Apply(ImportPlan plan, PlaceStore into, bool replace)
        {
            if (replace)
            {
                into.PutSave(plan.DestSave, Copy(plan.Source));
                return;
            }
            var dest = into.SaveOf(plan.DestSave);
            if (dest == null) { dest = new SaveData { Seed = 0 }; into.PutSave(plan.DestSave, dest); }
            foreach (var kv in plan.Source.Planets)
            {
                if (!dest.Planets.TryGetValue(kv.Key, out var mine)) dest.Planets[kv.Key] = mine = new PlanetPlaces();
                foreach (var add in plan.Additions) if (add.Key == kv.Key && !mine.Saved.Exists(p => SameName(p, add.Value.Name))) mine.Saved.Add(Copy(add.Value));
                foreach (var dup in plan.Duplicates)
                {
                    if (dup.Key != kv.Key) continue;
                    var yours = mine.Saved.Find(p => SameName(p, dup.Value.Name));
                    if (yours == null) continue;
                    if (dup.Value.Fav) yours.Fav = true;
                    if (yours.Note.Length == 0 && dup.Value.Note.Length > 0) yours.Note = dup.Value.Note;   // 0.3.2
                }
                mine.Visited.UnionWith(kv.Value.Visited);
                // 0.3.2: their notes where yours has none; a note you have is yours (as conflicts keep your place)
                foreach (var n in kv.Value.Notes) if (!mine.Notes.ContainsKey(n.Key)) mine.Notes[n.Key] = n.Value;
            }
        }

        private static Place Copy(Place p) => new Place
        {
            Name = p.Name, Colour = p.Colour, Group = p.Group, Pin = p.Pin, X = p.X, Y = p.Y, Z = p.Z, AutoHeight = p.AutoHeight, Fav = p.Fav, Note = p.Note,
        };

        private static SaveData Copy(SaveData s)
        {
            var c = new SaveData { Seed = s.Seed };
            if (s.Back != null) c.Back = new BackSpot { World = s.Back.World, Name = s.Back.Name, X = s.Back.X, Y = s.Back.Y, Z = s.Back.Z };
            foreach (var kv in s.Planets)
            {
                var p = new PlanetPlaces();
                foreach (var q in kv.Value.Saved) p.Saved.Add(Copy(q));
                foreach (var q in kv.Value.Recent) p.Recent.Add(Copy(q));
                p.Visited.UnionWith(kv.Value.Visited);
                foreach (var n in kv.Value.Notes) p.Notes[n.Key] = n.Value;
                c.Planets[kv.Key] = p;
            }
            return c;
        }
    }
}

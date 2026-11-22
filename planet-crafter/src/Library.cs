// GamesOMG Teleport 0.3.0: everything the panel can do to your data, in one Unity-free place the tests drive end to end:
// the places file, its rotating backups, export/import, Delete with Undo, the scoped bulk clears, and the settings resets
// (which go to Prefs and nowhere else). Saving stays automatic; every failure comes back as a line for the screen.
using System;
using System.Collections.Generic;
using System.IO;

namespace GamesOMG.PlanetCrafterTeleport
{
    internal sealed class Library
    {
        private readonly PlaceFile file;
        private readonly Backups backups;
        private readonly Action<string> log;
        private readonly Func<DateTime> now;
        private PlaceStore store;
        private string loadNote, blocked;
        private string boundSave;
        private uint boundSeed;
        private Func<string, bool> boundExists;

        public readonly Prefs Prefs;
        public readonly string DataDir;
        public readonly string ExportsDir;

        public Library(string placesPath, string dataDir, Prefs prefs, Action<string> log, Func<DateTime> now = null)
        {
            this.log = log ?? (_ => { });
            this.now = now ?? (() => DateTime.Now);
            file = new PlaceFile(placesPath, this.log);
            DataDir = dataDir;
            ExportsDir = Path.Combine(dataDir, "exports");
            backups = new Backups(Path.Combine(dataDir, "backups"), this.now);
            Prefs = prefs ?? new Prefs();
            PlacesPath = placesPath;
        }

        public string PlacesPath { get; }
        public string BackupsDir => backups.Folder;
        public PlaceStore Store { get { Ensure(); return store; } }

        private void Ensure()
        {
            if (store != null) return;
            store = file.Load();
            loadNote = file.Note;
            // a file from an older version is copied aside before anything writes over it (0.2.4/0.2.5: v1, 0.2.6: v2)
            if (file.LastText != null && store.ReadVersion < PlaceStore.FileVersion)
            {
                string failed = backups.Take(file.LastText, "before-migration", out var path);
                if (failed != null)
                {
                    blocked = L.T("lib.migrationBackupFailed", failed);
                    loadNote = (loadNote != null ? loadNote + " " : "") + blocked;
                    log("Migration backup failed (" + failed + "): nothing will be written this session.");
                }
                else log("Places file v" + store.ReadVersion + " backed up before its first v" + PlaceStore.FileVersion + " write: " + Path.GetFileName(path));
            }
        }

        public string TakeLoadNote()
        {
            Ensure();
            string said = loadNote;
            loadNote = null;
            return said;
        }

        // null when written, else the line to show (what you did stays in memory till you quit)
        public string Save()
        {
            Ensure();
            if (blocked != null) return blocked;
            return file.Save(store);
        }

        // the save in play (Plugin, each time the player is in a world). Returns a line for the screen, or null.
        public string Bind(string save, uint seed, Func<string, bool> exists, out string saveFailed)
        {
            Ensure();
            saveFailed = null;
            boundSave = save;
            boundSeed = seed;
            boundExists = exists;
            string said = store.Use(save, seed, exists);
            if (said != null) saveFailed = Save();
            return said;
        }

        public string CurrentSave => Store.Current;

        // ── one place: Delete with Undo (saved straight away either way) ──
        public Removed Delete(string world, Place place, out string saveFailed)
        {
            var removed = Store.Delete(world, place);
            saveFailed = removed != null ? Save() : null;
            return removed;
        }

        public bool Undo(Removed removed, out string saveFailed)
        {
            bool ok = Store.Undo(removed);
            saveFailed = ok ? Save() : null;
            return ok;
        }

        // ── the bulk clears: each names its scope on screen, takes a backup first, and refuses when it can't ──
        public enum Bulk { Found, Recent, Saved }

        public int CountOf(Bulk what, string world)
        {
            var here = Store.For(world);
            return what == Bulk.Found ? here.Visited.Count : what == Bulk.Recent ? here.Recent.Count : here.Saved.Count;
        }

        public string Clear(Bulk what, string world, out int cleared)
        {
            cleared = 0;
            if (CountOf(what, world) == 0) return null;
            string failed = Backup("before-clear-" + what.ToString().ToLowerInvariant());
            if (failed != null) return failed;
            cleared = what == Bulk.Found ? store.ClearFound(world) : what == Bulk.Recent ? store.ClearRecent(world) : store.ClearSaved(world);
            return Save();
        }

        private string Backup(string reason)
        {
            Ensure();
            string failed = backups.Take(store.ToJson(), reason, out _);
            return failed == null ? null : L.T("lib.backupFailed", failed);
        }

        public List<BackupInfo> ListBackups() => backups.List();

        // a backup back in place of what you have now (which is backed up first). A file that won't read changes nothing.
        public string Restore(BackupInfo backup, out string summary)
        {
            summary = null;
            PlaceStore read;
            try { read = PlaceStore.FromJson(File.ReadAllText(backup.Path)); }
            catch (Exception error)
            {
                log("Backup unreadable (" + error.GetType().Name + ").");
                return L.T("lib.backupUnreadable");
            }
            if (read.ReadVersion > PlaceStore.FileVersion) return L.T("lib.backupNewer");
            string failed = Backup("before-restore");
            if (failed != null) return failed;
            store = read;
            if (boundSave != null) store.Use(boundSave, boundSeed, boundExists);
            summary = Summary(store);
            return Save();
        }

        public static string Summary(PlaceStore s)
        {
            int saves = 0, places = 0, found = 0;
            foreach (var name in s.SaveNames)
            {
                var d = s.SaveOf(name);
                if (d == null || d.Empty) continue;
                saves++;
                foreach (var p in d.Planets.Values) { places += p.Saved.Count; found += p.Visited.Count; }
            }
            return L.T("lib.summary", saves, places, found);
        }

        // ── export / import ──
        public string Export(string modVersion, bool withSettings, out string path)
        {
            path = null;
            Ensure();
            try
            {
                Directory.CreateDirectory(ExportsDir);
                string name = "GamesOMG-Teleport-PlanetCrafter-" + now().ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
                path = Path.Combine(ExportsDir, name + ".json");
                for (int n = 2; File.Exists(path); n++) path = Path.Combine(ExportsDir, name + "-" + n + ".json");
                File.WriteAllText(path, Transfer.Export(store, modVersion, now().ToUniversalTime(), withSettings ? Prefs.Snapshot() : null));
                return null;
            }
            catch (Exception error)
            {
                log("Export not written: " + error.GetType().Name);
                path = null;
                return L.T("lib.exportFailed", error.GetType().Name);
            }
        }

        // the export files a player can import: everything *.json in the exports folder, newest first
        public List<string> ImportCandidates()
        {
            var list = new List<string>();
            try
            {
                if (Directory.Exists(ExportsDir)) list.AddRange(Directory.GetFiles(ExportsDir, "*.json"));
                list.Sort((a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
            }
            catch (Exception error) { log("Exports folder unreadable: " + error.GetType().Name); }
            return list;
        }

        // throws FormatException with the line to show; reading changes nothing
        public ExportFile ReadImport(string path)
        {
            string json;
            try { json = File.ReadAllText(path); }
            catch (Exception) { throw new FormatException(L.T("xfer.cantOpen")); }
            return Transfer.Read(json);
        }

        public ImportPlan Preview(ExportFile from, string sourceSave, string destSave) => Transfer.Plan(from, sourceSave, Store, destSave);

        // the import itself: a backup first (refused if it can't be taken), then merge or replace, then settings if asked
        public string Import(ImportPlan plan, bool replace, ExportFile from, bool applySettings, out int settingsChanged)
        {
            settingsChanged = 0;
            string failed = Backup(replace ? "before-import-replace" : "before-import");
            if (failed != null) return failed;
            Transfer.Apply(plan, store, replace);
            if (applySettings && from?.Settings != null) settingsChanged = Prefs.Apply(from.Settings);
            return Save();
        }

        // ── settings: one section back to its defaults. Places, found marks and progress are not reachable from here. ──
        public int ResetPrefs(string section) => Prefs.Reset(section);
    }
}

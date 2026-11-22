// GamesOMG Teleport 0.2.6 (ChatGPT's release review, 2026-11-19): the places file on disk, out of the panel so the WSL
// tests run it. 0.2.5 only logged a failed write, so the screen said nothing while your places went unsaved; and a file
// it could not read was copied aside "if it could", then overwritten by the next save even when that copy had failed.
// Now: a failed write is a line on screen, the data stays in memory and the last good file stays as it was; a file that
// can't be read is copied to .bad first, and if that copy fails nothing is written over it this session.
using System;
using System.IO;

namespace GamesOMG.PlanetCrafterTeleport
{
    internal sealed class PlaceFile
    {
        private readonly string path;
        private readonly Action<string> log;
        private string blocked;   // why nothing may be written this session (the unreadable file is still the only copy)

        public PlaceFile(string path, Action<string> log)
        {
            this.path = path;
            this.log = log ?? (_ => { });
        }

        // a line for the screen from the last Load: a damaged file set aside, or one left alone
        public string Note { get; private set; }

        // 0.3.0: the text the last Load read (null when there was no file, or it could not be read), so Library can back an
        // older schema up before the first write over it
        public string LastText { get; private set; }

        // 0.3.0 (Codex C61 F4): a file this version can only partly read is never written over unseen. Not ours at all (not
        // an object, no usable version, a known version without its saves): kept aside, start empty. Read, but with entries
        // it could not read: kept aside, the rest loads, the screen says how many were left out. From a newer version: read
        // what it can, write nothing over it this session. Kept aside = the first free places.json.bad, .bad2, … (an earlier
        // copy is never replaced; the same bytes already kept are not copied twice). No copy possible = nothing is written.
        public const int AsideMax = 20;

        public PlaceStore Load()
        {
            Note = null;
            blocked = null;
            LastText = null;
            if (!File.Exists(path)) return new PlaceStore();
            string text;
            PlaceStore store;
            try
            {
                text = File.ReadAllText(path);
                store = PlaceStore.FromJson(text);
            }
            catch (Exception error)
            {
                log("Places file unreadable (" + error.GetType().Name + ").");
                string kept = KeepAside();
                if (kept != null)
                {
                    Note = L.T("file.keptBad", kept);
                    log("Places file kept as " + kept + "; starting empty.");
                }
                else Block("Places file could not be copied aside: nothing will be written over it.");
                return new PlaceStore();
            }
            LastText = text;
            if (store.ReadVersion > PlaceStore.FileVersion)
            {
                blocked = L.T("file.newerWhy");
                Note = L.T("file.newer");
                log("Places file is schema v" + store.ReadVersion + ", newer than this version's v" + PlaceStore.FileVersion + ": read only, nothing written this session.");
            }
            else if (store.Unreadable > 0)
            {
                log("Places file: " + store.Unreadable + " entries could not be read.");
                string kept = KeepAside();
                if (kept != null)
                {
                    Note = L.N("file.someUnreadable", store.Unreadable, kept);
                    log("Places file kept as " + kept + " before anything is written over it.");
                }
                else Block("Places file could not be copied aside: nothing will be written over it.");
            }
            return store;
        }

        private void Block(string why)
        {
            blocked = L.T("file.blockedWhy");
            Note = L.T("file.blocked");
            log(why);
        }

        // the name it was kept under, or null when no copy could be made
        private string KeepAside()
        {
            try
            {
                byte[] now = File.ReadAllBytes(path);
                for (int i = 1; i <= AsideMax; i++)
                {
                    string aside = path + (i == 1 ? ".bad" : ".bad" + i);
                    if (File.Exists(aside))
                    {
                        if (Same(File.ReadAllBytes(aside), now)) return Path.GetFileName(aside);
                        continue;
                    }
                    File.Copy(path, aside, false);
                    return Path.GetFileName(aside);
                }
                log("Places file: every .bad name up to .bad" + AsideMax + " is taken.");
            }
            catch (Exception error) { log("Places file copy failed (" + error.GetType().Name + ")."); }
            return null;
        }

        private static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // null when written; otherwise the line for the screen (what you did stays in memory until you quit)
        public string Save(PlaceStore store)
        {
            if (blocked != null) return L.T("file.notSavedBecause", blocked);
            string tmp = path + ".tmp";
            try
            {
                File.WriteAllText(tmp, store.ToJson());
                if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
                return null;
            }
            catch (Exception error)
            {
                log("Places file not written: " + error.GetType().Name);
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { }
                return L.T("file.notSaved", error.GetType().Name);
            }
        }
    }
}

// GamesOMG Teleport 0.3.0: rotating local backups of the places file (ChatGPT's look-and-settings review, 2026-11-19, for
// the first public release). One is taken before every change that can't be undone in one click: a schema migration, a
// restore, an import, a bulk delete. Bounded: the newest Keep stay, the oldest go. Not the same thing as the .bad copy of an
// unreadable file (PlaceFile), which stays where it is. No Unity here: the tests drive it on a temp folder.
//   <BepInEx/config>/gamesomg-teleport/backups/places-20261119-201502-before-import.json
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace GamesOMG.PlanetCrafterTeleport
{
    internal sealed class BackupInfo
    {
        public string Path = "", Reason = "";
        public DateTime When;   // local time, from the file name
        public long Bytes;
    }

    internal sealed class Backups
    {
        public const int Keep = 10;
        private static readonly Regex Name = new Regex(@"^places-(\d{8}-\d{6})(?:-(\d+))?-([a-z0-9-]+)\.json$", RegexOptions.CultureInvariant);
        private readonly string dir;
        private readonly Func<DateTime> now;

        public Backups(string dir, Func<DateTime> now = null)
        {
            this.dir = dir;
            this.now = now ?? (() => DateTime.Now);
        }

        public string Folder => dir;

        // reasons are short ids, never a place name: "before-import", "before-restore", "before-clear", "before-migration"
        public static string Slug(string reason)
        {
            var clean = Regex.Replace((reason ?? "").ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
            return clean.Length == 0 ? "manual" : clean.Length > 40 ? clean.Substring(0, 40) : clean;
        }

        // writes one; null on success, else why not (the change that asked for it must then not go ahead)
        public string Take(string json, string reason, out string path)
        {
            path = null;
            try
            {
                Directory.CreateDirectory(dir);
                string stamp = now().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture), slug = Slug(reason);
                path = System.IO.Path.Combine(dir, "places-" + stamp + "-" + slug + ".json");
                for (int n = 2; File.Exists(path); n++) path = System.IO.Path.Combine(dir, "places-" + stamp + "-" + n + "-" + slug + ".json");
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, json ?? "");
                File.Move(tmp, path);
                Prune(path);
                return null;
            }
            catch (Exception error)
            {
                path = null;
                return error.GetType().Name;
            }
        }

        // the newest Keep stay; the one just written is never the one removed
        private void Prune(string keepThis)
        {
            var all = List();
            for (int i = Keep; i < all.Count; i++)
            {
                if (string.Equals(all[i].Path, keepThis, StringComparison.OrdinalIgnoreCase)) continue;
                try { File.Delete(all[i].Path); } catch (Exception) { }
            }
        }

        // newest first
        public List<BackupInfo> List()
        {
            var list = new List<BackupInfo>();
            if (!Directory.Exists(dir)) return list;
            foreach (var file in Directory.GetFiles(dir, "places-*.json"))
            {
                var m = Name.Match(System.IO.Path.GetFileName(file));
                if (!m.Success) continue;
                if (!DateTime.TryParseExact(m.Groups[1].Value, "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var when)) continue;
                long bytes = 0;
                try { bytes = new FileInfo(file).Length; } catch (Exception) { }
                list.Add(new BackupInfo { Path = file, Reason = m.Groups[3].Value, When = when.AddTicks(m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : 0), Bytes = bytes });
            }
            list.Sort((a, b) => b.When != a.When ? b.When.CompareTo(a.When) : string.CompareOrdinal(b.Path, a.Path));
            return list;
        }
    }
}

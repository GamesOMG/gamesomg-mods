// GamesOMG Item Spawner: the game's own item list, with the names a player reads. Read from the game at run time (build 26411416):
//   · GroupsHandler.GetGroupsItem(): every item group, the list the game's own (editor-only) item picker shows
//     (InventoryDisplayer.groupSelector.PopulateWithItems); a group with no icon is not something a player has seen: skipped
//   · Readable.GetGroupName(group): the name in the game's current language (Localization falls back to English, then the id)
//   · the English name, for search: Localization's private localizationDictionary["english"]["GROUP_NAME_" + id], read by
//     name; if a patch renames it, search falls back to the shown name and the id, and the log says so once
//   · the icon: group.GetImage(), the game's own Sprite
// Rebuilt when the game's language changes, or its item list does: the game makes every Group anew each time a world loads
// (StaticDataHandler.LoadStaticData), so a list kept from the last world would hold Groups no item in this one points at
// (0.2.0: in the second world of a game run, "In your backpack" read 0).
using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using SpaceCraft;
using UnityEngine;

namespace GamesOMG.PlanetCrafterSpawn
{
    internal static class Catalog
    {
        private const BindingFlags Hidden = BindingFlags.Static | BindingFlags.NonPublic;
        private static readonly FieldInfo Table = typeof(Localization).GetField("localizationDictionary", Hidden);
        private static readonly FieldInfo Language = typeof(Localization).GetField("currentLangage", Hidden);   // sic, the game's spelling

        private static List<ItemEntry> cache;
        private static string cachedLanguage;
        private static int cachedGroups = -1;
        private static List<GroupItem> cachedList;
        private static bool warned;
        public static int Skipped { get; private set; }

        public static string CurrentLanguage()
        {
            try { return Language?.GetValue(null) as string ?? "?"; } catch (Exception) { return "?"; }
        }

        private static Dictionary<string, string> English(ManualLogSource log)
        {
            try
            {
                if (Table?.GetValue(null) is Dictionary<string, Dictionary<string, string>> all && all.TryGetValue("english", out var en)) return en;
            }
            catch (Exception error) { if (!warned) log.LogWarning("English item names unreadable (" + error.GetType().Name + "): search uses the shown name and the id."); }
            if (!warned) { warned = true; log.LogWarning("English item names not found in this build: search uses the shown name and the id."); }
            return null;
        }

        public static List<ItemEntry> Items(ManualLogSource log)
        {
            List<GroupItem> groups;
            try { groups = GroupsHandler.GetGroupsItem(); } catch (Exception) { groups = null; }
            if (groups == null || groups.Count == 0) return cache ?? new List<ItemEntry>();
            string language = CurrentLanguage();
            if (cache != null && language == cachedLanguage && groups.Count == cachedGroups && ReferenceEquals(groups, cachedList)) return cache;
            var english = English(log);
            var list = new List<ItemEntry>(groups.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int order = 0, skipped = 0, unnamedCount = 0;
            foreach (var g in groups)
            {
                if (g == null) continue;
                string id = g.GetId();
                if (string.IsNullOrEmpty(id) || !seen.Add(id)) continue;
                if (g.GetImage() == null) { skipped++; continue; }
                string name = null, en = null;
                try { name = Readable.GetGroupName(g); } catch (Exception) { }
                if (english != null) english.TryGetValue(GameConfig.localizationGroupNameId + id, out en);
                // no name in English nor in the current language: Readable hands back the bare id
                bool unnamed = english != null && string.IsNullOrEmpty(en) && (string.IsNullOrEmpty(name) || name == id);
                if (unnamed) unnamedCount++;
                list.Add(ItemEntry.Make(id, name, en, order++, g, unnamed));
            }
            cache = list;
            cachedLanguage = language;
            cachedGroups = groups.Count;
            cachedList = groups;
            Skipped = skipped;
            log.LogInfo("Item list: " + list.Count + " items (language " + language + ", " + skipped + " without an icon left out, "
                + unnamedCount + " the game never names: shown only when a search matches their id).");
            return cache;
        }

        public static ItemEntry Find(IList<ItemEntry> items, string id)
        {
            foreach (var e in items) if (e.Id == id) return e;
            return null;
        }

        public static Sprite Icon(ItemEntry e) => (e?.Group as Group)?.GetImage();

        // how many of this item the inventory holds now
        public static int CountIn(Inventory inventory, ItemEntry e)
        {
            if (inventory == null || !(e?.Group is Group g)) return 0;
            int n = 0;
            try { foreach (var wo in inventory.GetInsideWorldObjects()) if (wo != null && wo.GetGroup() == g) n++; }
            catch (Exception) { }
            return n;
        }
    }
}

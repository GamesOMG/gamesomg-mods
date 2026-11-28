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
// 0.3.0: plus the effigies and posters, which the game files as buildings (GroupsHandler.GetGroupsConstructible(); the rule is
// Decor.IsPlaceable), after every item; spacesuit skins and those are marked Decoration (the Decoration tab, "one of each").
// 0.4.0: plus the furniture for looks (Decor.IsDecorBuilding, read off each GroupConstructible: GetGroupCategory, its inventories,
// GetGroupUnitGeneration in every world unit, and whether a chip unlocks it), after the crate finds: Decoration, Placeable and
// Building. And the blueprint chips (GroupItem.GetUnlocksGroup() != null) get their target's name, as the game shows them
// (Readable.GetGroupName(target); English: localizationDictionary["english"]["GROUP_NAME_" + target id]): Chip, browsed on the
// Blueprints tab and found by what they unlock.
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
        private static List<GroupConstructible> cachedBuildings;
        private static bool warned;
        public static int Skipped { get; private set; }
        public static int Decorations { get; private set; }
        public static int Buildings { get; private set; }
        public static int Chips { get; private set; }
        // every world unit of THIS game build (Enum.GetValues at run time: a unit a later build adds is read too)
        private static readonly DataConfig.WorldUnitType[] AllUnits = (DataConfig.WorldUnitType[])Enum.GetValues(typeof(DataConfig.WorldUnitType));

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
            List<GroupConstructible> buildings;
            try { buildings = GroupsHandler.GetGroupsConstructible(); } catch (Exception) { buildings = null; }
            string language = CurrentLanguage();
            if (cache != null && language == cachedLanguage && groups.Count == cachedGroups && ReferenceEquals(groups, cachedList)
                && ReferenceEquals(buildings, cachedBuildings)) return cache;
            var english = English(log);
            var list = new List<ItemEntry>(groups.Count + 64);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            // what a chip unlocks: every item's link, icon or not (a building one links to goes by the chip, never as decoration)
            var chipTargets = new HashSet<string>(StringComparer.Ordinal);
            int order = 0, skipped = 0, unnamedCount = 0, skins = 0, placeable = 0, furniture = 0, chips = 0;
            foreach (var g in groups)
            {
                if (g == null) continue;
                string id = g.GetId();
                if (string.IsNullOrEmpty(id) || !seen.Add(id)) continue;
                Group target = null;
                try { target = g.GetUnlocksGroup(); } catch (Exception) { }
                if (target != null && !string.IsNullOrEmpty(target.GetId())) chipTargets.Add(target.GetId());
                if (g.GetImage() == null) { skipped++; continue; }
                ItemEntry e;
                if (target != null && Decor.IsChip(true, target.GetId())) { e = ChipEntry(g, id, target, english, order++, ref unnamedCount); chips++; }
                else e = Entry(g, id, english, order++, ref unnamedCount);
                if (Decor.IsSkin(true, g.GetEquipableType().ToString())) { e.Decoration = true; skins++; }
                list.Add(e);
            }
            // the effigies and posters: buildings the game only hands out inside a crate, put down from the backpack
            if (buildings != null)
                foreach (var g in buildings)
                {
                    if (g == null) continue;
                    string id = g.GetId();
                    if (string.IsNullOrEmpty(id) || seen.Contains(id) || !Placeable(g)) continue;
                    seen.Add(id);
                    var e = Entry(g, id, english, order++, ref unnamedCount);
                    e.Decoration = e.Placeable = true;
                    placeable++;
                    list.Add(e);
                }
            // 0.4.0: the furniture for looks, after the crate finds
            if (buildings != null)
                foreach (var g in buildings)
                {
                    if (g == null) continue;
                    string id = g.GetId();
                    if (string.IsNullOrEmpty(id) || seen.Contains(id) || !DecorBuilding(g, chipTargets)) continue;
                    seen.Add(id);
                    var e = Entry(g, id, english, order++, ref unnamedCount);
                    e.Decoration = e.Placeable = e.Building = true;
                    furniture++;
                    list.Add(e);
                }
            cache = list;
            cachedLanguage = language;
            cachedGroups = groups.Count;
            cachedList = groups;
            cachedBuildings = buildings;
            Skipped = skipped;
            Decorations = skins + placeable + furniture;
            Buildings = furniture;
            Chips = chips;
            log.LogInfo("Item list: " + list.Count + " items (language " + language + ", " + skipped + " without an icon left out, "
                + unnamedCount + " the game never names: shown only when a search matches their id). Decoration: " + placeable
                + " effigies and posters (buildings the game hands out in crates), " + furniture + " furniture buildings for looks, "
                + skins + " spacesuit skins. Blueprints: " + chips + " blueprint chips, each named after what it unlocks.");
            return cache;
        }

        private static ItemEntry Entry(Group g, string id, Dictionary<string, string> english, int order, ref int unnamedCount)
        {
            string name = null, en = null;
            try { name = Readable.GetGroupName(g); } catch (Exception) { }
            if (english != null) english.TryGetValue(GameConfig.localizationGroupNameId + id, out en);
            // no name in English nor in the current language: Readable hands back the bare id
            bool unnamed = english != null && string.IsNullOrEmpty(en) && (string.IsNullOrEmpty(name) || name == id);
            if (unnamed) unnamedCount++;
            return ItemEntry.Make(id, name, en, order, g, unnamed);
        }

        // a chip under its target's name (Decor.ChipEntry): the game's own name for the target, and its English one
        private static ItemEntry ChipEntry(GroupItem g, string id, Group target, Dictionary<string, string> english, int order, ref int unnamedCount)
        {
            string targetId = target.GetId(), name = null, en = null;
            try { name = Readable.GetGroupName(target); } catch (Exception) { }
            if (english != null) english.TryGetValue(GameConfig.localizationGroupNameId + targetId, out en);
            // no English table to read (a renamed field): the shown name stands in, as for any item
            if (english == null && string.IsNullOrEmpty(en)) en = name;
            var e = Decor.ChipEntry(id, targetId, name, en, order, g, target);
            if (e.Unnamed) unnamedCount++;
            return e;
        }

        // Decor.IsDecorBuilding on the game's Group: furniture, no inventory of its own, makes nothing, no chip unlocks it
        private static bool DecorBuilding(GroupConstructible g, HashSet<string> chipTargets)
        {
            try
            {
                var more = g.GetSecondaryInventoriesSize();
                var made = new List<float>(AllUnits.Length);
                foreach (var u in AllUnits) made.Add(g.GetGroupUnitGeneration(u));
                return Decor.IsDecorBuilding(true, g.GetImage() != null, g.GetGroupCategory().ToString(), g.GetInventorySize(), more?.Count ?? 0,
                    Decor.Generates(made), chipTargets.Contains(g.GetId()));
            }
            catch (Exception) { return false; }
        }

        // Decor.IsPlaceable on the game's Group: an icon, no recipe, no inventory of its own
        private static bool Placeable(GroupConstructible g)
        {
            try
            {
                var recipe = g.GetRecipe()?.GetIngredientsGroupInRecipe();
                var more = g.GetSecondaryInventoriesSize();
                return Decor.IsPlaceable(true, g.GetImage() != null, recipe?.Count ?? 0, g.GetInventorySize(), more?.Count ?? 0);
            }
            catch (Exception) { return false; }
        }

        // the decorations this world has (Decor.Counts): any copy in an inventory, or built by a player. Read when the panel
        // opens, when the Decoration tab is picked and after a spawn, never per frame (a big world holds tens of thousands).
        public static HashSet<string> Owned(IList<ItemEntry> items)
        {
            var had = new HashSet<string>(StringComparer.Ordinal);
            var wanted = new HashSet<Group>();
            foreach (var e in items) if (e != null && e.Decoration && e.Group is Group g) wanted.Add(g);
            if (wanted.Count == 0) return had;
            try
            {
                var inInventories = new HashSet<int>();
                var inventories = InventoriesHandler.Instance?.GetAllInventories();
                if (inventories != null)
                    foreach (var inv in inventories.Values)
                        if (inv != null) foreach (var wo in inv.GetInsideWorldObjects()) if (wo != null) inInventories.Add(wo.GetId());
                var all = WorldObjectsHandler.Instance?.GetAllWorldObjects();
                if (all != null)
                    foreach (var kv in all)
                    {
                        var wo = kv.Value;
                        var g = wo?.GetGroup();
                        if (g == null || !wanted.Contains(g)) continue;
                        int id = wo.GetId();
                        if (Decor.Counts(inInventories.Contains(id), wo.GetIsPlaced(), WorldObjectsIdHandler.IsWorldObjectFromScene(id))) had.Add(g.GetId());
                    }
            }
            catch (Exception) { }
            return had;
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

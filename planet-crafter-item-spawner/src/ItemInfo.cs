// GamesOMG Item Spawner 0.2.0: what the panel says under the selected item, read from the game at run time (build 26411416), never
// typed in here. InfoText.cs turns it into words.
//   · the description: Readable.GetGroupDescription (GROUP_DESC_<id>), the path the game's own item card uses, so it is in the
//     game's current language; many items have none (ores, seeds), and then there is none here either
//   · a fuse (ItemCategory.Fuse): the boost from the fuse's own GroupItem (a unit fuse's unit multiplier, the production /
//     trade-rocket / growth fuse's value), and each optimizer's range and machines per fuse from the MachineOptimizer on the
//     optimizer building's own prefab
//   · food, water, oxygen, toxins (UsableType Eatable / Drinkable / Breathable / Purify): the item's value, the amount the
//     game adds to (or takes off) that gauge
//   · a multiplier (a seed, larva, egg…): the item's unit multiplier, and the buildings it counts in = every named building whose
//     prefab carries a WorldUnitMultiplierViaInventory for that unit (not one fed by hunger) and whose InventoryAuthorizedGroups
//     lets the item in. A named building that takes the item but multiplies nothing (a pot) is said too.
// Built once per game language (building names change with it) and per world (the game makes its Groups anew each load), and
// cached per item. A building's item filter is read from its prefab's saved categories, never through
// InventoryAuthorizedGroups.GetAuthorizedGroups(): that caches a set of this world's Groups on the prefab itself.
using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using SpaceCraft;
using UnityEngine;

namespace GamesOMG.PlanetCrafterSpawn
{
    internal sealed class Info
    {
        public string Description = "", Stat = "";
        public string Kind = "none";          // none · fuse · gauge · multiplier (the sitting checks the numbers by kind)
        public string Unit = "";              // the world unit a fuse or multiplier works on
        public float Percent;                 // a multiplier's or a fuse's percent
        public int Value;                     // a gauge amount
        public List<string> Places = new List<string>();     // multiplier: the ids of the buildings it counts in
        public List<string> Elsewhere = new List<string>();  // multiplier: named buildings that take it and multiply nothing
        public List<Tier> Tiers = new List<Tier>();          // fuse: each optimizer's reach
        public bool AddUp;
    }

    internal static class ItemInfo
    {
        private sealed class Building
        {
            public string Id = "", Name = "";
            public int Order, Size;
            public GroupConstructible Group;
            public bool HasUnit, Hunger;
            public DataConfig.WorldUnitType Unit;
            public bool Filtered;   // the prefab has an item filter, read
            public List<DataConfig.ItemCategory> Cats, NotCats;
            public List<DataConfig.ItemSubCategory> Subs, NotSubs;
            public float Range;
            public int Machines = -1;   // ≥ 0: an optimizer
        }

        // the fuse kinds the game's MachineOptimizer maps to a world unit (MachineOptimizer.Init, build 26411416)
        private static readonly Dictionary<DataConfig.ItemSubCategory, DataConfig.WorldUnitType> FuseUnits = new Dictionary<DataConfig.ItemSubCategory, DataConfig.WorldUnitType>
        {
            [DataConfig.ItemSubCategory.FusePressure] = DataConfig.WorldUnitType.Pressure,
            [DataConfig.ItemSubCategory.FuseOxygen] = DataConfig.WorldUnitType.Oxygen,
            [DataConfig.ItemSubCategory.FuseHeat] = DataConfig.WorldUnitType.Heat,
            [DataConfig.ItemSubCategory.FusePlants] = DataConfig.WorldUnitType.Plants,
            [DataConfig.ItemSubCategory.FuseEnergy] = DataConfig.WorldUnitType.Energy,
            [DataConfig.ItemSubCategory.FuseAnimals] = DataConfig.WorldUnitType.Animals,
            [DataConfig.ItemSubCategory.FuseInsects] = DataConfig.WorldUnitType.Insects,
            [DataConfig.ItemSubCategory.FusePurification] = DataConfig.WorldUnitType.Purification,
        };

        private static readonly DataConfig.WorldUnitType[] Units =
        {
            DataConfig.WorldUnitType.Oxygen, DataConfig.WorldUnitType.Heat, DataConfig.WorldUnitType.Pressure, DataConfig.WorldUnitType.Plants,
            DataConfig.WorldUnitType.Insects, DataConfig.WorldUnitType.Animals, DataConfig.WorldUnitType.Energy, DataConfig.WorldUnitType.Purification,
        };

        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static readonly FieldInfo AuthCats = typeof(InventoryAuthorizedGroups).GetField("authorizedItemCategories", Hidden);
        private static readonly FieldInfo AuthSubs = typeof(InventoryAuthorizedGroups).GetField("authorizedItemSubCategories", Hidden);
        private static readonly FieldInfo DenyCats = typeof(InventoryAuthorizedGroups).GetField("unauthorizedItemCategories", Hidden);
        private static readonly FieldInfo DenySubs = typeof(InventoryAuthorizedGroups).GetField("unauthorizedItemSubCategories", Hidden);

        private static List<Building> buildings;
        private static List<GroupConstructible> builtFrom;
        private static string builtFor;
        private static readonly Dictionary<string, Info> cache = new Dictionary<string, Info>(StringComparer.Ordinal);
        private static bool warned, warnedFilter;
        private static ManualLogSource Log;

        public static Info For(ItemEntry e, ManualLogSource log)
        {
            if (e == null) return new Info();
            Log = log;
            string language = Catalog.CurrentLanguage();
            List<GroupConstructible> now = null;
            try { now = GroupsHandler.GetGroupsConstructible(); } catch (Exception) { }
            if (language != builtFor || !ReferenceEquals(now, builtFrom)) { cache.Clear(); buildings = null; builtFor = language; builtFrom = now; }
            if (cache.TryGetValue(e.Id, out var hit)) return hit;
            Info info;
            try { info = Make(e); }
            catch (Exception error)
            {
                info = new Info();
                if (!warned) { warned = true; log?.LogWarning("Item info unreadable for " + e.Id + " (" + error.GetType().Name + ": " + error.Message + "): the panel shows the name only."); }
            }
            cache[e.Id] = info;
            return info;
        }

        private static List<Building> Buildings()
        {
            if (buildings != null) return buildings;
            var all = builtFrom;
            bool unreadFilter = false;
            var list = new List<Building>();
            int order = 0;
            if (all != null)
                foreach (var g in all)
                {
                    if (g == null) continue;
                    string id = g.GetId(), name = Readable.GetGroupName(g);
                    var b = new Building { Id = id, Name = name, Order = order++, Group = g, Size = g.GetInventorySize() };
                    var prefab = g.GetAssociatedGameObject();
                    if (prefab != null)
                    {
                        var mult = prefab.GetComponentInChildren<WorldUnitMultiplierViaInventory>(true);
                        // the planet-wide multipliers rockets fill ("Space oxygen multiplier") take anything and are never a place
                        // a seed goes: the game itself sums them apart (WorldUnit, GetWorldUnitMultiplied); sitting 11-21 17:25
                        if (mult != null && g.GetWorldUnitMultiplied() == DataConfig.WorldUnitType.Null) { b.HasUnit = true; b.Unit = mult.worldUnitType; b.Hunger = mult.influencedByHunger; }
                        var auth = prefab.GetComponentInChildren<InventoryAuthorizedGroups>(true);
                        if (auth != null && AuthCats != null && AuthSubs != null && DenyCats != null && DenySubs != null)
                        {
                            b.Filtered = true;
                            b.Cats = AuthCats.GetValue(auth) as List<DataConfig.ItemCategory> ?? new List<DataConfig.ItemCategory>();
                            b.Subs = AuthSubs.GetValue(auth) as List<DataConfig.ItemSubCategory> ?? new List<DataConfig.ItemSubCategory>();
                            b.NotCats = DenyCats.GetValue(auth) as List<DataConfig.ItemCategory> ?? new List<DataConfig.ItemCategory>();
                            b.NotSubs = DenySubs.GetValue(auth) as List<DataConfig.ItemSubCategory> ?? new List<DataConfig.ItemSubCategory>();
                        }
                        else if (auth != null) unreadFilter = true;
                        var opt = prefab.GetComponentInChildren<MachineOptimizer>(true);
                        if (opt != null) { b.Range = opt.range; b.Machines = opt.maxWorldObjectPerFuse; }
                    }
                    list.Add(b);
                }
            buildings = list;
            if (unreadFilter && !warnedFilter) { warnedFilter = true; Log?.LogWarning("A building's item filter is unreadable in this game build: the info line may name buildings that don't take the item."); }
            return list;
        }

        // a name the game gives in some language (Readable.GetGroupName falls back to the bare id)
        private static bool Named(Building b) => !string.IsNullOrEmpty(b.Name) && b.Name != b.Id;

        // the game's own rule (Inventory.AddItem, with the sets InventoryAuthorizedGroups builds from these categories): an
        // authorized list, when there is one, must name the item's category or sub-category; the unauthorized lists must not
        private static bool Lists(Building b, GroupItem g) => b.Cats.Contains(g.GetItemCategory()) || b.Subs.Contains(g.GetItemSubCategory());
        private static bool Denies(Building b, GroupItem g) => b.NotCats.Contains(g.GetItemCategory()) || b.NotSubs.Contains(g.GetItemSubCategory());
        private static bool HasList(Building b) => b.Cats.Count + b.Subs.Count > 0;

        private static bool Takes(Building b, GroupItem g) => !b.Filtered || ((!HasList(b) || Lists(b, g)) && !Denies(b, g));

        private static bool Explicitly(Building b, GroupItem g) => b.Filtered && HasList(b) && Lists(b, g) && !Denies(b, g);

        private static Info Make(ItemEntry e)
        {
            var info = new Info();
            if (!(e.Group is Group group)) return info;
            info.Description = InfoText.Description(Readable.GetGroupDescription(group), e.Name);
            if (!(group is GroupItem item)) return info;

            // a fuse
            if (item.GetItemCategory() == DataConfig.ItemCategory.Fuse)
            {
                var sub = item.GetItemSubCategory();
                string label = null;
                float percent = 0;
                if (FuseUnits.TryGetValue(sub, out var unit)) { label = Readable.GetMultiplerLabel(unit); percent = item.GetGroupUnitMultiplier(unit); info.Unit = unit.ToString(); }
                else if (sub == DataConfig.ItemSubCategory.FuseProduction) { label = L.T("info.fuseProduction"); percent = item.GetGroupValue(); info.Unit = "Production"; }
                else if (sub == DataConfig.ItemSubCategory.FuseTradeRockets) { label = L.T("info.fuseTrade"); percent = item.GetGroupValue(); info.Unit = "TradeRockets"; }
                else if (sub == DataConfig.ItemSubCategory.FuseGrowth) { label = L.T("info.fuseGrowth"); percent = item.GetGroupValue(); info.Unit = "Growth"; }
                if (label != null && percent != 0)
                {
                    foreach (var b in Buildings())
                        if (b.Machines >= 0 && Named(b)) info.Tiers.Add(new Tier { Name = b.Name, Range = b.Range, Machines = b.Machines });
                    info.Tiers.Sort((x, y) => x.Range.CompareTo(y.Range));
                    info.Kind = "fuse";
                    info.Percent = percent;
                    info.Stat = InfoText.Fuse(label, percent, info.Tiers);
                    return info;
                }
            }

            // food, water, oxygen, toxins
            string gauge = null;
            switch (item.GetUsableType())
            {
                case DataConfig.UsableType.Eatable: gauge = "info.food"; break;
                case DataConfig.UsableType.Drinkable: gauge = "info.water"; break;
                case DataConfig.UsableType.Breathable: gauge = "info.oxygen"; break;
                case DataConfig.UsableType.Purify: gauge = "info.toxic"; break;
            }
            if (gauge != null && item.GetGroupValue() > 0)
            {
                info.Kind = "gauge";
                info.Unit = gauge.Substring(5);
                info.Value = item.GetGroupValue();
                info.Stat = InfoText.Gauge(gauge, info.Value);
                return info;
            }

            // a multiplier: the first unit with a building it counts in. A building that takes anything (no authorized list)
            // counts only for an item with a multiplier of its own; one that names the item's kind counts even at 0 % (Lirma)
            foreach (var unit in Units)
            {
                var places = new List<Place>();
                bool addUp = false, own = item.GetGroupUnitMultiplier(unit) != 0;
                foreach (var b in Buildings())
                    if (b.HasUnit && !b.Hunger && b.Unit == unit && Named(b) && (Explicitly(b, item) || own && Takes(b, item)))
                    {
                        places.Add(new Place { Id = b.Id, Name = b.Name, Order = b.Order });
                        if (b.Size > 1) addUp = true;
                    }
                if (places.Count == 0) continue;
                foreach (var b in Buildings())
                    if (!(b.HasUnit && b.Unit == unit) && Named(b) && Explicitly(b, item)) info.Elsewhere.Add(b.Id);
                var cat = item.GetItemCategory();
                bool seed = cat == DataConfig.ItemCategory.SeedPlant || cat == DataConfig.ItemCategory.SeedTree;
                info.Kind = "multiplier";
                info.Unit = unit.ToString();
                info.Percent = item.GetGroupUnitMultiplier(unit);
                info.AddUp = addUp;
                foreach (var p in places) info.Places.Add(p.Id);
                info.Stat = InfoText.Multiplier(Readable.GetMultiplerLabel(unit), info.Percent, InfoText.Families(places), addUp,
                    info.Elsewhere.Count == 0 ? 0 : seed ? 1 : 2);
                return info;
            }
            return info;
        }
    }
}

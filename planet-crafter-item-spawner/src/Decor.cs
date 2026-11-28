// GamesOMG Item Spawner 0.3.0: the decoration set. Unity-free (tests run it against the game's own data, both builds).
//   · what counts as decoration, by the game's own data, never a list of ids (build 26411416, and the Magnetaris build 26763323):
//     effigies and posters are BUILDINGS in the game (GroupDataConstructible) that only ever come inside a crate: an icon, no
//     recipe, no inventory of their own (that last leaves out the Warden's altar, the only other one). A found one is put down
//     from the backpack (InventoryDisplayer.Consume → PlayerBuilder.SetNewGhost with the item itself: no recipe, no unlock).
//     Spacesuit skins are items: equipableType Skin. A new effigy or skin in a game update joins on its own.
//   · "one of each": every one shown that this world doesn't have yet. Had = any copy in an inventory (on you, in a locker, in a
//     crate you opened) or built by a player. A copy still lying where the game put it (a scene object) doesn't count, so a
//     loose effigy in a wreck is still offered. An unopened crate's contents don't exist yet, so they never count either.
// 0.4.0 (2026-11-28, Jason 09:08 "Yes, build it"): furniture that only changes a base's looks, and the blueprint chips, named.
//   · a decoration building (IsDecorBuilding): a building with an icon in the game's own furniture category
//     (GroupConstructible.GetGroupCategory() == Furnitures; Machines, BaseBuilding, Screens, Biomass, Purification are machines,
//     structure or instruments), with no inventory of its own (storage, a planter, a display case: function, not looks), that
//     generates nothing in any world unit (GetGroupUnitGeneration > 0: the Ivy, the Aquarium tube, the pots and the Fireplace
//     terraform; a power DRAW is fine) and that no blueprint chip unlocks (those come through the game's own unlock, as chips).
//     The game scales generation by the world's terraformation pace (GameSettingsHandler.ChangeGenerationForGroups), a
//     positive factor: the sign this reads never changes. Put down from the backpack as an effigy is (SetNewGhost with the
//     object: no recipe, Free-build or unlock check); taking it down gives its recipe back, and, for one the game unlocks from
//     wrecks (lootRecipeOnDeconstruct) and not unlocked yet, a blueprint chip of it (WorldObjectsHandler.
//     RetrieveResourcesFromDeconstructionServerRpc). Same pick on both builds.
//   · a blueprint chip (IsChip): an item whose GroupItem.GetUnlocksGroup() is set. The game gives none of them a name and shows
//     each under its target's (UiGroupLine.SetValues, GroupInfosDisplayer: Readable.GetGroupName(chip.GetUnlocksGroup())):
//     ChipEntry does the same, so a chip is browsed and found by what it unlocks; its own id still matches ("blueprint").
using System.Collections.Generic;

namespace GamesOMG.PlanetCrafterSpawn
{
    internal static class Decor
    {
        // a building the game only hands out inside a crate, put down from the backpack: an effigy, a poster
        public static bool IsPlaceable(bool constructible, bool hasIcon, int recipeCount, int inventorySize, int secondaryInventories)
            => constructible && hasIcon && recipeCount == 0 && inventorySize <= 0 && secondaryInventories <= 0;

        // 0.4.0: furniture for looks (DataConfig.GroupCategory read by name, so a renumbered enum can't move it)
        public static bool IsDecorBuilding(bool constructible, bool hasIcon, string category, int inventorySize, int secondaryInventories,
            bool generates, bool chipLinked)
            => constructible && hasIcon && category == "Furnitures" && inventorySize <= 0 && secondaryInventories <= 0 && !generates && !chipLinked;

        // does a building make anything in some world unit? (a negative number is a draw: power it uses)
        public static bool Generates(IEnumerable<float> perUnit)
        {
            if (perUnit != null) foreach (float v in perUnit) if (v > 0) return true;
            return false;
        }

        // 0.4.0: a blueprint chip, by the game's own link (GroupItem.GetUnlocksGroup)
        public static bool IsChip(bool item, string unlocksId) => item && !string.IsNullOrEmpty(unlocksId);

        // a chip as the game shows it: under its target's name, in the game's language and in English. Unnamed only when the
        // target has no name either (then it reads as its own id, as before)
        public static ItemEntry ChipEntry(string id, string targetId, string targetName, string targetEnglish, int order, object group = null, object target = null)
        {
            bool named = !string.IsNullOrEmpty(targetEnglish) || !(string.IsNullOrEmpty(targetName) || targetName == targetId);
            var e = named ? ItemEntry.Make(id, targetName, targetEnglish, order, group) : ItemEntry.Make(id, id, null, order, group, true);
            e.Chip = true;
            e.UnlocksId = targetId ?? "";
            e.Unlocks = target;
            return e;
        }

        // a spacesuit skin (DataConfig.EquipableType.Skin, read by name so a renumbered enum can't move it)
        public static bool IsSkin(bool item, string equipableType) => item && equipableType == "Skin";

        // the ids "one of each" adds, in the order shown: each shown decoration this world doesn't have, once
        public static List<ItemEntry> Missing(IList<ItemEntry> shown, ICollection<string> had)
        {
            var list = new List<ItemEntry>();
            var seen = new HashSet<string>();
            if (shown == null) return list;
            foreach (var e in shown)
            {
                if (e == null || !e.Decoration || !seen.Add(e.Id)) continue;
                if (had != null && had.Contains(e.Id)) continue;
                list.Add(e);
            }
            return list;
        }

        // the decorations among a list (the Decoration tab before its search)
        public static List<ItemEntry> Only(IList<ItemEntry> all)
        {
            var list = new List<ItemEntry>();
            if (all != null) foreach (var e in all) if (e != null && e.Decoration) list.Add(e);
            return list;
        }

        // 0.4.0: the blueprint chips among a list (the Blueprints tab before its search)
        public static List<ItemEntry> Chips(IList<ItemEntry> all)
        {
            var list = new List<ItemEntry>();
            if (all != null) foreach (var e in all) if (e != null && e.Chip) list.Add(e);
            return list;
        }

        // does a copy count as "had"? in an inventory, or placed and made in play (fromScene = the game's own
        // WorldObjectsIdHandler.IsWorldObjectFromScene: one of the scene's own objects, still where the game put it)
        public static bool Counts(bool inInventory, bool placed, bool fromScene) => inInventory || (placed && !fromScene);
    }
}

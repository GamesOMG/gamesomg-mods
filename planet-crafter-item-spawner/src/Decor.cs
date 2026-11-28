// GamesOMG Item Spawner 0.3.0: the decoration set. Unity-free (tests run it against the game's own data, both builds).
//   · what counts as decoration, by the game's own data, never a list of ids (build 26411416, and the Magnetaris build 26763323):
//     effigies and posters are BUILDINGS in the game (GroupDataConstructible) that only ever come inside a crate: an icon, no
//     recipe, no inventory of their own (that last leaves out the Warden's altar, the only other one). A found one is put down
//     from the backpack (InventoryDisplayer.Consume → PlayerBuilder.SetNewGhost with the item itself: no recipe, no unlock).
//     Spacesuit skins are items: equipableType Skin. A new effigy or skin in a game update joins on its own.
//   · "one of each": every one shown that this world doesn't have yet. Had = any copy in an inventory (on you, in a locker, in a
//     crate you opened) or built by a player. A copy still lying where the game put it (a scene object) doesn't count, so a
//     loose effigy in a wreck is still offered. An unopened crate's contents don't exist yet, so they never count either.
using System.Collections.Generic;

namespace GamesOMG.PlanetCrafterSpawn
{
    internal static class Decor
    {
        // a building the game only hands out inside a crate, put down from the backpack: an effigy, a poster
        public static bool IsPlaceable(bool constructible, bool hasIcon, int recipeCount, int inventorySize, int secondaryInventories)
            => constructible && hasIcon && recipeCount == 0 && inventorySize <= 0 && secondaryInventories <= 0;

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

        // does a copy count as "had"? in an inventory, or placed and made in play (fromScene = the game's own
        // WorldObjectsIdHandler.IsWorldObjectFromScene: one of the scene's own objects, still where the game put it)
        public static bool Counts(bool inInventory, bool placed, bool fromScene) => inInventory || (placed && !fromScene);
    }
}

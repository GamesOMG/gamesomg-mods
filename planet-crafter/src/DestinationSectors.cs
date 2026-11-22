// ChatGPT C56: preload only destination sectors, using the game's own streaming API.
// 0.2.6: a sector whose SectorExit box holds the target loads too. The game unloads a sector only when the player leaves
// that box (SectorExit.OnTriggerExit), so a player standing in it past the entry box has it loaded: Toxicity's
// Prison_Interior floor is 14.5 m past its entry box. The landing research does the same (floors.py loaded_at).
using System.Collections.Generic;
using SpaceCraft;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GamesOMG.PlanetCrafterTeleport
{
    internal sealed class DestinationSectors
    {
        private readonly List<Sector> sectors = new List<Sector>();
        private readonly List<Sector> requested = new List<Sector>();
        private SectorEnter[] entries;
        private SectorExit[] exits;
        private readonly HashSet<Sector> seen = new HashSet<Sector>();
        private float margin;

        private DestinationSectors(SectorEnter[] entries, SectorExit[] exits) { this.entries = entries; this.exits = exits; }

        public static bool TryStart(PlayerMainController player, TeleportRequest target,
            out DestinationSectors loading, out string reason)
        {
            reason = null;
            var entries = UnityEngine.Object.FindObjectsByType<SectorEnter>(FindObjectsSortMode.None);
            var exits = UnityEngine.Object.FindObjectsByType<SectorExit>(FindObjectsSortMode.None);
            loading = new DestinationSectors(entries, exits);
            var body = player.GetComponent<CharacterController>();
            loading.margin = body != null ? body.bounds.extents.magnitude + 1f : 3f;
            return loading.Add(target, out reason) >= 0;
        }

        // 0.2.5: a sector's own scene can hold the entry box of another (Selenea's River wreck enters from inside the
        // Hills sector), which does not exist until its parent loads. Once everything requested is loaded, scan again:
        // true when that found a new sector to wait for (the landing research loads them the same way, floors.py).
        public bool Rescan(TeleportRequest target, out string reason)
        {
            entries = UnityEngine.Object.FindObjectsByType<SectorEnter>(FindObjectsSortMode.None);
            exits = UnityEngine.Object.FindObjectsByType<SectorExit>(FindObjectsSortMode.None);
            return Add(target, out reason) > 0;
        }

        private int Add(TeleportRequest target, out string reason, bool capped = true)
        {
            reason = null;
            var added = new List<Sector>();
            foreach (var entry in entries)
                if (entry != null) Consider(entry.sector, entry.collider, target, added);
            foreach (var exit in exits)
                if (exit != null) Consider(exit.sector, exit.collider, target, added);
            sectors.AddRange(added);
            if (capped && sectors.Count > 16)
            {
                reason = L.T("land.tooManySectors");
                return -1;
            }
            foreach (var sector in added)
            {
                var scene = SceneManager.GetSceneByName(sector.gameObject.name);
                // An existing loading scene belongs to the game, not to this request's cleanup.
                if (!scene.IsValid()) requested.Add(sector);
                if (!scene.isLoaded) sector.LoadSector();
            }
            return added.Count;
        }

        private void Consider(Sector sector, Collider box, TeleportRequest target, List<Sector> added)
        {
            if (sector == null || !Holds(box, target) || !seen.Add(sector)) return;
            added.Add(sector);
        }

        // does this entry or exit box (with the player's margin) hold the target's X/Z, at any height
        private bool Holds(Collider box, TeleportRequest target)
        {
            if (box == null || !box.enabled) return false;
            var bounds = box.bounds;
            bounds.Expand(new Vector3(margin * 2, 0, margin * 2));
            // Include vertically stacked sectors: the capsule also needs its ceiling checked.
            return !(target.X < bounds.min.x || target.X > bounds.max.x || target.Z < bounds.min.z || target.Z > bounds.max.z);
        }

#if SURVEY
        // The landing survey (test-only build) loads, in one go, the sectors of every spot it will check around a POI:
        // what the mod loads for each of them. No 16-sector cap here; CountFor says what the mod would load for ONE spot.
        public int Extend(TeleportRequest target)
        {
            return Add(target, out _, capped: false);
        }

        public int CountFor(TeleportRequest target)
        {
            var hit = new HashSet<Sector>();
            foreach (var entry in entries) if (entry != null && entry.sector != null && Holds(entry.collider, target)) hit.Add(entry.sector);
            foreach (var exit in exits) if (exit != null && exit.sector != null && Holds(exit.collider, target)) hit.Add(exit.sector);
            return hit.Count;
        }

        // a requested scene that is neither loaded nor loading (unloaded under it as the player left its exit box): ask again
        public void Retry()
        {
            foreach (var sector in sectors)
                if (sector != null && !SceneManager.GetSceneByName(sector.gameObject.name).IsValid()) sector.LoadSector();
        }

        // the entry and exit boxes again (after a load round), without adding anything yet: Extend each spot after it
        public void Refresh()
        {
            entries = UnityEngine.Object.FindObjectsByType<SectorEnter>(FindObjectsSortMode.None);
            exits = UnityEngine.Object.FindObjectsByType<SectorExit>(FindObjectsSortMode.None);
        }

        public int Count => sectors.Count;
#endif

        public bool Ready
        {
            get
            {
                foreach (var sector in sectors)
                    if (sector != null && !SceneManager.GetSceneByName(sector.gameObject.name).isLoaded) return false;
                return true;
            }
        }

        public void ReleaseUnused()
        {
            // Native UnloadSector unloads unconditionally. Another co-op player
            // may have entered a requested scene while this request was failing.
            var occupants = UnityEngine.Object.FindObjectsByType<PlayerMainController>(FindObjectsSortMode.None);
            foreach (var sector in requested)
            {
                if (sector == null || !SceneManager.GetSceneByName(sector.gameObject.name).isLoaded) continue;
                bool needed = false;
                foreach (var entry in entries)
                    if (entry != null && entry.sector == sector && Occupied(entry.collider, occupants)) { needed = true; break; }
                // 0.2.6: a player inside the exit box keeps the scene too (the game unloads it only when they leave that box)
                if (!needed)
                    foreach (var exit in exits)
                        if (exit != null && exit.sector == sector && Occupied(exit.collider, occupants)) { needed = true; break; }
                if (!needed) sector.UnloadSector();
            }
        }

        private static bool Occupied(Collider box, PlayerMainController[] occupants)
        {
            if (box == null) return false;
            var bounds = box.bounds;
            foreach (var occupant in occupants)
            {
                if (occupant == null) continue;
                var body = occupant.GetComponent<CharacterController>();
                // A remote player's controller can be disabled; its
                // transform still keeps the native scene in use.
                if (bounds.Contains(occupant.transform.position)
                    || (body != null && body.enabled && bounds.Intersects(body.bounds)))
                    return true;
            }
            return false;
        }
    }
}

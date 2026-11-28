// Teleporter: every call into the game. The move itself is the game's own PlayerRespawn.TeleportTo(Vector3), the call its
// Teleport Stone skill and its developers' own teleport cheats make (read offline 2026-11-27, build 26969408: it switches
// the player's collider off, sets the position, resets the fall state, a frame later switches the collider back on and
// tells the camera and foot IK). What this file adds is where you land, and what happens if that goes wrong:
//   1. the ground: a ray straight down at the line's x, z (from 2.5 m over its height, or from the sky for "~"), against
//      the layers the game's own movement treats as ground (PlayerMovement.GroundLayers)
//   2. the game's own NavMesh, the walkable ground its AI and its Teleport Stone use: the landing is the nearest NavMesh
//      point within 4 m of that ground, on the same level. None = refused, nothing moves
//   3. the watch: for 3 s after the move, a drop of more than 8 m under the landing, or the game's own death-fall
//      collider, puts you straight back where you stood
//   4. your squad: the minions following you (PlayerSummonSquad.CurrentSquad) that are within 40 m come along, each to a
//      spot the game itself picks beside you (AiExtensions.GetPreferedTeleportPosition, as its own squad-teleport cheat
//      does). Minions on guard or patrol elsewhere stay where they are.
using System;
using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.AI;
using _Project.Enemies.Scripts;
using _Project.Scripts;
using Megame.AI;
using GamePlayer = _Project.Scripts.Player.Player;

namespace GamesOMG.LordOfUndeadTeleport
{
    internal sealed class Teleporter
    {
        public const float SnapRadius = 4f;
        public const float SameLevel = 3f;
        public const float DropLimit = 8f;
        public const float WatchSeconds = 3f;
        // Unity's own constants, not in the game's stripped build: NavMesh.AllAreas, Physics.DefaultRaycastLayers
        const int AllAreas = -1;
        const int DefaultRaycastLayers = ~(1 << 2);

        readonly ManualLogSource log;
        public bool BringSquad = true;
        public float SquadRadius = 40f;

        Vector3? back;                       // where F10 goes
        string backLabel;
        bool watching;
        float watchFrom, watchUntil;
        Vector3 watchLanding, watchStart;
        public string LastLabel;             // the place you went to (Return names it)

        public Teleporter(ManualLogSource log) { this.log = log; }

        public bool HasBack => back.HasValue;

        public static GamePlayer FindPlayer()
        {
            try
            {
                var em = EntitiesManager.Instance;
                if (em != null)
                {
                    var p = em._player;
                    if (p != null && p.gameObject != null && p.gameObject.activeInHierarchy) return p;
                }
            }
            catch (Exception) { /* not in a world yet */ }
            try
            {
                var all = UnityEngine.Object.FindObjectsOfType<GamePlayer>();
                for (int i = 0; i < all.Length; i++)
                    if (all[i] != null && all[i].gameObject.activeInHierarchy) return all[i];
            }
            catch (Exception) { }
            return null;
        }

        static int GroundMask(GamePlayer p)
        {
            try
            {
                int m = p.Movement.GroundLayers.value;
                if (m != 0) return m;
            }
            catch (Exception) { }
            return DefaultRaycastLayers;
        }

        static bool Cast(Vector3 from, float length, int mask, out Vector3 hitPoint)
        {
            hitPoint = default;
            if (!Physics.Raycast(from, Vector3.down, out RaycastHit hit, length, mask, QueryTriggerInteraction.Ignore)) return false;
            hitPoint = hit.point;
            return true;
        }

        /// <summary>Where a line lands you, or why it can't. Moves nothing.</summary>
        public bool Resolve(GamePlayer p, TpTarget t, out Vector3 land, out string why)
        {
            land = default;
            why = null;
            int mask = GroundMask(p);
            Vector3 ground;
            if (t.Y.HasValue)
            {
                float y = t.Y.Value;
                // a floor near the height the line gives (a cave floor, a jetty, a ledge), else whatever is under it
                if (!Cast(new Vector3(t.X, y + 2.5f, t.Z), 10f, mask, out ground) &&
                    !Cast(new Vector3(t.X, y + 60f, t.Z), 200f, mask, out ground))
                    ground = new Vector3(t.X, y, t.Z);
            }
            else if (!Cast(new Vector3(t.X, 1500f, t.Z), 3000f, mask, out ground))
            {
                why = "No ground under " + t + ". That spot is off the map.";
                return false;
            }
            if (!NavMesh.SamplePosition(ground, out NavMeshHit hit, SnapRadius, AllAreas))
            {
                why = "No walkable ground within " + SnapRadius + " m of " + t + ". Nothing moved.";
                return false;
            }
            if (Math.Abs(hit.position.y - ground.y) > SameLevel)
            {
                why = "The walkable ground near " + t + " is on another level (a roof or a ledge). Nothing moved.";
                return false;
            }
            land = hit.position + new Vector3(0f, 0.05f, 0f);
            return true;
        }

        /// <summary>Why you can't be moved right now, or null.</summary>
        public static string Blocked(GamePlayer p)
        {
            if (p == null) return "Load a world first.";
            try { if (p.Stats != null && p.Stats.IsDead()) return "You're dead. Nothing moved."; } catch (Exception) { }
            try { if (p.Movement != null && !p.Movement.EnabledMove) return "The game has your movement locked right now (a menu or an action). Nothing moved."; }
            catch (Exception) { }
            return null;
        }

        public string Go(GamePlayer p, Vector3 land, string label)
        {
            string b = Blocked(p);
            if (b != null) return b;
            Vector3 from = p.transform.position;
            var squad = Followers(p, from);
            p.Respawn.TeleportTo(land);
            back = from;
            backLabel = null;
            LastLabel = label;
            StartWatch(from, land);
            int moved = Squad(squad, land);
            log.LogInfo("teleported " + Fmt(from) + " -> " + Fmt(land) + (label != null ? " (" + label + ")" : "") +
                        (moved > 0 ? ", " + moved + " of your squad with you" : ""));
            return (label != null ? label : "There") + (moved > 0 ? ". " + moved + " of your squad came along." : ".") +
                   " F10 takes you back.";
        }

        /// <summary>F10: back to where you stood before the last teleport; press it again to swap back.</summary>
        public string Back(GamePlayer p)
        {
            if (!back.HasValue) return "Nowhere to go back to yet: teleport somewhere first.";
            string b = Blocked(p);
            if (b != null) return b;
            Vector3 from = p.transform.position, to = back.Value;
            var squad = Followers(p, from);
            p.Respawn.TeleportTo(to);
            string name = backLabel;
            back = from;
            backLabel = LastLabel;
            LastLabel = name;
            StartWatch(from, to);
            Squad(squad, to);
            log.LogInfo("back " + Fmt(from) + " -> " + Fmt(to));
            return "Back" + (name != null ? " at " + name : " where you were") + ". F10 again swaps back.";
        }

        void StartWatch(Vector3 from, Vector3 land)
        {
            watching = true;
            watchStart = from;
            watchLanding = land;
            watchFrom = Time.unscaledTime + 0.3f;
            watchUntil = Time.unscaledTime + WatchSeconds;
        }

        /// <summary>Called every frame. Returns a message when it had to step in.</summary>
        public string Watch(GamePlayer p)
        {
            if (!watching) return null;
            if (p == null) { watching = false; return null; }
            float now = Time.unscaledTime;
            if (now < watchFrom) return null;
            Vector3 pos = p.transform.position;
            bool deathFall = false;
            try { deathFall = p.Movement.IsFallingInDeathCollider; } catch (Exception) { }
            if (pos.y < watchLanding.y - DropLimit || deathFall)
            {
                watching = false;
                p.Respawn.TeleportTo(watchStart);
                back = null;
                log.LogWarning("landing " + Fmt(watchLanding) + " dropped to y " + pos.y.ToString("0.0") + (deathFall ? " (death fall)" : "") +
                               "; put back at " + Fmt(watchStart));
                return "That landing dropped you, so you're back where you stood. Tell us which pin it was.";
            }
            if (now >= watchUntil)
            {
                watching = false;
                var d = pos - watchLanding;
                if (d.x * d.x + d.z * d.z > 15f * 15f)
                {
                    log.LogWarning("after 3 s the player is " + Math.Sqrt(d.x * d.x + d.z * d.z).ToString("0") + " m from the landing " + Fmt(watchLanding));
                    return "The game didn't keep you there. Try again once you're out of combat or a menu.";
                }
            }
            return null;
        }

        List<AICharacter> Followers(GamePlayer p, Vector3 from)
        {
            var list = new List<AICharacter>();
            if (!BringSquad) return list;
            try
            {
                var sq = p.SummonSquad != null ? p.SummonSquad.CurrentSquad : null;
                if (sq == null) return list;
                for (int i = 0; i < sq.Count; i++)
                {
                    var a = sq[i];
                    if (a == null || a.IsDead()) continue;
                    var d = a.transform.position - from;
                    if (d.sqrMagnitude <= SquadRadius * SquadRadius) list.Add(a);
                }
            }
            catch (Exception e) { log.LogWarning("squad read failed: " + e.Message); }
            return list;
        }

        int Squad(List<AICharacter> squad, Vector3 land)
        {
            int n = 0;
            foreach (var a in squad)
            {
                try
                {
                    var spot = AiExtensions.GetPreferedTeleportPosition(land, land, 3f, 5f, false);
                    a.TeleportTo(spot);
                    a.ResetCheckSetDestination();
                    n++;
                }
                catch (Exception e) { log.LogWarning("a squad member stayed behind: " + e.Message); }
            }
            return n;
        }

        public static string Fmt(Vector3 v) => new TpTarget(v.x, v.y, v.z).ToString().Substring(4);
    }
}

// GamesOMG Item Spawner: putting items into the player's backpack the way the game itself does (build 26411416, checked
// 2026-11-21). Crafting into the backpack (CraftManager.TryToCraftInInventory) calls
// InventoriesHandler.Instance.AddItemToInventory(group, player.GetPlayerBackpack().GetInventory(), callback); so do this.
//   · solo / co-op host: the game's AddNewItemToInventoryServerRpc runs here: a new saved WorldObject, Inventory.AddItem,
//     PropagateModification, RefreshDisplayerContent, DirtyInventory (other players watching it), then callback(ok, id)
//   · co-op client: the same server RPC (the game allows it from everyone); the host makes and adds the item and answers,
//     the game mirrors it into this player's inventory. The host needs no mod.
//   · nothing is ever put on the ground: no CreateAndDropOnFloor, no DropObjectsIfNotEnoughSpace
//   · a refusal (full) still leaves the WorldObject the server made, saved, with no position and no inventory: its id comes
//     back in the callback and is destroyed with the game's own DestroyWorldObject (a server RPC on a client)
//   · never touches the world's settings (Free Craft, Everything Unlocked: the only two that switch achievements off)
using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Logging;
using SpaceCraft;
using Unity.Netcode;
using UnityEngine;

namespace GamesOMG.PlanetCrafterSpawn
{
    internal sealed class Spawner
    {
        public const float AnswerSeconds = 10f;
        private readonly ManualLogSource log;

        public Spawner(ManualLogSource log) { this.log = log; }

        public bool Busy { get; private set; }
        public SpawnPlan Last { get; private set; }
        public int StrayDestroyed { get; private set; }

        // why the game can't take items now (null = it can)
        public static string Blocked()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening) return "spawn.notInWorld";
            if (InventoriesHandler.Instance == null || WorldObjectsHandler.Instance == null) return "spawn.notInWorld";
            return null;
        }

        public static bool IsClient
        {
            get { var nm = NetworkManager.Singleton; return nm != null && nm.IsListening && !nm.IsServer; }
        }

        public IEnumerator Run(Group group, Inventory backpack, SpawnPlan plan, Action<SpawnPlan> done)
            => Run(_ => group, group?.GetId() ?? "?", backpack, plan, done);

        // 0.3.0: the k-th item of the plan is groupAt(k) ("one of each": a different decoration per ask, in the order shown).
        // The game answers a backpack's asks in the order sent (its callback queue is first in, first out), and a plan stops at
        // the first batch with a refusal, so the items that went in are always the first plan.Added.
        public IEnumerator Run(Func<int, Group> groupAt, string id, Inventory backpack, SpawnPlan plan, Action<SpawnPlan> done)
        {
            Busy = true;
            Last = plan;
            StrayDestroyed = 0;
            try { log.LogInfo("Spawn " + id + (plan.Fill ? " (fill)" : " x" + plan.Wanted) + (IsClient ? " as a co-op client" : "") + "."); } catch (Exception) { }
            while (!plan.Done)
            {
                int n;
                try { n = plan.NextBatch(backpack.GetSize(), backpack.GetInsideWorldObjects().Count); }
                catch (Exception error) { log.LogWarning("Backpack unreadable: " + error.GetType().Name); plan.TimedOut(0); break; }
                if (n <= 0) break;
                var answers = new Answers();
                int first = plan.Added;
                for (int i = 0; i < n; i++)
                {
                    try { InventoriesHandler.Instance.AddItemToInventory(groupAt(first + i), backpack, answers.Take); }
                    catch (Exception error) { log.LogWarning("AddItemToInventory threw: " + error.GetType().Name + ": " + error.Message); answers.Thrown++; }
                }
                float deadline = Time.realtimeSinceStartup + AnswerSeconds;
                while (answers.Count + answers.Thrown < n && Time.realtimeSinceStartup < deadline) yield return null;
                Clean(answers);
                if (answers.Count + answers.Thrown < n || answers.Thrown > 0)
                {
                    log.LogWarning("The game answered " + answers.Count + " of " + n + " in " + AnswerSeconds + " s.");
                    answers.Late = true;
                    plan.TimedOut(answers.Ok);
                }
                else plan.Answered(answers.Ok, answers.No);
            }
            log.LogInfo("Spawned " + plan.Added + " " + id + (plan.Full ? ", backpack full" : "") + (plan.Failed ? ", stopped (no answer)" : "")
                + (StrayDestroyed > 0 ? "; " + StrayDestroyed + " refused item(s) the game had made were destroyed" : "") + ".");
            Busy = false;
            done?.Invoke(plan);
        }

        // the objects the server made for refused adds: destroyed, so the save keeps no item that is nowhere
        private void Clean(Answers answers)
        {
            foreach (int stray in answers.Stray)
            {
                try { WorldObjectsHandler.Instance.DestroyWorldObject(stray); StrayDestroyed++; }
                catch (Exception error) { log.LogWarning("Refused item " + stray + " not destroyed: " + error.GetType().Name); }
            }
            answers.Stray.Clear();
        }

        private sealed class Answers
        {
            public int Ok, No, Thrown;
            public bool Late;
            public readonly List<int> Stray = new List<int>();
            public int Count => Ok + No;

            public void Take(bool success, int woId)
            {
                if (success) Ok++;
                else
                {
                    No++;
                    // an answer after the wait timed out still cleans up after itself
                    if (woId != 0)
                    {
                        if (!Late) Stray.Add(woId);
                        else try { WorldObjectsHandler.Instance.DestroyWorldObject(woId); } catch (Exception) { }
                    }
                }
            }
        }
    }
}

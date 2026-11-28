// GamesOMG Item Spawner: how many to ask the game for next, and what to tell the player at the end. Unity-free (tests run it).
// The game decides what fits (InventoriesHandler.AddItemToInventory answers success per item), not us: a stacking mod
// changes how many fit in a slot, so "size - count" is only a guess for the batch size, never a limit. A batch that comes
// back with any refusal means the backpack is full: stop, and say how many didn't fit. Nothing is ever dropped.
using System;

namespace GamesOMG.PlanetCrafterSpawn
{
    internal sealed class SpawnPlan
    {
        public const int MaxBatch = 50;     // one batch's RPCs in flight at most (a co-op client waits for the host's answers)
        public const int FillCap = 999;     // Fill stops here even if the backpack never says full (a stacking mod's huge stacks)
        public const int MaxAmount = 999;

        public readonly int Wanted;          // what the player asked for (Fill: FillCap)
        public readonly bool Fill;
        public int Added { get; private set; }
        public bool Full { get; private set; }
        public bool Failed { get; private set; }   // the game stopped answering (timeout) or refused for another reason
        public int InFlight { get; private set; }

        public SpawnPlan(int amount, bool fill)
        {
            Fill = fill;
            Wanted = fill ? FillCap : Clamp(amount);
        }

        public static int Clamp(int amount) => Math.Max(1, Math.Min(MaxAmount, amount));

        public bool Done => Full || Failed || Added >= Wanted;

        private int lastAllTaken;   // the size of the last batch the game took whole

        // how many to ask for now (never more than is left, never more than MaxBatch):
        //   · free slots (size - count) when there are some: exact in the vanilla game, so a vanilla backpack is never over-asked
        //   · more items than slots: a stacking mod is in play, so grow (double the last whole batch) while the game takes them
        //   · exactly full: one probe; the game's answer decides (a stacking mod may still have room in a stack)
        // every refused ask costs one WorldObject the server made, which Spawner destroys
        public int NextBatch(int size, int count)
        {
            if (Done || InFlight > 0) return 0;
            int left = Wanted - Added;
            int free = size - count;
            int n = free > 0 ? free : count > size ? Math.Max(1, lastAllTaken * 2) : 1;
            n = Math.Min(left, Math.Min(n, MaxBatch));
            InFlight = n;
            return n;
        }

        // a batch's answers: how many the game took; any refusal = full
        public void Answered(int succeeded, int refused)
        {
            InFlight = 0;
            Added += succeeded;
            lastAllTaken = refused == 0 ? succeeded : 0;
            if (refused > 0) Full = true;
        }

        public void TimedOut(int succeeded)
        {
            InFlight = 0;
            Added += succeeded;
            Failed = true;
        }

        public int DidNotFit => Fill || !Full ? 0 : Math.Max(0, Wanted - Added);

        // the line the player reads (ids in Strings.cs; {0} the count, {1} the item's name, {2} what didn't fit)
        public string ResultId()
        {
            if (Failed) return Added > 0 ? "result.partialTimeout" : "result.timeout";
            if (Fill) return Full ? (Added > 0 ? "result.filled" : "result.fullNone") : "result.fillCap";
            if (Full) return Added > 0 ? "result.partial" : "result.fullNone";
            return "result.added";
        }

        // 0.3.0, "one of each" (Wanted = how many decorations were missing; {0} how many went in, {1} how many didn't fit)
        public string SetResultId()
        {
            if (Failed) return Added > 0 ? "set.partialTimeout" : "result.timeout";
            if (Full) return Added > 0 ? "set.partial" : "set.fullNone";
            return "set.added";
        }
    }
}

// GamesOMG Teleport 0.3.0 (Codex C61 F7): the landing check sees the destination as you will find it. Some floors in the
// game switch off while the player is far away (PerformancerDisableIfFarAway, build 26411416: each registered one sets its
// own GameObject active when the player is within disableIfPlayerIsFarerThan, default 50 m, unless its network group root is
// inactive; the base platforms' "Colliders" children carry it, e.g. Prime's Cave-Iklhass launch pads, Selenea's City01
// biodome lab, Skeo's ice-cave lab). The check runs with the player still at the start, so 0.2.7 saw those floors missing:
// a good platform refused, or a lower floor picked. Now, just before the check, exactly the ones the game will switch on
// once you stand at the destination are switched on; a refused teleport switches those (and only those) off again, and a
// teleport that lands leaves them to the game's own switch. No Unity here: FarSwitches.cs hands the game's set to it.
using System;
using System.Collections.Generic;

namespace GamesOMG.PlanetCrafterTeleport
{
    // one of the game's distance switches, as the wake sees it
    internal interface IFarSwitch
    {
        bool ActiveSelf { get; }
        void SetActive(bool on);
        float X { get; }
        float Y { get; }
        float Z { get; }
        float Reach { get; }        // disableIfPlayerIsFarerThan
        bool RootActive { get; }    // false: its network group is inactive, and the game leaves it alone
    }

    internal sealed class DestinationWake
    {
        private readonly List<IFarSwitch> woken = new List<IFarSwitch>();

        public int Count => woken.Count;

        // will the game's switch have this on once the player stands at the destination? The game measures from the
        // player's position in 3D. With no height known (a map spot, /tp x ~ z) the landing's height is what the check is
        // about to find, so every height counts: the horizontal distance alone.
        public static bool WillBeOn(IFarSwitch s, TeleportRequest at)
        {
            if (s == null || !s.RootActive) return false;
            double dx = s.X - at.X, dz = s.Z - at.Z, d2 = dx * dx + dz * dz;
            if (!at.AutomaticHeight) { double dy = s.Y - at.Y; d2 += dy * dy; }
            return d2 <= (double)s.Reach * s.Reach;
        }

        // switches on what is off now and will be on at the destination; returns how many
        public int Wake(IEnumerable<IFarSwitch> all, TeleportRequest at)
        {
            if (all == null) return 0;
            var now = new List<IFarSwitch>(all);   // a copy: switching on never changes the list being walked
            foreach (var s in now)
            {
                if (s == null || s.ActiveSelf || !WillBeOn(s, at)) continue;
                s.SetActive(true);
                woken.Add(s);
            }
            return woken.Count;
        }

        // the teleport did not happen: off again, exactly what Wake switched on (the player is still where the game had them off)
        public int Undo(Action<string> log = null)
        {
            int undone = 0;
            foreach (var s in woken)
            {
                try { s.SetActive(false); undone++; }
                catch (Exception error) { log?.Invoke("A floor switched on for the check could not be switched off: " + error.GetType().Name); }
            }
            woken.Clear();
            return undone;
        }

        // the player arrived (or a move was attempted): the game's own switch decides from here
        public void Keep() => woken.Clear();
    }
}

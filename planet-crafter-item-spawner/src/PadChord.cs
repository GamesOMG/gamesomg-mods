// GamesOMG Item Spawner: the controller opener, LB + RB together by default. Every single pad button already does something in
// play (build 26411416's "PlayerMovable" map), and View opens GamesOMG Teleport. LB is the game's SecretDevKey (it does nothing in
// play) and RB its AccessibilityKey (a modifier the game reads only while building or clicking in an inventory), so
// both shoulders at once do nothing in normal play. Written as "<Gamepad>/leftShoulder + <Gamepad>/rightShoulder"; one
// path alone is a single button; empty = no controller opener. Read straight from the device each frame it updates.
using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace GamesOMG.PlanetCrafterSpawn
{
    internal sealed class PadChord
    {
        private readonly Func<string> path;
        private bool wasDown;

        public PadChord(Func<string> path) { this.path = path; }

        public string Value => path() ?? "";

        // true on the frame the last button of the chord goes down while the others are held
        public bool Pressed()
        {
            var pad = Gamepad.current;
            if (pad == null) { wasDown = false; return false; }
            var parts = Chord.Parts(Value);
            if (parts.Count == 0) return false;
            bool down = true;
            foreach (var p in parts)
            {
                var b = Control(pad, p);
                if (b == null || !b.isPressed) { down = false; break; }
            }
            bool fired = down && !wasDown;
            wasDown = down;
            return fired;
        }

        // the chord is still held (the panel ignores LB / RB as tabs until both are let go)
        public bool Held()
        {
            var pad = Gamepad.current;
            if (pad == null) return false;
            foreach (var p in Chord.Parts(Value))
            {
                var b = Control(pad, p);
                if (b != null && b.isPressed) return true;
            }
            return false;
        }

        private static ButtonControl Control(Gamepad pad, string path)
        {
            string p = KeyCheck.Norm(path);
            int slash = p.IndexOf('/');
            if (slash < 0 || !KeyCheck.IsPad(p)) return null;
            try { return pad.TryGetChildControl(p.Substring(slash + 1)) as ButtonControl; }
            catch (Exception) { return null; }
        }
    }
}

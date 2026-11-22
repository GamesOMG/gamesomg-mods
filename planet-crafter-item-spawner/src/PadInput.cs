// GamesOMG Teleport 0.3.0: keyboard and controller navigation while the panel is open (ChatGPT's review: "Controller users
// should get proper selection and navigation throughout, without moving a mouse cursor around"). Read straight from the
// devices: the panel holds every game action off while it is open (InputHold), so nothing here also fires in the game.
// Buttons follow the game's own UI map (build 26411416 PlayerActions "UI"): Submit = A / Cross, Cancel = B / Circle,
// TabLeft/TabRight = LB/RB; ours add X = ten, Y = fill (GamesOMG Item Spawner). Prompts follow the device last used.
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace GamesOMG.PlanetCrafterSpawn
{
    internal enum Cmd { None, Up, Down, Left, Right, PageUp, PageDown, Home, End, Next, Prev, Activate, Back, TabLeft, TabRight, Ten, Fill }

    internal sealed class PadInput
    {
        private const float Delay = .38f, Rate = .085f, Dead = .55f;
        private int heldDir;            // the direction being held (Cmd as int), for repeat
        private float nextRepeat;
        private Vector2 lastMouse;

        public bool UsingPad { get; private set; }
        public bool PlayStation { get; private set; }

        // one command per frame (the first one seen), with hold-to-repeat for the four directions
        public Cmd Read(bool textFocused, bool arrowsAlways = false)
        {
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            var mouse = Mouse.current;
            // which device the player is using now: prompts switch with it
            if (pad != null && AnyPad(pad)) { UsingPad = true; string layout = pad.layout ?? ""; PlayStation = layout.Contains("DualShock") || layout.Contains("DualSense"); }
            else if (kb != null && kb.anyKey.wasPressedThisFrame) UsingPad = false;
            else if (mouse != null)
            {
                var p = mouse.position.ReadValue();
                if (mouse.leftButton.wasPressedThisFrame || (p - lastMouse).sqrMagnitude > 64) UsingPad = false;
                lastMouse = p;
            }

            if (kb != null)
            {
                bool shift = kb.shiftKey.isPressed;
                if (kb.escapeKey.wasPressedThisFrame) return Cmd.Back;
                if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) return Cmd.Activate;
                if (kb.tabKey.wasPressedThisFrame) return shift ? Cmd.Prev : Cmd.Next;
                if (kb.pageUpKey.wasPressedThisFrame) return Cmd.PageUp;
                if (kb.pageDownKey.wasPressedThisFrame) return Cmd.PageDown;
                if (!textFocused)
                {
                    if (kb.homeKey.wasPressedThisFrame) return Cmd.Home;
                    if (kb.endKey.wasPressedThisFrame) return Cmd.End;
                    if (kb.spaceKey.wasPressedThisFrame) return Cmd.Activate;
                }
            }
            if (pad != null)
            {
                if (pad.buttonSouth.wasPressedThisFrame) return Cmd.Activate;
                if (pad.buttonEast.wasPressedThisFrame) return Cmd.Back;
                if (pad.leftShoulder.wasPressedThisFrame) return Cmd.TabLeft;
                if (pad.rightShoulder.wasPressedThisFrame) return Cmd.TabRight;
                if (pad.buttonWest.wasPressedThisFrame) return Cmd.Ten;
                if (pad.buttonNorth.wasPressedThisFrame) return Cmd.Fill;
                if (pad.leftTrigger.wasPressedThisFrame) return Cmd.PageUp;
                if (pad.rightTrigger.wasPressedThisFrame) return Cmd.PageDown;
            }
            // the four directions: arrows (Left/Right stay the text's own while you type), D-pad, left stick; held = repeat
            int dir = 0;
            if (kb != null)
            {
                if (kb.upArrowKey.isPressed) dir = (int)Cmd.Up;
                else if (kb.downArrowKey.isPressed) dir = (int)Cmd.Down;
                else if ((!textFocused || arrowsAlways) && kb.leftArrowKey.isPressed) dir = (int)Cmd.Left;
                else if ((!textFocused || arrowsAlways) && kb.rightArrowKey.isPressed) dir = (int)Cmd.Right;
            }
            if (dir == 0 && pad != null)
            {
                Vector2 stick = pad.leftStick.ReadValue();
                if (pad.dpad.up.isPressed || stick.y > Dead) dir = (int)Cmd.Up;
                else if (pad.dpad.down.isPressed || stick.y < -Dead) dir = (int)Cmd.Down;
                else if (pad.dpad.left.isPressed || stick.x < -Dead) dir = (int)Cmd.Left;
                else if (pad.dpad.right.isPressed || stick.x > Dead) dir = (int)Cmd.Right;
            }
            if (dir == 0) { heldDir = 0; return Cmd.None; }
            float now = Time.unscaledTime;
            if (dir != heldDir) { heldDir = dir; nextRepeat = now + Delay; return (Cmd)dir; }
            if (now >= nextRepeat) { nextRepeat = now + Rate; return (Cmd)dir; }
            return Cmd.None;
        }

        private static bool AnyPad(Gamepad pad)
        {
            foreach (var c in pad.allControls)
                if (c is ButtonControl b && b.wasPressedThisFrame) return true;
            var l = pad.leftStick.ReadValue();
            var r = pad.rightStick.ReadValue();
            return l.sqrMagnitude > Dead * Dead || r.sqrMagnitude > Dead * Dead;
        }

        // the next key or button pressed this frame, as an Input System path; Esc is never one (it cancels)
        public static string Captured(out bool cancelled)
        {
            cancelled = false;
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) { cancelled = true; return null; }
            if (kb != null && kb.anyKey.wasPressedThisFrame)
                foreach (var key in kb.allKeys)
                    if (key != null && key.wasPressedThisFrame && key != kb.escapeKey && key != kb.enterKey && key != kb.numpadEnterKey)
                        return "<Keyboard>/" + key.name;
            var pad = Gamepad.current;
            if (pad != null)
                foreach (var c in pad.allControls)
                {
                    if (!(c is ButtonControl b) || !b.wasPressedThisFrame) continue;
                    // a stick pushed far enough reads as a button too (leftStick/up): not a binding anyone means
                    string rel = c.path.Substring(pad.path.Length).TrimStart('/');
                    if (rel == "dpad" || rel.StartsWith("leftStick/") || rel.StartsWith("rightStick/")) continue;
                    return "<Gamepad>/" + rel;
                }
            return null;
        }
    }
}

// GamesOMG Teleport for Lord of Undead: copy a pin on gamesomg.com/lord-of-undead-map/, press F7 in game, you're standing
// beside it. F10 takes you back. A BepInEx 6 IL2CPP plugin (the loader proven on the game 2026-11-27, be.793).
// The game has no coordinate teleport of its own (its hidden dev console has items, exp and god mode, no teleport), so
// this is it. Free, no network calls; the clipboard is read only when you press F7.
using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using UnityEngine;
using Key = UnityEngine.InputSystem.Key;

namespace GamesOMG.LordOfUndeadTeleport
{
    [BepInPlugin(Guid, "GamesOMG Teleport", PluginVersion.Value)]
    public sealed class Plugin : BasePlugin
    {
        public const string Guid = "com.gamesomg.lordofundead.teleport";

        internal ConfigEntry<string> TeleportKey, BackKey;
        internal ConfigEntry<bool> BringSquad, BlockGameInput;
        internal ConfigEntry<float> SquadRadius;
        internal KeyCode TeleportKeyCode, BackKeyCode;
        internal Key TeleportInputKey, BackInputKey;
        internal System.Collections.Generic.List<Place> PlaceList;
        internal new BepInEx.Logging.ManualLogSource Log => base.Log;

        public override void Load()
        {
            TeleportKey = Config.Bind("Keys", "Teleport", "F7",
                "Teleport to the /tp line on your clipboard; with none there, open the box to paste one or type a place. " +
                "A key name: F1-F12, Insert, Home, Keypad0... The game itself uses F1-F4 and F8.");
            BackKey = Config.Bind("Keys", "Back", "F10", "Back to where you stood before your last teleport (press again to swap back).");
            BringSquad = Config.Bind("Teleport", "BringSquad", true, "Minions following you (your current squad) come along.");
            SquadRadius = Config.Bind("Teleport", "SquadRadius", 40f, "How close (metres) a following minion must be to come along.");
            BlockGameInput = Config.Bind("Panel", "BlockGameInput", true,
                "While the box is open the game gets no keyboard or mouse, so typing a place never moves you.");
            TeleportKeyCode = Code(TeleportKey, "F7"); BackKeyCode = Code(BackKey, "F10");
            TeleportInputKey = InputKey(TeleportKeyCode, Key.F7); BackInputKey = InputKey(BackKeyCode, Key.F10);
            if (TeleportKeyCode == BackKeyCode)
            {
                Log.LogWarning("Teleport and Back are the same key; Back goes back to F10");
                BackKeyCode = KeyCode.F10; BackInputKey = Key.F10;
            }

            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("places.tsv"))
            using (var r = new StreamReader(s))
                PlaceList = Places.Parse(r);

            Behaviour.Owner = this;
            AddComponent<Behaviour>();
            Log.LogInfo("GamesOMG Teleport " + PluginVersion.Value + " loaded: " + PlaceList.Count + " places");
        }

        KeyCode Code(ConfigEntry<string> e, string fallback)
        {
            if (Enum.TryParse<KeyCode>(e.Value.Trim(), true, out var k) && k != KeyCode.None) return k;
            Log.LogWarning("key \"" + e.Value + "\" is not a key name; using " + fallback);
            return (KeyCode)Enum.Parse(typeof(KeyCode), fallback);
        }

        static Key InputKey(KeyCode c, Key fallback)
        {
            string n = c.ToString();
            if (n.StartsWith("Alpha")) n = "Digit" + n.Substring(5);
            else if (n.StartsWith("Keypad")) n = "Numpad" + n.Substring(6);
            return Enum.TryParse<Key>(n, true, out var k) ? k : fallback;
        }
    }
}

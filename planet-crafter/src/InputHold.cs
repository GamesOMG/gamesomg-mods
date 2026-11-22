// GamesOMG Teleport 0.2.0: hold the player while one of our windows takes the keyboard, the way the game holds them in
// its own sign-text box (build 26411416, checked 2026-11-19):
//   · WindowsHandler.OpenAndReturnUi sets its private _openedUi; PlayerCanAct.Update then reports GetCanMove() false
//     (single player: no movement update at all) and frees the cursor through CursorStateManager, relocking it when
//     _openedUi is Null again. A sign's text box is UiType.TextInput.
//   · The same call raises onUiWindowEvent and WindowsGamepadHandler switches the gameplay map off. Setting only the
//     marker (akarnokd's Command Console does, then Harmony-patches the emote and chat handlers) leaves that map live:
//     1-7 are Emote1-7, Enter is OpenChatWindow, R and T rotate. So this also switches off every enabled action in the
//     game's input asset and switches exactly those back on when it ends.
// No Harmony patch; one private field read by name. Without it, Begin refuses and the caller falls back.
using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using SpaceCraft;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GamesOMG.PlanetCrafterTeleport
{
    internal sealed class InputHold
    {
        private static readonly FieldInfo OpenedUi =
            typeof(WindowsHandler).GetField("_openedUi", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly ManualLogSource log;
        private readonly List<InputAction> disabled = new List<InputAction>();
        private WindowsHandler windows;
        private PlayerMainController player;
        private bool markerSet, cursorWasLocked;

        public InputHold(ManualLogSource log) { this.log = log; }

        public bool Active { get; private set; }
        public PlayerMainController Player => player;
        public static bool Available => OpenedUi != null && OpenedUi.FieldType == typeof(DataConfig.UiType);

        public bool Begin(PlayerMainController who)
        {
            if (Active) return true;
            var handler = SafeManager<WindowsHandler>();
            if (handler == null || who == null || !Available)
            {
                if (!Available) log.LogWarning("Input hold unavailable: WindowsHandler._openedUi not found in this build.");
                return false;
            }
            windows = handler;
            player = who;
            // 0.3.0 (Codex C61 F8): all or nothing. A failure part way (a game action that won't switch off) undoes what was
            // done, so the player is never left half held: marker set, some keys off, no panel to close it.
            try
            {
                cursorWasLocked = Cursor.lockState == CursorLockMode.Locked;
                OpenedUi.SetValue(windows, DataConfig.UiType.TextInput);
                markerSet = true;
                Active = true;
                Keep();
                SetCursorLocked(false);
            }
            catch (Exception error)
            {
                log.LogWarning("Input hold could not start (" + error.GetType().Name + "); undone.");
                if (Active) End("the hold could not start");
                windows = null;
                player = null;
                return false;
            }
            log.LogInfo("Input held: " + disabled.Count + " game actions off.");
            return true;
        }

        // Each frame while held: anything the game switched back on goes off again. Returns why the hold must end (the
        // world changed, or a game window replaced our marker), or null while all is well.
        public string Check()
        {
            if (!Active) return null;
            var handler = SafeManager<WindowsHandler>();
            if (handler == null || handler != windows || player == null || !player) return "the world changed";
            DataConfig.UiType opened;
            try { opened = handler.GetOpenedUi(); }
            catch (Exception) { return "the window handler went away"; }
            if (opened != DataConfig.UiType.TextInput)
            {
                markerSet = false;   // a game window replaced or cleared it: not ours to clear now
                return "a game window opened (" + opened + ")";
            }
            Keep();
            return null;
        }

        private void Keep()
        {
            foreach (var asset in GameInputAssets())
                foreach (var map in asset.actionMaps)
                    foreach (var action in map.actions)
                    {
                        if (!action.enabled) continue;
                        action.Disable();
                        if (!disabled.Contains(action)) disabled.Add(action);
                    }
        }

        private IEnumerable<InputActionAsset> GameInputAssets()
        {
            var assets = new List<InputActionAsset>();
            try
            {
                var dispatcher = player != null ? player.GetComponent<PlayerInputDispatcher>() : null;
                if (dispatcher != null && dispatcher.inputMap != null) assets.Add(dispatcher.inputMap);
                var gamepad = SafeManager<WindowsGamepadHandler>();
                if (gamepad != null && gamepad.InputMap != null && !assets.Contains(gamepad.InputMap)) assets.Add(gamepad.InputMap);
            }
            catch (Exception error) { log.LogWarning("Game input lookup failed: " + error.GetType().Name); }
            return assets;
        }

        public void End(string why)
        {
            if (!Active) return;
            Active = false;
            var gameOwns = GameWindowActions();
            try
            {
                // clear the marker only while it is still ours: a game window that took over keeps its own
                if (markerSet && windows != null && windows && windows.GetOpenedUi() == DataConfig.UiType.TextInput)
                    OpenedUi.SetValue(windows, DataConfig.UiType.Null);
            }
            catch (Exception error) { log.LogWarning("Input hold marker cleanup: " + error.GetType().Name); }
            markerSet = false;
            int restored = 0, left = 0;
            foreach (var action in disabled)
            {
                if (gameOwns.Contains(action)) { left++; continue; }
                try { action.Enable(); restored++; }
                catch (Exception error) { log.LogWarning("Could not switch a game key back on: " + error.GetType().Name); }
            }
            if (left > 0) log.LogInfo("Input: " + left + " game actions left to the game's open window.");
            disabled.Clear();
            try
            {
                bool gameUiOpen = windows != null && windows && windows.GetHasUiOpen();
                if (cursorWasLocked && !gameUiOpen) SetCursorLocked(true);
            }
            catch (Exception error) { log.LogWarning("Input hold cursor restore: " + error.GetType().Name); }
            windows = null;
            player = null;
            GUIUtility.keyboardControl = 0;
            log.LogInfo("Input released (" + why + "); " + restored + " game actions back on.");
        }

        // 0.3.0 (Codex C61 F8): a game window that replaced our marker runs the game's own input policy
        // (WindowsGamepadHandler.OnUiWindowEvent, build 26411416: open = map 0 "PlayerMovable" off, the actions it lists as
        // active in windows on, map 1 "UI" on; close = the reverse). Those two maps are the game's until that window
        // closes; switching map 0 back on under an open inventory is what 0.2.7 did. Empty when no game window is open.
        private HashSet<InputAction> GameWindowActions()
        {
            var owned = new HashSet<InputAction>();
            try
            {
                if (windows == null || !windows) return owned;
                var open = windows.GetOpenedUi();
                if (open == DataConfig.UiType.Null || open == DataConfig.UiType.TextInput) return owned;
                var gamepad = SafeManager<WindowsGamepadHandler>();
                if (gamepad == null || gamepad.InputMap == null) return owned;
                var maps = gamepad.InputMap.actionMaps;
                for (int i = 0; i < maps.Count && i < 2; i++)
                    foreach (var action in maps[i].actions) owned.Add(action);
            }
            catch (Exception error) { log.LogWarning("Game window input lookup failed: " + error.GetType().Name); }
            return owned;
        }

        private static void SetCursorLocked(bool locked)
        {
            try { CursorStateManager.SetLockCursorStatus(locked); }
            catch (Exception)
            {
                // GamepadConfig is gone during teardown; set the same state the game's helper would
                Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
                Cursor.visible = !locked;
            }
        }

        public static T SafeManager<T>() where T : MonoBehaviour
        {
            try
            {
                var manager = Managers.GetManager<T>();
                return manager != null && manager ? manager : null;
            }
            catch (Exception) { return null; }
        }
    }
}

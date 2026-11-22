// GamesOMG Teleport: one configurable key. The Input System's player initialisation runs after BepInEx's chainloader
// and leaves an action enabled in Awake disabled (build 26411416, in game 2026-11-17: the shortcut never fired), so it is
// enabled lazily from Update. If the bound action misses a press the keyboard itself saw, the control at the bound path
// on any device counts too (0.1.1). Shared by the teleport key (F7) and the panel key (0.2.0).
using System;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace GamesOMG.PlanetCrafterTeleport
{
    internal sealed class HotKey : IDisposable
    {
        private readonly ConfigEntry<string> path;
        private readonly ManualLogSource log;
        private readonly string name;
        private InputAction action;
        private bool logged;

        public HotKey(string name, ConfigEntry<string> path, ManualLogSource log)
        {
            this.name = name;
            this.path = path;
            this.log = log;
            path.SettingChanged += (_, __) => Bind();
            Bind();
        }

        public string Path => path.Value;

        public string Label => KeyCheck.Label(path?.Value);

        // 0.3.0: a controller binding may be empty ("none"): no action, never a press
        private bool Unset => KeyCheck.Empty(path?.Value);

        private void Bind()
        {
            action?.Disable();
            action?.Dispose();
            action = null;
            logged = false;
            if (Unset) return;
            try { action = new InputAction("GamesOMG" + name, InputActionType.Button, path.Value); action.Enable(); }
            catch (Exception error) { log.LogWarning(name + " key binding was rejected: " + error.GetType().Name); }
        }

        // call once per frame, first thing in Update
        public void Tick()
        {
            if (action != null && !action.enabled) action.Enable();
            if (action != null && action.enabled && !logged)
            {
                logged = true;
                log.LogInfo(name + " key " + path.Value + " is on, matched to " + action.controls.Count + " control(s).");
            }
        }

        public bool Pressed(out string via)
        {
            via = null;
            if (Unset) return false;
            if (action != null && action.WasPressedThisFrame()) { via = "action"; return true; }
            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.anyKey.wasPressedThisFrame) return false;
            try
            {
                using (var found = InputSystem.FindControls(path.Value))
                {
                    for (int i = 0; i < found.Count; i++)
                    {
                        if (found[i] is ButtonControl button && button.wasPressedThisFrame)
                        {
                            via = "device " + found[i].device.displayName;
                            log.LogInfo("The " + name + " action missed a press that " + via + " saw; using it.");
                            return true;
                        }
                    }
                }
            }
            catch (Exception error) { log.LogWarning(name + " key lookup failed: " + error.GetType().Name); }
            return false;
        }

        public void Dispose()
        {
            action?.Disable();
            action?.Dispose();
            action = null;
        }
    }
}

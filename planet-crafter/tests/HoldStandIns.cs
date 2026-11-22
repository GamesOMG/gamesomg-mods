// GamesOMG Teleport 0.3.0 (Codex C61 F8): just enough of BepInEx, Unity and the game for src/InputHold.cs to compile and run
// here, no game assembly loaded. The window policy below is the game's own, as it behaves on build 26411416:
//   WindowsHandler.OpenAndReturnUi: CloseAllWindows (marker to Null), marker = the window, onUiWindowEvent(true)
//   WindowsHandler.CloseAllWindows: each open window's onUiWindowEvent(false), marker = Null
//   WindowsGamepadHandler.OnUiWindowEvent(true):  actionMaps[0].Disable(); each _gameplayInputsActiveInUI.Enable(); actionMaps[1].Enable()
//   WindowsGamepadHandler.OnUiWindowEvent(false): actionMaps[1].Disable(); actionMaps[0].Enable()
//   WindowsGamepadHandler.Start: actionMaps[0].Enable(); actionMaps[1].Disable()   (map 0 = PlayerMovable, map 1 = UI)
// Nothing else of these classes is modelled; a test that needs more adds it here, matching the game's behaviour.
using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace BepInEx.Logging
{
    public class ManualLogSource
    {
        public readonly List<string> Lines = new List<string>();
        public void LogWarning(object value) => Lines.Add("W " + value);
        public void LogInfo(object value) => Lines.Add("I " + value);
    }
}

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;   // Unity's "fake null": a destroyed object compares true to null through the bool operator
        public static implicit operator bool(Object o) => !ReferenceEquals(o, null) && !o.Destroyed;
    }

    public class MonoBehaviour : Object
    {
        public readonly Dictionary<Type, object> Components = new Dictionary<Type, object>();
        public T GetComponent<T>() where T : class => Components.TryGetValue(typeof(T), out var c) ? (T)c : null;
    }

    public enum CursorLockMode { None, Locked, Confined }

    public static class Cursor
    {
        public static CursorLockMode lockState = CursorLockMode.Locked;
        public static bool visible;
    }

    public static class GUIUtility { public static int keyboardControl; }
}

namespace UnityEngine.InputSystem
{
    public class InputAction
    {
        public readonly string name;
        public bool ThrowOnDisable;   // a test's planted failure
        public InputAction(string name) { this.name = name; }
        public bool enabled { get; private set; }
        public void Enable() => enabled = true;
        public void Disable()
        {
            if (ThrowOnDisable) throw new InvalidOperationException("stand-in: " + name + " would not switch off");
            enabled = false;
        }
        public override string ToString() => name;
    }

    public class InputActionMap
    {
        public readonly string name;
        public readonly List<InputAction> actions = new List<InputAction>();
        public InputActionMap(string name, params InputAction[] all) { this.name = name; actions.AddRange(all); }
        public void Enable() { foreach (var a in actions) a.Enable(); }
        public void Disable() { foreach (var a in actions) a.Disable(); }
    }

    public class InputActionAsset
    {
        public readonly List<InputActionMap> actionMaps = new List<InputActionMap>();
        public InputActionAsset(params InputActionMap[] maps) { actionMaps.AddRange(maps); }
    }
}

namespace SpaceCraft
{
    public static class DataConfig
    {
        public enum UiType { Null, TextInput, Inventory, Pause }
    }

    public class WindowsHandler : UnityEngine.MonoBehaviour
    {
        private DataConfig.UiType _openedUi;
        public WindowsGamepadHandler Pad;
        public DataConfig.UiType GetOpenedUi() => _openedUi;
        public bool GetHasUiOpen() => _openedUi != DataConfig.UiType.Null;

        public void OpenAndReturnUi(DataConfig.UiType ui)
        {
            CloseAllWindows();
            _openedUi = ui;
            Pad?.OnUiWindowEvent(true);
        }

        public void CloseAllWindows()
        {
            // only a real window raises the event: our TextInput marker has no window object behind it
            if (_openedUi != DataConfig.UiType.Null && _openedUi != DataConfig.UiType.TextInput) Pad?.OnUiWindowEvent(false);
            _openedUi = DataConfig.UiType.Null;
        }
    }

    public class WindowsGamepadHandler : UnityEngine.MonoBehaviour
    {
        public InputActionAsset InputMap;
        public readonly List<InputAction> GameplayInputsActiveInUI = new List<InputAction>();

        public void Start()
        {
            InputMap.actionMaps[0].Enable();
            InputMap.actionMaps[1].Disable();
        }

        public void OnUiWindowEvent(bool isOpen)
        {
            if (isOpen)
            {
                InputMap.actionMaps[0].Disable();
                foreach (var a in GameplayInputsActiveInUI) a.Enable();
                InputMap.actionMaps[1].Enable();
                return;
            }
            InputMap.actionMaps[1].Disable();
            InputMap.actionMaps[0].Enable();
        }
    }

    public class PlayerInputDispatcher : UnityEngine.MonoBehaviour { public InputActionAsset inputMap; }

    public class PlayerMainController : UnityEngine.MonoBehaviour { }

    public static class CursorStateManager
    {
        public static void SetLockCursorStatus(bool isLocked)
        {
            UnityEngine.Cursor.lockState = isLocked ? UnityEngine.CursorLockMode.Locked : UnityEngine.CursorLockMode.None;
            UnityEngine.Cursor.visible = !isLocked;
        }
    }

    public static class Managers
    {
        public static readonly Dictionary<Type, UnityEngine.MonoBehaviour> Items = new Dictionary<Type, UnityEngine.MonoBehaviour>();
        public static T GetManager<T>() where T : UnityEngine.MonoBehaviour => Items.TryGetValue(typeof(T), out var m) ? (T)m : null;
    }
}

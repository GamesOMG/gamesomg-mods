// GamesOMG Item Spawner for The Planet Crafter (build 26411416, v2.104): search any item by its in-game name and put it straight
// into your backpack. Built after the spawn mods we tried fell short (ids only, items dropped on the
// ground where they roll down hills, or broken on this build): items here go straight into the backpack.
//   · F6 (keyboard) or LB + RB (controller) opens the panel; both are settings in gamesomg.planetcrafter.spawn.cfg
//   · items go in through the game's own backpack path (Spawner.cs); nothing is dropped; achievements stay on
//   · the keys are enabled lazily (HotKey): an InputAction enabled in Awake is switched off by the game's input init
using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using SpaceCraft;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GamesOMG.PlanetCrafterSpawn
{
    [BepInPlugin(Guid, "GamesOMG Item Spawner", PluginVersion.Value)]
    public sealed class Plugin : BaseUnityPlugin, IPanelHost
    {
        public const string Guid = "gamesomg.planetcrafter.spawn";
        public const string TestedBuild = "26411416 (v2.104)";
        private const int RecentMax = 24;

        private ConfigEntry<string> key, padKey, recent;
        private ConfigEntry<int> textSize, lastAmount;
        private ConfigEntry<bool> firstRunShown;
        private HotKey openKey;
        private PadChord chord;
        private Panel panel;
        private Spawner spawner;
        private string message;
        private bool messageBad;
        private float messageUntil;
        private PlayerMainController checkedFor;
        private GUIStyle noticeStyle;
        private float noticeFor = -1;

        private void Awake()
        {
            key = Config.Bind("Controls", "Key", "<Keyboard>/f6",
                "Opens and closes the spawn panel. An Input System path: <Keyboard>/f6, <Keyboard>/insert, <Keyboard>/f5 … "
                + "F6 is free in the game (build 26411416), in GamesOMG Teleport (F3, F7, F10) and in Spawn Object (End).");
            padKey = Config.Bind("Controls", "PadKey", "<Gamepad>/leftShoulder + <Gamepad>/rightShoulder",
                "Opens and closes the panel on a controller: buttons joined by + are pressed together (LB + RB by default). Empty = none.");
            textSize = Config.Bind("Appearance", "TextSize", 100, new ConfigDescription("Text and panel size, percent.", new AcceptableValueRange<int>(70, 200)));
            lastAmount = Config.Bind("State", "Amount", 1, "The amount last typed in the panel. Leave it alone.");
            recent = Config.Bind("State", "Recent", "", "The items you spawned last, newest first (the Recent tab). Leave it alone.");
            firstRunShown = Config.Bind("State", "FirstRunShown", false, "The one-time 'press F6' notice has been shown.");
            // the mod's own words follow the game's language (Strings.cs); a language without a table falls back to English
            L.Language = Catalog.CurrentLanguage;
            openKey = new HotKey("Spawn", key, Logger);
            chord = new PadChord(() => padKey.Value);
            spawner = new Spawner(Logger);
            panel = new Panel(this, Logger);
            Logger.LogInfo("GamesOMG Item Spawner " + PluginVersion.Value + " loaded (tested on game build " + TestedBuild + "). Key " + KeyLabel + ", controller " + PadLabel(false) + ".");
        }

        private void OnDestroy()
        {
            panel?.Hide("the mod unloaded");
            openKey?.Dispose();
        }

        private void Update()
        {
            openKey.Tick();
            bool pressed = openKey.Pressed(out var via);
            bool padPressed = chord.Pressed();
            if (panel.Open)
            {
                panel.Update(pressed || padPressed);
                return;
            }
            if (Time.frameCount % 30 == 0) WorldCheck();
            if (!pressed && !padPressed) return;
            if (!Application.isFocused) { Logger.LogInfo("Key seen while the game is not focused: ignored."); return; }
            Logger.LogInfo("Open pressed (" + (padPressed ? "controller " + PadLabel(false) : via) + ").");
            OpenPanel(padPressed);
        }

        private void OpenPanel(bool viaPad)
        {
            if (spawner.Busy) { Notice(L.T("spawn.busy"), 3f, false); return; }
            if (!TryContext(out var player, out string blocked))
            {
                Notice(blocked ?? L.T("spawn.notInWorld", KeyLabel), 5f, true);
                return;
            }
            string why = Spawner.Blocked();
            if (why != null) { Notice(L.T(why, KeyLabel), 5f, true); return; }
            Inventory backpack = null;
            try { backpack = player.GetPlayerBackpack()?.GetInventory(); } catch (Exception) { }
            if (backpack == null) { Notice(L.T("spawn.notInWorld", KeyLabel), 5f, true); return; }
            if (Catalog.Items(Logger).Count == 0) { Notice(L.T("spawn.noItems"), 5f, true); return; }
            message = null;
            if (!panel.Show(player, backpack, viaPad)) Notice(L.T("spawn.cantOpen"), 5f, true);
        }

        // a world with this player in it, and no game window open (the inventory, a machine, the pause menu, a sign's text)
        private bool TryContext(out PlayerMainController player, out string blocked)
        {
            player = null;
            blocked = null;
            try
            {
                var players = Managers.GetManager<PlayersManager>();
                var planets = Managers.GetManager<PlanetLoader>();
                var windows = Managers.GetManager<WindowsHandler>();
                if (players == null || planets == null) return false;
                player = players.GetActivePlayerController();
                if (player == null || !player.IsOwner || !player.IsSpawned || planets.GetCurrentPlanetData() == null) return false;
                if (windows != null && windows.GetHasUiOpen()) { blocked = L.T("spawn.menuOpen", KeyLabel); return false; }
                var canAct = player.GetPlayerCanAct();
                return canAct != null && canAct.GetCanMoveAfterStart();
            }
            catch (MissingReferenceException) { return false; }
            catch (NullReferenceException) { return false; }
        }

        // once per world entered: the first-run tip, and the keys checked against the game's own bindings (with the player's
        // rebinds) so a clash is said, not discovered
        private void WorldCheck()
        {
            if (!TryContext(out var player, out _) || player == checkedFor) return;
            checkedFor = player;
            var game = GameBindings();
            var ours = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("Key", key.Value) };
            var clashes = KeyCheck.Find(ours, game);
            var padClashes = Chord.Clashes(padKey.Value, game);
            Logger.LogInfo("Keys checked against " + game.Count + " game bindings: " + KeyLabel + (clashes.Count == 0 ? " free" : " also " + clashes[0].With)
                + "; " + PadLabel(false) + (padClashes.Count == 0 ? " free together" : " also " + string.Join(", ", padClashes.ToArray())) + ".");
            string language = Catalog.CurrentLanguage();
            Logger.LogInfo("Game language " + language + ": the mod's own words " + (L.Translated(language) ? "in it" : "in English (no table for it)") + ".");
            if (clashes.Count > 0) { Notice(L.T("clash.key", KeyLabel, GameAction(clashes[0].With)), 12f, true); return; }
            if (padClashes.Count > 0) { Notice(L.T("clash.pad", PadLabel(false), GameAction(padClashes[0])), 12f, true); return; }
            if (!firstRunShown.Value)
            {
                firstRunShown.Value = true;
                string pad = Chord.Parts(padKey.Value).Count > 0 ? " " + L.T("tip.pad", PadLabel(false)) : "";
                Notice(L.T("tip.first", KeyLabel) + pad, 12f, false);
            }
        }

        // the game's own name for one of its actions, as its Options key list says it, in its language
        private static string GameAction(string action)
        {
            string s = null;
            try { s = Localization.GetLocalizedString("Menu_Options_Keys_" + action); } catch (Exception) { }
            return string.IsNullOrEmpty(s) ? KeyCheck.Words(action) : s;
        }

        private static List<GameBinding> GameBindings()
        {
            var list = new List<GameBinding>();
            try
            {
                var assets = new List<InputActionAsset>();
                var player = Managers.GetManager<PlayersManager>()?.GetActivePlayerController();
                var dispatcher = player != null ? player.GetComponent<PlayerInputDispatcher>() : null;
                if (dispatcher != null && dispatcher.inputMap != null) assets.Add(dispatcher.inputMap);
                var gamepad = InputHold.SafeManager<WindowsGamepadHandler>();
                if (gamepad != null && gamepad.InputMap != null && !assets.Contains(gamepad.InputMap)) assets.Add(gamepad.InputMap);
                foreach (var asset in assets)
                    foreach (var map in asset.actionMaps)
                        foreach (var action in map.actions)
                            foreach (var b in action.bindings)
                                if (!b.isComposite) list.Add(new GameBinding { Map = map.name, Action = action.name, Path = b.effectivePath ?? "" });
            }
            catch (Exception) { }
            return list;
        }

        // ── IPanelHost ──
        public bool Busy => spawner.Busy;
        public string KeyLabel => KeyCheck.Label(key.Value);
        public string PadLabel(bool playStation) => Chord.Label(padKey.Value, playStation);
        public bool ChordHeld() => chord.Held();
        public float TextSize => textSize.Value;
        public int LastAmount { get => SpawnPlan.Clamp(lastAmount.Value); set => lastAmount.Value = SpawnPlan.Clamp(value); }
        public string GameBuild => TestedBuild;

        public IList<string> Recent
        {
            get
            {
                var list = new List<string>();
                foreach (var id in (recent.Value ?? "").Split(',')) if (id.Trim().Length > 0 && !list.Contains(id.Trim())) list.Add(id.Trim());
                return list;
            }
        }

        public string LastId { get { var r = Recent; return r.Count > 0 ? r[0] : null; } }

        private void Remember(string id)
        {
            var list = (List<string>)Recent;
            list.Remove(id);
            list.Insert(0, id);
            if (list.Count > RecentMax) list.RemoveRange(RecentMax, list.Count - RecentMax);
            recent.Value = string.Join(",", list.ToArray());
        }

        void IPanelHost.Spawn(ItemEntry item, Inventory backpack, int amount, bool fill)
        {
            if (!(item?.Group is Group group) || backpack == null || spawner.Busy) return;
            string why = Spawner.Blocked();
            if (why != null) { panel.Say(L.T(why, KeyLabel), true); return; }
            var plan = new SpawnPlan(amount, fill);
            StartCoroutine(spawner.Run(group, backpack, plan, done =>
            {
                if (done.Added > 0) Remember(item.Id);
                if (panel.Open) panel.Result(done, item);
                else Notice(L.T(done.ResultId(), done.Added, item.Name, done.DidNotFit), 5f, done.Full || done.Failed);
            }));
        }

        // ── the notice card (outside the panel): below the top, so GamesOMG Teleport's notices never sit on it ──
        private void Notice(string text, float seconds, bool bad)
        {
            message = text;
            messageBad = bad;
            messageUntil = Time.realtimeSinceStartup + seconds;
        }

        private void OnGUI()
        {
            panel.OnGUI();
            if (panel.Open || string.IsNullOrEmpty(message) || Time.realtimeSinceStartup > messageUntil) return;
            var t = panel.Theme;
            t.Build(textSize.Value);
            if (!Mathf.Approximately(noticeFor, t.S))
            {
                noticeFor = t.S;
                noticeStyle = new GUIStyle(t.Text) { wordWrap = true };
            }
            float w = Mathf.Min(Screen.width - t.Px(32), t.Px(560));
            float textW = w - t.Px(62);
            float h = Mathf.Max(t.Px(48), noticeStyle.CalcHeight(new GUIContent(message), textW) + t.Px(24));
            var r = new Rect(Mathf.Round((Screen.width - w) / 2), Mathf.Round(Screen.height * .2f), w, h);
            t.Plate(r, Theme.Panel, t.Px(8));
            t.Outline(r, messageBad ? Theme.Bad : Theme.Line, Mathf.Max(1, t.Px(messageBad ? 2 : 1)), t.Px(8));
            var mark = t.Mark;
            if (mark != null && Event.current.type == EventType.Repaint) GUI.DrawTexture(new Rect(r.x + t.Px(14), r.y + (h - t.Px(28)) / 2, t.Px(28), t.Px(28)), mark, ScaleMode.ScaleToFit);
            GUI.Label(new Rect(r.x + t.Px(52), r.y + t.Px(12), textW, h - t.Px(24)), message, noticeStyle);
        }
    }
}

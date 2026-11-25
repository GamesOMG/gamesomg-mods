// ChatGPT C56: one local hotkey and the native teleport path; no patches, RPCs or game DLL redistribution.
// 0.2.0 (2026-11-19): a places panel on its own key; F7 with nothing to go to opens it.
// 0.2.6: places kept per save; Back on F10; the version is the csproj's <Version>, one source (PluginVersion.Value).
// 0.3.0 (2026-11-19, the look, controls and settings pass): the panel rebuilt (Panel.cs), a
// settings screen over this same .cfg (Prefs), a controller button for Places (View/Share: the one button the game leaves
// free in play, read from its own PlayerActions), "Back" renamed "Return to previous spot" and naming where it goes, a
// replayable first-run tip using the real binding, refusals that reopen the panel on the same place, optional sounds
// (always with text), Copy diagnostics, and every line from Strings.cs.
// 0.3.2 (player feedback 2026-11-21 08:19, "we can do it now" 08:33): compact rows and one-click teleport are the defaults again, and
// a .cfg that still holds 0.3.0/0.3.1's untouched defaults moves onto them once (State.ConfigVersion); a refused landing logs
// what the ground ray met.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using SpaceCraft;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GamesOMG.PlanetCrafterTeleport
{
    [BepInPlugin("gamesomg.planetcrafter.teleport", "GamesOMG Teleport", PluginVersion.Value)]
    public sealed class Plugin : BaseUnityPlugin, IPanelHost
    {
        private HotKey teleportKey, panelKey, backKey, padPanelKey, padBackKey;
        private Panel panel;
        private bool busy;
        private string message;
        private bool messageBad, messageTip;
        private float messageUntil;
        private string boundSave;   // 0.2.6: the save the places store was last bound to (null: none yet)
        private uint boundSeed;     // 0.3.0 (Codex C61 F5): and its world seed; a new world under the same name binds again
        private readonly Diagnostics diag = new Diagnostics();
        private Look look;
        private Prefs prefs;
        private readonly Dictionary<string, ConfigEntry<string>> keys = new Dictionary<string, ConfigEntry<string>>(StringComparer.Ordinal);
        private ConfigEntry<int> textSize, opacity;
        private ConfigEntry<string> side, rows, rowClick;
        private ConfigEntry<bool> reducedMotion, closeAfter, sounds, firstRunShown;
        private ConfigEntry<int> configVersion;
        private AudioSource audio;
        private AudioClip okClip, noClip;
        private bool reopenAfter;   // the teleport came from the panel and "Close after teleport" is off

        private void Awake()
        {
            prefs = new Prefs();
            // F7, not F6: Cheat Menu 2 saves your position on F6 and akarnokd's translation library reloads on it.
            keys[Panel.ActTeleport] = Key(Prefs.Controls, "TeleportKey", "<Keyboard>/f7",
                "Teleport from the clipboard: reads one /tp x y z line you copied (from the GamesOMG map's Copy teleport). Nothing valid copied: it opens a box to paste into. F7 is unbound in build 26411416's PlayerActions and unused by the Planet Crafter mods we checked.");
            // F3, not F8: the game's own F8 opens its feedback form (ShowFeedback, Steam). F3 is unbound in build 26411416.
            keys[Panel.ActPlaces] = Key(Prefs.Controls, "PanelKey", "<Keyboard>/f3",
                "Opens and closes Places: Save here, your places, the map's places for this planet, Return, and Settings.");
            // F10 (0.2.6): unbound in the game's 57 keyboard bindings, and not F3/F7 (ours), F5, F6, F11 (other mods) or F12 (Steam)
            keys[Panel.ActReturn] = Key(Prefs.Controls, "BackKey", "<Keyboard>/f10",
                "Return to previous spot: back to where you stood before your last teleport on this planet, through the same landing checks. Press it again to swap back (two spots, not a history).");
            // 0.3.0: the controller. View (Xbox) / Share (PlayStation) is the one button build 26411416 binds to nothing in play
            // (tests/bindings-all.json); not a chord, not a hold. Change it here or in the panel's Settings > Controls.
            keys[Panel.ActPadPlaces] = Key(Prefs.Controls, "PadPanelButton", "<Gamepad>/select",
                "Controller button that opens and closes Places. Default: View (Xbox) / Share (PlayStation), the one button the game leaves free while you play. Empty = none.");
            keys[Panel.ActPadReturn] = Key(Prefs.Controls, "PadReturnButton", "",
                "Controller button for Return to previous spot. Empty by default: every other button already does something in the game.");
            textSize = Bind(Prefs.Appearance, "TextSize", 100, "Text and panel size, in percent (70-160).", new AcceptableValueRange<int>(70, 160));
            side = Bind(Prefs.Appearance, "Side", "Right", "Which side of the screen the panel opens on.", new AcceptableValueList<string>("Right", "Left"));
            rows = Bind(Prefs.Appearance, "Rows", "Compact", "Row height: Compact (32 px at 1080p, the default) or Comfortable (44 px, two lines per place).", new AcceptableValueList<string>("Comfortable", "Compact"));
            opacity = Bind(Prefs.Appearance, "BackgroundOpacity", 96, "How solid the panel's background is, in percent (85-100). Text and focus always stay solid.", new AcceptableValueRange<int>(Theme.MinOpacity, 100));
            reducedMotion = Bind(Prefs.Appearance, "ReducedMotion", false, "No slide-in: the panel appears in place.", null);
            rowClick = Bind(Prefs.Behaviour, "RowClick", "Teleport", "Clicking a place: Teleport (at once, the default; right-click selects it) or Select (then Teleport, or click it again). Enter / A always teleports.", new AcceptableValueList<string>("Select", "Teleport"));
            closeAfter = Bind(Prefs.Behaviour, "CloseAfterTeleport", true, "Close Places after a teleport. Off: it opens again where you land.", null);
            sounds = Bind(Prefs.Behaviour, "Sounds", false, "Short sounds for done and refused, always with the text line.", null);
            firstRunShown = Bind(Prefs.State, "FirstRunShown", false, "The first-run tip has been shown. Settings > Behaviour shows it again.", null);
            // 0.3.2: BepInEx wrote 0.3.0/0.3.1's defaults into the .cfg on their first run, so a new default never reaches
            // anyone who ran them. Once per .cfg: a setting still on the OLD default moves to the new one; anything else stays.
            configVersion = Config.Bind(Prefs.State, "ConfigVersion", 0, "Which version's defaults this file has been moved onto (0.3.2 = 2). Leave it alone.");
            foreach (var line in Prefs.Migrate(configVersion.Value, id => prefs.Find(id)?.Get(), (id, v) => prefs.Find(id)?.Set(v)))
                Logger.LogInfo("Settings: " + line);
            if (configVersion.Value < Prefs.ConfigVersion) configVersion.Value = Prefs.ConfigVersion;
            look = new Look
            {
                TextSize = () => textSize.Value, Opacity = () => opacity.Value,
                Left = () => side.Value == "Left", Compact = () => rows.Value == "Compact", ReducedMotion = () => reducedMotion.Value,
                QuickTeleport = () => rowClick.Value == "Teleport", CloseAfter = () => closeAfter.Value, Sounds = () => sounds.Value,
                Set = (id, value) => { var p = prefs.Find(id); if (p != null) p.Set(value); },
            };
            teleportKey = new HotKey("Teleport", keys[Panel.ActTeleport], Logger);
            panelKey = new HotKey("Panel", keys[Panel.ActPlaces], Logger);
            backKey = new HotKey("Return", keys[Panel.ActReturn], Logger);
            padPanelKey = new HotKey("Panel (controller)", keys[Panel.ActPadPlaces], Logger);
            padBackKey = new HotKey("Return (controller)", keys[Panel.ActPadReturn], Logger);
            var library = new Library(Path.Combine(Paths.ConfigPath, "gamesomg.planetcrafter.teleport.places.json"),
                Path.Combine(Paths.ConfigPath, "gamesomg-teleport"), prefs, line => Logger.LogWarning(line));
            panel = new Panel(Logger, library, this, Path.Combine(Paths.CachePath, "gamesomg-teleport"));
        }

        private ConfigEntry<string> Key(string section, string key, string def, string about)
        {
            var e = Config.Bind(section, key, def, about);
            prefs.Add(section, key, def, () => e.Value ?? "", v => e.Value = v ?? "");
            return e;
        }

        private ConfigEntry<T> Bind<T>(string section, string key, T def, string about, AcceptableValueBase range)
        {
            var e = Config.Bind(section, key, def, new ConfigDescription(about, range));
            prefs.Add(section, key, TomlTypeConverter.ConvertToString(def, typeof(T)), e.GetSerializedValue, e.SetSerializedValue);
            return e;
        }

        private void Update()
        {
            teleportKey.Tick(); panelKey.Tick(); backKey.Tick(); padPanelKey.Tick(); padBackKey.Tick();
            bool tp = teleportKey.Pressed(out var via), pk = panelKey.Pressed(out var viaPanel) | padPanelKey.Pressed(out _);
            bool bk = backKey.Pressed(out var viaBack) | padBackKey.Pressed(out _);
            if (panel.Open)
            {
                if (bk) { Logger.LogInfo("Return pressed in the panel (" + viaBack + ")."); GoBack(true); return; }
                panel.Update(tp || pk);
                return;
            }
            // 0.1.1 (2026-11-19: F7 seemed to do nothing): a silent mod looks like a missing one. 0.3.0: the first time, the
            // first-run tip with the real keys; after that a short line on each save you load (when the places move to it).
            if (Time.frameCount % 30 == 0 && TryContext(out _, out _, out _))
            {
                string said = BindSave(out bool changed);
                if (changed)
                {
                    Logger.LogInfo("Player is in a world: ready notice shown.");
                    if (!firstRunShown.Value) FirstRunTip(said);
                    else Notice((said != null ? said + "\n" : "") + L.T("ready.short", KeyLabel(Panel.ActPlaces), KeyLabel(Panel.ActTeleport)), said != null ? 10f : 4f);
                }
            }
            if (bk)
            {
                if (!Application.isFocused) return;
                Logger.LogInfo("Return pressed (" + viaBack + ").");
                GoBack(false);
                return;
            }
            if (pk)
            {
                if (!Application.isFocused) return;
                Logger.LogInfo("Panel key pressed (" + viaPanel + ").");
                OpenPanel(null, null, false);
                return;
            }
            if (!tp) return;
            if (!Application.isFocused) { Logger.LogInfo("Key seen (" + via + ") while the game is not focused: ignored."); return; }
            if (busy) { Notice(L.T("tp.preparing")); return; }
            // The clipboard is read only on this press. Its text is never logged or stored; only its shape.
            string clip = null;
            try { clip = GUIUtility.systemCopyBuffer; }
            catch (Exception error) { Logger.LogWarning("Clipboard unavailable: " + error.GetType().Name); }
            if (!TeleportRequest.TryParse(clip, out var request))
            {
                Logger.LogInfo("Key pressed (" + via + "): the clipboard is not one /tp command (" + Shape(clip) + ").");
                // 0.1.2 (2026-11-19: a box to paste into was expected): nothing to go to
                // opens the paste box with the reason. A copied /tp near-miss or map link goes in filled.
                bool link = SpotSet.LooksLikeLink(clip) || FoundSet.LooksLikeLink(clip);
                bool nearMiss = clip != null && clip.Length <= 60000 && clip.Trim().IndexOf('\n') < 0
                    && (link || clip.TrimStart().StartsWith("/tp", StringComparison.Ordinal));
                string why = string.IsNullOrWhiteSpace(clip) ? L.T("paste.empty") : nearMiss && !link ? TeleportRequest.Explain(clip.Trim()) : nearMiss ? null : L.T("paste.notOne");
                OpenPanel(nearMiss ? clip.Trim() : "", why, true);
                return;
            }
            if (!TryContext(out var player, out var planet, out var blocked))
            {
                Logger.LogInfo("Key pressed (" + via + "), command read, but no teleport: " + (blocked ?? "no active player or planet"));
                Refuse(blocked ?? L.T("tp.notInWorld"));
                return;
            }
            BindSave(out _);
            Logger.LogInfo("Key pressed (" + via + "): teleport started.");
            reopenAfter = false;
            StartCoroutine(Teleport(player, planet, request, Label(request), false));
        }

        private string KeyLabel(string action) => KeyCheck.Label(keys[action].Value, panel != null && panel.PlayStation);

        private void FirstRunTip(string said)
        {
            firstRunShown.Value = true;
            string pad = KeyCheck.Empty(keys[Panel.ActPadPlaces].Value) ? "" : " " + L.T("tip.pad", KeyCheck.Label(keys[Panel.ActPadPlaces].Value, panel.PlayStation));
            Notice((said != null ? said + "\n" : "") + L.T("tip.first", KeyLabel(Panel.ActPlaces), KeyLabel(Panel.ActTeleport)) + pad, 14f);
            messageTip = true;
        }

        // ── 0.2.6: the save in play. The game's own name for it; a co-op guest's places sit under the host world's seed. ──
        private string BindSave(out bool changed)
        {
            changed = false;
            string key = null;
            uint seed = 0;
            try
            {
                key = Managers.GetManager<SavedDataHandler>()?.GetCurrentSaveFileName();
                var settings = Managers.GetManager<GameSettingsHandler>()?.GetCurrentGameSettings();
                if (settings != null) seed = settings.GetWorldSeed();
            }
            catch (Exception error) { Logger.LogWarning("Save name unavailable: " + error.GetType().Name); }
            if (string.IsNullOrEmpty(key)) key = "co-op " + seed;
            if (key == boundSave && (seed == boundSeed || seed == 0)) return null;   // a seed not read this time is no new world
            changed = true;
            boundSave = key;
            boundSeed = seed;
            string said = null;
            try
            {
                string loaded = panel.TakeLoadNote();
                said = panel.Lib.Bind(key, seed, SaveExists, out var saveFailed);
                Logger.LogInfo("Places: now this save's list" + (said != null ? " (" + said + ")" : "") + ".");
                if (saveFailed != null) said = (said != null ? said + " " : "") + saveFailed;
                if (loaded != null) said = said != null ? loaded + " " + said : loaded;
            }
            catch (Exception error) { Logger.LogWarning("Places not bound to the save: " + error.GetType().Name); }
            return said;
        }

        // does a save of this name still exist? On PC the game keeps <name>.json in persistentDataPath; unsure counts as yes
        private static bool SaveExists(string name)
        {
            try { return File.Exists(Path.Combine(Application.persistentDataPath, name + ".json")); }
            catch (Exception) { return true; }
        }

        // ── IPanelHost ──
        public bool Busy => busy;
        Look IPanelHost.Look => look;
        public string Version => PluginVersion.Value;
        public string Binding(string action) => keys.TryGetValue(action, out var e) ? e.Value ?? "" : "";

        public string SetBinding(string action, string path)
        {
            if (!keys.TryGetValue(action, out var entry)) return null;
            var ours = new List<KeyValuePair<string, string>>();
            foreach (var kv in keys) ours.Add(new KeyValuePair<string, string>(kv.Key, kv.Value.Value));
            // 0.3.3: the game's own play bindings too (F8 is its Show feedback), as the changelog and README promise
            var c = KeyCheck.Refuse(action, path, ours, ((IPanelHost)this).GameBindings());
            if (c != null)
            {
                Logger.LogInfo(action + " not set to " + path + ": " + (c.Game ? "the game's " + c.With : "our " + c.With) + " has it.");
                return c.Game ? L.T("bind.takenGame", KeyCheck.Words(c.With)) : L.T("bind.taken", L.T("act." + c.With));
            }
            entry.Value = path ?? "";
            Logger.LogInfo(action + " is now " + (KeyCheck.Empty(path) ? "not set" : path) + ".");
            return null;
        }

        // the game's own bindings as the player has them now (its rebinds included), from its input asset
        List<GameBinding> IPanelHost.GameBindings()
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
            catch (Exception error) { Logger.LogWarning("Game bindings unreadable: " + error.GetType().Name); }
            return list;
        }

        public List<string> GameSaves()
        {
            var names = new List<string>();
            try
            {
                foreach (var f in Directory.GetFiles(Application.persistentDataPath, "*.json"))
                {
                    string n = Path.GetFileNameWithoutExtension(f);
                    if (!string.Equals(n, "Backup", StringComparison.OrdinalIgnoreCase)) names.Add(n);   // the game's hidden autosave
                }
                names.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception error) { Logger.LogWarning("Save list unreadable: " + error.GetType().Name); }
            return names;
        }

        public string Diagnostics()
        {
            var facts = new List<KeyValuePair<string, string>>
            {
                new("Mod", "GamesOMG Teleport " + PluginVersion.Value),
                new("Game", Application.version + " (tested build 26411416 / v2.104)"),
                new("Unity", Application.unityVersion),
                new("BepInEx", typeof(BaseUnityPlugin).Assembly.GetName().Version.ToString()),
                new("OS", SystemInfo.operatingSystem),
                new("Screen", Screen.width + "x" + Screen.height),
                new("Input hold available", InputHold.Available.ToString()),
                new("Destination far switches readable", FarSwitches.Available.ToString()),
                new("Panel font", "the game's own (as 0.2.6)"),
                new("Controller in use", panel.UsingPad ? (panel.PlayStation ? "PlayStation layout" : "yes") : "no"),
            };
            facts.Add(new KeyValuePair<string, string>("Settings version", configVersion.Value.ToString()));
            foreach (var kv in keys) facts.Add(new KeyValuePair<string, string>("Key " + kv.Key, string.IsNullOrEmpty(kv.Value.Value) ? "none" : kv.Value.Value));
            foreach (var p in prefs.All) if (p.Section == Prefs.Appearance || p.Section == Prefs.Behaviour) facts.Add(new KeyValuePair<string, string>(p.Id, p.Get()));
            int saves = 0, places = 0, found = 0, notes = 0;
            foreach (var name in panel.Store.SaveNames)
            {
                var d = panel.Store.SaveOf(name);
                if (d == null || d.Empty) continue;
                saves++;
                foreach (var w in d.Planets.Values) { places += w.Saved.Count; found += w.Visited.Count; notes += w.NoteCount; }
            }
            facts.Add(new KeyValuePair<string, string>("Data", saves + " saves, " + places + " places, " + found + " found marks, " + notes + " notes (counts only)"));
            facts.Add(new KeyValuePair<string, string>("Backups kept", panel.Lib.ListBackups().Count.ToString()));
            return diag.Text(facts);
        }

        public void ReplayFirstRun() { firstRunShown.Value = false; FirstRunTip(null); }

        public void Opened(string line) => Logger.LogInfo(line);

        // ── sounds: two short tones made in code (no files), only when the setting is on, never without the text line ──
        public void Sound(bool ok)
        {
            if (!sounds.Value) return;
            try
            {
                if (audio == null) { audio = gameObject.AddComponent<AudioSource>(); audio.playOnAwake = false; audio.spatialBlend = 0; audio.ignoreListenerPause = true; }
                if (okClip == null) { okClip = Tone("gomg-ok", 660, 990); noClip = Tone("gomg-no", 440, 300); }
                audio.PlayOneShot(ok ? okClip : noClip, .35f);
            }
            catch (Exception error) { Logger.LogWarning("Sound unavailable: " + error.GetType().Name); }
        }

        private static AudioClip Tone(string name, float from, float to)
        {
            const int rate = 44100;
            int n = rate * 12 / 100;
            var data = new float[n];
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                phase += 2 * Math.PI * Mathf.Lerp(from, to, t) / rate;
                float env = Mathf.Min(1, t * 20) * (1 - t) * (1 - t);
                data[i] = (float)Math.Sin(phase) * env * .5f;
            }
            var clip = AudioClip.Create(name, n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // ── Return to previous spot: from the key or the panel. Resolved once the player can move, then the same checked path. ──
        public void GoBack() => GoBack(true);

        private void GoBack(bool fromPanel)
        {
            if (fromPanel) panel.Hide("return chosen");
            if (busy) { Notice(L.T("tp.preparing")); return; }
            reopenAfter = fromPanel && !closeAfter.Value;
            StartCoroutine(BackWhenFree(fromPanel));
        }

        private IEnumerator BackWhenFree(bool fromPanel)
        {
            float deadline = Time.realtimeSinceStartup + 1f;
            string blocked = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                if (busy) { Notice(L.T("tp.preparing")); yield break; }
                if (!TryContext(out var player, out var planet, out blocked)) continue;
                BindSave(out _);
                var spot = panel.Store.Back(planet.id);
                if (spot == null)
                {
                    Logger.LogInfo("Return: nothing recorded on this planet.");
                    Refuse(L.T("return.nothing"), fromPanel);
                    yield break;
                }
                Logger.LogInfo("Return: teleport started.");
                StartCoroutine(Teleport(player, planet, spot.Request, spot.Name, true, fromPanel));
                yield break;
            }
            Logger.LogInfo("Return: no teleport: " + (blocked ?? "no active player or planet"));
            Refuse(blocked ?? L.T("tp.notInWorld"), fromPanel);
        }

        private static string Label(TeleportRequest r) =>
            Mathf.RoundToInt(r.X) + ", " + (r.AutomaticHeight ? "~" : Mathf.RoundToInt(r.Y).ToString()) + ", " + Mathf.RoundToInt(r.Z);

        private void OpenPanel(string prefill, string note, bool noteBad)
        {
            if (busy) { Notice(L.T("tp.preparing")); return; }
            if (!TryContext(out var player, out var planet, out var blocked))
            {
                Refuse(blocked ?? L.T("panel.notInWorld", KeyLabel(Panel.ActPlaces)));
                return;
            }
            message = null;
            string said = BindSave(out _);
            if (note == null && said != null) { note = said; noteBad = false; }
            if (!panel.Show(player, planet, prefill, note, noteBad))
                Refuse(L.T("panel.cantOpen", KeyLabel(Panel.ActTeleport)));
        }

        // the panel's Teleport: close it, then teleport once the game lets the player move again
        void IPanelHost.Go(TeleportRequest request, string label)
        {
            if (busy) { panel.Refused(L.T("tp.preparing")); return; }
            panel.Hide("teleport chosen");
            reopenAfter = !closeAfter.Value;
            StartCoroutine(TeleportWhenFree(request, label));
        }

        private IEnumerator TeleportWhenFree(TeleportRequest request, string label)
        {
            float deadline = Time.realtimeSinceStartup + 1f;
            string blocked = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                if (busy) { Notice(L.T("tp.preparing")); yield break; }
                if (TryContext(out var player, out var planet, out blocked))
                {
                    BindSave(out _);
                    Logger.LogInfo("Panel: teleport started.");
                    StartCoroutine(Teleport(player, planet, request, label, false, true));
                    yield break;
                }
            }
            Logger.LogInfo("Panel: no teleport: " + (blocked ?? "no active player or planet"));
            Refuse(blocked ?? L.T("tp.notInWorld"), true);
        }

        // What the clipboard looked like, never what it said: its length, token count and the first token's kind.
        private static string Shape(string text)
        {
            if (text == null) return "empty";
            var parts = text.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            string first = parts.Length == 0 ? "none" : parts[0] == "/tp" ? "/tp" : "other";
            return text.Length + " chars, " + parts.Length + " tokens, first " + first + (text.IndexOf('\n') >= 0 ? ", has a line break" : "");
        }

        private static bool TryContext(out PlayerMainController player, out PlanetData planet, out string blocked)
        {
            player = null;
            planet = null;
            blocked = null;
            try
            {
                var players = Managers.GetManager<PlayersManager>();
                var planets = Managers.GetManager<PlanetLoader>();
                var windows = Managers.GetManager<WindowsHandler>();
                if (players == null || planets == null) return false;
                if (windows != null && windows.GetHasUiOpen())
                {
                    blocked = L.T("tp.menuOpen");
                    return false;
                }
                player = players.GetActivePlayerController();
                planet = planets.GetCurrentPlanetData();
                if (player == null || planet == null || !player.IsOwner || !player.IsSpawned) return false;
                var canAct = player.GetPlayerCanAct();
                if (canAct == null) return false;
                if (canAct.GetCanMoveAfterStart() && canAct.GetCanMove()) return true;
                blocked = L.T("tp.cantMove");
                return false;
            }
            // Manager teardown during scene transitions is not a teleportable context.
            catch (MissingReferenceException) { return false; }
            catch (NullReferenceException) { return false; }
        }

        private static bool SameContext(PlayerMainController player, PlanetData planet) =>
            TryContext(out var current, out var currentPlanet, out _) && current == player && currentPlanet == planet;

        private bool TryStartSectors(PlayerMainController player, TeleportRequest request, out DestinationSectors sectors, out string why)
        {
            sectors = null;
            why = null;
            try
            {
                bool okay = DestinationSectors.TryStart(player, request, out sectors, out var reason);
                if (!okay) why = reason;
                return okay;
            }
            catch (Exception error)
            {
                Logger.LogWarning("Destination streaming check failed: " + error.GetType().Name);
                why = L.T("tp.cantLoad");
                return false;
            }
        }

        private bool TryRescan(DestinationSectors sectors, TeleportRequest request, out bool more, out string why)
        {
            more = false;
            why = null;
            try
            {
                more = sectors.Rescan(request, out var reason);
                if (reason != null) { why = reason; return false; }
                return true;
            }
            catch (Exception error)
            {
                Logger.LogWarning("Destination streaming check failed: " + error.GetType().Name);
                why = L.T("tp.cantLoad");
                return false;
            }
        }

        private void WakeDestination(DestinationWake wake, TeleportRequest request)
        {
            try
            {
                int n = wake.Wake(FarSwitches.Now(), request);
                if (n > 0) Logger.LogInfo("Destination: " + n + " far-switched objects on for the landing check.");
            }
            catch (Exception error) { Logger.LogWarning("Destination far switches not prepared: " + error.GetType().Name); }
        }

        private bool TryLand(PlayerMainController player, TeleportRequest request, out Vector3 landing, out string why)
        {
            landing = default;
            why = null;
            try
            {
                Physics.SyncTransforms();
                bool okay = TeleportSafety.TryResolve(player, request, out landing, out var reason, out var probe);
                if (!okay)
                {
                    why = reason;
                    // 0.3.2 (11-20 20:48-20:57: every teleport, Return too, said "No safe ground was found"; cause never
                    // found): what the ray met, so the next refusal names its cause. Places and coordinates only, no clipboard.
                    Logger.LogWarning("Landing refused (" + L.IdOf(reason) + ") at " + Label(request) + ": " + probe.Trace);
                }
                return okay;
            }
            catch (Exception error)
            {
                Logger.LogWarning("Landing check failed: " + error.GetType().Name);
                why = L.T("tp.cantCheck");
                return false;
            }
        }

        // a refusal: the reason on screen (and in the panel, reopened on the same place, when the teleport came from it)
        private void Refuse(string why, bool fromPanel = false)
        {
            diag.Note(L.IdOf(why), Time.realtimeSinceStartup);
            Sound(false);
            if (fromPanel && TryContext(out var player, out var planet, out _) && panel.Show(player, planet, null, why, true)) return;
            Notice(why, 6f, true);
        }

        private IEnumerator Teleport(PlayerMainController player, PlanetData planet, TeleportRequest request, string label, bool back, bool fromPanel = false)
        {
            busy = true;
            DestinationSectors sectors = null;
            DestinationWake wake = null;
            bool moveAttempted = false, moved = false;
            string refused = null;
            try
            {
                // "Preparing destination…": a slow load never looks like an ignored press, and a second press waits
                Notice(L.T("tp.preparing"), 15f);
                if (!TryStartSectors(player, request, out sectors, out refused)) yield break;
                float deadline = Time.realtimeSinceStartup + 15f;
                for (int round = 0; ; round++)
                {
                    while (!sectors.Ready && SameContext(player, planet) && Time.realtimeSinceStartup < deadline)
                        yield return null;
                    if (!SameContext(player, planet)) { refused = L.T("tp.cancelled"); yield break; }
                    if (!sectors.Ready)
                    {
                        Refuse(L.T("tp.stillLoading"), fromPanel);
                        // Keep one transaction alive while pending scenes settle; no competing cleanup/unload.
                        float cleanupDeadline = Time.realtimeSinceStartup + 30f;
                        while (!sectors.Ready && SameContext(player, planet) && Time.realtimeSinceStartup < cleanupDeadline)
                            yield return null;
                        yield break;
                    }
                    // 0.2.5: a sector inside a loaded sector (a procedural wreck) has its entry box only now: load it too
                    if (round >= 4) break;   // nesting in build 26411416 is two deep
                    if (!TryRescan(sectors, request, out bool more, out refused)) yield break;
                    if (!more) break;
                }
                // Allow newly loaded components their first update before checking current collisions.
                yield return null;
                if (!SameContext(player, planet)) { refused = L.T("tp.cancelled"); yield break; }
                // 0.3.0 (Codex C61 F7): floors the game switches on only near the player are on for the check, as they will
                // be on arrival; same frame as the check, so the game's own switch can't turn them off in between
                wake = new DestinationWake();
                WakeDestination(wake, request);
                if (!TryLand(player, request, out var landing, out refused)) yield break;
                // Return: where you stand now (and what stands there), recorded once the move succeeds
                Vector3 from = Panel.Feet(player);
                string fromName = panel.NameForSpot(PlaceStore.WorldOf(planet.id), from.x, from.z);
                // The game's UiWindowTeleporter uses teleport=true, including the owner-authoritative co-op path.
                moveAttempted = true;
                if (MovePlayer(player, landing, label, request.Away, back))
                {
                    moved = true;
                    Remember(planet, label, landing, from, fromName, back);
                }
            }
            finally
            {
                try
                {
                    // no move: off again what the wake switched on (before any unload); a move: the game's switch takes over
                    if (wake != null) { if (moveAttempted) wake.Keep(); else wake.Undo(Logger.LogWarning); }
                    // Never unload after a partially completed native move.
                    if (!moveAttempted && sectors != null && SameContext(player, planet)) sectors.ReleaseUnused();
                }
                catch (Exception error) { Logger.LogWarning("Destination cleanup was deferred: " + error.GetType().Name); }
                finally
                {
                    busy = false;
                    // here, not after the try: a yield break inside it ends the coroutine and skips anything below
                    if (refused != null) Refuse(refused, fromPanel);
                    else if (moved && fromPanel && reopenAfter) StartCoroutine(ReopenSoon());
                }
            }
        }

        // "Close after teleport" off: Places opens again where you landed
        private IEnumerator ReopenSoon()
        {
            yield return new WaitForSecondsRealtime(.6f);
            if (!panel.Open) OpenPanel(null, null, false);
        }

        private bool MovePlayer(PlayerMainController player, Vector3 landing, string label, string away, bool back)
        {
            try
            {
                player.SetPlayerPlacement(landing, player.transform.rotation, true);
                Managers.GetManager<MeshOccluderHandler>()?.SpeedUpProcess(5);
                // 0.2.4: no under-water line (11-19). 0.2.5: a far-ring landing says how far from the place.
                Notice(back ? (string.IsNullOrEmpty(label) ? L.T("return.done") : L.T("return.doneTo", label))
                    : L.T("tp.done", (string.IsNullOrEmpty(label) ? L.T("tp.copiedSpot") : label) + (away ?? "")));
                Sound(true);
                return true;
            }
            catch (Exception error)
            {
                Logger.LogWarning("Native teleport did not complete: " + error.GetType().Name);
                Notice(L.T("tp.incomplete"), 6f, true);
                diag.Note("tp.incomplete", Time.realtimeSinceStartup);
                return false;
            }
        }

        // 0.2.0: each completed teleport is a Recently travelled row, at the checked landing (a real height, even for ~).
        // 0.2.6: and where you stood before it is this save's Return (Return's own move too: the next one swaps back).
        // Arriving NEVER marks anything found (ChatGPT R4, held): Mark found is by hand.
        private void Remember(PlanetData planet, string label, Vector3 landing, Vector3 from, string fromName, bool back)
        {
            try
            {
                var store = panel.Store;
                store.Teleported(planet.id, from.x, from.y, from.z, fromName);
                store.Arrived(planet.id, label, landing.x, landing.z);   // the next teleport's Return is named for it
                if (!back) store.AddRecent(planet.id, string.IsNullOrEmpty(label) ? L.T("recent.teleport") : label, landing.x, landing.y, landing.z);
                string failed = panel.Lib.Save();
                if (failed != null) Notice(failed, 8f, true);
            }
            catch (Exception error) { Logger.LogWarning("Recent not saved: " + error.GetType().Name); }
        }

        private void Notice(string text) => Notice(text, 5f, false);
        private void Notice(string text, float seconds) => Notice(text, seconds, false);

        private void Notice(string text, float seconds, bool bad)
        {
            message = text;
            messageBad = bad;
            messageTip = false;
            messageUntil = Time.realtimeSinceStartup + seconds;
        }

        // the notice card: top centre, dark, the gear-G mark, solid text at any panel opacity; a refusal has a red edge
        private void OnGUI()
        {
            panel.OnGUI();
            if (string.IsNullOrEmpty(message) || Time.realtimeSinceStartup > messageUntil) return;
            var theme = panel.Theme;
            theme.Build(look.TextSize(), look.Compact(), look.Opacity());
            float s = theme.S;
            float width = Mathf.Min(Mathf.Round(520 * s), Screen.width - 32);
            var text = theme.Wrap;
            text.font = theme.FontFor(message, false);
            float textW = width - theme.Px(70);
            float h = Mathf.Max(theme.Px(64), text.CalcHeight(new GUIContent(message), textW) + theme.Px(40));
            var card = new Rect((Screen.width - width) / 2, Mathf.Round(56 * Mathf.Clamp(Screen.height / 1080f, .7f, 3f)), width, h);
            theme.Plate(card, Theme.A(Theme.Panel, .97f), theme.Px(12));
            theme.Fill(new Rect(card.x + theme.Px(12), card.y, card.width - theme.Px(24), Mathf.Max(2, theme.Px(2.5f))), messageBad ? Theme.Bad : Theme.Orange);
            if (theme.Mark != null && Event.current.type == EventType.Repaint)
                GUI.DrawTexture(new Rect(card.x + theme.Px(16), card.y + theme.Px(16), theme.Px(32), theme.Px(32)), theme.Mark, ScaleMode.ScaleToFit, true);
            var head = theme.Head;
            head.font = theme.FontFor("GAMESOMG TELEPORT", true);
            GUI.Label(new Rect(card.x + theme.Px(60), card.y + theme.Px(12), textW, theme.Px(16)), messageTip ? L.T("tip.head") : "GAMESOMG TELEPORT", head);
            GUI.Label(new Rect(card.x + theme.Px(60), card.y + theme.Px(30), textW, h - theme.Px(36)), message, text);
        }

        private void OnDestroy()
        {
            panel?.Hide("the mod was unloaded");
            teleportKey?.Dispose();
            panelKey?.Dispose();
            backKey?.Dispose();
            padPanelKey?.Dispose();
            padBackKey?.Dispose();
        }
    }
}

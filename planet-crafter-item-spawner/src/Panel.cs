// GamesOMG Item Spawner: the panel. The shape (2026-11-21): a grid of the game's item icons with a search box;
// the search matches the in-game names; an amount field with Add, ×1, ×10 and Fill; items go straight into the backpack.
// Drawn with IMGUI as GamesOMG Teleport is (the same plates, the G Emblem mark, the game's font), held like a game text box
// (InputHold) so no key also fires in the game. Keyboard: type to search, arrows move the selection (always: Left/Right
// never move the caret), Enter adds, Esc clears the search, then closes. Controller: D-pad / left stick move, A adds,
// X ×10, Y Fill, LB/RB tabs, LT/RT a page, B closes.
// 0.2.0: under the selected item, in small print, the game's own description and one line of what it
// does (ItemInfo); every word of the mod's own in the game's language (Strings.cs). The status line holds two lines, Help takes
// the grid and the detail bar, and with MeasureFit on (the sitting) every text is measured where it is drawn: what doesn't fit
// lands in Overflow, a description cut short in Truncated.
// 0.3.0: a Decoration tab (All | Decoration | Recent; LB/RB step through them): the effigies, posters and spacesuit skins
// (Decor.cs), searchable like All, with a strip above the grid: "One of each: n" adds every one shown that the world doesn't
// have yet, through the same backpack path, batching and full-backpack rule as Add. On that tab the controller's Y is that
// button (Fill stays on the mouse).
using System;
using System.Collections.Generic;
using BepInEx.Logging;
using SpaceCraft;
using UnityEngine;

namespace GamesOMG.PlanetCrafterSpawn
{
    internal interface IPanelHost
    {
        bool Busy { get; }
        string KeyLabel { get; }
        string PadLabel(bool playStation);
        bool ChordHeld();
        float TextSize { get; }
        int LastAmount { get; set; }
        IList<string> Recent { get; }
        string LastId { get; }
        void Spawn(ItemEntry item, Inventory backpack, int amount, bool fill);
        void SpawnSet(List<ItemEntry> items, Inventory backpack);
        string GameBuild { get; }
    }

    internal sealed class Panel
    {
        private const string SearchName = "gomg-spawn-search", AmountName = "gomg-spawn-amount";
        private readonly IPanelHost host;
        private readonly ManualLogSource log;
        private readonly InputHold hold;
        private readonly PadInput input = new PadInput();
        public readonly Theme Theme = new Theme();

        public bool Open { get; private set; }
        public Rect Drawn { get; private set; }
        private PlayerMainController player;
        private Inventory backpack;
        private List<ItemEntry> all = new List<ItemEntry>();
        private List<ItemEntry> shown = new List<ItemEntry>();
        private string query = "", shownFor;
        public const int TabAll = 0, TabDeco = 1, TabRecent = 2, Tabs = 3;
        private static readonly string[] TabIds = { "tab.all", "tab.deco", "tab.recent" };
        private int tab, shownTab = -1;
        // the Decoration tab: what this world has (Catalog.Owned, read when it can have changed) and what "one of each" would add
        private HashSet<string> owned = new HashSet<string>(StringComparer.Ordinal);
        private List<ItemEntry> missing = new List<ItemEntry>();
        private bool ownedStale = true;
        private int sel, top, cols = 1, visRows = 1;
        private string amountText = "1";
        private string status;
        private bool statusBad, focusSearch, ignoreTabs, help, client;
        private GUIStyle amountStyle, helpStyle, emptyStyle;
        private float stylesFor = -1;
        private float helpScroll, helpOver;
        private const int InfoLines = 4;

        // the sitting's fit check (off for players): each text measured against the room it is drawn in
        public bool MeasureFit { get; set; }
        public readonly HashSet<string> Overflow = new HashSet<string>(StringComparer.Ordinal);
        public readonly HashSet<string> Truncated = new HashSet<string>(StringComparer.Ordinal);
        public Info SelectedInfo { get; private set; }

        public Panel(IPanelHost host, ManualLogSource log)
        {
            this.host = host;
            this.log = log;
            hold = new InputHold(log);
        }

        public bool PlayStation => input.PlayStation;
        public ItemEntry Selected => sel >= 0 && sel < shown.Count ? shown[sel] : null;
        public string Query => query;
        public int Tab => tab;
        public int ShownCount => shown.Count;
        public string Status => status;
        public bool StatusBad => statusBad;
        public int MissingCount => missing.Count;
        public IList<ItemEntry> Shown => shown;

        public bool Show(PlayerMainController who, Inventory inventory, bool viaPad)
        {
            if (Open) return true;
            all = Catalog.Items(log);
            if (all.Count == 0) { status = null; return false; }
            if (!hold.Begin(who)) return false;
            player = who;
            backpack = inventory;
            client = Spawner.IsClient;
            Open = true;
            query = "";
            shownFor = null;
            tab = 0;
            help = false;
            status = null;
            amountText = SpawnPlan.Clamp(host.LastAmount).ToString(System.Globalization.CultureInfo.InvariantCulture);
            focusSearch = true;
            ignoreTabs = viaPad;
            ownedStale = true;
            Refresh();
            // the last item spawned is selected, so the open key then Enter adds it again
            int last = host.LastId == null ? -1 : shown.FindIndex(e => e.Id == host.LastId);
            sel = last >= 0 ? last : 0;
            Visible();
            log.LogInfo("Panel opened (" + all.Count + " items" + (client ? ", co-op client" : "") + ").");
            return true;
        }

        public void Hide(string why)
        {
            if (!Open) return;
            Open = false;
            hold.End(why);
            GUIUtility.keyboardControl = 0;
            log.LogInfo("Panel closed (" + why + ").");
        }

        // the spawner's answer, said in the status line
        public void Result(SpawnPlan plan, ItemEntry item)
        {
            string id = plan.ResultId();
            status = L.T(id, plan.Added, item.Name, plan.DidNotFit);
            statusBad = id != "result.added" && id != "result.filled";
            ownedStale = true;
        }

        // "one of each": {0} went in, {1} didn't fit
        public void SetResult(SpawnPlan plan)
        {
            string id = plan.SetResultId();
            status = L.T(id, plan.Added, plan.DidNotFit);
            statusBad = id != "set.added";
            ownedStale = true;
        }

        public void Say(string text, bool bad) { status = text; statusBad = bad; }

        // select an item by its id in the All tab (the sitting walks the probes with it); false when no item has that id
        public bool SelectId(string id)
        {
            if (!Open || id == null) return false;
            query = "";
            tab = TabAll;
            help = false;
            Refresh();
            int i = shown.FindIndex(e => e.Id == id);
            if (i < 0) return false;
            sel = i;
            Visible();
            return true;
        }

        public void ShowHelp(bool on) { help = on; helpScroll = 0; }

        // ── each frame while open (Plugin.Update) ──
        public void Update(bool toggle)
        {
            if (toggle) { Hide("the open key"); return; }
            string why = hold.Check();
            if (why != null) { Hide(why); return; }
            if (player == null || !player) { Hide("the player is gone"); return; }
            if (ignoreTabs && !host.ChordHeld()) ignoreTabs = false;
            Refresh();
            bool textFocused = GUIUtility.keyboardControl != 0;
            var cmd = input.Read(textFocused, true);
            if (ignoreTabs && (cmd == Cmd.TabLeft || cmd == Cmd.TabRight)) cmd = Cmd.None;
            // Help that is taller than its room scrolls with Up / Down (and a page); any other command closes it
            if (help && helpOver > 0 && (cmd == Cmd.Up || cmd == Cmd.Down || cmd == Cmd.PageUp || cmd == Cmd.PageDown))
            {
                float step = Theme.Px(cmd == Cmd.Up || cmd == Cmd.Down ? 40 : 200);
                helpScroll = Mathf.Clamp(helpScroll + (cmd == Cmd.Up || cmd == Cmd.PageUp ? -step : step), 0, helpOver);
                return;
            }
            if (help && cmd != Cmd.None && cmd != Cmd.Back) help = false;
            switch (cmd)
            {
                case Cmd.Left: Move(-1); break;
                case Cmd.Right: Move(1); break;
                case Cmd.Up: Move(-cols); break;
                case Cmd.Down: Move(cols); break;
                case Cmd.PageUp: Move(-cols * Math.Max(1, visRows - 1)); break;
                case Cmd.PageDown: Move(cols * Math.Max(1, visRows - 1)); break;
                case Cmd.Home: sel = 0; Visible(); break;
                case Cmd.End: sel = Math.Max(0, shown.Count - 1); Visible(); break;
                case Cmd.TabLeft: SetTab((tab + Tabs - 1) % Tabs); break;
                case Cmd.TabRight: SetTab((tab + 1) % Tabs); break;
                case Cmd.Next: case Cmd.Prev: switchFocus = true; break;   // Tab: the search box <-> the amount
                case Cmd.Activate: Add(null, false); break;
                case Cmd.Ten: Add(10, false); break;
                case Cmd.Fill: if (tab == TabDeco) AddSet(); else Add(null, true); break;
                case Cmd.Back:
                    if (help) help = false;
                    else if (query.Length > 0 && !input.UsingPad) { query = ""; focusSearch = true; }
                    else Hide("Esc / B");
                    break;
            }
        }

        private bool switchFocus;

        private void Move(int by)
        {
            if (shown.Count == 0) return;
            sel = Mathf.Clamp(sel + by, 0, shown.Count - 1);
            Visible();
        }

        private void SetTab(int t)
        {
            string keep = Selected?.Id;
            tab = t;
            Refresh();
            int i = keep == null ? -1 : shown.FindIndex(e => e.Id == keep);
            sel = i >= 0 ? i : 0;
            Visible();
        }

        private void Visible()
        {
            if (cols < 1) cols = 1;
            int row = sel / cols;
            if (row < top) top = row;
            if (row >= top + visRows) top = row - visRows + 1;
            if (top < 0) top = 0;
        }

        private void Refresh()
        {
            // what the world has is read again only when it can have changed (open, a spawn done, the Decoration tab picked)
            bool ownedNow = false;
            if (tab == TabDeco && ownedStale && !host.Busy) { owned = Catalog.Owned(all); ownedStale = false; ownedNow = true; }
            if (query == shownFor && tab == shownTab && !ownedNow) return;
            bool newQuery = query != shownFor;
            shownFor = query;
            shownTab = tab;
            if (tab == TabRecent)
            {
                var recent = new List<ItemEntry>();
                foreach (var id in host.Recent) { var e = Catalog.Find(all, id); if (e != null) recent.Add(e); }
                shown = query.Trim().Length == 0 ? recent : Search.Filter(recent, query);
            }
            else if (tab == TabDeco) shown = Search.Filter(Decor.Only(all), query);
            else shown = Search.Filter(all, query);
            missing = tab == TabDeco ? Decor.Missing(shown, owned) : new List<ItemEntry>();
            if (newQuery) { sel = 0; top = 0; }
            if (sel >= shown.Count) sel = Math.Max(0, shown.Count - 1);
        }

        // "one of each": every decoration shown that this world doesn't have yet
        private void AddSet()
        {
            if (tab != TabDeco || help) return;
            if (host.Busy) { Say(L.T("spawn.busy"), true); return; }
            owned = Catalog.Owned(all);
            ownedStale = false;
            missing = Decor.Missing(shown, owned);
            if (missing.Count == 0) { Say(L.T("set.none"), false); return; }
            Say(L.T("set.adding", missing.Count), false);
            host.SpawnSet(new List<ItemEntry>(missing), backpack);
        }

        private void Add(int? amount, bool fill)
        {
            var item = Selected;
            if (item == null || help) return;
            if (host.Busy) { Say(L.T("spawn.busy"), true); return; }
            int n = amount ?? ParseAmount();
            if (amount == null && !fill) host.LastAmount = n;
            Say(L.T("spawn.adding", item.Name), false);
            host.Spawn(item, backpack, n, fill);
        }

        private int ParseAmount()
        {
            int.TryParse(amountText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n);
            n = SpawnPlan.Clamp(n);
            amountText = n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return n;
        }

        // ── drawing ──────────────────────────────────────────────────────────────────────────────────────────────────
        public void OnGUI()
        {
            if (!Open) return;
            var t = Theme;
            t.Build(host.TextSize);
            if (!Mathf.Approximately(stylesFor, t.S))
            {
                stylesFor = t.S;
                amountStyle = new GUIStyle(t.Field) { padding = new RectOffset(t.Px(6), t.Px(6), 0, 0), alignment = TextAnchor.MiddleCenter };
                // it sits on the detail bar (Surface): its own plate is Raised, focused or not
                amountStyle.normal.background = amountStyle.hover.background = amountStyle.focused.background = amountStyle.active.background = t.Solid(Theme.Raised);
                helpStyle = new GUIStyle(t.Small) { wordWrap = true };
                emptyStyle = new GUIStyle(t.Small) { wordWrap = true, alignment = TextAnchor.MiddleCenter };
            }
            GUI.depth = -1000;
            var e = Event.current;
            float pad = t.Px(16);
            float w = Mathf.Min(Screen.width - t.Px(32), t.Px(660)), h = Mathf.Min(Screen.height - t.Px(48), t.Px(780));
            var r = new Rect(Mathf.Round((Screen.width - w) / 2), Mathf.Round((Screen.height - h) / 2), w, h);
            Drawn = r;
            t.Plate(r, Theme.Panel, t.Px(10));
            t.Outline(r, Theme.Line, Mathf.Max(1, t.Px(1)), t.Px(10));
            // clicks on the panel stay on the panel
            bool over = r.Contains(e.mousePosition);

            float x = r.x + pad, y = r.y + pad, inner = r.width - pad * 2;

            // header: mark, title, sub; Help and Close on the right
            var mark = t.Mark;
            float ms = t.Px(30);
            if (mark != null && e.type == EventType.Repaint) GUI.DrawTexture(new Rect(x, y + t.Px(2), ms, ms), mark, ScaleMode.ScaleToFit);
            var titleR = new Rect(x + ms + t.Px(10), y, inner - ms - t.Px(90), t.Px(24));
            var subR = new Rect(x + ms + t.Px(10), y + t.Px(24), inner - ms - t.Px(90), t.Px(18));
            string sub = L.T(client ? "sub.client" : "sub.solo");
            GUI.Label(titleR, L.T("title"), t.Title);
            GUI.Label(subR, sub, t.Sub);
            Fits("title", t.Title, L.T("title"), titleR, false);
            Fits("sub", t.Sub, sub, subR, false);
            // both subtitles, whichever is shown: a client sees the longer one
            Fits("sub.client", t.Sub, L.T("sub.client"), subR, false);
            float ib = t.Px(30);
            var closeR = new Rect(r.xMax - pad - ib, y, ib, ib);
            var helpR = new Rect(closeR.x - ib - t.Px(6), y, ib, ib);
            if (IconButton(helpR, Icon.Help, help)) help = !help;
            if (IconButton(closeR, Icon.Close, false)) { Hide("the close button"); return; }
            y += t.Px(48);

            // search
            var sr = new Rect(x, y, inner, t.Field.fixedHeight);
            GUI.SetNextControlName(SearchName);
            string q = GUI.TextField(sr, query, 60, t.Field);
            if (q != query) { query = q; help = false; }
            t.DrawIcon(new Rect(sr.x + t.Px(9), sr.y + (sr.height - t.Px(16)) / 2, t.Px(16), t.Px(16)), Icon.Search, Theme.Muted);
            if (query.Length == 0 && e.type == EventType.Repaint) GUI.Label(sr, L.T("search.placeholder"), t.Placeholder);
            Fits("placeholder", t.Placeholder, L.T("search.placeholder"), new Rect(sr.x, sr.y, sr.width - t.Placeholder.padding.left - t.Placeholder.padding.right, sr.height), false);
            if (GUI.GetNameOfFocusedControl() == SearchName) t.Outline(sr, Theme.Blue, Mathf.Max(1, t.Px(2)), t.Px(4));
            y += sr.height + t.Px(10);

            // tabs + count
            float tx = x;
            for (int i = 0; i < Tabs; i++)
            {
                string label = L.T(TabIds[i]);
                var st = i == tab ? t.TabOn : t.Tab;
                float tw = st.CalcSize(new GUIContent(label)).x + t.Px(4);
                if (GUI.Button(new Rect(tx, y, tw, t.TabH), label, st) && i != tab) SetTab(i);
                tx += tw + t.Px(4);
            }
            string count = L.N("count", shown.Count);
            float cw = t.Small.CalcSize(new GUIContent(count)).x;
            GUI.Label(new Rect(r.xMax - pad - cw, y + (t.TabH - t.Small.lineHeight) / 2, cw + 2, t.TabH), count, t.Small);
            // the tabs and the longest count ("999 items") side by side
            if (MeasureFit && e.type == EventType.Repaint && tx + t.Px(8) > r.xMax - pad - t.Small.CalcSize(new GUIContent(L.N("count", 999))).x)
                Overflow.Add(L.Current + " tabs + count: " + L.T("tab.all") + " | " + L.T("tab.deco") + " | " + L.T("tab.recent") + " | " + L.N("count", 999));
            y += t.TabH + t.Px(8);

            // the Decoration tab's strip: what "one of each" does, and the button (its count = what it would add now)
            if (tab == TabDeco && !help)
            {
                string setLabel = L.T("set.button", missing.Count);
                float bw = Mathf.Max(t.Px(120), t.Primary.CalcSize(new GUIContent(L.T("set.button", 999))).x + t.Px(8));
                float noteW = inner - bw - t.Px(12);
                string note = L.T("set.note");
                float stripH = Mathf.Max(t.ButtonH, LinesTall(helpStyle, 2));
                var noteR = new Rect(x, y + (stripH - LinesTall(helpStyle, 2)) / 2, noteW, LinesTall(helpStyle, 2));
                GUI.Label(noteR, note, helpStyle);
                Fits("set.note", helpStyle, note, noteR, true);
                bool was = GUI.enabled;
                GUI.enabled = !host.Busy && missing.Count > 0;
                if (GUI.Button(new Rect(r.xMax - pad - bw, y + (stripH - t.ButtonH) / 2, bw, t.ButtonH), setLabel, t.Primary)) AddSet();
                GUI.enabled = was;
                y += stripH + t.Px(8);
            }

            // the foot first (fixed heights), the grid takes what is left
            float promptH = t.Px(22), statusH = LinesTall(t.Status, 2) + t.Px(2), detailH = DetailHeight();
            float footY = r.yMax - pad - promptH;
            float statusY = footY - statusH - t.Px(4);
            float detailY = statusY - detailH - t.Px(6);
            var grid = new Rect(x, y, inner, detailY - t.Px(8) - y);

            if (help) DrawHelp(new Rect(x, y, inner, detailY + detailH - y), e);
            else
            {
                DrawGrid(grid, e);
                DrawDetail(new Rect(x, detailY, inner, detailH), e);
            }
            var statusR = new Rect(x, statusY, inner, statusH);
            var statusStyle = host.Busy ? helpStyle : statusBad ? t.StatusBad : t.Status;
            if (!string.IsNullOrEmpty(status)) GUI.Label(statusR, status, statusStyle);
            Fits("status", statusStyle, status, statusR, true);
            DrawPrompts(new Rect(x, footY, inner, promptH));

            if (focusSearch && e.type == EventType.Repaint) { GUI.FocusControl(SearchName); focusSearch = false; }
            if (switchFocus && e.type == EventType.Repaint) { GUI.FocusControl(GUI.GetNameOfFocusedControl() == AmountName ? SearchName : AmountName); switchFocus = false; }
            if (over && (e.type == EventType.MouseDown || e.type == EventType.MouseUp || e.type == EventType.ScrollWheel)) e.Use();
        }

        private bool IconButton(Rect r, Icon icon, bool on)
        {
            var t = Theme;
            bool hover = r.Contains(Event.current.mousePosition);
            t.Plate(r, on ? Theme.Surface : hover ? Theme.Hover : Theme.Raised, t.Px(6));
            t.DrawIcon(new Rect(r.x + (r.width - t.Px(16)) / 2, r.y + (r.height - t.Px(16)) / 2, t.Px(16), t.Px(16)), icon, Theme.Ink);
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        private void DrawGrid(Rect g, Event e)
        {
            var t = Theme;
            int c0 = cols, r0 = visRows;
            cols = Mathf.Max(1, Mathf.FloorToInt(g.width / t.CellW));
            visRows = Mathf.Max(1, Mathf.FloorToInt(g.height / t.CellH));
            if (cols != c0 || visRows != r0) Visible();   // the first frame (and a resize): keep the selection in view
            float cw = g.width / cols;
            int rows = (shown.Count + cols - 1) / cols;
            top = Mathf.Clamp(top, 0, Mathf.Max(0, rows - visRows));
            if (shown.Count == 0)
            {
                string why = tab == 1 && query.Trim().Length == 0 ? L.T("empty.recent") : L.T("empty.search", query.Trim());
                var er = new Rect(g.x + t.Px(24), g.y, g.width - t.Px(48), g.height);
                GUI.Label(er, why, emptyStyle);
                Fits("empty", emptyStyle, why, er, true);
                return;
            }
            if (e.type == EventType.ScrollWheel && g.Contains(e.mousePosition))
            {
                top = Mathf.Clamp(top + (e.delta.y > 0 ? 1 : -1), 0, Mathf.Max(0, rows - visRows));
                e.Use();
            }
            float gap = t.Px(6);
            for (int row = top; row < Math.Min(rows, top + visRows); row++)
                for (int c = 0; c < cols; c++)
                {
                    int i = row * cols + c;
                    if (i >= shown.Count) break;
                    var cell = new Rect(g.x + c * cw, g.y + (row - top) * t.CellH, cw - gap, t.CellH - gap);
                    bool hover = cell.Contains(e.mousePosition);
                    bool chosen = i == sel;
                    t.Plate(cell, chosen ? Theme.Selected : hover ? Theme.Hover : Theme.Surface, t.Px(6));
                    if (chosen) t.Outline(cell, Theme.Blue, Mathf.Max(1, t.Px(2)), t.Px(6));
                    var item = shown[i];
                    float s = t.IconSize;
                    Theme.DrawSprite(new Rect(cell.x + (cell.width - s) / 2, cell.y + t.Px(6), s, s), Catalog.Icon(item));
                    var nameR = new Rect(cell.x + t.Px(4), cell.y + t.Px(8) + s, cell.width - t.Px(8), cell.height - s - t.Px(10));
                    GUI.Label(nameR, Fit(item.Name, nameR.width, nameR.height), t.Cell);
                    if (e.type == EventType.MouseDown && e.button == 0 && hover)
                    {
                        sel = i;
                        if (e.clickCount >= 2) Add(null, false);
                        e.Use();
                    }
                }
            // a thin position bar when there is more than one screen of items
            if (rows > visRows && e.type == EventType.Repaint)
            {
                var track = new Rect(g.xMax - t.Px(3), g.y, t.Px(3), visRows * t.CellH - gap);
                t.Fill(track, Theme.Line);
                float k = track.height / rows;
                t.Fill(new Rect(track.x, track.y + top * k, track.width, Mathf.Max(t.Px(12), visRows * k)), Theme.Muted);
            }
        }

        // a name that needs more than the cell's lines ends in "…" at a word (the detail bar shows it whole); cached per width
        private readonly Dictionary<string, string> fitted = new Dictionary<string, string>();
        private float fittedFor = -1;

        private string Fit(string name, float width, float height)
        {
            if (!Mathf.Approximately(fittedFor, width)) { fitted.Clear(); fittedFor = width; }
            if (fitted.TryGetValue(name, out var done)) return done;
            var st = Theme.Cell;
            string fit = name;
            if (st.CalcHeight(new GUIContent(name), width) > height + 1)
            {
                string[] words = name.Split(' ');
                fit = "…";
                for (int n = words.Length - 1; n >= 1; n--)
                {
                    string tryText = string.Join(" ", words, 0, n) + "…";
                    if (st.CalcHeight(new GUIContent(tryText), width) <= height + 1) { fit = tryText; break; }
                }
                if (fit == "…")
                    for (int n = name.Length - 1; n >= 1; n--)
                        if (st.CalcHeight(new GUIContent(name.Substring(0, n) + "…"), width) <= height + 1) { fit = name.Substring(0, n) + "…"; break; }
            }
            return fitted[name] = fit;
        }

        // Help takes the grid and the detail bar; when it is taller than that in a language, it scrolls (wheel, Up / Down)
        private void DrawHelp(Rect g, Event e)
        {
            var t = Theme;
            string[][] lines =
            {
                new[] { "help.keysHead", L.T("help.keys", host.KeyLabel, host.PadLabel(PlayStation)) },
                new[] { "help.searchHead", L.T("help.search") },
                new[] { "help.decoHead", L.T("help.deco", GameLabel("Ui_clickbuildable", "Right Click to build")) },
                new[] { "help.infoHead", L.T("help.info") },
                new[] { "help.backpackHead", L.T("help.backpack") },
                new[] { "help.achievementsHead", L.T("help.achievements", GameLabel("GameSettings_freeCraft", "Free build"), GameLabel("GameSettings_unlockedEverything", "All unlocked")) },
                new[] { "help.coopHead", L.T("help.coop") },
            };
            string v = L.T("help.version", PluginVersion.Value, host.GameBuild);
            float w = g.width - t.Px(8);
            float total = 0;
            foreach (var l in lines) total += t.Px(18) + helpStyle.CalcHeight(new GUIContent(l[1]), w) + t.Px(10);
            total += helpStyle.CalcHeight(new GUIContent(v), w);
            helpOver = Mathf.Max(0, total - g.height);
            if (helpOver > 0 && e.type == EventType.ScrollWheel && g.Contains(e.mousePosition)) { helpScroll += e.delta.y * t.Px(12); e.Use(); }
            helpScroll = Mathf.Clamp(helpScroll, 0, helpOver);
            if (MeasureFit && e.type == EventType.Repaint && helpOver > 0) Truncated.Add(L.Current + " help scrolls: " + Mathf.RoundToInt(total) + " px in " + Mathf.RoundToInt(g.height));
            GUI.BeginGroup(g);
            float y = -helpScroll;
            foreach (var l in lines)
            {
                string head = L.T(l[0]).ToUpperInvariant();
                GUI.Label(new Rect(0, y, w, t.Px(18)), head, t.Head);
                Fits("help head", t.Head, head, new Rect(0, 0, w, t.Px(18)), false);
                y += t.Px(18);
                float hh = helpStyle.CalcHeight(new GUIContent(l[1]), w);
                GUI.Label(new Rect(0, y, w, hh), l[1], helpStyle);
                y += hh + t.Px(10);
            }
            GUI.Label(new Rect(0, y, w, helpStyle.CalcHeight(new GUIContent(v), w)), v, helpStyle);
            GUI.EndGroup();
            // where it scrolls: a thin bar, as the grid has
            if (helpOver > 0 && e.type == EventType.Repaint)
            {
                var track = new Rect(g.xMax - t.Px(3), g.y, t.Px(3), g.height);
                t.Fill(track, Theme.Line);
                float k = g.height / total;
                t.Fill(new Rect(track.x, track.y + helpScroll * k, track.width, Mathf.Max(t.Px(12), g.height * k)), Theme.Muted);
            }
        }

        // the game's own words for one of its settings, in its language (an English stand-in only if the game has none)
        private static string GameLabel(string key, string english)
        {
            string s = null;
            try { s = Localization.GetLocalizedString(key); } catch (Exception) { }
            return string.IsNullOrEmpty(s) ? english : s;
        }

        // the detail bar's height: the icon row, the info lines, the action row
        private float DetailHeight()
        {
            var t = Theme;
            return t.Px(12) + t.Px(44) + t.Px(6) + InfoHeight() + t.Px(8) + t.ButtonH + t.Px(12);
        }

        private float InfoHeight() => LinesTall(helpStyle, InfoLines);

        // n lines as tall as they really draw: a Chinese / Japanese / Korean glyph comes from a fallback font whose lines are taller
        // than the style's own lineHeight (sitting 11-21 17:30: two Japanese lines measured over two Latin ones). Cached per size.
        private readonly Dictionary<string, float> tall = new Dictionary<string, float>();

        private float LinesTall(GUIStyle st, int n)
        {
            string key = st.fontSize + "/" + n;
            if (tall.TryGetValue(key, out float h)) return h;
            var sample = new System.Text.StringBuilder("Ag漢한");
            for (int i = 1; i < n; i++) sample.Append("\nAg漢한");
            h = Mathf.Max(Mathf.Ceil(st.lineHeight) * n, Mathf.Ceil(st.CalcHeight(new GUIContent(sample.ToString()), 10000)));
            return tall[key] = h;
        }

        private void DrawDetail(Rect d, Event e)
        {
            var t = Theme;
            t.Plate(d, Theme.Surface, t.Px(8));
            float p = t.Px(12);
            var item = Selected;
            if (item == null)
            {
                SelectedInfo = null;
                GUI.Label(new Rect(d.x + p, d.y + p, d.width - p * 2, t.Px(20)), L.T("detail.none"), t.Small);
                return;
            }
            float s = t.Px(44);
            Theme.DrawSprite(new Rect(d.x + p, d.y + p, s, s), Catalog.Icon(item));
            float tx = d.x + p + s + t.Px(12), tw = d.xMax - p - tx;
            GUI.Label(new Rect(tx, d.y + p - t.Px(1), tw, t.Px(22)), OneLine(item.Name, t.TextStrong, tw, "name"), t.TextStrong);
            string have = L.T("detail.have", Catalog.CountIn(backpack, item));
            string idLine = item.Unnamed ? "  ·  " + L.T("detail.unnamed", item.Id) : item.Id != item.Name ? "  ·  " + L.T("detail.id", item.Id) : "";
            GUI.Label(new Rect(tx, d.y + p + t.Px(22), tw, t.Px(18)), OneLine(have + idLine, t.Small, tw, "detail"), t.Small);

            // the info: the game's description, then what the item does (never cut: the description gives way)
            var info = ItemInfo.For(item, log);
            SelectedInfo = info;
            var ir = new Rect(d.x + p, d.y + p + s + t.Px(6), d.width - p * 2, InfoHeight());
            // the game's "Right Click to build" is a mouse player's line (the game shows it only to them)
            string stat = info.Kind == "place" && input.UsingPad ? "" : info.Stat;
            float statH = string.IsNullOrEmpty(stat) ? 0 : t.Info.CalcHeight(new GUIContent(stat), ir.width);
            if (MeasureFit && e.type == EventType.Repaint && statH > ir.height + 1) Overflow.Add(L.Current + " info " + item.Id + ": " + stat);
            float descRoom = ir.height - statH - (statH > 0 ? t.Px(2) : 0);
            float iy = ir.y;
            if (!string.IsNullOrEmpty(info.Description) && descRoom >= helpStyle.lineHeight)
            {
                string desc = Lines(info.Description, helpStyle, ir.width, descRoom, item.Id);
                float dh = helpStyle.CalcHeight(new GUIContent(desc), ir.width);
                GUI.Label(new Rect(ir.x, iy, ir.width, dh), desc, helpStyle);
                iy += dh + t.Px(2);
            }
            else if (!string.IsNullOrEmpty(info.Description) && MeasureFit && e.type == EventType.Repaint) Truncated.Add(L.Current + " description " + item.Id + " (no room)");
            if (statH > 0) GUI.Label(new Rect(ir.x, iy, ir.width, statH), stat, t.Info);

            // the action row: amount, Add, ×1, ×10, Fill
            float by = d.yMax - p - t.ButtonH;
            bool was = GUI.enabled;
            GUI.enabled = !host.Busy;
            float bx = d.x + p;
            float aw = t.Px(64);
            GUI.SetNextControlName(AmountName);
            string a = GUI.TextField(new Rect(bx, by, aw, t.ButtonH), amountText, 3, amountStyle);
            if (a != amountText) amountText = Digits(a);
            if (GUI.GetNameOfFocusedControl() == AmountName) t.Outline(new Rect(bx, by, aw, t.ButtonH), Theme.Blue, Mathf.Max(1, t.Px(2)), t.Px(4));
            bx += aw + t.Px(8);
            string addLabel = L.T("btn.add", amountText.Length > 0 ? amountText : "1");
            float addW = Mathf.Max(t.Px(96), t.Primary.CalcSize(new GUIContent(addLabel)).x + t.Px(8));
            // the row measured at its widest, the Add button saying 999
            float rowEnd = bx + Mathf.Max(t.Px(96), t.Primary.CalcSize(new GUIContent(L.T("btn.add", "999"))).x + t.Px(8)) + t.Px(8);
            if (GUI.Button(new Rect(bx, by, addW, t.ButtonH), addLabel, t.Primary)) Add(null, false);
            bx += addW + t.Px(8);
            foreach (var b in new[] { "btn.one", "btn.ten", "btn.fill" })
            {
                string lab = L.T(b);
                float bw = Mathf.Max(t.Px(56), t.Button.CalcSize(new GUIContent(lab)).x + t.Px(8));
                if (GUI.Button(new Rect(bx, by, bw, t.ButtonH), lab, t.Button))
                {
                    if (b == "btn.one") Add(1, false);
                    else if (b == "btn.ten") Add(10, false);
                    else Add(null, true);
                }
                bx += bw + t.Px(6);
                rowEnd += bw + t.Px(6);
            }
            if (MeasureFit && e.type == EventType.Repaint && rowEnd - t.Px(6) > d.xMax - p)
                Overflow.Add(L.Current + " buttons: " + L.T("btn.add", "999") + " | " + L.T("btn.one") + " | " + L.T("btn.ten") + " | " + L.T("btn.fill"));
            GUI.enabled = was;
        }

        // one line: whole if it fits, else cut with "…" (said in Truncated when measuring)
        private string OneLine(string text, GUIStyle st, float width, string where)
        {
            if (string.IsNullOrEmpty(text) || st.CalcSize(new GUIContent(text)).x <= width + 1) return text;
            if (MeasureFit && Event.current.type == EventType.Repaint) Truncated.Add(L.Current + " " + where + ": " + text);
            for (int n = text.Length - 1; n >= 1; n--)
            {
                string tryText = text.Substring(0, n).TrimEnd() + "…";
                if (st.CalcSize(new GUIContent(tryText)).x <= width + 1) return tryText;
            }
            return "…";
        }

        // as many lines as the room holds, cut at a word with "…"
        private string Lines(string text, GUIStyle st, float width, float height, string what)
        {
            if (st.CalcHeight(new GUIContent(text), width) <= height + 1) return text;
            if (MeasureFit && Event.current.type == EventType.Repaint) Truncated.Add(L.Current + " description " + what + ": " + text);
            string[] words = text.Split(' ');
            for (int n = words.Length - 1; n >= 1; n--)
            {
                string tryText = string.Join(" ", words, 0, n) + "…";
                if (st.CalcHeight(new GUIContent(tryText), width) <= height + 1) return tryText;
            }
            for (int n = text.Length - 1; n >= 1; n--)
            {
                string tryText = text.Substring(0, n) + "…";
                if (st.CalcHeight(new GUIContent(tryText), width) <= height + 1) return tryText;
            }
            return "…";
        }

        // the sitting's measure: a text against the room it is drawn in (one line, or wrapped into the height)
        private void Fits(string where, GUIStyle st, string text, Rect r, bool wrap)
        {
            if (!MeasureFit || Event.current.type != EventType.Repaint || string.IsNullOrEmpty(text)) return;
            bool ok = wrap ? st.CalcHeight(new GUIContent(text), r.width) <= r.height + 1 : st.CalcSize(new GUIContent(text)).x <= r.width + 1;
            if (!ok) Overflow.Add(L.Current + " " + where + ": " + text);
        }

        private static string Digits(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in s ?? "") if (c >= '0' && c <= '9') sb.Append(c);
            return sb.ToString();
        }

        // the prompts for a device: keyboard (with or without a search typed), Xbox or PlayStation names
        private List<KeyValuePair<string, string>> PromptItems(bool pad, bool ps, bool searching, bool deco)
        {
            var items = new List<KeyValuePair<string, string>>();
            if (pad)
            {
                items.Add(new KeyValuePair<string, string>(ps ? L.T("pad.cross") : "A", L.T("prompt.add")));
                items.Add(new KeyValuePair<string, string>(ps ? L.T("pad.square") : "X", L.T("prompt.ten")));
                items.Add(new KeyValuePair<string, string>(ps ? L.T("pad.triangle") : "Y", L.T(deco ? "prompt.set" : "prompt.fill")));
                items.Add(new KeyValuePair<string, string>(ps ? "L1/R1" : "LB/RB", L.T("prompt.tabs")));
                items.Add(new KeyValuePair<string, string>(ps ? L.T("pad.circle") : "B", L.T("prompt.close")));
            }
            else
            {
                items.Add(new KeyValuePair<string, string>("Enter", L.T("prompt.add")));
                items.Add(new KeyValuePair<string, string>("↑↓←→", L.T("prompt.move")));
                if (searching)
                {
                    items.Add(new KeyValuePair<string, string>("Esc", L.T("prompt.clear")));
                    items.Add(new KeyValuePair<string, string>(host.KeyLabel, L.T("prompt.close")));
                }
                else items.Add(new KeyValuePair<string, string>("Esc", L.T("prompt.close")));
            }
            return items;
        }

        private float PromptsWidth(List<KeyValuePair<string, string>> items)
        {
            var t = Theme;
            float w = 0;
            foreach (var kv in items) w += t.Key.CalcSize(new GUIContent(kv.Key)).x + t.Px(6) + t.Prompt.CalcSize(new GUIContent(kv.Value)).x + t.Px(14);
            return w - t.Px(14);
        }

        private void DrawPrompts(Rect f)
        {
            var t = Theme;
            var items = PromptItems(input.UsingPad, input.PlayStation, query.Length > 0, tab == TabDeco);
            // every variant measured, not only the device in hand: a controller player's row must fit too, on either tab
            if (MeasureFit && Event.current.type == EventType.Repaint)
                foreach (var v in new[] { new[] { false, false, false, false }, new[] { false, false, true, false }, new[] { true, false, false, false }, new[] { true, true, false, false }, new[] { true, false, false, true }, new[] { true, true, false, true } })
                {
                    var row = PromptItems(v[0], v[1], v[2], v[3]);
                    if (PromptsWidth(row) > f.width + 1)
                        Overflow.Add(L.Current + " prompts (" + (v[0] ? v[1] ? "PlayStation" : "Xbox" : v[2] ? "keyboard, searching" : "keyboard") + (v[3] ? ", Decoration" : "") + "): " + string.Join(" ", row.ConvertAll(kv => kv.Key + " " + kv.Value).ToArray()));
                }
            float x = f.x;
            foreach (var kv in items)
            {
                float kw = t.Key.CalcSize(new GUIContent(kv.Key)).x;
                float kh = t.Px(18);
                var kr = new Rect(x, f.y + (f.height - kh) / 2, kw, kh);
                if (x + kw > f.xMax) break;
                t.Plate(kr, Theme.Raised, t.Px(4));
                GUI.Label(kr, kv.Key, t.Key);
                x += kw + t.Px(6);
                float lw = t.Prompt.CalcSize(new GUIContent(kv.Value)).x;
                if (x + lw > f.xMax) break;
                GUI.Label(new Rect(x, f.y + (f.height - t.Prompt.lineHeight) / 2, lw + 2, f.height), kv.Value, t.Prompt);
                x += lw + t.Px(14);
            }
        }
    }
}

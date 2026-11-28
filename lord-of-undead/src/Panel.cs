// Panel: the F7 box, drawn with IMGUI on the right side of the screen, full height, see-through.
// Its text box is ours (key events read one by one), not GUI.TextField: the game's build strips TextField, and the
// interop this mod compiles against is the stripped one (tools/interop-gen), so nothing here relies on a method the game
// may not carry. Ctrl+V pastes. While it is open the game gets no keyboard or mouse (the Input System devices are switched
// off, then back on when it closes), so typing "wood" never walks you forward.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace GamesOMG.LordOfUndeadTeleport
{
    internal sealed class Panel
    {
        public bool Open { get; private set; }
        public string Text = "";
        public string Status;
        public bool StatusBad;
        public int Selected;
        int scroll;
        List<Place> results = new List<Place>();
        string lastQuery;
        readonly List<Place> places;
        Texture2D bg, rowSel, line;
        GUIStyle title, body, small, box, row, rowSub, status, statusBad, hint;
        float scale = 1f;

        public Action<TpTarget, string> OnGo;     // a place picked or a /tp line entered
        public Func<Vector3?> PlayerPos;
        public Action OnCopyHere;
        public Action OnBack;
        public string Keys = "F7 / F10";
        public string BackKey = "F10";

        public Panel(List<Place> places) { this.places = places; }

        public void Show(string text = null)
        {
            Open = true;
            if (text != null) Text = text;
            Status = null;
            Selected = 0;
            scroll = 0;
            lastQuery = null;
        }

        public void Hide() { Open = false; }

        // the game's build keeps only the long DrawTexture overload
        static void Tex(Rect r, Texture2D t) => GUI.DrawTexture(r, t, ScaleMode.StretchToFill, true, 0f);

        static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            return t;
        }

        void Styles()
        {
            float s = Mathf.Clamp(Screen.height / 1080f, 0.8f, 2.2f);
            if (title != null && Math.Abs(s - scale) < 0.01f && bg != null) return;
            scale = s;
            bg = Solid(new Color(0.07f, 0.08f, 0.10f, 0.93f));
            rowSel = Solid(new Color(0.24f, 0.45f, 0.80f, 0.55f));
            line = Solid(new Color(1f, 1f, 1f, 0.10f));
            GUIStyle L(int size, Color c, FontStyle f = FontStyle.Normal)
            {
                var st = new GUIStyle(GUI.skin.label) { fontSize = (int)(size * scale), fontStyle = f, wordWrap = true, richText = false };
                st.normal.textColor = c;
                return st;
            }
            title = L(22, new Color(0.95f, 0.96f, 0.98f), FontStyle.Bold);
            body = L(16, new Color(0.88f, 0.90f, 0.93f));
            small = L(13, new Color(0.62f, 0.66f, 0.72f));
            hint = L(15, new Color(0.62f, 0.66f, 0.72f));
            row = L(16, new Color(0.95f, 0.96f, 0.98f), FontStyle.Bold);
            row.wordWrap = false;
            rowSub = L(13, new Color(0.66f, 0.70f, 0.76f));
            rowSub.wordWrap = false;
            status = L(15, new Color(0.55f, 0.85f, 0.60f));
            statusBad = L(15, new Color(1f, 0.62f, 0.55f));
            box = new GUIStyle(GUI.skin.box) { fontSize = (int)(18 * scale), alignment = TextAnchor.MiddleLeft };
            box.normal.textColor = Color.white;
            box.padding = new RectOffset((int)(10 * scale), (int)(10 * scale), 0, 0);
        }

        /// <summary>Feeds one IMGUI event to the box. Returns true when it used it.</summary>
        public bool Key(Event e, KeyCode closeKey)
        {
            if (!Open || e.type != EventType.KeyDown) return false;
            var k = e.keyCode;
            if (k == KeyCode.Escape || (k == closeKey && k != KeyCode.None)) { Hide(); return true; }
            if (k == KeyCode.Return || k == KeyCode.KeypadEnter) { Enter(); return true; }
            if (k == KeyCode.UpArrow) { Selected = Math.Max(0, Selected - 1); return true; }
            if (k == KeyCode.DownArrow) { Selected = Math.Min(Math.Max(0, results.Count - 1), Selected + 1); return true; }
            if (k == KeyCode.PageUp) { Selected = Math.Max(0, Selected - 8); return true; }
            if (k == KeyCode.PageDown) { Selected = Math.Min(Math.Max(0, results.Count - 1), Selected + 8); return true; }
            if (k == KeyCode.Backspace) { if (Text.Length > 0) Text = e.control ? "" : Text.Substring(0, Text.Length - 1); return true; }
            if (k == KeyCode.Delete) { Text = ""; return true; }
            if (k == KeyCode.V && (e.control || e.command)) { Paste(GUIUtility.systemCopyBuffer); return true; }
            char ch = e.character;
            if (ch != '\0' && !char.IsControl(ch) && !e.control && !e.alt)
            {
                if (Text.Length < 160) Text += ch;
                return true;
            }
            return k != KeyCode.None;   // swallow the rest (the game has no keyboard while the box is open anyway)
        }

        public void Paste(string clip)
        {
            if (string.IsNullOrEmpty(clip)) return;
            clip = clip.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Trim();
            if (clip.Length > 160) clip = clip.Substring(0, 160);
            Text = clip;
        }

        void Enter()
        {
            if (TpLine.TryParse(Text, out var t)) { OnGo?.Invoke(t, null); return; }
            if (results.Count > 0)
            {
                var p = results[Math.Min(Selected, results.Count - 1)];
                OnGo?.Invoke(p.Target, p.Title);
                return;
            }
            Status = TpLine.LooksLikeTp(Text) ? "That /tp line has no x and z in it. Copy one from the map again." :
                     Text.Trim().Length == 0 ? "Paste a /tp line (Ctrl+V) or type a place." : "No place matches \"" + Text.Trim() + "\".";
            StatusBad = true;
        }

        public void Draw()
        {
            if (!Open) return;
            Styles();
            float w = Mathf.Min(480f * scale, Screen.width * 0.9f), h = Screen.height, x0 = Screen.width - w, pad = 18f * scale;
            Tex(new Rect(x0, 0, w, h), bg);
            float x = x0 + pad, iw = w - 2 * pad, y = pad;
            GUI.Label(new Rect(x, y, iw, 32 * scale), "GamesOMG Teleport", title); y += 34 * scale;
            GUI.Label(new Rect(x, y, iw, 40 * scale), "Paste a /tp line from gamesomg.com/lord-of-undead-map (Ctrl+V), or type a place. Enter goes.", hint);
            y += 44 * scale;
            // the box, with a caret that blinks
            bool caret = ((int)(Time.unscaledTime * 2)) % 2 == 0;
            GUI.Box(new Rect(x, y, iw, 38 * scale), new GUIContent(Text + (caret ? "|" : " ")), box);
            y += 46 * scale;

            if (Text != lastQuery)
            {
                lastQuery = Text;
                results = TpLine.TryParse(Text, out _) ? new List<Place>() : Places.Search(places, Text);
                Selected = 0; scroll = 0;
                if (Status != null && !StatusBad) Status = null;
            }
            if (Status != null)
            {
                GUI.Label(new Rect(x, y, iw, 44 * scale), Status, StatusBad ? statusBad : status);
                y += 46 * scale;
            }

            if (TpLine.TryParse(Text, out var line1))
            {
                GUI.Label(new Rect(x, y, iw, 26 * scale), "Enter: teleport to " + line1 + (line1.Y.HasValue ? "" : " (the ground there)"), body);
                y += 30 * scale;
            }
            else if (Text.Trim().Length == 0)
            {
                GUI.Label(new Rect(x, y, iw, 120 * scale),
                    "Try: copper mine · rare chest · troll · graveyard · spider queen.\nEvery pin on the map is in here: places, chests, ore, plants, camps and the enemies they hold.\nYou land beside the place, never on it.", body);
                y += 124 * scale;
            }

            float footer = 96 * scale, rowH = 44 * scale;
            int visible = Math.Max(1, (int)((h - y - footer) / rowH));
            if (Selected < scroll) scroll = Selected;
            if (Selected >= scroll + visible) scroll = Selected - visible + 1;
            var e = Event.current;
            Vector3? me = PlayerPos?.Invoke();
            for (int i = scroll; i < results.Count && i < scroll + visible; i++)
            {
                var p = results[i];
                var r = new Rect(x - 6 * scale, y, iw + 12 * scale, rowH - 2 * scale);
                if (i == Selected) Tex(r, rowSel);
                GUI.Label(new Rect(x, y + 2 * scale, iw, 22 * scale), p.Title, row);
                string sub = p.LayerName + (p.Region != null ? " · " + p.Region : "") +
                             (me.HasValue ? " · " + Places.Away(me.Value.x, me.Value.z, p.X, p.Z) : "");
                GUI.Label(new Rect(x, y + 22 * scale, iw, 20 * scale), sub, rowSub);
                if (e != null && e.type == EventType.MouseDown && r.Contains(e.mousePosition))
                {
                    Selected = i;
                    if (e.button == 0) OnGo?.Invoke(p.Target, p.Title);
                    e.Use();
                }
                y += rowH;
            }
            if (results.Count > visible)
                GUI.Label(new Rect(x, y, iw, 20 * scale), (scroll + 1) + "-" + Math.Min(results.Count, scroll + visible) + " of " + results.Count + "  (↑ ↓ to see more)", small);
            if (e != null && e.type == EventType.ScrollWheel && new Rect(x0, 0, w, h).Contains(e.mousePosition))
            {
                Selected = Math.Max(0, Math.Min(results.Count - 1, Selected + (e.delta.y > 0 ? 3 : -3)));
                e.Use();
            }

            // footer: the keys, Back and Copy where you stand as buttons too
            float fy = h - footer + 8 * scale;
            Tex(new Rect(x, fy - 6 * scale, iw, 1), line);
            float bw = (iw - 10 * scale) / 2, bh = 34 * scale;
            var rb = new Rect(x, fy, bw, bh); var rc = new Rect(x + bw + 10 * scale, fy, bw, bh);
            GUI.Box(rb, new GUIContent("Back (" + BackKey + ")"), box); GUI.Box(rc, new GUIContent("Copy where I stand"), box);
            if (e != null && e.type == EventType.MouseDown && e.button == 0)
            {
                if (rb.Contains(e.mousePosition)) { OnBack?.Invoke(); e.Use(); }
                else if (rc.Contains(e.mousePosition)) { OnCopyHere?.Invoke(); e.Use(); }
            }
            GUI.Label(new Rect(x, fy + bh + 6 * scale, iw, 20 * scale), "Enter go · ↑ ↓ pick · Esc close · " + Keys + " · free from gamesomg.com", small);
        }
    }
}

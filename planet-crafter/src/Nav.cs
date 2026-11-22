// GamesOMG Teleport 0.3.0: focus without a mouse (keyboard arrows/Tab, a controller's D-pad and stick). IMGUI has no focus
// for buttons, so the panel registers every control it draws, in order, each frame; Nav keeps focus on the same control by
// its id while the list rebuilds, moves it, remembers where it was before a dialog and puts it back after, and says how far
// to scroll so the focused control is in view. No Unity here: the tests drive it.
using System;
using System.Collections.Generic;

namespace GamesOMG.PlanetCrafterTeleport
{
    internal sealed class Nav
    {
        private readonly List<string> order = new List<string>(), building = new List<string>();
        private readonly Dictionary<string, float[]> rects = new Dictionary<string, float[]>(StringComparer.Ordinal);   // id -> y, height (scroll content)
        private readonly Stack<string> saved = new Stack<string>();
        private int lastIndex;

        public string Focus { get; private set; } = "";
        public bool Visible;           // focus is drawn only after a key or button moved it (a mouse user never sees it)
        public bool Moved;             // focus moved by a key this frame: scroll it into view

        public IReadOnlyList<string> Order => order;

        public void Begin() => building.Clear();

        public void Add(string id)
        {
            if (!string.IsNullOrEmpty(id) && !building.Contains(id)) building.Add(id);
        }

        // the frame's controls are known: keep focus on the same id; if it went away, the control now where it stood
        public void End()
        {
            order.Clear();
            order.AddRange(building);
            if (order.Count == 0) { Focus = ""; return; }
            int at = order.IndexOf(Focus);
            if (at < 0) { Focus = order[Math.Min(Math.Max(lastIndex, 0), order.Count - 1)]; at = order.IndexOf(Focus); }
            lastIndex = at;
        }

        public bool Is(string id) => Focus == id;

        public void Set(string id, bool byKey = false)
        {
            if (string.IsNullOrEmpty(id)) return;
            Focus = id;
            int at = order.IndexOf(id);
            if (at >= 0) lastIndex = at;
            if (byKey) { Visible = true; Moved = true; }
        }

        // +1 next, -1 previous; stops at the ends (no wrap: a long list should not jump to its top)
        public void Move(int step)
        {
            if (order.Count == 0) return;
            int at = order.IndexOf(Focus);
            if (at < 0) at = Math.Min(Math.Max(lastIndex, 0), order.Count - 1);
            else if (Visible) at = Math.Min(Math.Max(at + step, 0), order.Count - 1);
            Set(order[at], true);
        }

        public void First() { if (order.Count > 0) Set(order[0], true); }
        public void Last() { if (order.Count > 0) Set(order[order.Count - 1], true); }

        // the next control whose id starts with prefix (Tab between areas), else the next one
        public void NextArea(int step, Func<string, string> areaOf)
        {
            if (order.Count == 0) return;
            int at = Math.Max(order.IndexOf(Focus), 0);
            string area = areaOf(order[at]);
            for (int i = at + step; i >= 0 && i < order.Count; i += step)
                if (areaOf(order[i]) != area) { Set(order[i], true); return; }
            Set(order[step > 0 ? 0 : order.Count - 1], true);
        }

        // a dialog opens: remember where focus was; it closes: back there (or where that control's neighbour is now)
        public void Push(string focusInDialog)
        {
            saved.Push(Focus);
            Focus = focusInDialog ?? "";
        }

        public void Pop()
        {
            if (saved.Count == 0) return;
            Focus = saved.Pop();
            Moved = Visible;
        }

        // ── scrolling: each control's place inside the scroll view, as the last repaint drew it ──
        public void Place(string id, float y, float height) => rects[id] = new[] { y, height };

        public void ClearPlaces() => rects.Clear();

        // the scroll offset that shows the focused control whole, keeping the offset when it already does
        public float ScrollFor(float scroll, float viewHeight, float margin = 0)
        {
            if (!rects.TryGetValue(Focus, out var r)) return scroll;
            float top = r[0] - margin, bottom = r[0] + r[1] + margin;
            if (top < scroll) return Math.Max(0, top);
            if (bottom > scroll + viewHeight) return Math.Max(0, bottom - viewHeight);
            return scroll;
        }
    }
}

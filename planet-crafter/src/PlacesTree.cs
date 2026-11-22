// GamesOMG Teleport 0.2.3: the Places tab as a tree (2026-11-19, from play: headers that fold open with an
// arrow, and a layer's places opening right under it instead of at the bottom of the list).
//   ▾ NEAREST            the old flat list, now on top: nearest places on the layers ticked on
//   ▸ RARE ORES          a group header folds
//     ✓ ▾ Golden crates   3/48   a layer row: the tick = on (feeds Nearest), the name opens its places right under it
//         ✓ Golden crate   212 m      nearest first, the visited tick, click = teleport
//         Show 25 more · 33 left
// A search is a flat list of matches, as before. What is open is kept per planet for the game session (the Panel lives as
// long as the plugin). No Unity here: Panel draws the rows, the WSL tests check them.
// 0.3.1 Found (player feedback 11-20 17:26, "the super alloy list is long ... rather than darken them out, or strike them, move them?"):
// a ticked place leaves the list and sits in a Found drawer at the bottom, closed until opened, under the same group and
// layer headings in the same order; unticked, it goes back. A layer (or group) with every place found leaves the list.
//   > FOUND              12   the drawer: closed by default
//     v RARE ORES         9   inside it the headings start open
//       v Super Alloy     7
//           [x] Super Alloy  212 m
// The drawer is built from the save's own ticks (the same Visited set 0.2.x kept), so nothing changes in the places file.
// "Hide found" now hides the drawer; a search lists the found matches in it, open.
using System;
using System.Collections.Generic;

namespace GamesOMG.PlanetCrafterTeleport
{
    internal enum RowKind { Nearest, Group, Layer, Place, More, Note, Found }

    internal sealed class TreeRow
    {
        public RowKind Kind;
        public int Depth;          // 0 = a header or a flat search row, 1 = under a header, 2 = a place under a layer
        public string Group = "";  // Group: its label; Layer/Place/More: the group it sits in
        public int Cat = -1;       // Layer/Place/More: the layer
        public Poi Poi;            // Place
        public int Count, Visited; // Layer: pins in it, how many of them are ticked visited (findable layers only)
        public int Left;           // More: how many more there are
        public bool Open, On;      // Nearest/Group/Layer: folded open; Layer: ticked on
        public string Text = "";   // Note
        public bool InFound;       // 0.3.1: the Found drawer's header and every row inside it
    }

    internal sealed class PlacesTree
    {
        public const int NearestMax = 10, LayerFirst = 15, LayerMore = 25, FindMax = 60;

        public readonly HashSet<int> On;                                                        // layers ticked on (feed Nearest)
        public readonly HashSet<string> OpenGroups = new HashSet<string>(StringComparer.Ordinal);  // group labels folded open
        public readonly HashSet<string> OpenLayers = new HashSet<string>(StringComparer.Ordinal);  // layer keys folded open
        public readonly Dictionary<string, int> Cap = new Dictionary<string, int>(StringComparer.Ordinal); // per layer: how many places show
        public bool NearestOpen = true;
        public bool ByName;   // 0.3.0 Sort: A-Z inside each layer and in a search; Nearest stays nearest by what it is
        // 0.3.1 the Found drawer: closed until opened; inside it every heading starts open, so these hold the ones folded shut
        public bool FoundOpen;
        public readonly HashSet<string> FoundShutGroups = new HashSet<string>(StringComparer.Ordinal);
        public readonly HashSet<string> FoundShutLayers = new HashSet<string>(StringComparer.Ordinal);
        public readonly Dictionary<string, int> FoundCap = new Dictionary<string, int>(StringComparer.Ordinal);

        public PlacesTree(HashSet<int> on) { On = on ?? new HashSet<int>(); }

        public bool GroupOpen(string group) => OpenGroups.Contains(group);
        public bool LayerOpen(PoiWorld pw, int cat) => OpenLayers.Contains(pw.Cats[cat].Key);
        public void ToggleGroup(string group) { if (!OpenGroups.Remove(group)) OpenGroups.Add(group); }
        public void ToggleLayer(PoiWorld pw, int cat)
        {
            string key = pw.Cats[cat].Key;
            if (!OpenLayers.Remove(key)) OpenLayers.Add(key);
            else Cap.Remove(key);   // folding a layer resets its Show more
        }
        // the tick: on/off. Ticking a layer ON also opens it ("open as a sub area under the things u select"); off leaves it as it is
        public void ToggleOn(PoiWorld pw, int cat)
        {
            if (On.Remove(cat)) return;
            On.Add(cat);
            OpenLayers.Add(pw.Cats[cat].Key);
        }
        public void ShowMore(PoiWorld pw, int cat)
        {
            string key = pw.Cats[cat].Key;
            Cap[key] = CapOf(key) + LayerMore;
        }
        private int CapOf(string key) => Cap.TryGetValue(key, out int n) ? n : LayerFirst;

        public void ToggleDrawer() => FoundOpen = !FoundOpen;
        public void ToggleFoundGroup(string group) { if (!FoundShutGroups.Remove(group)) FoundShutGroups.Add(group); }
        public void ToggleFoundLayer(PoiWorld pw, int cat)
        {
            string key = pw.Cats[cat].Key;
            if (FoundShutLayers.Add(key)) FoundCap.Remove(key);   // folding resets its Show more, as above
            else FoundShutLayers.Remove(key);
        }
        public void ShowMoreFound(PoiWorld pw, int cat)
        {
            string key = pw.Cats[cat].Key;
            FoundCap[key] = FoundCapOf(key) + LayerMore;
        }
        private int FoundCapOf(string key) => FoundCap.TryGetValue(key, out int n) ? n : LayerFirst;

        // what moves to Found: this world's ticked places on a layer that takes a tick (Mark found). A stray id from another
        // world or from a layer without ticks stays where it is, so nothing can drop out of both lists.
        public static HashSet<string> FoundOf(PoiWorld pw, HashSet<string> visited)
        {
            var found = new HashSet<string>(StringComparer.Ordinal);
            if (visited == null) return found;
            foreach (var id in visited) if (pw.ById.TryGetValue(id, out var p) && pw.Cats[p.Cat].Findable) found.Add(id);
            return found;
        }

        // the groups in the order their first layer comes in the map's own layer order
        public static List<string> Groups(PoiWorld pw)
        {
            var groups = new List<string>();
            foreach (var c in pw.Cats) if (c.Count > 0 && !groups.Contains(c.Group)) groups.Add(c.Group);
            return groups;
        }

        // hideFound ("Hide found"): no Found drawer. A found place never shows in the list above the drawer either way.
        public List<TreeRow> Rows(PoiWorld pw, string find, float x, float z, HashSet<string> visited, bool hideFound)
        {
            var rows = new List<TreeRow>();
            var found = FoundOf(pw, visited);
            bool drawer = !hideFound && found.Count > 0;
            string q = (find ?? "").Trim();
            if (q.Length > 0)
            {
                // a search: one flat list of matches across every layer, nearest first; the found ones in the drawer, open
                var hits = pw.Query(null, q, x, z, FindMax, found, out int total, ByName);
                foreach (var p in hits) rows.Add(new TreeRow { Kind = RowKind.Place, Depth = 0, Cat = p.Cat, Group = pw.Cats[p.Cat].Group, Poi = p });
                int fTotal = 0;
                var fHits = drawer ? pw.Query(null, q, x, z, FindMax, null, out fTotal, ByName, found) : new List<Poi>();
                if (hits.Count == 0 && fHits.Count == 0) rows.Add(new TreeRow { Kind = RowKind.Note, Text = L.T("tree.noMatch") });
                else if (total > hits.Count) rows.Add(new TreeRow { Kind = RowKind.Note, Text = L.T("tree.nearestOf", hits.Count, total) });
                if (fHits.Count > 0)
                {
                    rows.Add(new TreeRow { Kind = RowKind.Found, Open = true, Count = fTotal, InFound = true });
                    foreach (var p in fHits) rows.Add(new TreeRow { Kind = RowKind.Place, Depth = 1, Cat = p.Cat, Group = pw.Cats[p.Cat].Group, Poi = p, InFound = true });
                    if (fTotal > fHits.Count) rows.Add(new TreeRow { Kind = RowKind.Note, Depth = 1, InFound = true, Text = L.T("tree.nearestOf", fHits.Count, fTotal) });
                }
                return rows;
            }

            rows.Add(new TreeRow { Kind = RowKind.Nearest, Open = NearestOpen, Count = On.Count });
            if (NearestOpen)
            {
                if (On.Count == 0) rows.Add(new TreeRow { Kind = RowKind.Note, Depth = 1, Text = L.T("tree.tickLayer") });
                else
                {
                    var near = pw.Query(On, "", x, z, NearestMax, found, out _);
                    foreach (var p in near) rows.Add(new TreeRow { Kind = RowKind.Place, Depth = 1, Cat = p.Cat, Group = pw.Cats[p.Cat].Group, Poi = p });
                    if (near.Count == 0) rows.Add(new TreeRow { Kind = RowKind.Note, Depth = 1, Text = L.T("tree.allFoundTicked") });
                }
            }

            // how many of each layer are found (a layer without ticks: none)
            var nFound = new int[pw.Cats.Count];
            if (found.Count > 0) for (int i = 0; i < pw.Cats.Count; i++) if (pw.Cats[i].Findable) nFound[i] = pw.VisitedIn(i, found);

            foreach (var g in Groups(pw))
            {
                // a layer with every place found leaves the list; a group left with no layers goes too
                var left = new List<int>();
                for (int i = 0; i < pw.Cats.Count; i++)
                    if (pw.Cats[i].Count > 0 && pw.Cats[i].Group == g && pw.Cats[i].Count > nFound[i]) left.Add(i);
                if (left.Count == 0) continue;
                bool gOpen = OpenGroups.Contains(g);
                // the heading's "found / all" (as Grounded 2's headings), every layer of the group counted, the gone ones too
                int gAll = 0, gFound = 0;
                for (int i = 0; i < pw.Cats.Count; i++) if (pw.Cats[i].Group == g) { gAll += pw.Cats[i].Count; gFound += nFound[i]; }
                rows.Add(new TreeRow { Kind = RowKind.Group, Group = g, Open = gOpen, Count = gAll, Visited = gFound });
                if (!gOpen) continue;
                foreach (int i in left)
                {
                    var c = pw.Cats[i];
                    bool lOpen = OpenLayers.Contains(c.Key);
                    rows.Add(new TreeRow
                    {
                        Kind = RowKind.Layer, Depth = 1, Group = g, Cat = i, Count = c.Count,
                        Visited = nFound[i], Open = lOpen, On = On.Contains(i),
                    });
                    if (!lOpen) continue;
                    var places = pw.InLayer(i, x, z, CapOf(c.Key), found, out int total, ByName);
                    foreach (var p in places) rows.Add(new TreeRow { Kind = RowKind.Place, Depth = 2, Cat = i, Group = g, Poi = p });
                    if (total > places.Count)
                        rows.Add(new TreeRow { Kind = RowKind.More, Depth = 2, Cat = i, Group = g, Left = total - places.Count });
                }
            }

            // the Found drawer, at the bottom: the same headings, only those with a found place under them
            if (!drawer) return rows;
            rows.Add(new TreeRow { Kind = RowKind.Found, Open = FoundOpen, Count = found.Count, InFound = true });
            if (!FoundOpen) return rows;
            foreach (var g in Groups(pw))
            {
                var has = new List<int>();
                int inGroup = 0;
                for (int i = 0; i < pw.Cats.Count; i++)
                    if (pw.Cats[i].Group == g && nFound[i] > 0) { has.Add(i); inGroup += nFound[i]; }
                if (has.Count == 0) continue;
                bool gOpen = !FoundShutGroups.Contains(g);
                rows.Add(new TreeRow { Kind = RowKind.Group, Depth = 1, Group = g, Open = gOpen, Count = inGroup, InFound = true });
                if (!gOpen) continue;
                foreach (int i in has)
                {
                    var c = pw.Cats[i];
                    bool lOpen = !FoundShutLayers.Contains(c.Key);
                    rows.Add(new TreeRow { Kind = RowKind.Layer, Depth = 2, Group = g, Cat = i, Count = nFound[i], Visited = nFound[i], Open = lOpen, InFound = true });
                    if (!lOpen) continue;
                    var places = pw.InLayer(i, x, z, FoundCapOf(c.Key), null, out int total, ByName, found);
                    foreach (var p in places) rows.Add(new TreeRow { Kind = RowKind.Place, Depth = 3, Cat = i, Group = g, Poi = p, InFound = true });
                    if (total > places.Count)
                        rows.Add(new TreeRow { Kind = RowKind.More, Depth = 3, Cat = i, Group = g, Left = total - places.Count, InFound = true });
                }
            }
            return rows;
        }
    }
}

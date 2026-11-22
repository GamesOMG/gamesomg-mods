// GamesOMG Teleport 0.2.1: the Places list, the GamesOMG map's POIs for one world, packed by build-pois.mjs into
// data/pois-<world>.json.z (raw deflate) and embedded in the DLL, so it works offline. No Unity here: the WSL tests read
// the same packs. A pin teleports only to its CHECKED landing (C56's landings, the map's Copy Teleport ones): no landing,
// no teleport, as on the map.
using System;
using System.Collections.Generic;
using System.Text;

namespace GamesOMG.PlanetCrafterTeleport
{
    internal sealed class PoiCat
    {
        public string Key = "", Name = "", Group = "";
        public bool Findable, Start;   // Findable = the map's Mark found applies (cats[c].f); Start = a map start layer
        public int Count;
    }

    internal sealed class Poi
    {
        public string Id = "", Name = "";
        public int Cat;
        public float X, Z;
        public bool HasLanding;
        public float LX, LY, LZ;
        // 0.2.5: past the near rings (13 m) a landing is the far ring's: the notice says how far from the place it is
        public TeleportRequest Landing => new TeleportRequest(LX, LY, LZ, false, LandingOffset > 13.5f ? ", " + (int)Math.Round(LandingOffset) + " m from it" : null);
        public float LandingOffset => (float)Math.Sqrt((LX - X) * (LX - X) + (LZ - Z) * (LZ - Z));
        public float Distance2D(float x, float z) => (float)Math.Sqrt((X - x) * (X - x) + (Z - z) * (Z - z));
    }

    internal sealed class PoiWorld
    {
        public string World = "", Map = "", Made = "";
        public int Build;
        public readonly List<PoiCat> Cats = new List<PoiCat>();
        public readonly List<Poi> Pins = new List<Poi>();
        public readonly Dictionary<string, Poi> ById = new Dictionary<string, Poi>(StringComparer.Ordinal);

        public static PoiWorld Read(byte[] deflated)
        {
            var o = MiniJson.O(MiniJson.Parse(Encoding.UTF8.GetString(SpotSet.Inflate(deflated, 1 << 22))));
            if (o == null || MiniJson.N(MiniJson.Get(o, "v")) != 1) throw new FormatException("not a v1 POI pack");
            var w = new PoiWorld
            {
                World = MiniJson.S(MiniJson.Get(o, "world")),
                Map = MiniJson.S(MiniJson.Get(o, "map")),
                Made = MiniJson.S(MiniJson.Get(o, "made")),
                Build = (int)MiniJson.N(MiniJson.Get(o, "build"), 0),
            };
            foreach (var c in MiniJson.A(MiniJson.Get(o, "cats")) ?? new List<object>())
            {
                var r = MiniJson.A(c);
                w.Cats.Add(new PoiCat
                {
                    Key = MiniJson.S(r[0]), Name = MiniJson.S(r[1]), Group = MiniJson.S(r[2]),
                    Findable = MiniJson.N(r[3]) == 1, Start = MiniJson.N(r[4]) == 1,
                });
            }
            foreach (var p in MiniJson.A(MiniJson.Get(o, "pins")) ?? new List<object>())
            {
                var r = MiniJson.A(p);
                if (r == null || r.Count < 5) continue;
                int cat = (int)MiniJson.N(r[1], -1);
                if (cat < 0 || cat >= w.Cats.Count) continue;
                var poi = new Poi { Id = MiniJson.S(r[0]), Cat = cat, Name = MiniJson.S(r[2]), X = (float)MiniJson.N(r[3]), Z = (float)MiniJson.N(r[4]) };
                if (r.Count >= 8)
                {
                    poi.HasLanding = true;
                    poi.LX = (float)MiniJson.N(r[5]); poi.LY = (float)MiniJson.N(r[6]); poi.LZ = (float)MiniJson.N(r[7]);
                }
                w.Pins.Add(poi);
                w.ById[poi.Id] = poi;
                w.Cats[cat].Count++;
            }
            return w;
        }

        // the map's pinName fallback: the layer's own name in the singular
        public string NameOf(Poi p) => p.Name.Length > 0 ? p.Name : Singular(Cats[p.Cat].Name);
        private static string Singular(string n) => n.EndsWith("s", StringComparison.Ordinal) ? n.Substring(0, n.Length - 1) : n;

        // the layers a fresh list opens on: the map's start layers this world has, else its first findable layer
        public HashSet<int> StartLayers()
        {
            var on = new HashSet<int>();
            for (int i = 0; i < Cats.Count; i++) if (Cats[i].Start && Cats[i].Count > 0) on.Add(i);
            if (on.Count == 0) for (int i = 0; i < Cats.Count; i++) if (Cats[i].Findable && Cats[i].Count > 0) { on.Add(i); break; }
            return on;
        }

        // nearest first. A search looks through every layer (a thing you name is never hidden by a layer left off);
        // without one, the layers that are on. hideIds: left out (0.3.1: the found, which sit in the Found drawer instead);
        // onlyIds: when set, nothing else (the Found drawer's own search)
        public List<Poi> Query(HashSet<int> layers, string search, float x, float z, int limit, HashSet<string> hideIds, out int total, bool byName = false, HashSet<string> onlyIds = null)
        {
            string q = (search ?? "").Trim();
            var hits = new List<Poi>();
            foreach (var p in Pins)
            {
                if (hideIds != null && hideIds.Contains(p.Id)) continue;
                if (onlyIds != null && !onlyIds.Contains(p.Id)) continue;
                if (q.Length > 0)
                {
                    if (NameOf(p).IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 && Cats[p.Cat].Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                }
                else if (layers == null || !layers.Contains(p.Cat)) continue;
                hits.Add(p);
            }
            return Nearest(hits, x, z, limit, out total, byName);
        }

        // 0.3.0 Sort: nearest first (the default), or A-Z by the name shown (then nearest among equal names)
        private List<Poi> Nearest(List<Poi> hits, float x, float z, int limit, out int total, bool byName = false)
        {
            total = hits.Count;
            if (byName)
                hits.Sort((a, b) =>
                {
                    int c = string.Compare(NameOf(a), NameOf(b), StringComparison.OrdinalIgnoreCase);
                    return c != 0 ? c : a.Distance2D(x, z).CompareTo(b.Distance2D(x, z));
                });
            else hits.Sort((a, b) => a.Distance2D(x, z).CompareTo(b.Distance2D(x, z)));
            if (hits.Count > limit) hits.RemoveRange(limit, hits.Count - limit);
            return hits;
        }

        // 0.2.3 tree: one layer's places, nearest first (hideIds: the visited, when "Hide visited" is on)
        private List<Poi>[] byCat;
        private List<Poi> PinsOf(int cat)
        {
            if (byCat == null)
            {
                byCat = new List<Poi>[Cats.Count];
                for (int i = 0; i < byCat.Length; i++) byCat[i] = new List<Poi>();
                foreach (var p in Pins) byCat[p.Cat].Add(p);
            }
            return cat >= 0 && cat < byCat.Length ? byCat[cat] : new List<Poi>();
        }

        public List<Poi> InLayer(int cat, float x, float z, int limit, HashSet<string> hideIds, out int total, bool byName = false, HashSet<string> onlyIds = null)
        {
            var hits = new List<Poi>();
            foreach (var p in PinsOf(cat))
                if ((hideIds == null || !hideIds.Contains(p.Id)) && (onlyIds == null || onlyIds.Contains(p.Id))) hits.Add(p);
            return Nearest(hits, x, z, limit, out total, byName);
        }

        // 0.3.0: the closest place to a spot within `within` metres (flat), or null. Names Save here's automatic name and
        // the Return spot ("Return to Golden crate")
        public Poi Closest(float x, float z, float within)
        {
            Poi best = null;
            float bestD = within;
            foreach (var p in Pins)
            {
                float d = p.Distance2D(x, z);
                if (d <= bestD) { best = p; bestD = d; }
            }
            return best;
        }

        public int VisitedIn(int cat, HashSet<string> visited)
        {
            if (visited == null || visited.Count == 0) return 0;
            int n = 0;
            foreach (var p in PinsOf(cat)) if (visited.Contains(p.Id)) n++;
            return n;
        }
    }
}

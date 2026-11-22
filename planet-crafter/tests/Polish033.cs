// GamesOMG Teleport 0.3.3 (player feedback 2026-11-21 13:00, in game on 0.3.2): the Unity-free half. The distance line says how high
// a place is only when that changes what you'd do (RowMeta), a compact row drops the height before the distance, and a key
// the game's own play map uses is refused when you pick it (KeyCheck.Refuse; the 0.3.0 sitting took F8 without a word).
using System;
using System.Collections.Generic;
using System.IO;
using GamesOMG.PlanetCrafterTeleport;

internal static class Polish033
{
    public static List<string> Run(Func<string, PoiWorld> pack)
    {
        var fails = new List<string>();
        void Expect(bool ok, string name) { if (!ok) fails.Add(name); }

        // ── H. the height rule ──
        Expect(!RowMeta.ShowsHeight(115, -26, false), "H1 26 m below, 115 m away: distance only (0.3.2 said \"26 m below you\")");
        Expect(!RowMeta.ShowsHeight(205, -7, false) && !RowMeta.ShowsHeight(20, 5, false), "H2 a few metres up or down never shows");
        Expect(RowMeta.ShowsHeight(30, -26, false) && RowMeta.ShowsHeight(0, 20, false), "H3 mostly straight up or down, 20 m or more: it shows");
        Expect(!RowMeta.ShowsHeight(10, 19.9f, false), "H4 under 20 m never shows, however close");
        Expect(!RowMeta.ShowsHeight(1500, 60, false), "H5 60 m higher, 1.5 km off: terrain, not news");
        Expect(RowMeta.ShowsHeight(500, -5, true) && !RowMeta.ShowsHeight(500, -2, true), "H6 a cave place says above/below from 3 m");
        Expect(RowMeta.Underground("Rainbow Cave: ice melts", "Opens as you terraform") && RowMeta.Underground("Underground Lake: rocks sink", null)
            && RowMeta.Underground("Golden crate", "Caves") && !RowMeta.Underground("Golden crate", "Crates & lockers") && !RowMeta.Underground("", null),
            "H7 a cave or underground place, by its name or its layer's");

        // the share of rows that show a height, the same sample as RowMeta's comment: from 400 landings per world, the 30
        // nearest places (0.3.0/0.3.2: |dy| >= 3 m, about 68-70 %)
        foreach (var w in new[] { "prime", "humble", "toxicity" })
        {
            var pw = pack(w);
            var landed = pw.Pins.FindAll(p => p.HasLanding);
            int n = 0, before = 0, now = 0;
            for (int s = 0; s < 400; s++)
            {
                var me = landed[(s * 7919) % landed.Count];
                var near = new List<KeyValuePair<float, Poi>>();
                foreach (var p in landed) if (p != me) near.Add(new KeyValuePair<float, Poi>((float)Math.Sqrt((p.LX - me.LX) * (p.LX - me.LX) + (p.LZ - me.LZ) * (p.LZ - me.LZ)), p));
                near.Sort((a, b) => a.Key.CompareTo(b.Key));
                for (int i = 0; i < 30 && i < near.Count; i++)
                {
                    float dy = near[i].Value.LY - me.LY;
                    n++;
                    if (Math.Abs(dy) >= 3) before++;
                    if (RowMeta.ShowsHeight(near[i].Key, dy, RowMeta.Underground(pw.NameOf(near[i].Value), pw.Cats[near[i].Value.Cat].Name))) now++;
                }
            }
            Expect(now * 4 <= n && now * 2 < before, "H8 " + w + ": height on " + (100 * now / n) + " % of rows (0.3.2: " + (100 * before / n) + " %), at most a quarter");
        }

        // ── M. a one-line row that doesn't fit: the height goes first, then the layer name; the distance stays ──
        Func<string, float> len = t => t.Length;
        const string lead = "Storage crates · 115 m", tail = "26 m below you";
        Expect(RowMeta.Fit(lead, tail, len, 99) == lead + " · " + tail, "M1 room for it all: all of it");
        Expect(RowMeta.Fit(lead, tail, len, lead.Length) == lead, "M2 a little short: the height goes, the layer stays");
        Expect(RowMeta.Fit(lead, tail, len, 8) == "115 m", "M3 short: only the distance");
        Expect(RowMeta.Fit(lead, tail, len, 2) == "115 m", "M4 too short even for that: still the distance (clipped), never the height");
        Expect(RowMeta.Fit("115 m", null, len, 2) == "115 m" && RowMeta.Fit("", null, len, 5) == "", "M5 no tail, no layer: as it was");
        Expect(RowMeta.Join("", "x") == "x" && RowMeta.Join("a", null) == "a" && RowMeta.Join("a", "b") == "a · b", "M6 joins skip the empty");

        // ── K. keys: a key the game's play map uses is refused (the 0.3.0 sitting took F8, the game's Show feedback) ──
        var game = new List<GameBinding>();
        var bj = MiniJson.O(MiniJson.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "bindings-all.json"))));
        foreach (var o in MiniJson.A(bj["bindings"])) { var g = MiniJson.O(o); game.Add(new GameBinding { Map = MiniJson.S(g["map"]), Action = MiniJson.S(g["action"]), Path = MiniJson.S(g["path"]) }); }
        var ours = new List<KeyValuePair<string, string>> { new("Places", "<Keyboard>/f3"), new("Teleport", "<Keyboard>/f7"), new("Return", "<Keyboard>/f10"), new("Places (controller)", "<Gamepad>/select"), new("Return (controller)", "") };
        var f8 = KeyCheck.Refuse("Places", "<Keyboard>/F8", ours, game);
        Expect(f8 != null && f8.Game && f8.With == "ShowFeedback", "K1 F8 for Places: refused, the game's Show feedback");
        var f4 = KeyCheck.Refuse("Places", "<Keyboard>/f4", ours, game);
        Expect(f4 != null && f4.Game && f4.With == "Unstuck", "K2 F4: refused, the game's Unstuck");
        bool defaultsPass = true;
        foreach (var kv in ours) if (KeyCheck.Refuse(kv.Key, kv.Value, ours, game) != null) defaultsPass = false;
        Expect(defaultsPass, "K3 the defaults (F3, F7, F10, View/Share, none) each pass");
        Expect(KeyCheck.Refuse("Places", "<Keyboard>/f6", ours, game) == null, "K4 F6 is free: taken");
        var mine = KeyCheck.Refuse("Places", "<Keyboard>/f7", ours, game);
        Expect(mine != null && !mine.Game && mine.With == "Teleport", "K5 our own Teleport key: refused as ours");
        Expect(KeyCheck.Refuse("Places", "", ours, game) == null && KeyCheck.Refuse("Places", "<Keyboard>/f8", ours, null) == null,
            "K6 not set always may; no game bindings read: only our own are checked");
        Expect(KeyCheck.Refuse("Places", "<Keyboard>/f3", ours, game) == null, "K7 setting a key to what it already is passes");

        return fails;
    }
}

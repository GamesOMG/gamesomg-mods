// "Return to …" names the place you teleported to while you're within 30 m of where it landed you (in game on 0.3.3,
// 2026-11-24: Floating Rocks, then Offline drone, and the Return button said "Return to Osmium"). PlaceStore.NameForSpot
// and Arrived, driven the way Plugin.Teleport and Remember drive them, against the real Humble places pack.
using System;
using System.Collections.Generic;
using GamesOMG.PlanetCrafterTeleport;

internal static class ReturnName
{
    public static List<string> Run(Func<string, PoiWorld> pack)
    {
        var fails = new List<string>();
        void Expect(bool ok, string name) { if (!ok) fails.Add(name); }
        var humble = pack("humble");
        var prime = pack("prime");
        Func<PoiWorld, Func<float, float, string>> near = pw => (x, z) => pw.Closest(x, z, PlaceStore.SpotNameReach) is Poi p ? pw.NameOf(p) : null;
        var humbleNear = near(humble);

        // the Floating Rocks pin whose landing has an Osmium vein nearer than the rocks' own pin (7.5 m against 8.6 m)
        var rocks = humble.ById["hb50370f4"];
        var drone = humble.ById["hd9785fe5"];
        string rocksName = humble.NameOf(rocks), droneName = humble.NameOf(drone);
        Expect(rocksName.StartsWith("Floating Rocks", StringComparison.Ordinal), "R0 the fixture is Floating Rocks (" + rocksName + ")");
        Expect(droneName == "Offline drone", "R0b the second fixture is an Offline drone (" + droneName + ")");
        string nearest = humbleNear(rocks.LX, rocks.LZ);
        Expect(nearest != null && nearest != rocksName && nearest.StartsWith("Osmium", StringComparison.Ordinal),
            "R1 the nearest-name rule alone says " + nearest + " at that landing: the bug, reproduced");

        var s = new PlaceStore();
        s.Use("Standard-1", 11u, _ => true);
        Expect(s.NameForSpot("humble", rocks.LX, rocks.LZ, humbleNear) == nearest, "R2 before any teleport, the nearest-name rule is unchanged");

        // the reported trip, step by step as Plugin does it: arrive at Floating Rocks; teleport to the drone (Return recorded
        // from where you stand, named before the move); then Return, and Return again
        s.Arrived("humble", rocksName, rocks.LX, rocks.LZ);
        string fromName = s.NameForSpot("humble", rocks.LX, rocks.LZ, humbleNear);
        s.Teleported("humble", rocks.LX, rocks.LY, rocks.LZ, fromName);
        s.Arrived("humble", droneName, drone.LX, drone.LZ);
        Expect(s.Back("humble")?.Name == rocksName, "R3 after Floating Rocks -> Offline drone, Return says Floating Rocks, not " + s.Back("humble")?.Name);
        var back = s.Back("humble");
        fromName = s.NameForSpot("humble", drone.LX, drone.LZ, humbleNear);
        s.Teleported("humble", drone.LX, drone.LY, drone.LZ, fromName);
        s.Arrived("humble", back.Name, back.X, back.Z);
        Expect(s.Back("humble")?.Name == droneName, "R4 pressing Return again swaps to the drone, by name");
        Expect(s.NameForSpot("humble", rocks.LX, rocks.LZ, humbleNear) == rocksName, "R5 back at the rocks after Return, they name it again");

        // how far "still there" reaches
        Expect(s.NameForSpot("humble", rocks.LX + 20, rocks.LZ + 20, humbleNear) == rocksName, "R6 28 m off the landing: still Floating Rocks");
        Expect(s.NameForSpot("humble", rocks.LX + 40, rocks.LZ, humbleNear) == (humbleNear(rocks.LX + 40, rocks.LZ) ?? ""),
            "R7 40 m off the landing: the nearest-name rule again");

        // your own place beside it: the place you went to wins while you're there; without one, yours names it as before
        s.Save("humble", "Rock base", rocks.LX + 5, rocks.LY, rocks.LZ);
        Expect(s.NameForSpot("humble", rocks.LX, rocks.LZ, humbleNear) == rocksName, "R8 the place you went to wins over your own place 5 m away");
        s.Arrived("humble", "", rocks.LX, rocks.LZ);
        Expect(s.NameForSpot("humble", rocks.LX, rocks.LZ, humbleNear) == "Rock base", "R9 a move with no name (a pasted /tp line) forgets it: your place within 30 m names it");

        // another planet, another save: never carried over
        s.Arrived("humble", rocksName, rocks.LX, rocks.LZ);
        var primeNear = near(prime);
        Expect(s.NameForSpot("prime", rocks.LX, rocks.LZ, primeNear) == (primeNear(rocks.LX, rocks.LZ) ?? ""), "R10 the same spot on another planet doesn't take it");
        s.Use("Standard-2", 12u, _ => true);
        Expect(s.NameForSpot("humble", rocks.LX, rocks.LZ, humbleNear) == nearest, "R11 another save doesn't take it");
        s.Use("Standard-1", 11u, _ => true);
        Expect(s.NameForSpot("humble", rocks.LX, rocks.LZ, humbleNear) == rocksName, "R12 back in the first save this session, it does again");

        // no map pack for the planet (mapNear gives null): "" as before
        var bare = new PlaceStore();
        Expect(bare.NameForSpot("humble", 0, 0, null) == "" && bare.NameForSpot("humble", 0, 0, (x, z) => null) == "", "R13 nothing near and no pack: empty, as before");
        return fails;
    }
}

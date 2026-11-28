// GamesOMG Item Spawner: checks of the Unity-free half, as it ships. `dotnet run --project tests/Spawn.Tests.csproj` runs them;
// `… -- <the built DLL>` also reads that DLL's metadata and proves what it never calls.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using GamesOMG.PlanetCrafterSpawn;

int checks = 0;
void Check(bool condition, string name)
{
    checks++;
    if (!condition) throw new Exception("FAIL " + name);
}

// ── search: folding ──
Check(Search.Fold("Fer à souder") == "fer a souder", "accents fold (French)");
Check(Search.Fold("Straße") == "strasse", "ß folds to ss");
Check(Search.Fold("Ølsen Œuvre") == "olsen oeuvre", "ø and œ fold");
Check(Search.Fold("  Iron--Plate_ ") == "iron plate", "separators become one space, trimmed");
Check(Search.Fold("ÉCLAT D'OSMIUM") == "eclat d osmium", "capitals and apostrophes");
Check(Search.Fold("Żelazo") == "zelazo" && Search.Fold("Šroub") == "sroub", "Polish / Czech marks");
Check(Search.Fold("") == "" && Search.Fold(null) == "", "empty stays empty");
Check(Search.SplitId("OxygenCapsule1").StartsWith("Oxygen Capsule 1"), "an id splits at its capitals and digits");
Check(Search.Fold(Search.SplitId("Rod-iridium")).Contains("rod iridium"), "an id with a dash");

// ── search: matching and order (a French player, English guides) ──
var items = new List<ItemEntry>
{
    ItemEntry.Make("Iron", "Fer", "Iron", 0),
    ItemEntry.Make("Rod-iron", "Barre de fer", "Iron Rod", 1),
    ItemEntry.Make("Rod-iridium", "Barre d'iridium", "Iridium Rod", 2),
    ItemEntry.Make("Iridium", "Iridium", "Iridium", 3),
    ItemEntry.Make("OxygenCapsule1", "Capsule d'oxygène T1", "Oxygen Capsule T1", 4),
    ItemEntry.Make("Bioplastic1", "Nodule de bioplastique", "Bioplastic Nugget", 5),
    ItemEntry.Make("Alloy", "Super alliage", "Super Alloy", 6),
};
List<string> Ids(string q) => Search.Filter(items, q).Select(e => e.Id).ToList();
Check(Ids("fer").First() == "Iron", "the player's own language: 'fer' is Iron first");
Check(Ids("iron").First() == "Iron" && Ids("iron").Contains("Rod-iron"), "English works for a French player: 'iron' is Iron first, Iron Rod too");
Check(Ids("IRON")[0] == "Iron", "case doesn't matter");
Check(Ids("oxygene").SequenceEqual(new[] { "OxygenCapsule1" }), "accents don't matter: 'oxygene' finds Capsule d'oxygène");
Check(Ids("oxyg caps").SequenceEqual(new[] { "OxygenCapsule1" }), "parts of two words, any order of fields");
Check(Ids("capsule oxygen")[0] == "OxygenCapsule1", "words in any order");
Check(Ids("OxygenCapsule1").SequenceEqual(new[] { "OxygenCapsule1" }), "the game's id matches");
Check(Ids("rod-iridium")[0] == "Rod-iridium", "an id with a dash matches");
Check(Ids("alliage")[0] == "Alloy" && Ids("super alloy")[0] == "Alloy" && Ids("alloy")[0] == "Alloy", "Super Alloy by French, English or id");
Check(Ids("iridium")[0] == "Iridium", "the whole name first: Iridium before Iridium Rod");
Check(Ids("rod").Count == 2, "'rod' finds both rods");
Check(Ids("xyzzy").Count == 0, "nonsense finds nothing");
Check(Ids("fer xyzzy").Count == 0, "every word must be found");
Check(Ids("").Count == items.Count, "an empty search shows everything");
Check(Ids("")[0] == "Rod-iridium" && Ids("")[1] == "Rod-iron", "an empty search is alphabetical by the shown name (Barre d'iridium, Barre de fer, …)");
Check(Ids("bio")[0] == "Bioplastic1", "the start of a word");
Check(Ids("plast")[0] == "Bioplastic1", "inside a word");
// a German player: umlauts typed plainly
var de = new List<ItemEntry> { ItemEntry.Make("Seed0", "Lirmalsamen", "Lirma Seed", 0), ItemEntry.Make("Uraninite", "Uraninit", "Uraninite", 1), ItemEntry.Make("Ice", "Eis", "Ice", 2), ItemEntry.Make("Fertilizer1", "Dünger T1", "Fertilizer T1", 3) };
Check(Search.Filter(de, "dunger")[0].Id == "Fertilizer1" && Search.Filter(de, "DÜNGER")[0].Id == "Fertilizer1", "German: dunger / DÜNGER find Dünger");
Check(Search.Filter(de, "ice")[0].Id == "Ice" && Search.Filter(de, "eis")[0].Id == "Ice", "German: Eis by German or English");

// items the game never names (build 26411416: Fish1Hatched, Tree5Growable…; the Blueprint… chips until 0.4.0 named them after
// what they unlock, so these two stand in for any nameless id): out of browsing, found by id
var withUnnamed = new List<ItemEntry>(items)
{
    ItemEntry.Make("Fish1Hatched", "Fish1Hatched", null, 10, null, true),
    ItemEntry.Make("BlueprintAutocrafter", "BlueprintAutocrafter", null, 11, null, true),
    ItemEntry.Make("Fish1Eggs", "Oeufs de Lorpen", "Lorpen fish eggs", 12),
};
Check(Search.Words("BlueprintAutocrafter") == "Blueprint Autocrafter" && Search.Words("Fish1Hatched") == "Fish 1 Hatched", "an unnamed id reads as words");
Check(withUnnamed.First(e => e.Id == "Fish1Hatched").Name == "Fish 1 Hatched", "an unnamed item shows its id as words");
Check(!Search.Filter(withUnnamed, "").Any(e => e.Unnamed), "browsing (no search) leaves unnamed items out");
Check(Search.Filter(withUnnamed, "fish").First().Id == "Fish1Eggs" && Search.Filter(withUnnamed, "fish").Last().Id == "Fish1Hatched", "a search finds them by id, after every named item");
Check(Search.Filter(withUnnamed, "fish1hatched").Single().Id == "Fish1Hatched", "the id typed whole finds it");
Check(Search.Filter(withUnnamed, "autocrafter").Single().Id == "BlueprintAutocrafter", "part of the id finds it");
Check(!Search.Filter(withUnnamed, "lorpen").Any(e => e.Unnamed), "a name search never matches an unnamed item's made-up words");
// the in-game sitting 14:32: "Blueprint Pod 9 x B" picked BlueprintPod9xA (all ties, then alphabetical; "b" is inside "blueprint")
var pods = new List<ItemEntry> { ItemEntry.Make("BlueprintPod9xA", "BlueprintPod9xA", null, 0, null, true), ItemEntry.Make("BlueprintPod9xB", "BlueprintPod9xB", null, 1, null, true), ItemEntry.Make("BlueprintPod9xC", "BlueprintPod9xC", null, 2, null, true) };
foreach (var pod in pods)
    Check(Search.Filter(pods, pod.English).First().Id == pod.Id, "an unnamed item's own words find it first: " + pod.English);
Check(Search.Filter(pods, "blueprintpod9xb").First().Id == "BlueprintPod9xB", "its id typed whole finds it first");
// every item of every fixture: its English name finds it (or one of the same name) first, as the sitting checks in game
foreach (var set in new[] { items, de, withUnnamed, pods })
    foreach (var e in set)
        Check(Search.Filter(set, e.English).First().FoldEnglish == e.FoldEnglish, "its English name finds it first: " + e.English);

// ── the batch planner: vanilla backpack (size 30) ──
var p = new SpawnPlan(10, false);
Check(p.NextBatch(30, 25) == 5, "vanilla: ask for the 5 free slots");
Check(p.NextBatch(30, 25) == 0, "never a second batch while one is in flight");
p.Answered(5, 0);
Check(p.Added == 5 && !p.Done, "5 in, 5 to go");
Check(p.NextBatch(30, 30) == 1, "a full vanilla backpack: one probe, not 5 asks");
p.Answered(0, 1);
Check(p.Full && p.Done && p.DidNotFit == 5 && p.ResultId() == "result.partial", "full: 5 didn't fit, said as partial");

p = new SpawnPlan(10, false);
Check(p.NextBatch(30, 0) == 10, "room for all 10: one batch");
p.Answered(10, 0);
Check(p.Done && !p.Full && p.ResultId() == "result.added" && p.DidNotFit == 0, "x10: added");

p = new SpawnPlan(1, false);
Check(p.NextBatch(30, 30) == 1, "x1 into a full backpack: one probe");
p.Answered(0, 1);
Check(p.Added == 0 && p.ResultId() == "result.fullNone", "nothing went in: said so");

p = new SpawnPlan(0, true);
Check(p.Wanted == SpawnPlan.FillCap, "Fill asks for up to the cap");
Check(p.NextBatch(30, 12) == 18, "Fill: the free slots");
p.Answered(18, 0);
Check(p.NextBatch(30, 30) == 1, "Fill then probes once");
p.Answered(0, 1);
Check(p.Full && p.Added == 18 && p.DidNotFit == 0 && p.ResultId() == "result.filled", "Fill: filled, nothing 'didn't fit'");

// a stacking mod (e.g. akarnokd's Inventory Stacking): more items than slots, the game keeps taking them
p = new SpawnPlan(0, true);
int count = 30, size = 30, asked = 0, guard = 0;
int stackRoom = 170;   // say the stacks hold 170 more
while (!p.Done && guard++ < 1000)
{
    int n = p.NextBatch(size, count);
    asked += n;
    int ok = Math.Min(n, stackRoom);
    stackRoom -= ok;
    count += ok;
    p.Answered(ok, n - ok);
}
Check(p.Added == 170 && p.Full, "stacking: Fill takes all 170 the stacks hold");
Check(asked - 170 <= SpawnPlan.MaxBatch, "stacking: refused asks stay within one batch");
Check(guard < 40, "stacking: batches grow, not one ask per item (" + guard + " batches)");

p = new SpawnPlan(5000, false);
Check(p.Wanted == SpawnPlan.MaxAmount, "an amount is capped at 999");
Check(new SpawnPlan(0, false).Wanted == 1 && new SpawnPlan(-4, false).Wanted == 1, "an amount below 1 is 1");
Check(p.NextBatch(100, 0) == SpawnPlan.MaxBatch, "one batch is at most 50");
p.TimedOut(20);
Check(p.Failed && p.Done && p.ResultId() == "result.partialTimeout" && p.Added == 20, "no answer: stopped, said so");
p = new SpawnPlan(3, false); p.NextBatch(30, 0); p.TimedOut(0);
Check(p.ResultId() == "result.timeout", "no answer, nothing added");

// ── 0.3.0: what counts as decoration, against the game's own data (tests/decoration-groups.json: every GroupData of build
// 26411416 and of the Magnetaris preview 26763323, read from the game files). The rule is Decor's; the families it must pick are
// written here by their ids, so a rule that drifts (or a game update that adds a kind) goes red ──
var familyRe = new System.Text.RegularExpressions.Regex(@"^(Poster|AnimalEffigie|GoldenEffigie|GoldenToxicityEffigie|GoldenMagnetarisEffigie|StarformEffigie)\d+$");
var skinRe = new System.Text.RegularExpressions.Regex(@"^Skin-\d+$");
using (var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "decoration-groups.json"))))
{
    var builds = doc.RootElement.GetProperty("builds");
    Check(builds.TryGetProperty("26411416", out _) && builds.TryGetProperty("26763323", out _), "the decoration fixture holds both builds");
    foreach (var build in builds.EnumerateObject())
    {
        var placeable = new List<string>();
        var skins = new List<string>();
        var families = new SortedSet<string>();
        foreach (var c in build.Value.GetProperty("constructibles").EnumerateObject())
        {
            // 0.4.0: the row carries more (category, generation, chip-linked, loot) after these four
            var f = c.Value.EnumerateArray().Take(4).Select(x => x.GetInt32()).ToArray();
            if (Decor.IsPlaceable(true, f[0] == 1, f[1], f[2], f[3])) { placeable.Add(c.Name); families.Add(System.Text.RegularExpressions.Regex.Replace(c.Name, @"\d+$", "")); }
            else Check(!familyRe.IsMatch(c.Name), build.Name + ": the rule leaves out " + c.Name);
        }
        int itemCount = 0;
        foreach (var it in build.Value.GetProperty("items").EnumerateObject())
        {
            itemCount++;
            var a = it.Value.EnumerateArray().ToArray();
            bool icon = a[0].GetInt32() == 1;
            string eq = a[1].ValueKind == JsonValueKind.String ? a[1].GetString() : null;
            Check(!Decor.IsPlaceable(false, icon, 0, 0, 0), build.Name + ": an item is never a placeable building: " + it.Name);
            if (Decor.IsSkin(true, eq)) { skins.Add(it.Name); Check(icon, build.Name + ": every skin has an icon (the list shows it): " + it.Name); }
            else Check(!skinRe.IsMatch(it.Name), build.Name + ": the rule leaves out the skin " + it.Name);
        }
        Check(itemCount > 400, build.Name + ": the fixture holds the game's items");
        Check(placeable.All(id => familyRe.IsMatch(id)), build.Name + ": the rule picks only effigies and posters: " + string.Join(", ", placeable.Where(id => !familyRe.IsMatch(id))));
        Check(!placeable.Contains("WardenAustel"), build.Name + ": the Warden's altar (an icon, no recipe, an inventory of 1) is not decoration");
        Check(skins.Count > 20 && skins.All(id => skinRe.IsMatch(id)), build.Name + ": the spacesuit skins and nothing else (" + skins.Count + ")");
        var want = new SortedSet<string> { "AnimalEffigie", "GoldenEffigie", "GoldenToxicityEffigie", "Poster", "StarformEffigie" };
        if (build.Name == "26763323") want.Add("GoldenMagnetarisEffigie");
        Check(families.SetEquals(want), build.Name + ": every family of effigy and poster: " + string.Join(", ", families));
        Console.WriteLine("decoration " + build.Name + ": " + placeable.Count + " effigies and posters, " + skins.Count + " spacesuit skins");
    }
}
Check(!Decor.IsSkin(true, "OxygenTank") && !Decor.IsSkin(false, "Skin") && Decor.IsSkin(true, "Skin"), "a skin is an item whose equipableType is Skin");
Check(!Decor.IsPlaceable(true, false, 0, 0, 0), "no icon: not something a player has seen");
Check(!Decor.IsPlaceable(true, true, 2, 0, 0) && !Decor.IsPlaceable(true, true, 0, 1, 0) && !Decor.IsPlaceable(true, true, 0, 0, 1), "a recipe, or an inventory: not a found decoration");

// "had": a copy in any inventory, or built by a player; one still lying where the game put it (a scene object) doesn't count
Check(Decor.Counts(true, false, false) && Decor.Counts(true, false, true), "in an inventory (yours, a locker, a crate you opened): had");
Check(Decor.Counts(false, true, false), "built by a player: had");
Check(!Decor.Counts(false, true, true), "a scene object still where the game put it (a loose effigy in a wreck): not had");
Check(!Decor.Counts(false, false, false) && !Decor.Counts(false, false, true), "nowhere (no position, no inventory): not had");

// one of each: the ids shown, in order, each once, skipping what the world has
var deco = new List<ItemEntry>
{
    ItemEntry.Make("Poster1", "Affiche A", "Poster A", 0),
    ItemEntry.Make("Poster2", "Affiche B", "Poster B", 1),
    ItemEntry.Make("GoldenEffigie1", "Maquette dorée", "Golden effigy", 2),
    ItemEntry.Make("GoldenEffigie2", "Maquette dorée", "Golden effigy", 3),
    ItemEntry.Make("Skin-01", "Combinaison spatiale Comto", "Spacesuit Comto", 4),
    ItemEntry.Make("Iron", "Fer", "Iron", 5),
};
foreach (var e in deco) { e.Decoration = e.Id != "Iron"; e.Placeable = e.Decoration && !e.Id.StartsWith("Skin"); }
Check(Decor.Only(deco).Select(e => e.Id).SequenceEqual(new[] { "Poster1", "Poster2", "GoldenEffigie1", "GoldenEffigie2", "Skin-01" }), "the Decoration tab: decorations only, Iron out");
Check(Decor.Missing(deco, new HashSet<string>()).Select(e => e.Id).SequenceEqual(new[] { "Poster1", "Poster2", "GoldenEffigie1", "GoldenEffigie2", "Skin-01" }), "one of each, nothing had: every decoration once, in the order shown, never Iron");
Check(Decor.Missing(deco, new HashSet<string> { "Poster2", "Skin-01" }).Select(e => e.Id).SequenceEqual(new[] { "Poster1", "GoldenEffigie1", "GoldenEffigie2" }), "one of each skips what the world has");
Check(Decor.Missing(deco, new HashSet<string> { "Poster1", "Poster2", "GoldenEffigie1", "GoldenEffigie2", "Skin-01" }).Count == 0, "everything had: nothing to add");
Check(Decor.Missing(new List<ItemEntry> { deco[0], deco[0], deco[1] }, null).Count == 2, "an id shown twice is added once");
Check(Decor.Missing(null, null).Count == 0, "no list: nothing");
// the search narrows it, in the player's language or English: "affiche" / "poster" = the posters; two golden effigies share a name
Check(Search.Filter(Decor.Only(deco), "affiche").Select(e => e.Id).SequenceEqual(new[] { "Poster1", "Poster2" }) && Search.Filter(Decor.Only(deco), "poster").Count == 2, "a search on the Decoration tab: 'affiche' or 'poster' = the posters");
Check(Decor.Missing(Search.Filter(Decor.Only(deco), "golden"), new HashSet<string> { "GoldenEffigie1" }).Single().Id == "GoldenEffigie2", "one of each of what the search shows: the golden effigy not had");

// one of each through the batch planner: 68 missing, a 30-slot backpack holding 20
p = new SpawnPlan(68, false);
Check(p.NextBatch(30, 20) == 10, "one of each: ask for the 10 free slots");
p.Answered(10, 0);
Check(p.NextBatch(30, 30) == 1, "then one probe");
p.Answered(0, 1);
Check(p.Full && p.Added == 10 && p.DidNotFit == 58 && p.SetResultId() == "set.partial", "full: 10 in, 58 didn't fit");
Check(L.T(p.SetResultId(), p.Added, p.DidNotFit) == "Backpack full: added 10, 58 didn't fit. Nothing was dropped. Make room and press again for the rest.", "the one-of-each full line");
p = new SpawnPlan(5, false); p.NextBatch(30, 0); p.Answered(5, 0);
Check(p.Done && p.SetResultId() == "set.added" && L.T("set.added", p.Added, p.DidNotFit) == "Added one of each: 5 into your backpack.", "all fit: added");
p = new SpawnPlan(5, false); p.NextBatch(30, 30); p.Answered(0, 1);
Check(p.SetResultId() == "set.fullNone", "a full backpack: nothing added, said so");
p = new SpawnPlan(5, false); p.NextBatch(30, 0); p.TimedOut(2);
Check(p.SetResultId() == "set.partialTimeout" && new Func<SpawnPlan>(() => { var q = new SpawnPlan(5, false); q.NextBatch(30, 0); q.TimedOut(0); return q; })().SetResultId() == "result.timeout", "no answer: said so");

// ── 0.4.0: furniture for looks and the blueprint chips, against the same fixture (both builds, tests/decoration-groups.json).
// The rule is Decor's; what it must pick, and every piece of furniture it leaves out with its reason, are written here by id,
// so a rule that drifts, or a game update that adds furniture or a chip, goes red ──
string[] furniturePick =
{
    "ArcadeMachine1", "BedDouble", "BedSimple", "Chair1", "Chair2", "Desktop1", "Desktop2", "ExerciseBike1", "Faucet1", "InsideLamp1",
    "InsideLamp2", "LavaLamp1", "Library1", "PlanetViewer1", "Pooltable1", "Server1", "Shelves1", "Sofa", "SofaAngle", "SofaAngleColored",
    "Table1", "TableSmall", "TableSmall2", "TableSmall3", "Toilet1", "Treadmill1", "VendingMachine1",
};
// furniture left out on purpose: inventory = storage, a pot or planter, a display case; generates = it terraforms; chip = the
// game unlocks it with a blueprint chip of its own (it comes as that chip). A power draw (FountainBig, HologramGenerator,
// LightBoxMedium) is not generating
var furnitureOut = new Dictionary<string, string[]>
{
    ["AquariumTube1"] = new[] { "generates" }, ["Ivy1"] = new[] { "generates" },
    ["Fireplace"] = new[] { "generates", "chip" },
    ["BedDoubleColored"] = new[] { "chip" }, ["CookingStation1"] = new[] { "chip" }, ["FountainBig"] = new[] { "chip" },
    ["LightBoxMedium"] = new[] { "chip" }, ["Shower1"] = new[] { "chip" }, ["SofaColored"] = new[] { "chip" },
    ["ToxicityDiorama1"] = new[] { "chip" }, ["ToxicityDiorama2"] = new[] { "chip" },
    ["HologramGenerator"] = new[] { "inventory", "chip" }, ["Container3"] = new[] { "inventory", "chip" },
    ["TreePlanter"] = new[] { "inventory", "generates", "chip" },
    ["TreePlanter2"] = new[] { "inventory", "generates" }, ["TreePlanter3"] = new[] { "inventory", "generates" },
    ["FlowerPot1"] = new[] { "inventory", "generates" }, ["FlowerPot2"] = new[] { "inventory", "generates" }, ["FlowerPot3"] = new[] { "inventory", "generates" },
    ["Container1"] = new[] { "inventory" }, ["Container2"] = new[] { "inventory" }, ["Locker1"] = new[] { "inventory" }, ["Vault1"] = new[] { "inventory" },
    ["Fridge1"] = new[] { "inventory" }, ["Counter1"] = new[] { "inventory" }, ["Counter2"] = new[] { "inventory" }, ["Trashcan1"] = new[] { "inventory" },
    ["DisplayCase"] = new[] { "inventory" }, ["ButterflyDisplayer1"] = new[] { "inventory" }, ["FishDisplayer1"] = new[] { "inventory" },
    ["FrogDisplayer1"] = new[] { "inventory" }, ["SkinDisplayer"] = new[] { "inventory" },
};
string[] drawOnly = { "FountainBig", "HologramGenerator", "LightBoxMedium" };
string[] chipIds =
{
    "BlueprintAutocrafter", "BlueprintBedDoubleColored", "BlueprintContainer3", "BlueprintCookingStation", "BlueprintDeparturePlatform",
    "BlueprintDrone2", "BlueprintEquipmentIncrease2", "BlueprintEquipmentIncrease5", "BlueprintFireplace", "BlueprintFlare",
    "BlueprintFountainBig", "BlueprintHologramGenerator", "BlueprintLightBoxMedium", "BlueprintPod9xA", "BlueprintPod9xB",
    "BlueprintPod9xC", "BlueprintShower", "BlueprintSmartFabric", "BlueprintSofaColored", "BlueprintSolarQuartz",
    "BlueprintToxicityDiorama1", "BlueprintToxicityDiorama2", "BlueprintTradePlatform", "BlueprintTreePlanter",
    "BlueprintVehicleJetpack1", "BlueprintWallWaterLife", "BlueprintWardensChip",
};
// the rule as it reads one fixture row (Catalog.DecorBuilding reads the same off the game's Group)
bool DecorRow(JsonElement[] r, bool chipLinked, bool noInventory = false, bool noGeneration = false, bool noChip = false)
{
    var gen = r[5].EnumerateObject().Select(u => (float)u.Value.GetDouble()).ToList();
    return Decor.IsDecorBuilding(true, r[0].GetInt32() == 1, r[4].GetString(), noInventory ? 0 : r[2].GetInt32(), noInventory ? 0 : r[3].GetInt32(),
        !noGeneration && Decor.Generates(gen), !noChip && chipLinked);
}
using (var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "decoration-groups.json"))))
{
    var english = doc.RootElement.GetProperty("english").EnumerateObject().ToDictionary(x => x.Name, x => x.Value.GetString());
    foreach (var build in doc.RootElement.GetProperty("builds").EnumerateObject())
    {
        var rows = build.Value.GetProperty("constructibles").EnumerateObject().ToDictionary(x => x.Name, x => x.Value.EnumerateArray().ToArray());
        var itemRows = build.Value.GetProperty("items").EnumerateObject().ToDictionary(x => x.Name, x => x.Value.EnumerateArray().ToArray());
        // what a chip unlocks: every item's link (Catalog reads GroupItem.GetUnlocksGroup the same way)
        var targets = new HashSet<string>(itemRows.Where(x => Decor.IsChip(true, x.Value[2].ValueKind == JsonValueKind.String ? x.Value[2].GetString() : null)).Select(x => x.Value[2].GetString()));
        foreach (var kv in rows) Check((kv.Value[6].GetInt32() == 1) == targets.Contains(kv.Key), build.Name + ": the fixture's chip-linked flag agrees with the items' links: " + kv.Key);
        var picked = rows.Where(kv => DecorRow(kv.Value, targets.Contains(kv.Key))).Select(kv => kv.Key).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var want = furniturePick.OrderBy(x => x, StringComparer.Ordinal).ToList();
        Check(picked.SequenceEqual(want), build.Name + ": the rule picks exactly the furniture written here (extra: " + string.Join(", ", picked.Except(want)) + "; missing: " + string.Join(", ", want.Except(picked)) + ")");
        Check(picked.All(id => rows[id][4].GetString() == "Furnitures"), build.Name + ": every pick is in the game's Furnitures category: no machine, no structure, no screen");
        Check(!picked.Any(id => Decor.IsPlaceable(true, rows[id][0].GetInt32() == 1, rows[id][1].GetInt32(), rows[id][2].GetInt32(), rows[id][3].GetInt32())), build.Name + ": no pick is also an effigy or poster (each comes once)");
        Check(picked.All(id => rows[id][1].GetInt32() > 0), build.Name + ": every pick has a recipe: taking it down gives parts back");
        // Help's "for furniture the game unlocks from wrecks, its blueprint chip": some picks carry lootRecipeOnDeconstruct, some don't
        Check(picked.Any(id => rows[id][7].GetInt32() == 1) && picked.Any(id => rows[id][7].GetInt32() == 0), build.Name + ": some picks give a chip back on deconstruction and some don't (Help says 'for furniture the game unlocks from wrecks')");
        // every piece of the game's furniture is either picked or out for a reason written here: a new one goes red
        var furniture = rows.Where(kv => kv.Value[4].GetString() == "Furnitures").Select(kv => kv.Key).ToList();
        var unaccounted = furniture.Where(id => !furniturePick.Contains(id) && !furnitureOut.ContainsKey(id)).ToList();
        Check(unaccounted.Count == 0, build.Name + ": every piece of the game's furniture is picked or out for a stated reason: " + string.Join(", ", unaccounted));
        foreach (var kv in furnitureOut)
        {
            Check(rows.ContainsKey(kv.Key), build.Name + ": the game has " + kv.Key);
            if (!rows.ContainsKey(kv.Key)) continue;
            var r = rows[kv.Key];
            var gen = r[5].EnumerateObject().Select(u => (float)u.Value.GetDouble()).ToList();
            var why = new List<string>();
            if (r[2].GetInt32() > 0 || r[3].GetInt32() > 0) why.Add("inventory");
            if (Decor.Generates(gen)) why.Add("generates");
            if (targets.Contains(kv.Key)) why.Add("chip");
            Check(why.SequenceEqual(kv.Value), build.Name + ": " + kv.Key + " is out for " + string.Join(" + ", kv.Value) + ", the data says " + string.Join(" + ", why));
            Check(!DecorRow(r, targets.Contains(kv.Key)), build.Name + ": the rule leaves out " + kv.Key);
            // and for that reason alone: with it gone, the rule would take it
            Check(DecorRow(r, targets.Contains(kv.Key), kv.Value.Contains("inventory"), kv.Value.Contains("generates"), kv.Value.Contains("chip")), build.Name + ": " + kv.Key + " is out for exactly " + string.Join(" + ", kv.Value));
        }
        foreach (var id in drawOnly)
            Check(rows[id][5].EnumerateObject().Any(u => u.Value.GetDouble() < 0) && !Decor.Generates(rows[id][5].EnumerateObject().Select(u => (float)u.Value.GetDouble())), build.Name + ": " + id + " draws power, which is not generating");
        // everything outside the furniture category is out, the screens (the Sign among them) included
        var other = rows.Where(kv => kv.Value[4].GetString() != "Furnitures").ToList();
        Check(other.All(kv => !DecorRow(kv.Value, targets.Contains(kv.Key))), build.Name + ": no building outside Furnitures is decoration furniture");
        Check(rows["Sign"][4].GetString() == "Screens" && !DecorRow(rows["Sign"], targets.Contains("Sign")), build.Name + ": the Sign is a screen, out");
        foreach (var cat in new[] { "BaseBuilding", "Machines", "Screens", "Biomass", "Purification" })
            Check(other.Any(kv => kv.Value[4].GetString() == cat), build.Name + ": the fixture holds the game's " + cat);

        // the chips: exactly the items with an unlocksGroup, each named after its target
        var chips = itemRows.Where(x => Decor.IsChip(true, x.Value[2].ValueKind == JsonValueKind.String ? x.Value[2].GetString() : null)).Select(x => x.Key).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Check(chips.SequenceEqual(chipIds.OrderBy(x => x, StringComparer.Ordinal)), build.Name + ": the chips are exactly the items written here (extra: " + string.Join(", ", chips.Except(chipIds)) + "; missing: " + string.Join(", ", chipIds.Except(chips)) + ")");
        Check(itemRows.Where(x => x.Value[2].ValueKind == JsonValueKind.Null).All(x => !Decor.IsChip(true, null)), build.Name + ": an item with no link is no chip");
        var entries = new List<ItemEntry>();
        int order = 0;
        foreach (var id in chips)
        {
            string target = itemRows[id][2].GetString();
            Check(itemRows[id][0].GetInt32() == 1, build.Name + ": the chip " + id + " has an icon (it is browsed)");
            Check(rows.ContainsKey(target) || itemRows.ContainsKey(target), build.Name + ": " + id + "'s target " + target + " is one of the game's groups");
            Check(english.TryGetValue(target, out var en) && !string.IsNullOrEmpty(en), build.Name + ": " + id + "'s target " + target + " has an English name");
            // in English, the game's shown name and its English name are the same
            var e = Decor.ChipEntry(id, target, english[target], english[target], order++);
            Check(e.Chip && !e.Unnamed && e.Name == english[target] && e.English == english[target] && e.UnlocksId == target && e.Id == id, build.Name + ": " + id + " is named after its target: " + e.Name + " (" + target + ")");
            entries.Add(e);
        }
        foreach (var e in entries)
            Check(Search.Filter(entries, e.English).First().FoldEnglish == e.FoldEnglish, build.Name + ": its target's name finds the chip first: " + e.English);
        Check(Search.Filter(entries, "blueprint").Count == entries.Count, build.Name + ": \"blueprint\" (the ids) finds every chip");
        Check(Search.Filter(entries, "").Count == entries.Count, build.Name + ": browsing shows every chip (none is unnamed now)");
        Console.WriteLine("0.4.0 " + build.Name + ": " + picked.Count + " furniture picked, " + furnitureOut.Count + " out on purpose, " + chips.Count + " blueprint chips named");
    }
}
// a chip in another language: the game's name in it, the English one for search, its own id still matches
var fireChip = Decor.ChipEntry("BlueprintFireplace", "Fireplace", "Cheminée", "Fireplace", 0);
Check(fireChip.Name == "Cheminée" && fireChip.English == "Fireplace" && !fireChip.Unnamed, "a French chip: the game's French name, the English one beside it");
var mixed = new List<ItemEntry>(items) { fireChip, Decor.ChipEntry("BlueprintLightBoxMedium", "LightBoxMedium", "Boîte à lumière", "Light Box", 99) };
Check(Search.Filter(mixed, "cheminee").First().Id == "BlueprintFireplace" && Search.Filter(mixed, "fireplace").First().Id == "BlueprintFireplace", "a chip found by its target's name, French or English");
Check(Search.Filter(mixed, "blueprint").Select(e => e.Id).OrderBy(x => x).SequenceEqual(new[] { "BlueprintFireplace", "BlueprintLightBoxMedium" }), "\"blueprint\" finds the chips by their ids, nothing else");
Check(Decor.Chips(mixed).Select(e => e.Id).SequenceEqual(new[] { "BlueprintFireplace", "BlueprintLightBoxMedium" }) && Decor.Chips(null).Count == 0, "the Blueprints tab: chips only");
Check(!Decor.Only(mixed).Any(e => e.Chip), "a chip is not decoration (no one of each for chips)");
// one of each covers furniture as it covers the crate finds (Catalog marks a piece Decoration, Placeable and Building)
var lamp = ItemEntry.Make("LavaLamp1", "Lampe à lave", "Lava lamp", 7);
lamp.Decoration = lamp.Placeable = lamp.Building = true;
var decoPlus = new List<ItemEntry>(deco) { lamp };
Check(Decor.Only(decoPlus).Contains(lamp) && Decor.Only(decoPlus).Count(e => e.Building) == 1, "the Decoration tab holds furniture beside the crate finds");
Check(Decor.Missing(decoPlus, new HashSet<string>()).Contains(lamp) && !Decor.Missing(decoPlus, new HashSet<string> { "LavaLamp1" }).Contains(lamp), "one of each adds a piece of furniture the world lacks, and skips one it has");
var nameless = Decor.ChipEntry("BlueprintSomething", "Something", "Something", null, 0);
Check(nameless.Unnamed && nameless.Chip && nameless.Name == "Blueprint Something", "a chip whose target has no name either reads as its own id, out of browsing");
Check(Decor.ChipEntry("BlueprintSomething", "Something", "Un truc", null, 0) is var shown && !shown.Unnamed && shown.Name == "Un truc", "a target named only in the game's language is still a name");
Check(!Decor.IsChip(false, "Fireplace") && !Decor.IsChip(true, "") && Decor.IsChip(true, "Fireplace"), "a chip is an item with a target");
Check(!Decor.Generates(null) && !Decor.Generates(new float[] { 0, -30 }) && Decor.Generates(new float[] { 0, 0.5f }), "generating = more than nothing in some unit; a draw is not");
Check(!Decor.IsDecorBuilding(true, true, "Machines", 0, 0, false, false) && !Decor.IsDecorBuilding(true, false, "Furnitures", 0, 0, false, false)
    && !Decor.IsDecorBuilding(false, true, "Furnitures", 0, 0, false, false) && Decor.IsDecorBuilding(true, true, "Furnitures", 0, 0, false, false), "furniture with an icon, as a building, only");
// the chip's info line (ItemInfo puts in the game's own words: GROUP_NAME_BlueprintT1, GROUP_NAME_ScreenUnlockables, the target)
L.Language = () => "english";
Check(L.T("info.chip", "Blueprint microchip", "Screen - Blueprints", "Fireplace") == "Blueprint microchip: decode it at a \"Screen - Blueprints\" to unlock Fireplace for good.", "a chip's line, English");
Check(L.T("info.chipDone", "Fireplace") == "Already unlocked in this world: Fireplace. Decoding this chip adds nothing.", "an already unlocked chip's line");
// the Blueprints tab's stand-in is the game's own BLUEPRINTS word in each language (Localization, build 26411416)
var gameBlueprints = new Dictionary<string, string>
{
    ["english"] = "Blueprints", ["french"] = "Plans", ["spanish"] = "Planos", ["portuguese"] = "Diagramas", ["german"] = "Baupläne", ["schinese"] = "蓝图",
    ["tchinese"] = "藍圖", ["japanese"] = "設計図", ["korean"] = "설계도", ["polish"] = "Schematy", ["russian"] = "Чертежи", ["turk"] = "Planlar", ["italian"] = "Progetti",
};
foreach (var lang in L.Languages) Check(gameBlueprints.TryGetValue(lang, out var word) && L.In(lang, "tab.blue") == word, lang + ": the Blueprints stand-in is the game's own word (" + L.In(lang, "tab.blue") + ")");

// ── every result line exists and fills its blanks ──
foreach (var id in new[] { "result.added", "result.partial", "result.fullNone", "result.filled", "result.fillCap", "result.timeout", "result.partialTimeout" })
{
    Check(L.Has(id), "string table has " + id);
    Check(!L.T(id, 4, "Iron", 6).Contains("{"), id + " fills every blank");
}
Check(L.T("result.partial", 4, "Iron", 6) == "Backpack full: added 4 Iron, 6 didn't fit. Nothing was dropped.", "the full-backpack line");

// ── 0.2.0: the mod's words in every language the game ships, English when a language or a line is missing ──
L.Language = () => "english";
Check(L.Languages.Count() == 13, "a table for each of the game's 13 languages");
Check(L.In("klingon", "tab.all") == "All" && !L.Translated("klingon"), "a language without a table falls back to English");
Check(L.In("french", "tab.all") == "Tout" && L.In("german", "btn.fill") == "Füllen" && L.In("schinese", "prompt.close") == "关闭", "French, German, Chinese lines");
Check(L.In("french", "no.such.id") == "no.such.id", "an id no table has reads as itself (the strings test keeps that from shipping)");
L.Language = () => "russian";
Check(L.T("result.partial", 4, "Железо", 6) == "Рюкзак полон: добавлено 4 × Железо, не поместилось: 6. Ничего не выпало на землю.", "L.T follows the game's language");
L.Language = () => null;
Check(L.T("tab.all") == "All", "no language from the game: English");
L.Language = () => throw new InvalidOperationException("the game isn't ready");
Check(L.T("tab.all") == "All", "the game's language unreadable: English");
foreach (var lang in L.Languages)
    foreach (var id in L.Ids)
    {
        string line = L.In(lang, id, "A", "B", "C", "D");
        Check(!System.Text.RegularExpressions.Regex.IsMatch(line, @"\{\d+\}"), lang + " " + id + " fills every blank");
    }
L.Language = () => "english";
Check(L.Number(6) == "6" && L.Number(1.25) == "1.25" && L.Number(12.5) == "12.5" && L.Number(3.5) == "3.5", "× numbers: no trailing zeros");
L.Language = () => "french";
Check(L.Number(1.25) == "1,25" && L.Number(12.5) == "12,5", "× numbers: the French decimal comma");

// ── 0.2.0: the info line. Numbers as the game shows them on build 26411416: Rosea tree seed 600, Pleom 350, Iterra 125, Malissea 1250, Lirma 0; an O2 fuse 500, T1 optimizer 120 m / 5,
// T2 250 m / 8. The buildings' names are the game's own, every language (tests/building-names.json) ──
var names = new Dictionary<string, Dictionary<string, string>>();
using (var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "building-names.json"))))
    foreach (var lang in doc.RootElement.GetProperty("languages").EnumerateObject())
        names[lang.Name] = lang.Value.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString());
Check(names.Count == 13, "the building-name fixture holds the game's 13 languages");
List<Place> Places(string lang, params string[] ids) => ids.Select((id, i) => new Place { Id = id, Name = names[lang][id], Order = i }).ToList();
string[] flower = { "Vegetube1", "Vegetube2", "VegetubeOutside1", "SeedSpreader1", "SeedSpreader2" };
string[] tree = { "TreeSpreader0", "TreeSpreader1", "TreeSpreader2" };
string[] butterfly = { "ButterflyDome1", "ButterflyFarm1", "ButterflyFarm2", "ButterflyFarm3" };
string[] fish = { "Aquarium1", "Aquarium2", "FishFarm1", "FishFarm2" };
foreach (var lang in names.Keys)
{
    foreach (var kv in names[lang])
        if (!kv.Key.StartsWith("Optimizer"))
            Check(!InfoText.Untiered(kv.Value).Any(char.IsDigit), lang + ": the tier mark comes off " + kv.Value + " -> " + InfoText.Untiered(kv.Value));
    Check(InfoText.Families(Places(lang, flower)).Count == 2, lang + ": a flower seed counts in two families (Vegetube, Flower Spreader): " + string.Join(" | ", InfoText.Families(Places(lang, flower))));
    Check(InfoText.Families(Places(lang, tree)).Count == 1, lang + ": a tree seed counts in one (Tree Spreader)");
    Check(InfoText.Families(Places(lang, butterfly)).Count == 2, lang + ": a larva counts in two (Butterfly dome, Butterfly farm)");
    Check(InfoText.Families(Places(lang, fish)).Count == 2, lang + ": fish eggs count in two (Aquarium, Fish farm)");
}
Check(InfoText.Families(Places("english", flower)).SequenceEqual(new[] { "Vegetube", "Flower Spreader" }), "English: Vegetube, Flower Spreader");
Check(InfoText.Families(Places("french", flower)).SequenceEqual(new[] { "Végétube", "Semoir de fleurs" }), "French: Végétube, Semoir de fleurs");
Check(InfoText.Families(Places("german", flower)).SequenceEqual(new[] { "Pflanzenröhre", "Blumenverteiler" }), "German: Pflanzenröhre, Blumenverteiler");
Check(InfoText.Families(Places("schinese", flower)).SequenceEqual(new[] { "绿植光合管", "花卉播种机" }), "Chinese: the 1级 / 2级 tier marks come off");
Check(InfoText.Families(Places("english", tree)).SequenceEqual(new[] { "Tree Spreader" }), "English: Tree Spreader");
Check(InfoText.Untiered("Tree Spreader") == "Tree Spreader" && InfoText.Untiered("T2 Fish farm") == "Fish farm" && InfoText.Untiered("Aquarien St 2") == "Aquarien", "Untiered leaves a plain name alone");

L.Language = () => "english";
Check(InfoText.Multiplier("Oxygen multiplier", 600, new[] { "Tree Spreader" }, false, 1) == "Oxygen multiplier: 600% (×6). Counts in: Tree Spreader. In a pot it adds nothing.", "Rosea tree seed: 600% = ×6 in a Tree Spreader, nothing in a pot");
Check(InfoText.Multiplier("Oxygen multiplier", 350, new[] { "Tree Spreader" }, false, 1).Contains("350% (×3.5)"), "Pleom: 350% = ×3.5");
Check(InfoText.Multiplier("Oxygen multiplier", 125, new[] { "Tree Spreader" }, false, 1).Contains("125% (×1.25)"), "Iterra: 125% = ×1.25");
Check(InfoText.Multiplier("Oxygen multiplier", 1250, new[] { "Tree Spreader" }, false, 1).Contains("1250% (×12.5)"), "Malissea: 1250% = ×12.5");
Check(InfoText.Multiplier("Oxygen multiplier", 0, new[] { "Vegetube", "Flower Spreader" }, false, 1) == "Oxygen multiplier: none (×1), wherever it goes.", "Lirma: 0% counts as ×1 (the game's own rule)");
Check(InfoText.Multiplier("Insect multiplier", 500, new[] { "Butterfly dome", "Butterfly farm" }, true, 0) == "Insect multiplier: 500% (×5). Counts in: Butterfly dome, Butterfly farm. Several in one building add up.", "a larva: ×5, and a farm that holds several adds them up");
Check(InfoText.Multiplier("Animal multiplier", 300, new[] { "Aquarium" }, false, 2).EndsWith("Anywhere else it adds nothing."), "not a seed: 'anywhere else', not 'a pot'");
var tiers = new List<Tier> { new Tier { Name = "Machine optimizer", Range = 120, Machines = 5 }, new Tier { Name = "T2 Machine Optimizer", Range = 250, Machines = 8 } };
Check(InfoText.Fuse("Oxygen multiplier", 500, tiers) == "Oxygen multiplier +500% per fuse; fuses add up. Nearest machines: 5 within 120 m (Machine optimizer) · 8 within 250 m (T2 Machine Optimizer).", "an O2 fuse: +500% each, stacking; T1 5 within 120 m, T2 8 within 250 m");
Check(InfoText.Fuse("Production speed", 25, new List<Tier>()) == "Production speed +25% per fuse; fuses add up.", "a production fuse, no optimizer known: the boost alone");
Check(InfoText.Gauge("info.food", 12) == "Food +12" && InfoText.Gauge("info.water", 30) == "Hydration +30" && InfoText.Gauge("info.toxic", 10) == "Toxicity −10", "food, water, toxins");
Check(InfoText.Description("Restores health", "Space Food") == "Restores health", "a description stays");
Check(InfoText.Description("Sulfur", "Sulfur") == "" && InfoText.Description("", "Iron") == "" && InfoText.Description(null, "Iron") == "", "nothing when it only repeats the name, or is empty (most ores and seeds)");
Check(InfoText.Description("Energy production blocked!<br>Build <b>purification</b>  machines", "x") == "Energy production blocked! Build purification machines", "the game's rich-text tags come out (the panel draws plain text)");
L.Language = () => "french";
Check(InfoText.Multiplier("Multiplicateur d'oxygène", 350, InfoText.Families(Places("french", flower)), false, 1) == "Multiplicateur d'oxygène : 350 % (×3,5). Compte dans : Végétube, Semoir de fleurs. En pot, elle n'apporte rien.", "French: the game's label, the decimal comma, our words around them");
Check(InfoText.Fuse("Multiplicateur d'oxygène", 500, tiers).StartsWith("Multiplicateur d'oxygène +500 % par fusible"), "French fuse line");
L.Language = () => "japanese";
Check(InfoText.Multiplier("酸素乗数", 600, new[] { "ツリースプレッダー" }, false, 1) == "酸素乗数：600%（×6）。有効な建物：ツリースプレッダー。鉢植えでは効果がありません。", "Japanese: sentences joined without spaces");
L.Language = () => "english";

// ── contrast: every text style on every plate it sits on, WCAG ≥ 4.5:1 ──
foreach (var row in Palette.Table())
    Check(row.Value >= Palette.Floor, "contrast " + row.Key + " = " + row.Value.ToString("0.00"));
Check(Math.Abs(Palette.Ratio(0xffffff, 0x000000) - 21) < 0.01, "the WCAG formula: white on black is 21:1");
Check(Math.Abs(Palette.Ratio(0x767676, 0xffffff) - 4.54) < 0.01, "the WCAG formula: #767676 on white is 4.54:1");
Check(Palette.Ratio(0x6f7a89, Palette.Raised) < Palette.Floor, "the teleport panel's Faint on Raised fails: it is not used for text here");

// ── keys: F6 and LB + RB against the game's own bindings (build 26411416, every PlayerActions binding) ──
string fixture = Path.Combine(AppContext.BaseDirectory, "bindings-all.json");
var game = new List<GameBinding>();
using (var doc = JsonDocument.Parse(File.ReadAllText(fixture)))
{
    Check(doc.RootElement.GetProperty("build").GetRawText().Trim('"') == "26411416", "bindings fixture is build 26411416");
    foreach (var b in doc.RootElement.GetProperty("bindings").EnumerateArray())
        game.Add(new GameBinding { Map = b.GetProperty("map").GetString(), Action = b.GetProperty("action").GetString(), Path = b.GetProperty("path").GetString() });
}
Check(game.Count > 100, "the fixture holds the game's bindings");
List<Clash> Against(string path) => KeyCheck.Find(new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("Key", path) }, game);
Check(Against("<Keyboard>/f6").Count == 0, "F6 is free in the game");
Check(Against("<Keyboard>/f8").Any(c => c.With == "ShowFeedback"), "control: F8 is the game's Show feedback (the check can fail)");
Check(Against("<Keyboard>/f4").Any(c => c.With == "Unstuck"), "control: F4 is the game's Unstuck");
Check(KeyCheck.Label("<Keyboard>/f6") == "F6", "F6 reads as F6");
foreach (var ours in new[] { "<Keyboard>/f3", "<Keyboard>/f7", "<Keyboard>/f10", "<Keyboard>/end", "<Gamepad>/select" })
    Check(!KeyCheck.Overlap("<Keyboard>/f6", ours), "F6 is not GamesOMG Teleport's or Spawn Object's " + ours);
string chord = "<Gamepad>/leftShoulder + <Gamepad>/rightShoulder";
Check(Chord.Parts(chord).Count == 2, "the chord has two buttons");
Check(Chord.Label(chord, false) == "LB + RB" && Chord.Label(chord, true) == "L1 + R1", "the chord reads LB + RB / L1 + R1");
Check(Chord.Clashes(chord, game).Count == 0, "LB + RB together: only the game's SecretDevKey and AccessibilityKey are on them");
Check(Chord.Clashes("<Gamepad>/leftShoulder", game).Contains("SecretDevKey"), "control: LB alone is the game's SecretDevKey");
Check(Chord.Clashes("<Gamepad>/buttonSouth + <Gamepad>/rightShoulder", game).Contains("Action"), "control: A + RB would also be the game's Action");
Check(Chord.Clashes("", game).Count == 0 && Chord.Label("", false) == "not set", "no controller opener");
Check(!Chord.Parts(chord).Any(p => KeyCheck.Overlap(p, "<Gamepad>/select")), "the chord never uses View (GamesOMG Teleport's)");

// ── the built DLL (dotnet run -- <dll>): what it never references ──
if (args.Length == 1)
{
    var refs = new List<string>();
    using (var fs = File.OpenRead(args[0]))
    using (var pe = new PEReader(fs))
    {
        var md = pe.GetMetadataReader();
        string TypeName(EntityHandle h)
        {
            if (h.Kind == HandleKind.TypeReference) { var t = md.GetTypeReference((TypeReferenceHandle)h); return md.GetString(t.Namespace) + "." + md.GetString(t.Name); }
            if (h.Kind == HandleKind.TypeDefinition) { var t = md.GetTypeDefinition((TypeDefinitionHandle)h); return md.GetString(t.Namespace) + "." + md.GetString(t.Name); }
            if (h.Kind == HandleKind.TypeSpecification) return "(spec)";
            return h.Kind.ToString();
        }
        foreach (var h in md.MemberReferences)
        {
            var m = md.GetMemberReference(h);
            refs.Add(TypeName(m.Parent) + "::" + md.GetString(m.Name));
        }
        foreach (var h in md.TypeReferences)
        {
            var t = md.GetTypeReference(h);
            refs.Add("type " + md.GetString(t.Namespace) + "." + md.GetString(t.Name));
        }
    }
    Check(refs.Count > 50, "the DLL's references were read");
    // the raw bytes too, ASCII and UTF-16: a name called through reflection would sit there as a string
    byte[] raw = File.ReadAllBytes(args[0]);
    string ascii = System.Text.Encoding.ASCII.GetString(raw), utf16 = System.Text.Encoding.Unicode.GetString(raw) + System.Text.Encoding.Unicode.GetString(raw, 1, raw.Length - 1);
    refs.Add("raw " + ascii);
    refs.Add("raw " + utf16);
    // 0.4.0: and it never unlocks or decodes anything itself (a chip is decoded by the player, at the game's own screen)
    string[] never = { "SetFreeCraft", "SetEverythingUnlocked", "CreateAndDropOnFloor", "DropObjectsIfNotEnoughSpace", "SetInventorySize", "UnlockGroupGlobally", "DecodeBlueprint" };
    foreach (var name in never)
        Check(!refs.Any(r => r.EndsWith("::" + name) || r.Contains(name)), "the DLL never references " + name);
    Check(!refs.Any(r => r.Contains("GameSettingsHandler") || r.Contains("JsonableGameState")), "the DLL never touches the world's settings (GameSettingsHandler, JsonableGameState)");
    Check(refs.Any(r => r == "SpaceCraft.InventoriesHandler::AddItemToInventory"), "the DLL puts items in through InventoriesHandler.AddItemToInventory");
    Check(refs.Any(r => r == "SpaceCraft.WorldObjectsHandler::DestroyWorldObject"), "the DLL cleans refused items up with WorldObjectsHandler.DestroyWorldObject");
    Check(!refs.Any(r => r.StartsWith("type HarmonyLib")), "no Harmony patch anywhere");
    Check(refs.Any(r => r == "SpaceCraft.GroupItem::GetUnlocksGroup") && refs.Any(r => r == "SpaceCraft.GroupConstructible::GetGroupCategory") && refs.Any(r => r == "SpaceCraft.GroupConstructible::GetGroupUnitGeneration"), "the DLL reads the chips' links and the furniture rule's fields from the game");
    Console.WriteLine("DLL scan: " + (refs.Count - 2) + " references and the raw bytes read; none to the achievement settings or a ground drop.");
}

Console.WriteLine("spawn checks: " + checks + " passed");
foreach (var row in Palette.Table()) Console.WriteLine("  " + row.Value.ToString("0.00").PadLeft(6) + ":1  " + row.Key);

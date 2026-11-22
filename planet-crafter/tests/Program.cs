// ChatGPT C56: meaningful protocol acceptance/rejection, independent of Unity and BepInEx.
using System;
using System.Globalization;
using GamesOMG.PlanetCrafterTeleport;

int checks = 0;
void Check(bool condition, string name)
{
    checks++;
    if (!condition) throw new Exception("FAIL " + name);
}

// -- <file>: every line is a command the map copied (tools/planet-crafter-map/test/tp.mjs writes them); each must parse.
if (args.Length == 1)
{
    int lines = 0;
    foreach (var line in System.IO.File.ReadAllLines(args[0]))
    {
        if (line.Length == 0) continue;
        lines++;
        Check(TeleportRequest.TryParse(line, out _), "the mod reads the map's copied line: " + line);
    }
    Check(lines > 0, "the file holds copied lines");
    Console.WriteLine(lines + " copied map lines: the mod's parser accepts every one");
    return;
}

foreach (var culture in new[] { "en-US", "de-DE", "fr-FR" })
{
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
    Check(TeleportRequest.TryParse("/tp -12.5 +41.25 300", out var point), culture + " numeric command");
    Check(point.X == -12.5f && point.Y == 41.25f && point.Z == 300f && !point.AutomaticHeight, culture + " exact coordinates");
    Check(TeleportRequest.TryParse(" /tp 10 ~ -20.5 \r\n", out var automatic), culture + " automatic-height command");
    Check(automatic.X == 10f && automatic.Z == -20.5f && automatic.AutomaticHeight, culture + " automatic-height flag");
}

foreach (string text in new[]
{
    "", "words copied from somewhere else", "Teleport: 1 2 3", "/gps 1 2 3", "/tp home",
    "/tp 1 2", "/tp 1 2 3 4", "/tp 1,2,3", "/tp 1:2:3", "/tp ~ 2 3", "/tp 1 2 ~",
    "/tp 1 NaN 3", "/tp 1 Infinity 3", "/tp 1 -Infinity 3", "/tp 1 1e100 3", "/tp 1 2,5 3",
    "/tp 1\n2 3", "/tp 1 2 3\n/tp 4 5 6", "/tp 1 2 3; quit", "/tp 1 2 3 # Prime",
    "/tp 1000001 2 3", new string('x', 257)
})
{
    Check(!TeleportRequest.TryParse(text, out _), "unrelated or malformed text remains inert: " + text.Replace("\n", "\\n"));
    Check(!string.IsNullOrEmpty(TeleportRequest.Explain(text)), "a refused line says why: " + text.Replace("\n", "\\n"));
}
Check(!TeleportRequest.TryParse(null, out _), "null clipboard remains inert");
// 0.1.2 paste box: the reason it shows for each kind of refusal, and none for a line that teleports.
Check(TeleportRequest.Explain(null) == "Paste a /tp line first (Ctrl+V).", "empty box asks for a paste");
Check(TeleportRequest.Explain("   ") == "Paste a /tp line first (Ctrl+V).", "blank box asks for a paste");
Check(TeleportRequest.Explain("785 31 795").StartsWith("A teleport line starts with /tp"), "missing /tp is named");
Check(TeleportRequest.Explain("/tp 785 31").StartsWith("Put three values"), "missing value is named");
Check(TeleportRequest.Explain("/tp ~ 31 795").StartsWith("~ only works for the height"), "~ outside the height is named");
Check(TeleportRequest.Explain("/tp 785 31 7,5").StartsWith("x, y and z must be plain numbers"), "decimal comma is named");
Check(TeleportRequest.Explain("/tp 785 31 1000001") == "That spot is far outside any world.", "out-of-range is named");
Check(TeleportRequest.Explain("/tp 1\n2 3") == "Paste one line only.", "two lines are named");
Check(TeleportRequest.Explain("/tp 785 31 795") == null && TeleportRequest.Explain("/tp 785 ~ 795") == null,
    "a valid line has no reason");
Check(TeleportRequest.TryParse("/tp\t.5\t0\t-.75", out var fractional) && fractional.X == .5f && fractional.Z == -.75f,
    "single-line whitespace and decimal fractions");
Check(LandingGeometry.TryPivotY(10, 1, 2, .4f, 0, .08f, out float centered) && Math.Abs(centered - 11.08f) < .0001,
    "center-pivot capsule stands over the floor");
Check(LandingGeometry.TryPivotY(10, 1, 2, .4f, 1, .08f, out float feet) && Math.Abs(feet - 10.08f) < .0001,
    "feet-pivot capsule uses its actual center offset");
float ny = (float)Math.Sqrt(.5);
Check(LandingGeometry.TryPivotY(10, ny, 2, .4f, 1, .08f, out float slope), "walkable slope resolves");
Check((slope + .4f - 10) * ny >= .4f, "lower capsule sphere clears the slope plane");
Check(!LandingGeometry.TryPivotY(10, 0, 2, .4f, 1, .08f, out _), "vertical surface cannot support a landing");
Check(!LandingGeometry.TryPivotY(10, 1, .2f, .4f, 1, .08f, out _), "invalid capsule shape is rejected");
Check(!LandingGeometry.TryPivotY(float.NaN, 1, 2, .4f, 1, .08f, out _), "nonfinite ground is rejected");

// ── 0.2.0: the panel's JSON, the map's spot-set links, the places store ──
CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");   // a comma-decimal PC must still write dots
var tree = MiniJson.Parse("{\"a\":[1,2.5,-3e2,true,false,null],\"s\":\"Caf\\u00e9 \\\"Ü\\\" 北\\n\"}") as System.Collections.Generic.Dictionary<string, object>;
Check(tree != null && MiniJson.S(tree["s"]) == "Café \"Ü\" 北\n" && MiniJson.N(MiniJson.A(tree["a"])[2]) == -300, "JSON reads escapes, unicode and numbers");
Check(MiniJson.Parse(MiniJson.Write(tree)) is System.Collections.Generic.Dictionary<string, object> back && MiniJson.S(back["s"]) == "Café \"Ü\" 北\n"
    && MiniJson.Write(1.25) == "1.25", "JSON writes what it reads, with a dot for decimals");
foreach (var bad in new[] { "", "{", "[1,]", "{\"a\" 1}", "\"open", "[1] x", "{\"a\":01e}" })
{
    bool threw = false;
    try { MiniJson.Parse(bad); } catch (FormatException) { threw = true; }
    Check(threw, "JSON refuses damaged text: " + bad);
}

// a link packed the way map-spots.js packSet packs it: "z" + base64url(deflate-raw(JSON))
string Pack(string json, bool z) => (z ? "z" : "j") + SpotSet.Base64Url(z ? SpotSet.Deflate(System.Text.Encoding.UTF8.GetBytes(json)) : System.Text.Encoding.UTF8.GetBytes(json));
string setJson = "[1,\"Frog crew\",{\"d\":[[1,2,3]]},[785.4,795,\"Frog eggs, north side\",\"note\",\"prime\",\"green\",\"w103761069\",\"Eggs  &  things\"],"
    + "[-12,-34,\"Café Ü 北 base\",\"\",\"humble\",\"nope\"],[1,\"x\"],[\"bad\",2],[5,6]]";
foreach (bool z in new[] { true, false })
{
    string link = "https://gamesomg.com/planet-crafter-map/#spotset=" + Pack(setJson, z);
    Check(SpotSet.TryRead(link, out var spots, out var from, out var why), (z ? "z" : "j") + " spot-set link reads: " + why);
    Check(from == "Frog crew" && spots.Count == 3, "the drawings object and broken rows are skipped");
    Check(spots[0].X == 785.4f && spots[0].Z == 795 && spots[0].Name == "Frog eggs, north side" && spots[0].World == "prime"
        && spots[0].Colour == "green" && spots[0].Pin == "w103761069" && spots[0].Group == "Eggs & things", "a spot's fields, group cleaned as the map does");
    Check(spots[1].Name == "Café Ü 北 base" && spots[1].World == "humble" && spots[1].Colour == "", "non-ASCII names; an unknown colour is dropped");
    Check(spots[2].Name == "Spot" && spots[2].World == "", "a nameless spot is Spot, a viewless one has no world");
}
Check(SpotSet.TryRead("spotset=" + Pack(setJson, true), out _, out _, out _), "a bare spotset= code reads too");
Check(!SpotSet.TryRead("https://gamesomg.com/planet-crafter-map/#spotset=zAAAA", out _, out _, out var damaged) && damaged.StartsWith("The link is damaged"), "a damaged link says so");
Check(!SpotSet.TryRead("#spotset=" + Pack("[2,\"\",[1,2]]", true), out _, out _, out var newer) && newer.Contains("newer"), "a newer format says update");
Check(!SpotSet.TryRead("#spotset=" + Pack("[1,\"\"]", false), out _, out _, out var none) && none == "That link has no spots in it.", "an empty set says so");
Check(!SpotSet.TryRead("/tp 1 2 3", out _, out _, out _) && !SpotSet.LooksLikeLink("/tp 1 2 3"), "a /tp line is not a link");
var bomb = new byte[SpotSet.InflateMax + 10];
Check(!SpotSet.TryRead("#spotset=z" + SpotSet.Base64Url(SpotSet.Deflate(bomb)), out _, out _, out _), "a link that unpacks too large is refused");

var store = new PlaceStore();
var home = store.Save("Prime", "  Home   base ", 10.04f, 20.06f, 30f);
Check(home.Name == "Home base" && home.X == 10f && home.Y == 20.1f && !home.AutoHeight, "Save here: name cleaned, real height kept");
Check(store.Save("prime", "home base", 1, 2, 3).Name == "home base 2", "a repeated name gets a number");
Check(store.Save("prime", "", 1, 2, 3).Name == "Place 3", "an empty name gets one");
for (int i = 0; i < 7; i++) store.AddRecent("prime", "spot " + i, i * 10, 5, 0);
store.AddRecent("prime", "again", 60.5f, 5, 0);
var recent = store.For("prime").Recent;
Check(recent.Count == PlaceStore.RecentMax && recent[0].Name == "again" && !recent.Exists(p => p.Name == "spot 6"), "Recent keeps the newest five; a teleport within 3 m replaces");
Check(SpotSet.TryRead("#spotset=" + Pack(setJson, true), out var linkSpots, out _, out _), "spot-set for import");
var imported = store.Import(linkSpots, "prime", (world, pin) => pin == "w103761069" && world == "prime" ? new[] { 785f, 31f, 795f } : null);
Check(imported.Added == 3 && imported.AddedByWorld["prime"] == 2 && imported.AddedByWorld["humble"] == 1, "each spot goes to its own world; a viewless one to the planet you are on");
var eggs = store.For("prime").Saved.Find(p => p.Name == "Frog eggs, north side");
Check(eggs != null && !eggs.AutoHeight && eggs.Y == 31f && eggs.X == 785f, "a spot on a pin with a checked landing takes the landing");
var cafe = store.For("humble").Saved[0];
Check(cafe.AutoHeight && cafe.Request.AutomaticHeight && cafe.X == -12 && cafe.Z == -34, "a plain map spot lands with ~");
Check(store.Import(linkSpots, "prime").Added == 0, "a second import of the same link adds nothing (merge by name)");
store.For("prime").Visited.Add("w103761069");
var reread = PlaceStore.FromJson(store.ToJson());
Check(reread.ToJson() == store.ToJson() && reread.For("prime").Saved.Count == 5 && reread.For("humble").Saved[0].Name == "Café Ü 北 base"
    && reread.For("prime").Visited.Contains("w103761069") && reread.For("prime").Recent.Count == 5, "the places file reads back exactly");
Check(PlaceStore.FromJson("").For("prime").Saved.Count == 0 && PlaceStore.FromJson("{\"v\":1,\"planets\":{\"prime\":{\"saved\":[{\"n\":\"no y\",\"x\":1,\"z\":2}]}}}").For("prime").Saved.Count == 0,
    "an empty file is an empty store; a row with no height and no auto is dropped");
Check(store.Remove("prime", eggs) && !store.For("prime").Saved.Contains(eggs), "× removes a place");

// ── 0.2.6 (ChatGPT's release review 2026-11-19; Back on F10). Soft checks: one run lists every
// failure, so the same block run on 0.2.5's code (its API stubbed to its old behaviour) proves each one red first.
var soft = new System.Collections.Generic.List<string>();
void Expect(bool condition, string name) { checks++; if (!condition) soft.Add(name); }
Func<string, bool> allThere = _ => true;

// 1. per save: two saves on one planet keep their own ticks, places and Back
var ps = new PlaceStore();
Expect(ps.Use("Custom-1", 11u, allThere) == null, "a first save on a new file has nothing to say");
ps.For("prime").Visited.Add("w1");
ps.Save("prime", "base", 1, 2, 3);
ps.Use("Custom-3", 33u, allThere);
Expect(!ps.For("prime").Visited.Contains("w1") && ps.For("prime").Saved.Count == 0, "another save on the same planet starts clean: ticks and places are per save");
ps.For("prime").Visited.Add("w2");
ps.Use("Custom-1", 11u, allThere);
Expect(ps.For("prime").Visited.Contains("w1") && !ps.For("prime").Visited.Contains("w2") && ps.For("prime").Saved.Count == 1, "back on the first save, its own ticks and places");
var psRead = PlaceStore.FromJson(ps.ToJson());
psRead.Use("Custom-3", 33u, allThere);
Expect(psRead.For("prime").Visited.Contains("w2") && !psRead.For("prime").Visited.Contains("w1"), "the file keeps each save apart");

// 2. the 0.2.4/0.2.5 file goes to the first save loaded, once, with a line saying so
string v1 = "{\"v\":1,\"planets\":{\"prime\":{\"saved\":[{\"n\":\"home\",\"x\":1,\"y\":2,\"z\":3}],\"recent\":[],\"visited\":[\"w9\"]}}}";
var mig = PlaceStore.FromJson(v1);
string said = mig.Use("Custom-1", 11u, allThere);
Expect(said != null && said.Contains("Custom-1"), "the first save loaded gets the old file and says so: " + said);
Expect(mig.For("prime").Visited.Contains("w9") && mig.For("prime").Saved.Count == 1, "its ticks and places came with it");
Expect(mig.Use("Custom-3", 33u, allThere) == null && mig.For("prime").Saved.Count == 0 && !mig.For("prime").Visited.Contains("w9"), "a second save does not get them too");
var migRead = PlaceStore.FromJson(mig.ToJson());
Expect(migRead.Use("Custom-1", 11u, allThere) == null && migRead.For("prime").Visited.Contains("w9"), "migrated once: the line never shows again");

// 3. a save renamed outside the game keeps its places (same world seed, its old name gone); a copy does not take them
var ren = new PlaceStore();
ren.Use("Custom-1", 11u, allThere);
ren.For("prime").Visited.Add("w5");
string renamed = ren.Use("Main base", 11u, name => name != "Custom-1");
Expect(renamed != null && renamed.Contains("Custom-1") && ren.For("prime").Visited.Contains("w5"), "a renamed save keeps its places: " + renamed);
var copied = new PlaceStore();
copied.Use("Custom-1", 11u, allThere);
copied.For("prime").Visited.Add("w5");
copied.Use("Backup", 11u, allThere);
Expect(!copied.For("prime").Visited.Contains("w5"), "a copied save (same seed, the original still there) starts clean");

// 4. a link import names the spots it skipped
var imp = new PlaceStore();
imp.Use("Custom-1", 11u, allThere);
imp.Save("prime", "Frog eggs, north side", 0, 0, 0);
var some = imp.Import(linkSpots, "prime");
string importSaid = PlaceStore.Describe(some, w => w == "prime" ? "Prime" : "Humble");
Expect(some.Added == 2 && some.SkippedNames.Count == 1 && some.SkippedNames[0] == "Frog eggs, north side", "an import keeps the names it skipped");
Expect(importSaid.Contains("Frog eggs, north side") && importSaid.StartsWith("Added 2 places"), "and the line names them: " + importSaid);

// 5. Back: a teleport records where you stood; Back goes there and swaps; a planet change clears it; kept per save
var bk = new PlaceStore();
bk.Use("Custom-1", 11u, allThere);
Expect(bk.Back("prime") == null, "nothing recorded: Back is greyed");
bk.Teleported("prime", 10.04f, 20.06f, 30f);
var b1 = bk.Back("prime");
Expect(b1 != null && b1.X == 10.04f && b1.Y == 20.06f && b1.Z == 30f && !b1.Request.AutomaticHeight, "a teleport records where you stood, height and all");
bk.Teleported("prime", 500f, 60f, 700f);
var b2 = bk.Back("prime");
Expect(b2 != null && b2.X == 500f && b2.Z == 700f, "Back again returns to where Back took you from: one step that swaps");
var bkRead = PlaceStore.FromJson(bk.ToJson());
bkRead.Use("Custom-1", 11u, allThere);
Expect(bkRead.Back("prime") is BackSpot kept && kept.X == 500f && kept.Y == 60f, "kept per save in the file");
bk.Use("Custom-3", 33u, allThere);
Expect(bk.Back("prime") == null, "another save has its own Back, empty");
bk.Use("Custom-1", 11u, allThere);
Expect(bk.Back("humble") == null && bk.Back("prime") == null, "a planet change clears it");

// 6. the places file: a failed write says so and keeps the last good file; a damaged file that cannot be set aside is
// never overwritten; one that can be is kept as .bad and says so
string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "c56-placefile-" + Guid.NewGuid().ToString("N"));
System.IO.Directory.CreateDirectory(dir);
try
{
    var nowhere = new PlaceFile(System.IO.Path.Combine(dir, "no-such-folder", "places.json"), _ => { });
    string err = nowhere.Save(nowhere.Load());
    Expect(err != null && err.Contains("not saved"), "a write that fails says so: " + err);
    string good = System.IO.Path.Combine(dir, "good.json");
    System.IO.File.WriteAllText(good, v1);
    var goodFile = new PlaceFile(good, _ => { });
    var goodStore = goodFile.Load();
    System.IO.Directory.CreateDirectory(good + ".tmp");   // the write cannot be made
    string err2 = goodFile.Save(goodStore);
    Expect(err2 != null && System.IO.File.ReadAllText(good) == v1, "a failed write leaves the last good file as it was: " + err2);
    string broken = "{\"v\":1,\"planets\":{broken";
    string damagedPath = System.IO.Path.Combine(dir, "damaged.json");
    System.IO.File.WriteAllText(damagedPath, broken);
    System.IO.Directory.CreateDirectory(damagedPath + ".bad");   // the copy aside cannot be made
    var damagedFile = new PlaceFile(damagedPath, _ => { });
    string err3 = damagedFile.Save(damagedFile.Load());
    Expect(err3 != null && System.IO.File.ReadAllText(damagedPath) == broken, "a damaged file that could not be set aside is never overwritten: " + err3);
    string damaged2 = System.IO.Path.Combine(dir, "damaged2.json");
    System.IO.File.WriteAllText(damaged2, broken);
    var damagedFile2 = new PlaceFile(damaged2, _ => { });
    var fresh = damagedFile2.Load();
    Expect(damagedFile2.Note != null && System.IO.File.ReadAllText(damaged2 + ".bad") == broken && damagedFile2.Save(fresh) == null,
        "a damaged file is kept as .bad, says so, and the new file saves: " + damagedFile2.Note);
}
finally { try { System.IO.Directory.Delete(dir, true); } catch (Exception) { } }
if (soft.Count > 0) throw new Exception("FAIL " + soft.Count + " of the 0.2.6 checks:\n  FAIL " + string.Join("\n  FAIL ", soft));

// links the LIVE map's own packSet made (make-spotset-fixtures.mjs), checked against what its own unpackSet reads
var fixtures = MiniJson.O(MiniJson.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "spotset-fixtures.json"))));
int fixtureSets = 0;
foreach (var setObj in MiniJson.A(fixtures["sets"]))
{
    var set = MiniJson.O(setObj);
    string link = MiniJson.S(set["link"]);
    Check(SpotSet.TryRead(link, out var got, out var gotFrom, out var gotWhy), "a live-map link reads (" + link.Substring(50, 1) + "): " + gotWhy);
    var want = MiniJson.A(set["spots"]);
    Check(gotFrom == MiniJson.S(set["from"]) && got.Count == want.Count, "the live-map link's sender and spot count match the map's own reader");
    for (int i = 0; i < want.Count && i < got.Count; i++)
    {
        var w = MiniJson.O(want[i]);
        Check(Math.Abs(got[i].X - MiniJson.N(w["x"])) < .001 && Math.Abs(got[i].Z - MiniJson.N(w["z"])) < .001 && got[i].Name == MiniJson.S(w["name"])
            && got[i].World == MiniJson.S(w["world"]) && got[i].Colour == MiniJson.S(w["colour"]) && got[i].Pin == MiniJson.S(w["pin"])
            && got[i].Group == MiniJson.S(w["group"]), "live-map spot " + i + " reads as the map reads it: " + MiniJson.S(w["name"]));
    }
    fixtureSets++;
}
Check(fixtureSets == 2, "both live-map links (deflated and plain) were checked");

// 0.2.1 Places: the packs build-pois.mjs made from the map's data, read as the panel reads them
var prime = PoiWorld.Read(System.IO.File.ReadAllBytes(System.IO.Path.Combine(AppContext.BaseDirectory, "pois-prime.json.z")));
Check(prime.World == "prime" && prime.Build == 26411416 && prime.Map.Length == 40 && prime.Pins.Count > 3000, "Prime's pack: its world, the game build and the map commit it came from");
var frog = prime.ById["w103761069"];
Check(prime.NameOf(frog) == "Generic frog eggs" && prime.Cats[frog.Cat].Key == "eggs" && prime.Cats[frog.Cat].Findable, "a pin keeps the map's own name and layer");
Check(frog.HasLanding && frog.LX == 785 && frog.LY == 31 && frog.LZ == 795 && !frog.Landing.AutomaticHeight, "its checked landing is the map's Copy Teleport one: /tp 785 31 795");
Check(prime.ById["h1769479a"].HasLanding && prime.ById["h1769479a"].LY == 1, "a fish vein's landing too");
var lakeCrate = prime.ById["w104157983"];   // 0.2.4: refused for its water until the research took floors under water
Check(lakeCrate.HasLanding && lakeCrate.LX == -151 && lakeCrate.LY == -59 && lakeCrate.LZ == 380, "a crate under water has its landing on the bed: /tp -151 -59 380");
// 0.2.5 the landing fix: a landing out on the far ring (16-30 m) says how far from the place it sets you down; a near one
// says nothing more, and a copied /tp line never does
var farPoi = new Poi { X = 10.4f, Z = -3f, HasLanding = true, LX = 34, LY = 5, LZ = -3 };
Check(farPoi.Landing.Away == ", 24 m from it" && !farPoi.Landing.AutomaticHeight, "a far-ring landing's notice says how far from the place: 24 m");
Check(new Poi { X = 0, Z = 0, HasLanding = true, LX = 12, LY = 5, LZ = 5 }.Landing.Away == null, "a landing 13 m out says nothing more");
Check(TeleportRequest.TryParse("/tp 34 5 -3", out var copiedFar) && copiedFar.Away == null, "a copied line never carries a distance");
var noLanding = prime.Pins.Find(p => !p.HasLanding);
Check(noLanding != null && !noLanding.HasLanding, "a pin the landing check refused has no teleport");
var startOn = prime.StartLayers();
Check(startOn.Count == 2 && startOn.Contains(prime.Cats.FindIndex(c => c.Key == "zeolite")) && startOn.Contains(prime.Cats.FindIndex(c => c.Key == "uranium")), "a fresh list opens on the map's start layers");
var nearFrog = prime.Query(startOn, "frog", 785, 795, 10, null, out int frogTotal);
Check(nearFrog.Count > 0 && nearFrog[0].Id == "w103761069" && frogTotal >= nearFrog.Count, "a search finds across every layer, nearest first");
var near = prime.Query(startOn, "", 0, 0, 5, null, out int layerTotal);
Check(near.Count == 5 && near.TrueForAll(p => startOn.Contains(p.Cat)) && near[0].Distance2D(0, 0) <= near[4].Distance2D(0, 0) && layerTotal > 5, "no search: the layers that are on, nearest first, capped");
var hidden = new System.Collections.Generic.HashSet<string> { near[0].Id };
Check(prime.Query(startOn, "", 0, 0, 5, hidden, out int lessTotal)[0].Id != near[0].Id && lessTotal == layerTotal - 1, "Hide visited leaves a ticked pin out");
foreach (var w in new[] { "humble", "selenea", "aqualis", "toxicity", "skeo" })
{
    var pack = PoiWorld.Read(System.IO.File.ReadAllBytes(System.IO.Path.Combine(AppContext.BaseDirectory, "pois-" + w + ".json.z")));
    Check(pack.World == w && pack.Pins.Count > 500 && pack.Pins.Exists(p => p.HasLanding) && pack.StartLayers().Count > 0, w + "'s pack reads, with landings and a start layer");
}
// 0.2.3 the Places tree (11-19, from play: headers fold with an arrow; a layer's places open right under it)
{
    var empty = new System.Collections.Generic.HashSet<string>();
    var t = new PlacesTree(prime.StartLayers());
    int golden = prime.Cats.FindIndex(c => c.Key == "golden");
    string crates = prime.Cats[golden].Group;
    var groups = PlacesTree.Groups(prime);
    Check(groups.Count == new System.Collections.Generic.HashSet<string>(groups).Count && groups.Contains(crates) && groups[0] == prime.Cats[0].Group,
        "each group once, in the map's own layer order");
    var r0 = t.Rows(prime, "", 785, 795, empty, false);
    Check(r0[0].Kind == RowKind.Nearest && r0[0].Open && r0[0].Count == 2, "Nearest heads the list, open, on the two start layers");
    var near0 = r0.FindAll(r => r.Kind == RowKind.Place);
    Check(near0.Count == PlacesTree.NearestMax && near0.TrueForAll(r => r.Depth == 1 && startOn.Contains(r.Cat))
        && near0[0].Poi.Distance2D(785, 795) <= near0[near0.Count - 1].Poi.Distance2D(785, 795), "Nearest: the ticked layers' closest places, nearest first, capped");
    Check(r0.FindAll(r => r.Kind == RowKind.Group).Count == groups.Count && !r0.Exists(r => r.Kind == RowKind.Layer) && r0.FindAll(r => r.Kind == RowKind.Group).TrueForAll(r => !r.Open),
        "every group starts folded: a drop-down, no layer rows");

    t.ToggleGroup(crates);
    var r1 = t.Rows(prime, "", 785, 795, empty, false);
    int gi = r1.FindIndex(r => r.Kind == RowKind.Group && r.Group == crates);
    var layerRows = r1.FindAll(r => r.Kind == RowKind.Layer);
    Check(r1[gi].Open && layerRows.Count > 1 && layerRows.TrueForAll(r => r.Group == crates && r.Depth == 1) && r1[gi + 1].Kind == RowKind.Layer,
        "an opened group lists its own layers right under its header, and only those");
    var gold = r1.Find(r => r.Kind == RowKind.Layer && r.Cat == golden);
    Check(gold.Count == prime.Pins.FindAll(p => p.Cat == golden).Count && gold.Visited == 0 && !gold.Open && !gold.On, "a layer row: its count, none visited, folded, off");

    t.ToggleLayer(prime, golden);
    var r2 = t.Rows(prime, "", 785, 795, empty, false);
    int li = r2.FindIndex(r => r.Kind == RowKind.Layer && r.Cat == golden);
    var under = new System.Collections.Generic.List<TreeRow>();
    for (int i = li + 1; i < r2.Count && (r2[i].Kind == RowKind.Place || r2[i].Kind == RowKind.More) && r2[i].Cat == golden; i++) under.Add(r2[i]);
    var goldPlaces = under.FindAll(r => r.Kind == RowKind.Place);
    int goldTotal = gold.Count;
    Check(r2[li].Open && goldPlaces.Count == Math.Min(goldTotal, PlacesTree.LayerFirst) && goldPlaces.TrueForAll(r => r.Depth == 2 && r.Poi.Cat == golden),
        "clicking Golden crates opens ITS places right under its row, not at the bottom");
    bool sorted = true;
    for (int i = 1; i < goldPlaces.Count; i++) sorted &= goldPlaces[i - 1].Poi.Distance2D(785, 795) <= goldPlaces[i].Poi.Distance2D(785, 795);
    Check(sorted, "a layer's places are nearest first");
    Check(goldTotal <= PlacesTree.LayerFirst || under[under.Count - 1].Kind == RowKind.More && under[under.Count - 1].Left == goldTotal - PlacesTree.LayerFirst,
        "a Show more row says how many are left");
    t.ShowMore(prime, golden);
    Check(t.Rows(prime, "", 785, 795, empty, false).FindAll(r => r.Kind == RowKind.Place && r.Depth == 2).Count == Math.Min(goldTotal, PlacesTree.LayerFirst + PlacesTree.LayerMore),
        "Show more adds the next ones under the same row");

    var ticked = new System.Collections.Generic.HashSet<string> { goldPlaces[0].Poi.Id, goldPlaces[1].Poi.Id };
    var r3 = t.Rows(prime, "", 785, 795, ticked, false);
    int fi3 = r3.FindIndex(r => r.Kind == RowKind.Found);
    var g3 = r3.Find(r => r.Kind == RowKind.Group && r.Group == crates && !r.InFound);
    Check(g3.Visited == 2 && g3.Count == prime.Pins.FindAll(p => prime.Cats[p.Cat].Group == crates).Count,
        "0.3.1 a heading says found / all of its group, as Grounded 2's do");
    Check(r3.Find(r => r.Kind == RowKind.Layer && r.Cat == golden && !r.InFound).Visited == 2
        && !r3.Exists(r => r.Kind == RowKind.Place && !r.InFound && ticked.Contains(r.Poi.Id)),
        "0.3.1 a ticked place leaves the list (not dimmed, not struck: moved); the layer row still counts it");
    Check(fi3 == r3.Count - 1 && !r3[fi3].Open && r3[fi3].Count == 2 && r3[fi3].InFound,
        "0.3.1 Found: a drawer at the very bottom, closed, with how many are in it");
    var r4 = t.Rows(prime, "", 785, 795, ticked, true);
    Check(!r4.Exists(r => r.Kind == RowKind.Place && ticked.Contains(r.Poi.Id)) && !r4.Exists(r => r.Kind == RowKind.Found)
        && r4.Find(r => r.Kind == RowKind.Layer && r.Cat == golden).Count == goldTotal,
        "Hide found: no drawer either; the layer's count stays whole");
    t.ToggleDrawer();
    var r3o = t.Rows(prime, "", 785, 795, ticked, false);
    int fo = r3o.FindIndex(r => r.Kind == RowKind.Found);
    var inDrawer = r3o.GetRange(fo + 1, r3o.Count - fo - 1);
    Check(r3o[fo].Open && inDrawer.Count == 4 && inDrawer[0].Kind == RowKind.Group && inDrawer[0].Group == crates && inDrawer[0].Open && inDrawer[0].Count == 2
        && inDrawer[1].Kind == RowKind.Layer && inDrawer[1].Cat == golden && inDrawer[1].Open && inDrawer[1].Count == 2
        && inDrawer[2].Kind == RowKind.Place && inDrawer[3].Kind == RowKind.Place && inDrawer.TrueForAll(r => r.InFound),
        "0.3.1 opened, Found shows only the headings it has places under (Crates & lockers > Golden crates), open, then the places");
    Check(inDrawer[2].Poi.Id == goldPlaces[0].Poi.Id && inDrawer[3].Poi.Id == goldPlaces[1].Poi.Id,
        "0.3.1 found places keep the order they had in the list");
    t.ToggleFoundLayer(prime, golden);
    Check(!t.Rows(prime, "", 785, 795, ticked, false).Exists(r => r.InFound && r.Kind == RowKind.Place), "0.3.1 a heading inside Found folds");
    t.ToggleFoundLayer(prime, golden);
    // every Golden crate found: the layer leaves the list; its group stays for its other layers
    var allGold = new System.Collections.Generic.HashSet<string>(prime.Pins.FindAll(p => p.Cat == golden).ConvertAll(p => p.Id));
    var r6 = t.Rows(prime, "", 785, 795, allGold, false);
    Check(!r6.Exists(r => r.Kind == RowKind.Layer && r.Cat == golden && !r.InFound) && r6.Exists(r => r.Kind == RowKind.Group && r.Group == crates && !r.InFound)
        && r6.Find(r => r.Kind == RowKind.Found).Count == allGold.Count,
        "0.3.1 a layer with every place found leaves the list; its group stays while other layers have places");
    var wholeGroup = new System.Collections.Generic.HashSet<string>(prime.Pins.FindAll(p => prime.Cats[p.Cat].Group == crates && prime.Cats[p.Cat].Findable).ConvertAll(p => p.Id));
    bool groupAllFindable = prime.Cats.TrueForAll(c => c.Group != crates || c.Count == 0 || c.Findable);
    var r7 = t.Rows(prime, "", 785, 795, wholeGroup, false);
    Check(!groupAllFindable || !r7.Exists(r => r.Kind == RowKind.Group && r.Group == crates && !r.InFound),
        "0.3.1 a group whose places are all found leaves the list");
    // a tick on a layer that takes none (or from another world) is not moved: nothing drops out of both lists
    int landingLayer = prime.Cats.FindIndex(c => !c.Findable && c.Count > 0);
    var stray = new System.Collections.Generic.HashSet<string> { prime.Pins.Find(p => p.Cat == landingLayer).Id, "no-such-pin" };
    Check(PlacesTree.FoundOf(prime, stray).Count == 0 && !t.Rows(prime, "", 785, 795, stray, false).Exists(r => r.Kind == RowKind.Found),
        "0.3.1 a stray tick (a layer without ticks, another world) stays in the list, no drawer");
    // untick: back in the list where it was
    var one = new System.Collections.Generic.HashSet<string> { goldPlaces[1].Poi.Id };
    var r8 = t.Rows(prime, "", 785, 795, one, false);
    Check(r8.Exists(r => r.Kind == RowKind.Place && !r.InFound && r.Poi.Id == goldPlaces[0].Poi.Id) && !r8.Exists(r => r.InFound && r.Kind == RowKind.Place && r.Poi.Id == goldPlaces[0].Poi.Id),
        "0.3.1 unticked, a place is back in its layer and out of Found");
    // a search: the found matches in the drawer, open even if it was folded
    t.ToggleDrawer();
    string goldName = prime.NameOf(goldPlaces[0].Poi);
    var s1 = t.Rows(prime, goldName, 785, 795, ticked, false);
    int sf = s1.FindIndex(r => r.Kind == RowKind.Found);
    Check(sf > 0 && s1[sf].Open && s1[sf].Count >= 1 && s1.GetRange(sf + 1, s1.Count - sf - 1).Exists(r => r.Kind == RowKind.Place && r.Poi.Id == goldPlaces[0].Poi.Id)
        && !s1.GetRange(0, sf).Exists(r => r.Kind == RowKind.Place && ticked.Contains(r.Poi.Id)),
        "0.3.1 a search lists found matches in the Found drawer, open, and not above it");
    Check(!t.Rows(prime, goldName, 785, 795, ticked, true).Exists(r => r.Kind == RowKind.Found || r.Kind == RowKind.Place && ticked.Contains(r.Poi.Id)),
        "0.3.1 a search with Hide found shows no found match at all");

    t.ToggleLayer(prime, golden);
    Check(!t.Rows(prime, "", 785, 795, empty, false).Exists(r => r.Kind == RowKind.Place && r.Depth == 2), "clicking it again folds it");
    t.ToggleOn(prime, golden);
    var r5 = t.Rows(prime, "", 785, 795, empty, false);
    Check(t.On.Contains(golden) && r5.Find(r => r.Kind == RowKind.Layer && r.Cat == golden).Open && r5[0].Count == 3
        && r5.FindAll(r => r.Kind == RowKind.Place && r.Depth == 2).Count == Math.Min(goldTotal, PlacesTree.LayerFirst), "ticking a layer on also opens it, from the first page again");
    t.ToggleOn(prime, golden);
    Check(!t.On.Contains(golden) && t.LayerOpen(prime, golden), "ticking it off leaves it open");
    t.ToggleGroup(crates);
    Check(!t.Rows(prime, "", 785, 795, empty, false).Exists(r => r.Kind == RowKind.Layer) && t.LayerOpen(prime, golden), "folding a group hides its layers; what was open inside stays open for next time");
    t.NearestOpen = false;
    Check(!t.Rows(prime, "", 785, 795, empty, false).Exists(r => r.Kind == RowKind.Place), "Nearest folds too");

    var f = t.Rows(prime, "frog", 785, 795, empty, false);
    Check(f.Count > 1 && f[0].Kind == RowKind.Place && f[0].Depth == 0 && f[0].Poi.Id == "w103761069" && !f.Exists(r => r.Kind == RowKind.Group || r.Kind == RowKind.Nearest),
        "Find is still one flat list of matches, nearest first");
    Check(t.Rows(prime, "no such thing here", 0, 0, empty, false)[0].Text.StartsWith("Nothing"), "Find with no match says so");
}
// 0.2.4 the map's found list, both ways (#foundset=): links the map's own GomgSets.link made (build-foundset.mjs), read
// here as the map reads them; codes made here, written out for the map's own GomgSets.read (build-foundset.mjs --verify)
{
    var fs = FoundSet.Read(System.IO.File.ReadAllBytes(System.IO.Path.Combine(AppContext.BaseDirectory, "foundset-pc.json.z")));
    var fx = MiniJson.O(MiniJson.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "foundset-fixtures.json"))));
    Check(fs.Key == "gomg_pc_found" && fs.Url == "/planet-crafter-map/" && fs.Ids.Count == (int)MiniJson.N(fx["n"]) && fs.Hash == MiniJson.S(fx["hash"])
        && FoundSet.HashOf(fs.Ids) == fs.Hash && fs.Worlds.Count == 6, "the found set: the map's key, its pin count and its own FNV-1a hash");
    var packs = new System.Collections.Generic.Dictionary<string, PoiWorld>();
    foreach (var w in fs.Worlds) packs[w] = PoiWorld.Read(System.IO.File.ReadAllBytes(System.IO.Path.Combine(AppContext.BaseDirectory, "pois-" + w + ".json.z")));
    bool allIn = true;
    for (int i = 0; i < fs.Ids.Count; i++) allIn &= packs[fs.Worlds[fs.W[i]]].ById.TryGetValue(fs.Ids[i], out var poi) && packs[fs.Worlds[fs.W[i]]].Cats[poi.Cat].Findable;
    Check(allIn, "every pin of the map's found list is in its planet's Places, on a layer that takes a ✓");
    int sets = 0;
    foreach (var setObj in MiniJson.A(fx["sets"]))
    {
        var set = MiniJson.O(setObj);
        string link = MiniJson.S(set["link"]);
        var want = new System.Collections.Generic.HashSet<string>();
        foreach (var id in MiniJson.A(set["ids"])) want.Add(MiniJson.S(id));
        Check(FoundSet.LooksLikeLink(link) && !SpotSet.LooksLikeLink(link), "a found-set link is one (" + MiniJson.S(set["name"]) + ")");
        Check(fs.TryRead(link, out var got, out var why), "the map's own link reads: " + MiniJson.S(set["name"]) + " " + why);
        var gotIds = new System.Collections.Generic.HashSet<string>();
        foreach (int i in got) gotIds.Add(fs.Ids[i]);
        Check(gotIds.SetEquals(want), "the mod reads the map's link as the map reads it: " + MiniJson.S(set["name"]));
        var on = new System.Collections.Generic.HashSet<int>(got);
        Check(fs.TryRead("#foundset=" + fs.CodeOf(on.Contains), out var again, out _) && new System.Collections.Generic.HashSet<int>(again).SetEquals(on), "and writes it back: " + MiniJson.S(set["name"]));
        sets++;
    }
    Check(sets == 5, "every fixture link was read");
    // codes made here, for the map's own reader to check (written beside the test, read by build-foundset.mjs --verify)
    var made = new System.Collections.Generic.List<object>();
    var rnd = new Random(56);
    foreach (var pick in new Func<int, bool>[] { i => false, i => true, i => i % 3 == 0, i => fs.W[i] == 4, i => rnd.Next(10) == 0, i => i == fs.Ids.Count - 1 })
    {
        var ticks = new System.Collections.Generic.List<object>();
        for (int i = 0; i < fs.Ids.Count; i++) if (pick(i)) ticks.Add(fs.Ids[i]);
        var set = new System.Collections.Generic.HashSet<string>();
        foreach (var t in ticks) set.Add((string)t);
        string code = fs.CodeOf(i => set.Contains(fs.Ids[i]));
        Check(fs.LinkOf(i => set.Contains(fs.Ids[i])) == "https://gamesomg.com/planet-crafter-map/#foundset=" + code, "Copy for the map: the map's own link shape");
        made.Add(new System.Collections.Generic.Dictionary<string, object> { ["code"] = code, ["ids"] = ticks });
    }
    System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "foundset-made.json"), MiniJson.Write(made));
    string goodCode = fs.CodeOf(i => i < 9);
    Check(!fs.TryRead("#foundset=" + goodCode.Replace("." + fs.Hash + ".", ".zzzzzz."), out _, out var changed) && changed.Contains("Update the mod"), "a link over other pins (another hash) is refused, never guessed");
    Check(!fs.TryRead("#foundset=gomg_other." + fs.Hash + ".bAQ", out _, out var other) && other.Contains("another list"), "another list's link is refused");
    Check(!fs.TryRead("#foundset=" + fs.Key + "." + fs.Hash + ".z!!!", out _, out var bad) && bad.Contains("damaged"), "damaged bits say so");
    Check(!fs.TryRead("#foundset=" + fs.Key + "." + fs.Hash + ".b" + SpotSet.Base64Url(new byte[(fs.Ids.Count + 7) / 8 + 1]), out _, out var tooLong) && tooLong.Contains("damaged"), "more bits than pins is damaged");
    Check(fs.TryRead("#foundset=" + fs.Key + "." + fs.Hash + ".b", out var noneTicked, out _) && noneTicked.Count == 0, "an empty set reads as no ticks");
    // Send to game: spots and found ticks side by side in one link, each reader takes its own key
    string spotLink = MiniJson.S(MiniJson.O(MiniJson.A(fixtures["sets"])[0])["link"]);
    string both = spotLink + "&foundset=" + fs.CodeOf(i => i == 0 || i == 5);
    Check(SpotSet.LooksLikeLink(both) && FoundSet.LooksLikeLink(both) && SpotSet.TryRead(both, out var bothSpots, out _, out _) && bothSpots.Count > 0
        && fs.TryRead(both, out var bothTicks, out _) && bothTicks.Count == 2 && bothTicks[0] == 0 && bothTicks[1] == 5, "a Send to game link: the spots read and the ticks read, from one link");
    // the store: a tick lands on its own planet, merge only
    var st = new PlaceStore();
    st.For("prime").Visited.Add(fs.Ids[fs.W.IndexOf(0)]);
    int toxicFirst = fs.W.IndexOf(4);
    Check(fs.Worlds[4] == "toxicity" && toxicFirst >= 0 && packs["toxicity"].ById.ContainsKey(fs.Ids[toxicFirst]), "a toxicity tick belongs to Toxicity's Places");
}
// 0.3.0: the data, settings, keys and focus half of the look-and-settings pass (tests/Polish030.cs)
CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
var fails030 = Polish030.Run(w => PoiWorld.Read(System.IO.File.ReadAllBytes(System.IO.Path.Combine(AppContext.BaseDirectory, "pois-" + w + ".json.z"))));
if (fails030.Count > 0) throw new Exception("FAIL " + fails030.Count + " of the 0.3.0 checks:\n  FAIL " + string.Join("\n  FAIL ", fails030));
// 0.3.0: the Codex C61 review's findings this release closes (tests/Review061.cs)
var failsR = Review061.Run();
if (failsR.Count > 0) throw new Exception("FAIL " + failsR.Count + " of the C61 review checks:\n  FAIL " + string.Join("\n  FAIL ", failsR));
// 0.3.2: notes, schema 4, and the .cfg moved onto compact rows and one-click teleport (tests/Polish032.cs)
var fails032 = Polish032.Run();
if (fails032.Count > 0) throw new Exception("FAIL " + fails032.Count + " of the 0.3.2 checks:\n  FAIL " + string.Join("\n  FAIL ", fails032));
Console.WriteLine("0.3.2 checks: passed");
// 0.3.3: the distance line's height rule and fit, and the game's keys refused (tests/Polish033.cs)
var fails033 = Polish033.Run(w => PoiWorld.Read(System.IO.File.ReadAllBytes(System.IO.Path.Combine(AppContext.BaseDirectory, "pois-" + w + ".json.z"))));
if (fails033.Count > 0) throw new Exception("FAIL " + fails033.Count + " of the 0.3.3 checks:\n  FAIL " + string.Join("\n  FAIL ", fails033));
Console.WriteLine("0.3.3 checks: passed");
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
Console.WriteLine(checks + " parser/geometry checks passed; no game assembly loaded");

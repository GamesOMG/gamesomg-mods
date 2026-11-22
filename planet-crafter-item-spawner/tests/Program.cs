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

// items the game never names (build 26411416: Fish1Hatched, Tree5Growable, BlueprintAutocrafter…): out of browsing, found by id
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
    string[] never = { "SetFreeCraft", "SetEverythingUnlocked", "CreateAndDropOnFloor", "DropObjectsIfNotEnoughSpace", "SetInventorySize" };
    foreach (var name in never)
        Check(!refs.Any(r => r.EndsWith("::" + name) || r.Contains(name)), "the DLL never references " + name);
    Check(!refs.Any(r => r.Contains("GameSettingsHandler") || r.Contains("JsonableGameState")), "the DLL never touches the world's settings (GameSettingsHandler, JsonableGameState)");
    Check(refs.Any(r => r == "SpaceCraft.InventoriesHandler::AddItemToInventory"), "the DLL puts items in through InventoriesHandler.AddItemToInventory");
    Check(refs.Any(r => r == "SpaceCraft.WorldObjectsHandler::DestroyWorldObject"), "the DLL cleans refused items up with WorldObjectsHandler.DestroyWorldObject");
    Check(!refs.Any(r => r.StartsWith("type HarmonyLib")), "no Harmony patch anywhere");
    Console.WriteLine("DLL scan: " + (refs.Count - 2) + " references and the raw bytes read; none to the achievement settings or a ground drop.");
}

Console.WriteLine("spawn checks: " + checks + " passed");
foreach (var row in Palette.Table()) Console.WriteLine("  " + row.Value.ToString("0.00").PadLeft(6) + ":1  " + row.Key);

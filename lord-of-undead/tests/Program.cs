// dotnet run --project tests   (exit 0 = every check passed)
using System;
using System.IO;
using System.Linq;
using GamesOMG.LordOfUndeadTeleport;

int fails = 0, checks = 0;
void Check(bool ok, string what) { checks++; if (!ok) { fails++; Console.WriteLine("FAIL " + what); } }
void Line(string s, float x, float? y, float z)
{
    bool ok = TpLine.TryParse(s, out var t);
    Check(ok && Math.Abs(t.X - x) < 1e-3 && Math.Abs(t.Z - z) < 1e-3 && (y.HasValue ? t.Y.HasValue && Math.Abs(t.Y.Value - y.Value) < 1e-3 : !t.Y.HasValue),
          "parse \"" + s + "\" -> " + (ok ? t.ToString() : "no line"));
}
void NoLine(string s) => Check(!TpLine.TryParse(s, out _), "no line in \"" + s + "\"");

// what the map's button copies (tp-copy.js: "/tp {x} {y} {z}", rounded)
Line("/tp 1257 5 860", 1257, 5, 860);
Line("/tp -63 -2 1114", -63, -2, 1114);
Line("  /tp 785 31 795  ", 785, 31, 795);
Line("/tp 785 ~ 795", 785, null, 795);
Line("/tp 785 795", 785, null, 795);
Line("tp 10, 20, 30", 10, 20, 30);
Line("/TP 10.5 2.25 -7.75", 10.5f, 2.25f, -7.75f);
Line("copied: /tp 1 2 3 from the map", 1, 2, 3);
Line("/tp 1257\t5\t860", 1257, 5, 860);
NoLine("");
NoLine("hello");
NoLine("/tp");
NoLine("/tp 12");
NoLine("/tp a b c");
NoLine("/tp 1e9 2 3");
NoLine("/tp 999999 2 3");
NoLine("stop 1 2 3");                // "tp" inside a word is not the command
Check(TpLine.LooksLikeTp("/tp oops"), "a broken /tp line is still seen as one");
Check(!TpLine.LooksLikeTp("copper mine"), "a place name is not a /tp line");
Check(new TpTarget(1.4f, 2.6f, -3.5f).ToString() == "/tp 1 3 -4", "TpTarget prints whole metres: " + new TpTarget(1.4f, 2.6f, -3.5f));
Check(new TpTarget(1, null, 2).ToString() == "/tp 1 ~ 2", "no height prints ~");

// the place list the DLL embeds
string tsv = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data", "places.tsv");
var places = Places.Parse(new StringReader(File.ReadAllText(tsv)));
int rows = File.ReadAllLines(tsv).Count(l => l.Length > 0 && l[0] != '#');
Check(places.Count == rows && rows > 0, "every row of places.tsv parses (" + places.Count + " of " + rows + ")");
Check(places.Select(p => p.Id).Distinct().Count() == places.Count, "place ids are unique");
foreach (var p in places)
{
    double d = Math.Sqrt((p.X - p.PX) * (p.X - p.PX) + (p.Z - p.PZ) * (p.Z - p.PZ));
    // the landing rule: beside the pin (rings of 1.5-10 m, /tp rounds to whole metres), never on it
    if (d < 1.0 || d > 11.5) Check(false, "landing of " + p.Id + " " + p.Title + " is " + d.ToString("0.0") + " m from its pin");
    if (string.IsNullOrWhiteSpace(p.Name)) Check(false, p.Id + " has no name");
}
Check(true, "landing distances");

void Top(string q, Func<Place, bool> want, string what)
{
    var r = Places.Search(places, q);
    Check(r.Count > 0 && want(r[0]), "search \"" + q + "\" first = " + (r.Count > 0 ? r[0].Title + " (" + r[0].LayerName + ")" : "nothing") + "; want " + what);
}
Top("copper mine", p => p.Name == "Copper mine", "the Copper mine place");
Top("COPPER mine", p => p.Name == "Copper mine", "case does not matter");
Top("spider queen", p => p.Name.StartsWith("Spider queen"), "the Spider queen");
Top("graveyard", p => p.Name == "Graveyard", "the Graveyard");
Top("rare chest graveyard", p => p.Layer == "rare" && p.Near == "Graveyard", "a rare chest near the Graveyard");
Top("morkanite", p => p.Name.Contains("Morkanite"), "Morkanite");
Check(Places.Search(places, "zzzz").Count == 0, "nonsense finds nothing");
Check(Places.Search(places, "").Count == 0, "an empty box lists nothing (the hint shows)");
Check(Places.Search(places, "chest").Count > 10, "a layer word finds the layer");

// how far and which way
Check(Places.Away(0, 0, 0, 5) == "here", "under 15 m is here");
Check(Places.Away(0, 0, 0, 1000) == "1.0 km north", "north is +Z: " + Places.Away(0, 0, 0, 1000));
Check(Places.Away(0, 0, 340, 0) == "340 m east", "east is +X: " + Places.Away(0, 0, 340, 0));
Check(Places.Away(0, 0, -100, -100) == "140 m south-west", "south-west: " + Places.Away(0, 0, -100, -100));

Console.WriteLine((fails == 0 ? "PASS" : "FAIL") + ": " + (checks - fails) + " of " + checks + " checks, " + places.Count + " places");
return fails == 0 ? 0 : 1;

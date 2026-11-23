// GamesOMG Teleport for Satisfactory: the places file's rules, checked without the game. Run headless in the editor:
//   UnrealEditor-Cmd.exe <SML>\FactoryGame.uproject -ExecCmds="Automation RunTests GamesOMG.Store;Quit" -unattended -nullrhi -nosplash
#include "GOMGStore.h"

#include "Misc/AutomationTest.h"

#if WITH_DEV_AUTOMATION_TESTS

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FGOMGStoreTest, "GamesOMG.Store", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

bool FGOMGStoreTest::RunTest(const FString&)
{
	GOMGStore::ResetForTest();

	// names: one line, spaces squeezed, cut at the limit
	TestEqual(TEXT("clean squeezes"), GOMGStore::Clean(TEXT("  Iron \t base\n 2 "), 60), FString(TEXT("Iron base 2")));
	TestEqual(TEXT("clean cuts"), GOMGStore::Clean(FString::ChrN(70, TEXT('a')), 60).Len(), 60);

	// Save here: unique names in a world, the default when nothing is typed, the cap
	FGOMGWorld& W = GOMGStore::World(TEXT("Test World"));
	FGOMGSpot* A = GOMGStore::AddSaved(W, TEXT("Base"), FVector(100.04, 200, 300), 90, TEXT(""));
	FGOMGSpot* B = GOMGStore::AddSaved(W, TEXT("base"), FVector(1, 2, 3), 0, TEXT(""));
	TestTrue(TEXT("added"), A != nullptr && B != nullptr);
	TestEqual(TEXT("second gets a number"), W.Saved[1].Name, FString(TEXT("base 2")));
	TestEqual(TEXT("rounded to 0.1 cm"), W.Saved[0].Feet.X, 100.0);
	GOMGStore::AddSaved(W, TEXT("   "), FVector::ZeroVector, 0, TEXT("Near Iron Ore, Grass Fields"));
	TestEqual(TEXT("default name"), W.Saved[2].Name, FString(TEXT("Near Iron Ore, Grass Fields")));

	// Rename: unique, never empty
	TestEqual(TEXT("rename taken (Base and base 2 both are)"), GOMGStore::Rename(W, 2, TEXT("BASE")), FString(TEXT("BASE 3")));
	TestEqual(TEXT("rename own name"), GOMGStore::Rename(W, 0, TEXT("Base")), FString(TEXT("Base")));
	TestEqual(TEXT("rename empty"), GOMGStore::Rename(W, 0, TEXT("  ")), FString());
	TestEqual(TEXT("unchanged"), W.Saved[0].Name, FString(TEXT("Base")));

	// Recently travelled: newest first, one within 3 m (or the same name) replaces, five at most
	for (int32 i = 0; i < 7; ++i) GOMGStore::AddRecent(W, FString::Printf(TEXT("Trip %d"), i), FVector(i * 10000.0, 0, 0), 0);
	TestEqual(TEXT("five kept"), W.Recent.Num(), 5);
	TestEqual(TEXT("newest first"), W.Recent[0].Name, FString(TEXT("Trip 6")));
	GOMGStore::AddRecent(W, TEXT("Same spot"), FVector(60000.0 + 150, 0, 0), 0);
	TestEqual(TEXT("near one replaced"), W.Recent.Num(), 5);
	TestEqual(TEXT("replacement first"), W.Recent[0].Name, FString(TEXT("Same spot")));
	TestFalse(TEXT("old one gone"), W.Recent.ContainsByPredicate([](const FGOMGSpot& S) { return S.Name == TEXT("Trip 6"); }));

	// notes: kept by pin, "" deletes
	TestEqual(TEXT("note kept"), GOMGStore::SetNote(W, TEXT("0a1b2c3d"), TEXT(" Come back  later ")), FString(TEXT("Come back later")));
	GOMGStore::SetNote(W, TEXT("ffff0000"), TEXT("x"));
	GOMGStore::SetNote(W, TEXT("ffff0000"), TEXT(""));
	TestEqual(TEXT("note deleted"), W.Notes.Num(), 1);

	// the file: everything survives a round trip
	W.Saved[0].bFav = true;
	W.Saved[0].Note = TEXT("Coal next door");
	W.Saved[1].Pin = TEXT("12345678");
	W.Found.Add(TEXT("deadbeef"));
	W.bHasBack = true; W.Back = FVector(5, 6, 7); W.BackYaw = 45; W.BackName = TEXT("Iron Ore, Grass Fields");
	GOMGStore::Settings().TextSize = 120;
	GOMGStore::Settings().bLeft = true;
	GOMGStore::Settings().On.Add(TEXT("somersloop"));
	const FString Json = GOMGStore::ToJson();
	int32 Version = 0;
	TestTrue(TEXT("reads back"), GOMGStore::FromJson(Json, Version));
	TestEqual(TEXT("version"), Version, GOMGStore::FileVersion);
	const FGOMGWorld& R = GOMGStore::World(TEXT("Test World"));
	TestEqual(TEXT("saved kept"), R.Saved.Num(), 3);
	TestTrue(TEXT("fav kept"), R.Saved[0].bFav);
	TestEqual(TEXT("note on a place kept"), R.Saved[0].Note, FString(TEXT("Coal next door")));
	TestEqual(TEXT("pin kept"), R.Saved[1].Pin, FString(TEXT("12345678")));
	TestEqual(TEXT("yaw kept"), R.Saved[0].Yaw, 90.f);
	TestEqual(TEXT("recent kept"), R.Recent.Num(), 5);
	TestEqual(TEXT("map note kept"), R.Notes.FindRef(TEXT("0a1b2c3d")), FString(TEXT("Come back later")));
	TestTrue(TEXT("found kept"), R.Found.Contains(TEXT("deadbeef")));
	TestEqual(TEXT("back name kept"), R.BackName, FString(TEXT("Iron Ore, Grass Fields")));
	TestEqual(TEXT("text size kept"), GOMGStore::Settings().TextSize, 120);
	TestTrue(TEXT("side kept"), GOMGStore::Settings().bLeft);
	TestTrue(TEXT("layer on kept"), GOMGStore::Settings().On.Contains(TEXT("somersloop")));

	// a 1.0.0 file (version 1): keys, ticks and the back array read; nothing else is lost
	const FString Old = TEXT("{\"version\":1,\"settings\":{\"panelKey\":\"F8\",\"backKey\":\"F10\",\"closeAfterTeleport\":false,\"collapsed\":[\"Mg:0\"]},")
		TEXT("\"worlds\":{\"Rubber Ducky\":{\"found\":[\"aa\",\"bb\"],\"back\":[100,200,300,90]}}}");
	TestTrue(TEXT("1.0.0 file reads"), GOMGStore::FromJson(Old, Version));
	TestEqual(TEXT("1.0.0 version"), Version, 1);
	TestEqual(TEXT("1.0.0 panel key"), GOMGStore::Settings().PanelKey, EKeys::F8);
	TestFalse(TEXT("1.0.0 close after"), GOMGStore::Settings().bCloseAfterTeleport);
	const FGOMGWorld& D = GOMGStore::World(TEXT("Rubber Ducky"));
	TestEqual(TEXT("1.0.0 ticks"), D.Found.Num(), 2);
	TestTrue(TEXT("1.0.0 back"), D.bHasBack && D.Back.Equals(FVector(100, 200, 300)) && D.BackYaw == 90.f);
	TestEqual(TEXT("1.0.0 no saved places"), D.Saved.Num(), 0);

	// not ours, or damaged: refused
	TestFalse(TEXT("garbage refused"), GOMGStore::FromJson(TEXT("not json"), Version));
	TestFalse(TEXT("no version refused"), GOMGStore::FromJson(TEXT("{\"worlds\":{}}"), Version));

	// a test never writes the player's file
	FString Why;
	TestFalse(TEXT("save blocked in a test"), GOMGStore::Save(&Why));
	return true;
}

#endif

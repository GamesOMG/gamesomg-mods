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
	TestEqual(TEXT("1.0.0 closed group: 1.1's default, nothing flipped"), GOMGStore::Settings().Flipped.Num(), 0);

	// 1.0.0's fold state carries over (1.1.0 dropped it). A real 1.0.0 file's list: purple slugs and iron closed (1.1's
	// default anyway), hard drives opened, so the hard drives layer and its Loot group open
	const FString Played10 = TEXT("{\"version\":1,\"settings\":{\"panelKey\":\"F9\",\"backKey\":\"F10\",\"closeAfterTeleport\":true,")
		TEXT("\"collapsed\":[\"Mc:slug-purple\",\"Mc:oreiron\",\"!Mc:pod\"]},\"worlds\":{\"Mean Bean\":{\"found\":[],\"back\":[-78634,292640,-4822,67]}}}");
	TestTrue(TEXT("played 1.0.0 file reads"), GOMGStore::FromJson(Played10, Version));
	{
		const TSet<FString>& F = GOMGStore::Settings().Flipped;
		TestTrue(TEXT("hard drives layer open"), F.Contains(TEXT("l:pod")));
		TestTrue(TEXT("its Loot group open"), F.Contains(TEXT("g:1")));
		TestEqual(TEXT("nothing else turned"), F.Num(), 2);
		TestEqual(TEXT("1.0.0's list kept as it came"), GOMGStore::Settings().Collapsed10.Num(), 3);
		TestTrue(TEXT("and written back for 1.0.0"), GOMGStore::ToJson().Contains(TEXT("\"!Mc:pod\"")));
	}
	// every kind of 1.0.0 key: only a choice off 1.1's default is kept; a layer you opened under a group you closed stays hidden
	{
		TSet<FString> F;
		GOMGStore::FlipsFrom10({ TEXT("!Mg:3"), TEXT("Mg:2"), TEXT("!Mc:sulfur"), TEXT("Mc:coal"), TEXT("!Mc:well-water"), TEXT("Mg:4"),
			TEXT("!F:drawer"), TEXT("Fg:0"), TEXT("!Fg:2"), TEXT("Fc:mercer"), TEXT("!Fc:tapes"), TEXT("Mg:99"), TEXT("!Mc:nope"), TEXT("junk") }, F);
		TestEqual(TEXT("group opened"), F.Contains(TEXT("g:3")), true);
		TestEqual(TEXT("sulfur layer opened"), F.Contains(TEXT("l:sulfur")), true);
		TestEqual(TEXT("water layer opened"), F.Contains(TEXT("l:well-water")), true);
		TestEqual(TEXT("its closed group stays closed"), F.Contains(TEXT("g:4")), false);
		TestEqual(TEXT("drawer opened"), F.Contains(TEXT("found")), true);
		TestEqual(TEXT("drawer group closed"), F.Contains(TEXT("fg:0")), true);
		TestEqual(TEXT("drawer layer closed"), F.Contains(TEXT("fl:mercer")), true);
		TestEqual(TEXT("exactly those"), F.Num(), 6);
	}

	// Settings › Controls: Enter types '\r', a control character, so it gets the panel's own-key refusal, not the typing one
	TestFalse(TEXT("Enter doesn't type"), GOMGStore::TypesInBoxes(EKeys::Enter, TEXT('\r')));
	TestTrue(TEXT("Enter is the panel's"), GOMGStore::PanelOwnKey(EKeys::Enter));
	TestTrue(TEXT("a letter types"), GOMGStore::TypesInBoxes(EKeys::A, TEXT('a')));
	TestTrue(TEXT("Space types"), GOMGStore::TypesInBoxes(EKeys::SpaceBar, TEXT(' ')));
	TestTrue(TEXT("Backspace edits"), GOMGStore::TypesInBoxes(EKeys::BackSpace, TEXT('\b')));
	TestTrue(TEXT("Tab moves between boxes"), GOMGStore::TypesInBoxes(EKeys::Tab, TEXT('\t')));
	TestFalse(TEXT("F6 is free to bind"), GOMGStore::TypesInBoxes(EKeys::F6, 0) || GOMGStore::PanelOwnKey(EKeys::F6));
	// the suggestion never names a key either action is on
	GOMGStore::Settings().PanelKey = EKeys::F9; GOMGStore::Settings().BackKey = EKeys::F10;
	TestTrue(TEXT("defaults: F6 or Insert"), GOMGStore::FreeKeys() == TArray<FKey>({ EKeys::F6, EKeys::Insert }));
	GOMGStore::Settings().PanelKey = EKeys::F6;
	TestTrue(TEXT("Places on F6: Insert or F7"), GOMGStore::FreeKeys() == TArray<FKey>({ EKeys::Insert, EKeys::F7 }));
	GOMGStore::Settings().BackKey = EKeys::Insert;
	TestTrue(TEXT("both taken: F7 or F8"), GOMGStore::FreeKeys() == TArray<FKey>({ EKeys::F7, EKeys::F8 }));

	// back to 1.0.0 and back again: 1.0.0 read 1.1's file and wrote only keys, ticks and Return over it. places.v2.json (1.1.1's
	// copy) gives back the rest; 1.0.0's keys, a tick it added and its new Return stand
	GOMGStore::ResetForTest();
	{
		FGOMGWorld& M = GOMGStore::World(TEXT("Mean Bean"));
		GOMGStore::AddSaved(M, TEXT("Main base"), FVector(1000, 2000, 300), 90, TEXT(""))->bFav = true;
		GOMGStore::AddSaved(M, TEXT("Oil"), FVector(-5000, 100, 50), 0, TEXT(""));
		GOMGStore::AddRecent(M, TEXT("Hard Drive, Grass Fields"), FVector(7000, 7000, 0), 0);
		GOMGStore::SetNote(M, TEXT("0a1b2c3d"), TEXT("Locked"));
		M.Found.Add(TEXT("aa"));
		M.bHasBack = true; M.Back = FVector(100, 200, 300); M.BackYaw = 10; M.BackName = TEXT("Iron Ore, Grass Fields");
		FGOMGWorld& Other = GOMGStore::World(TEXT("Second World"));
		Other.bHasBack = true; Other.Back = FVector(5, 6, 7); Other.BackName = TEXT("Coal, Swamp");
		GOMGStore::Settings().TextSize = 140;
		GOMGStore::Settings().Flipped.Add(TEXT("g:3"));
	}
	const FString Mirror = GOMGStore::ToJson();
	// what 1.0.0 writes after a teleport and a tick, and with its keys file-edited to F8: the same worlds, version 1
	const FString By10 = TEXT("{\"version\":1,\"settings\":{\"panelKey\":\"F8\",\"backKey\":\"F10\",\"closeAfterTeleport\":false,\"collapsed\":[\"!Mc:pod\"]},")
		TEXT("\"worlds\":{\"Mean Bean\":{\"found\":[\"aa\",\"bb\"],\"back\":[9000,9000,100,45]},\"Second World\":{\"found\":[]}}}");
	TestTrue(TEXT("1.0.0's rewrite reads"), GOMGStore::FromJson(By10, Version) && Version == 1);
	TestEqual(TEXT("1.0.0 left no saved places"), GOMGStore::World(TEXT("Mean Bean")).Saved.Num(), 0);
	TestTrue(TEXT("taken back"), GOMGStore::TakeBack(Mirror));
	{
		const FGOMGWorld& M = GOMGStore::World(TEXT("Mean Bean"));
		TestEqual(TEXT("saved places back"), M.Saved.Num(), 2);
		TestTrue(TEXT("favourite back"), M.Saved.Num() == 2 && M.Saved[0].bFav && M.Saved[0].Name == TEXT("Main base"));
		TestEqual(TEXT("recent back"), M.Recent.Num(), 1);
		TestEqual(TEXT("note back"), M.Notes.FindRef(TEXT("0a1b2c3d")), FString(TEXT("Locked")));
		TestTrue(TEXT("tick from 1.0.0 kept"), M.Found.Contains(TEXT("bb")) && M.Found.Contains(TEXT("aa")));
		TestTrue(TEXT("1.0.0's Return wins"), M.bHasBack && M.Back.Equals(FVector(9000, 9000, 100)) && M.BackYaw == 45.f);
		TestTrue(TEXT("and loses the old spot's name"), M.BackName.IsEmpty());
		const FGOMGWorld& O = GOMGStore::World(TEXT("Second World"));
		TestTrue(TEXT("a Return 1.0.0 couldn't read stays, with its name"), O.bHasBack && O.BackName == TEXT("Coal, Swamp"));
		const FGOMGSettings& S = GOMGStore::Settings();
		TestEqual(TEXT("1.0.0's key stands"), S.PanelKey, EKeys::F8);
		TestFalse(TEXT("1.0.0's close-after stands"), S.bCloseAfterTeleport);
		TestEqual(TEXT("1.1's text size back"), S.TextSize, 140);
		TestTrue(TEXT("1.1's folds back, not 1.0.0's mapped again"), S.Flipped.Num() == 1 && S.Flipped.Contains(TEXT("g:3")));
		TestEqual(TEXT("1.0.0's fold list kept for it"), S.Collapsed10.Num(), 1);
	}
	// a copy that isn't 1.1's file changes nothing
	TestTrue(TEXT("re-read 1.0.0's"), GOMGStore::FromJson(By10, Version));
	TestFalse(TEXT("damaged copy refused"), GOMGStore::TakeBack(TEXT("{\"version\":2,")));
	TestFalse(TEXT("a version-1 copy refused"), GOMGStore::TakeBack(By10));
	TestEqual(TEXT("1.0.0's file as read"), GOMGStore::Settings().PanelKey, EKeys::F8);
	TestTrue(TEXT("its ticks as read"), GOMGStore::World(TEXT("Mean Bean")).Found.Num() == 2 && GOMGStore::World(TEXT("Mean Bean")).Saved.Num() == 0);

	// not ours, or damaged: refused
	TestFalse(TEXT("garbage refused"), GOMGStore::FromJson(TEXT("not json"), Version));
	TestFalse(TEXT("no version refused"), GOMGStore::FromJson(TEXT("{\"worlds\":{}}"), Version));

	// a test never writes the player's file
	FString Why;
	TestFalse(TEXT("save blocked in a test"), GOMGStore::Save(&Why));
	return true;
}

#endif

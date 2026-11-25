// GamesOMG Teleport for Satisfactory: the player's own file, Saved/GamesOMGTeleport/places.json. 1.1.0 adds your own places,
// kept the way the Planet Crafter mod keeps them (PlaceStore.cs): per world, a "saved" list (name, where you stood, a
// favourite star, a note), "recent" (the last few teleports), the map places' notes by pin id, and the Return spot with
// the name of what stood there. The file is version 2; a 1.0.0 file (version 1) reads as it is and is copied to
// places.v1.json before the first write over it. A file from a newer version is read and never written over.
// 1.1.1: every write also leaves places.v2.json, a copy 1.0.0 never touches. 1.0.0 reads a version-2 file but writes back
// only its keys, ticks and Return, so going back to it dropped your own places; when places.json comes back at version 1
// and places.v2.json is there, what 1.0.0 couldn't keep is taken from it (GOMGStore::TakeBack).
#pragma once

#include "CoreMinimal.h"
#include "InputCoreTypes.h"

struct FGOMGSpot
{
	FString Name;
	FVector Feet = FVector::ZeroVector;   // world cm, where you stood (the bottom of the capsule)
	float Yaw = 0;
	FString Pin;                          // the map place it was saved from ("Save to My places"), else empty
	bool bFav = false;
	FString Note;
};

struct FGOMGWorld
{
	TSet<FString> Found;                  // map place ids you ticked by hand
	bool bHasBack = false;
	FVector Back = FVector::ZeroVector;   // feet
	float BackYaw = 0;
	FString BackName;                     // what stood there ("Iron Ore, Grass Fields"), "" when nothing did
	TArray<FGOMGSpot> Saved;
	TArray<FGOMGSpot> Recent;             // newest first
	TMap<FString, FString> Notes;         // a map place's note, by its id
};

struct FGOMGSettings
{
	FKey PanelKey = EKeys::F9;
	FKey BackKey = EKeys::F10;
	bool bCloseAfterTeleport = true;
	bool bClickTeleports = true;          // "Clicking a place": teleports at once (the model's default), or selects it
	int32 TextSize = 100;                 // percent, 70-200
	bool bLeft = false;                   // panel side
	int32 Opacity = 96;                   // background, percent, 85-100
	bool bHideFound = false;
	bool bByName = false;                 // Sort: A-Z, else nearest
	TSet<FString> Flipped;                // tree headings folded the other way from their default
	TSet<FString> On;                     // layers ticked on (they feed Nearest), by layer key
	TArray<FString> Collapsed10;          // 1.0.0's own fold state, written back as it came so a return to 1.0.0 finds it
};

namespace GOMGStore
{
	constexpr int32 FileVersion = 2, SavedMax = 500, RecentMax = 5, NameMax = 60, NoteMax = 80;
	constexpr int32 TextMin = 70, TextMax = 200, OpacityMin = 85;

	FGOMGSettings& Settings();
	FGOMGWorld& World(const FString& Key);

	void Load();                          // once; later calls do nothing
	bool IsLoaded();
	/** Writes the file. False (and Why says what to tell the player) when it couldn't, or mustn't */
	bool Save(FString* Why = nullptr);
	FString Path();
	FString MirrorPath();                 // places.v2.json, beside it

	// ── the rules, no file or world needed (GOMGStoreTest.cpp runs them) ──
	FString Clean(const FString& In, int32 Max);                 // one line, spaces squeezed, cut at Max letters
	/** Adds a place; its name made unique in this world ("Base 2"). Null when the list is full */
	FGOMGSpot* AddSaved(FGOMGWorld& W, const FString& Name, const FVector& Feet, float Yaw, const FString& DefaultName);
	/** The new name as kept, "" when it had no letter in it (nothing changes then) */
	FString Rename(FGOMGWorld& W, int32 Index, const FString& Name);
	/** The last few teleports, newest first; one within 3 m of another, or with the same name, replaces it */
	void AddRecent(FGOMGWorld& W, const FString& Name, const FVector& Feet, float Yaw);
	FString SetNote(FGOMGWorld& W, const FString& Pin, const FString& Note);   // "" deletes it; returns what is kept
	bool SameName(const FString& A, const FString& B);

	/** 1.0.0's "collapsed" fold keys as 1.1's "flipped" ones: only the headings you turned, each to the same open or closed */
	void FlipsFrom10(const TArray<FString>& Collapsed, TSet<FString>& Flipped);

	/** A key Settings › Controls refuses because it types in the panel's boxes. Typed = the character the key makes (0 for none);
	 *  Enter, Tab and Esc make control characters, which type nothing */
	bool TypesInBoxes(const FKey& K, uint32 Typed);
	/** Keys the panel answers itself while it's open (Enter, the arrows, Page Up/Down): never a binding */
	bool PanelOwnKey(const FKey& K);
	/** Keys to suggest for a binding: the first Count of F6, Insert, F7, F8 that neither action is on now */
	TArray<FKey> FreeKeys(int32 Count = 2);

	FString ToJson();
	/** Replaces everything in memory with the file's text. Version comes back as the file said it (0 = not ours) */
	bool FromJson(const FString& Text, int32& Version);
	/** After a version-1 places.json has been read: put back from places.v2.json's text what 1.0.0 can't keep (your places,
	 *  recent trips, notes, the other settings, Return's name). 1.0.0's keys, close-after and fold state stand, a tick from
	 *  either file stays, and 1.0.0's Return wins when it moved. False (memory untouched) when the text isn't a version-2 file */
	bool TakeBack(const FString& MirrorText);
	void ResetForTest();
}

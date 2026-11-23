// GamesOMG Teleport for Satisfactory: the player's own file, Saved/GamesOMGTeleport/places.json. 1.1.0 adds your own places,
// kept the way the Planet Crafter mod keeps them (PlaceStore.cs): per world, a "saved" list (name, where you stood, a
// favourite star, a note), "recent" (the last few teleports), the map places' notes by pin id, and the Return spot with
// the name of what stood there. The file is version 2; a 1.0.0 file (version 1) reads as it is and is copied to
// places.v1.json before the first write over it. A file from a newer version is read and never written over.
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

	FString ToJson();
	/** Replaces everything in memory with the file's text. Version comes back as the file said it (0 = not ours) */
	bool FromJson(const FString& Text, int32& Version);
	void ResetForTest();
}

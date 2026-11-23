// GamesOMG Teleport for Satisfactory: the place list (built from the GamesOMG map data by build-places.mjs) and the
// game's own names for it, read at runtime so they come out in the player's language.
#pragma once

#include "CoreMinimal.h"

enum class EGOMGTick : uint8
{
	Manual,     // the player ticks it (nodes, wells, starting areas)
	Pickup,     // the game's destroyed-pickup set, by mItemPickupGuid (slugs, Somersloops, Mercer Spheres)
	DropPod,    // the game's looted-drop-pod set, by mDropPodGuid (hard drives)
	Schematic,  // the schematic the pickup unlocks is bought (boombox tapes, the cosmetic helmet)
};

struct FGOMGCatDef
{
	const TCHAR* Key;
	int32 Group;
	const TCHAR* NameClass;   // descriptor class whose name is the heading; empty = our own words
	EGOMGTick Tick;
};

struct FGOMGRegionDef
{
	const TCHAR* English;
	const TCHAR* AreaClass;   // a map-area class; its display name is the game's region name
};

struct FGOMGPlaceDef
{
	const char* Id;           // the map's pin id
	int16 Cat;
	int16 Region;             // -1: none
	int32 X, Y, Z;            // world cm
	uint32 Guid[4];
	uint8 LandKind;           // 0 search live, 1 checked landing, 2 no safe landing
	int32 LX, LY, LZ;
	int32 Aux0, Aux1, Aux2;
};

namespace GOMGPlaces
{
	int32 Num();
	const FGOMGPlaceDef& Get(int32 Index);
	int32 NumCats();
	const FGOMGCatDef& Cat(int32 CatIndex);
	int32 NumGroups();
	uint32 ListHash();

	FString IdOf(int32 Index);
	int32 FindById(const FString& Id);
	FGuid GuidOf(int32 Index);
	FVector LocationOf(int32 Index);
	bool HasCheckedLanding(int32 Index);
	bool HasNoLanding(int32 Index);
	FVector LandingOf(int32 Index);
	EGOMGTick TickOf(int32 Index);

	// names (game thread)
	/** One of the game's own strings (string-table namespace + key) in the player's language; English players get ours, and
	 *  so do the languages in WrongIn (two-letter codes, comma-separated) where the game's own word means something else. */
	FText GameText(const TCHAR* Namespace, const TCHAR* Key, const TCHAR* English, const TCHAR* WrongIn = nullptr);
	FText GroupName(int32 Group);
	FText CatName(int32 CatIndex);
	FText RegionName(int32 Region);
	FText PlaceDetail(int32 Index);     // the muted second line: unlock cost, purity, well nodes; may be empty
	FText PlaceSearchText(int32 Index); // what search matches against, lower-cased
	UClass* PathClass(int32 PathIndex);
	void WarmNames();                   // resolve every class once (called when the panel first opens)
}

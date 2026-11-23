// GamesOMG Teleport for Satisfactory: the player's side. Keys, the panel, ticks, Return, your own places, and the requests
// to the server.
#pragma once

#include "CoreMinimal.h"
#include "InputCoreTypes.h"
#include "GOMGStore.h"
#include "GOMGTeleportRCO.h"

class AFGPlayerController;

namespace GOMGClient
{
	void Startup();
	void Shutdown();
	void Tick(float DeltaSeconds);

	// from the server
	void OnTeleportState(UGOMGTeleportRCO* Rco, int32 RequestId, EGOMGResult Result, const FVector& Where);
	void OnFound(uint32 ListHash, const TArray<uint8>& Bits);

	// from the panel
	void TogglePanel();
	void ClosePanel();
	bool IsPanelOpen();
	void GoToPlace(int32 Index);
	void GoToSpot(const FGOMGSpot& Spot);
	void GoBack();
	bool HasBack();
	bool IsBusy();
	bool SubmitLine(const FString& Line);   // a "/tp x y z" line (in-game map metres) or a place id; false if it's neither
	bool IsLine(const FString& Text);
	void FromClipboard();
	void ToggleManualFound(int32 Index);
	bool IsFound(int32 Index);
	bool IsGameFound(int32 Index);
	bool HasGameState();                    // the found bits from this save have arrived
	FKey PanelKey();
	FKey BackKey();
	bool PlayerLocation(FVector& Out);      // the character's centre
	bool PlayerFeet(FVector& Out, float& Yaw);
	void Toast(const FText& Message, bool bBad = false);
	FText LastStatus();
	bool IsServerMissingMod();

	// 1.1.0: your own places, the model's way
	FGOMGWorld& World();                    // this world's part of places.json
	FString SessionLabel();                 // the header's second line: this session's name (a guest: "· co-op")
	int32 SaveHere();                       // where you stand, into My places; its index, or INDEX_NONE
	bool Persist();                         // writes places.json; says on screen why when it can't
	FText PlaceLabel(int32 Index);          // "Iron Ore, Grass Fields"
	FString NameNear(const FVector& Feet, double RadiusCm);   // one of your places, else a map place, within the radius; "" if none
	void OpenUrl(const FString& Url);
	FString MapUrl();
	/** The next key pressed goes to Done (Esc: EKeys::Invalid), instead of the game or the panel */
	void CaptureKey(TFunction<void(FKey)> Done);
	bool IsCapturing();
	void CancelCapture();
}

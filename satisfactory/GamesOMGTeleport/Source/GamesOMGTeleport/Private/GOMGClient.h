// GamesOMG Teleport for Satisfactory: the player's side. Keys, the panel, ticks, Back, and the requests to the server.
#pragma once

#include "CoreMinimal.h"
#include "InputCoreTypes.h"
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
	void GoToPlace(int32 Index);
	void GoBack();
	bool HasBack();
	bool SubmitLine(const FString& Line);   // a "/tp x y z" line (in-game map metres) or a place id; false if it's neither
	bool IsLine(const FString& Text);
	void ToggleManualFound(int32 Index);
	bool IsFound(int32 Index);
	bool IsGameFound(int32 Index);
	bool HasGameState();                    // the found bits from this save have arrived
	bool IsCollapsed(const FString& Key, bool bDefault);
	void SetCollapsed(const FString& Key, bool bCollapsed);
	FKey PanelKey();
	FKey BackKey();
	bool PlayerLocation(FVector& Out);
	void Toast(const FText& Message, bool bBad = false);
	FText LastStatus();
	bool IsServerMissingMod();
}

// GamesOMG Teleport for Satisfactory: the server half (runs on whoever has authority: your own game solo or as host, or the
// dedicated server). Moves only the player who asked; checks the landing in the world as the server sees it.
#pragma once

#include "CoreMinimal.h"
#include "GOMGTeleportRCO.h"

class AFGPlayerController;
class ACharacter;

namespace GOMGServer
{
	void StartTeleport(UGOMGTeleportRCO* Rco, FVector Target, FVector Hint, uint8 Flags, int32 RequestId);
	void ClientStreamingDone(UGOMGTeleportRCO* Rco, int32 RequestId);
	void ComputeFound(UWorld* World, TArray<uint8>& OutBits);

	/** The landing search: a spot to stand on beside Target, at Target's height. Never from the sky unless FromSky. */
	bool FindLanding(UWorld* World, ACharacter* Character, const FVector& Target, const FVector* Hint, uint8 Flags, FVector& OutFeet,
		FString* OutWhy = nullptr);

	/** Called by the module's ticker */
	void Tick(float DeltaSeconds);
	void Shutdown();

	/** The world's edges (the map's border box, cm): a target outside is refused */
	bool IsInsideWorld(const FVector& V);

	/** The game's own "has the world around this player streamed in" (AFGPlayerController::IsLevelStreamingComplete, the gate
	 *  respawn and portals use). It is protected in C++ but a Blueprint function, so it is called through reflection. */
	bool LevelStreamingDone(AFGPlayerController* PC);
}

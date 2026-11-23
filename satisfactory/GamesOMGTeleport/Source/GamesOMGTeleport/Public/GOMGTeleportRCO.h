// GamesOMG Teleport for Satisfactory: the one network object. Every teleport runs on the server (solo and host included: there
// the "server" is your own game), so the same code moves you on a dedicated server. The game creates one per player controller.
#pragma once

#include "CoreMinimal.h"
#include "FGRemoteCallObject.h"
#include "GOMGTeleportRCO.generated.h"

UENUM()
enum class EGOMGResult : uint8
{
	Ok,
	NoLanding,      // nothing there to stand on, or no room: you were put back
	Busy,           // a teleport is already running
	NotNow,         // in a vehicle, dead, ragdolled, or no character yet
	BadTarget,      // outside the world
	Disabled,       // the server turned teleporting off for guests
	Holding,        // not a result: the server is holding you at the destination while it streams in
};

// Request flags
namespace GOMGFlags
{
	static constexpr uint8 HasHint = 1;     // Hint is a landing checked ahead of time: try it first
	static constexpr uint8 ExactFirst = 2;  // the target itself is a landing spot (a pasted line, Back): try it before the ring
	static constexpr uint8 FromSky = 4;     // the line had "~" for the height: take the top surface
	static constexpr uint8 IsBack = 8;
}

UCLASS(NotBlueprintable)
class GAMESOMGTELEPORT_API UGOMGTeleportRCO final : public UFGRemoteCallObject
{
	GENERATED_BODY()

public:
	UFUNCTION(Server, Reliable, WithValidation)
	void Server_Teleport(FVector Target, FVector Hint, uint8 Flags, int32 RequestId);

	/** The client has streamed in the destination (its own copy of the world) */
	UFUNCTION(Server, Reliable)
	void Server_ClientStreamingDone(int32 RequestId);

	/** Ask what this save has already collected, as one bit per place in the shared list */
	UFUNCTION(Server, Reliable)
	void Server_RequestFound(uint32 ListHash);

	UFUNCTION(Client, Reliable)
	void Client_TeleportState(int32 RequestId, EGOMGResult Result, FVector Where);

	UFUNCTION(Client, Reliable)
	void Client_Found(uint32 ListHash, const TArray<uint8>& Bits);

	virtual void GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const override;

private:
	// an RCO needs one replicated property for its channel to open reliably
	UPROPERTY(Replicated)
	int32 Dummy = 0;

	double LastRequestTime = -1000.0;
	double LastFoundTime = -1000.0;
};

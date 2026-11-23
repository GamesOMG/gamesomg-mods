#include "GOMGTeleportRCO.h"

#include "GOMGClient.h"
#include "GOMGPlaces.h"
#include "GOMGServer.h"
#include "GamesOMGTeleportModule.h"
#include "Net/UnrealNetwork.h"

void UGOMGTeleportRCO::GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const
{
	Super::GetLifetimeReplicatedProps(OutLifetimeProps);
	DOREPLIFETIME(UGOMGTeleportRCO, Dummy);
}

bool UGOMGTeleportRCO::Server_Teleport_Validate(FVector Target, FVector Hint, uint8 Flags, int32 RequestId)
{
	return !Target.ContainsNaN() && !Hint.ContainsNaN();
}

void UGOMGTeleportRCO::Server_Teleport_Implementation(FVector Target, FVector Hint, uint8 Flags, int32 RequestId)
{
	const double T = FPlatformTime::Seconds();
	if (T - LastRequestTime < 0.4)   // a held key or a script: one request at a time
	{
		Client_TeleportState(RequestId, EGOMGResult::Busy, FVector::ZeroVector);
		return;
	}
	LastRequestTime = T;
	if (!FGamesOMGTeleportModule::AllowGuests() && GetOwnerPlayerController() && !GetOwnerPlayerController()->IsLocalController())
	{
		Client_TeleportState(RequestId, EGOMGResult::Disabled, FVector::ZeroVector);
		return;
	}
	GOMGServer::StartTeleport(this, Target, Hint, Flags, RequestId);
}

void UGOMGTeleportRCO::Server_ClientStreamingDone_Implementation(int32 RequestId)
{
	GOMGServer::ClientStreamingDone(this, RequestId);
}

void UGOMGTeleportRCO::Server_RequestFound_Implementation(uint32 ListHash)
{
	const double T = FPlatformTime::Seconds();
	if (T - LastFoundTime < 1.0) return;
	LastFoundTime = T;
	TArray<uint8> Bits;
	if (ListHash == GOMGPlaces::ListHash()) GOMGServer::ComputeFound(GetWorld(), Bits);
	Client_Found(GOMGPlaces::ListHash(), Bits);
}

void UGOMGTeleportRCO::Client_TeleportState_Implementation(int32 RequestId, EGOMGResult Result, FVector Where)
{
	GOMGClient::OnTeleportState(this, RequestId, Result, Where);
}

void UGOMGTeleportRCO::Client_Found_Implementation(uint32 ListHash, const TArray<uint8>& Bits)
{
	GOMGClient::OnFound(ListHash, Bits);
}

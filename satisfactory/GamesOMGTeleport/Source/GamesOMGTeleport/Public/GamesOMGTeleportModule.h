#pragma once

#include "CoreMinimal.h"
#include "Modules/ModuleManager.h"
#include "Containers/Ticker.h"

DECLARE_LOG_CATEGORY_EXTERN(LogGamesOMGTeleport, Log, All);

class FGamesOMGTeleportModule : public IModuleInterface
{
public:
	virtual void StartupModule() override;
	virtual void ShutdownModule() override;

	/** Server setting: may players other than the host teleport? (Saved/GamesOMGTeleport/server.json, "allowGuests") */
	static bool AllowGuests();
	static FString VersionString();

private:
	bool Tick(float DeltaSeconds);
	FTSTicker::FDelegateHandle TickHandle;
};

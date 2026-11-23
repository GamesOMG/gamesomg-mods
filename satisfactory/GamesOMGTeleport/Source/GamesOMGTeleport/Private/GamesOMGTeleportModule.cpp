#include "GamesOMGTeleportModule.h"

#include "Dom/JsonObject.h"
#include "Engine/Engine.h"
#include "Engine/World.h"
#include "FGGameMode.h"
#include "GOMGClient.h"
#include "GOMGServer.h"
#include "GOMGTeleportRCO.h"
#include "Interfaces/IPluginManager.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"

DEFINE_LOG_CATEGORY(LogGamesOMGTeleport);

namespace
{
	bool GAllowGuests = true;
	TSet<TWeakObjectPtr<AFGGameMode>> GRegisteredModes;

	void ReadServerSettings()
	{
		const FString Path = FPaths::ProjectSavedDir() / TEXT("GamesOMGTeleport") / TEXT("server.json");
		FString Text;
		if (!FFileHelper::LoadFileToString(Text, *Path))
		{
			// write the default once so an admin can find it
			FFileHelper::SaveStringToFile(TEXT("{\n  \"allowGuests\": true\n}\n"), *Path);
			return;
		}
		TSharedPtr<FJsonObject> Obj;
		if (FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Text), Obj) && Obj.IsValid())
			Obj->TryGetBoolField(TEXT("allowGuests"), GAllowGuests);
		UE_LOG(LogGamesOMGTeleport, Log, TEXT("server.json: allowGuests=%d"), GAllowGuests ? 1 : 0);
	}
}

void FGamesOMGTeleportModule::StartupModule()
{
	UE_LOG(LogGamesOMGTeleport, Log, TEXT("GamesOMG Teleport %s loaded%s"), *VersionString(), IsRunningDedicatedServer() ? TEXT(" (dedicated server)") : TEXT(""));
	ReadServerSettings();
	TickHandle = FTSTicker::GetCoreTicker().AddTicker(FTickerDelegate::CreateRaw(this, &FGamesOMGTeleportModule::Tick), 0.1f);
	if (!IsRunningDedicatedServer()) GOMGClient::Startup();
}

void FGamesOMGTeleportModule::ShutdownModule()
{
	FTSTicker::GetCoreTicker().RemoveTicker(TickHandle);
	GOMGServer::Shutdown();
	if (!IsRunningDedicatedServer()) GOMGClient::Shutdown();
}

bool FGamesOMGTeleportModule::AllowGuests() { return GAllowGuests; }

FString FGamesOMGTeleportModule::VersionString()
{
	if (TSharedPtr<IPlugin> P = IPluginManager::Get().FindPlugin(TEXT("GamesOMGTeleport"))) return P->GetDescriptor().VersionName;
	return TEXT("?");
}

bool FGamesOMGTeleportModule::Tick(float DeltaSeconds)
{
	// the game normally finds every Remote Call Object class by itself; register ours too, once per game mode, in case it doesn't
	if (GEngine)
	{
		for (const FWorldContext& Ctx : GEngine->GetWorldContexts())
		{
			UWorld* W = Ctx.World();
			if (!W || (Ctx.WorldType != EWorldType::Game && Ctx.WorldType != EWorldType::PIE)) continue;
			AFGGameMode* GM = Cast<AFGGameMode>(W->GetAuthGameMode());
			if (GM && !GRegisteredModes.Contains(GM))
			{
				GRegisteredModes.Add(GM);
				const bool bNew = GM->RegisterRemoteCallObjectClass(UGOMGTeleportRCO::StaticClass());
				GM->RegisterCallObjectOnAllCurrentPlayers(UGOMGTeleportRCO::StaticClass());
				UE_LOG(LogGamesOMGTeleport, Log, TEXT("remote call object registered on %s (%s)"), *W->GetMapName(), bNew ? TEXT("new") : TEXT("already there"));
			}
		}
		for (auto It = GRegisteredModes.CreateIterator(); It; ++It)
			if (!It->IsValid()) It.RemoveCurrent();
	}
	GOMGServer::Tick(DeltaSeconds);
	if (!IsRunningDedicatedServer()) GOMGClient::Tick(DeltaSeconds);
	return true;
}

IMPLEMENT_GAME_MODULE(FGamesOMGTeleportModule, GamesOMGTeleport);

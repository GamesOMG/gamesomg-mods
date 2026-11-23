#include "GOMGClient.h"

#include "GOMGPlaces.h"
#include "GOMGServer.h"
#include "GamesOMGTeleportModule.h"
#include "SGOMGPanel.h"

#include "Components/CapsuleComponent.h"
#include "Dom/JsonObject.h"
#include "Engine/Engine.h"
#include "Engine/GameViewportClient.h"
#include "Engine/World.h"
#include "FGCharacterPlayer.h"
#include "FGDriveablePawn.h"
#include "FGGameState.h"
#include "FGPlayerController.h"
#include "Framework/Application/IInputProcessor.h"
#include "Framework/Application/SlateApplication.h"
#include "GameFramework/CharacterMovementComponent.h"
#include "HAL/FileManager.h"
#include "HAL/PlatformApplicationMisc.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"
#include "Serialization/JsonWriter.h"

namespace
{
	// ── the store: Saved/GamesOMGTeleport/places.json ─────────────────────────────────────────────────────────────────────────
	struct FWorldStore
	{
		TSet<FString> Found;          // place ids the player ticked
		bool bHasBack = false;
		FVector Back = FVector::ZeroVector;   // feet
		float BackYaw = 0;
	};
	struct FStore
	{
		FKey PanelKey = EKeys::F9;
		FKey BackKey = EKeys::F10;
		bool bCloseAfterTeleport = true;
		TSet<FString> Collapsed;      // heading keys the player closed (or opened, for ones closed by default: "!" prefix)
		TMap<FString, FWorldStore> Worlds;
	};
	FStore GStore;
	bool GStoreLoaded = false;

	FString StorePath() { return FPaths::ProjectSavedDir() / TEXT("GamesOMGTeleport") / TEXT("places.json"); }

	void SaveStore();

	void LoadStore()
	{
		GStoreLoaded = true;
		FString Text;
		if (!FFileHelper::LoadFileToString(Text, *StorePath()))
		{
			SaveStore();   // the defaults, written once, so the key settings are there to find
			return;
		}
		TSharedPtr<FJsonObject> Root;
		if (!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Text), Root) || !Root.IsValid())
		{
			// never throw a player's file away: keep it beside the new one
			FFileHelper::SaveStringToFile(Text, *(StorePath() + TEXT(".bad")));
			UE_LOG(LogGamesOMGTeleport, Warning, TEXT("places.json would not read; kept as places.json.bad"));
			return;
		}
		const TSharedPtr<FJsonObject>* Settings;
		if (Root->TryGetObjectField(TEXT("settings"), Settings))
		{
			FString K;
			if ((*Settings)->TryGetStringField(TEXT("panelKey"), K) && FKey(*K).IsValid()) GStore.PanelKey = FKey(*K);
			if ((*Settings)->TryGetStringField(TEXT("backKey"), K) && FKey(*K).IsValid()) GStore.BackKey = FKey(*K);
			(*Settings)->TryGetBoolField(TEXT("closeAfterTeleport"), GStore.bCloseAfterTeleport);
			const TArray<TSharedPtr<FJsonValue>>* Arr;
			if ((*Settings)->TryGetArrayField(TEXT("collapsed"), Arr))
				for (const TSharedPtr<FJsonValue>& V : *Arr) GStore.Collapsed.Add(V->AsString());
		}
		const TSharedPtr<FJsonObject>* Worlds;
		if (Root->TryGetObjectField(TEXT("worlds"), Worlds))
		{
			for (const auto& Pair : (*Worlds)->Values)
			{
				const TSharedPtr<FJsonObject> W = Pair.Value->AsObject();
				if (!W.IsValid()) continue;
				FWorldStore& S = GStore.Worlds.Add(Pair.Key);
				const TArray<TSharedPtr<FJsonValue>>* Arr;
				if (W->TryGetArrayField(TEXT("found"), Arr))
					for (const TSharedPtr<FJsonValue>& V : *Arr) S.Found.Add(V->AsString());
				if (W->TryGetArrayField(TEXT("back"), Arr) && Arr->Num() == 4)
				{
					S.bHasBack = true;
					S.Back = FVector((*Arr)[0]->AsNumber(), (*Arr)[1]->AsNumber(), (*Arr)[2]->AsNumber());
					S.BackYaw = (*Arr)[3]->AsNumber();
				}
			}
		}
	}

	void SaveStore()
	{
		TSharedRef<FJsonObject> Root = MakeShared<FJsonObject>();
		Root->SetNumberField(TEXT("version"), 1);
		TSharedRef<FJsonObject> Settings = MakeShared<FJsonObject>();
		Settings->SetStringField(TEXT("panelKey"), GStore.PanelKey.ToString());
		Settings->SetStringField(TEXT("backKey"), GStore.BackKey.ToString());
		Settings->SetBoolField(TEXT("closeAfterTeleport"), GStore.bCloseAfterTeleport);
		TArray<TSharedPtr<FJsonValue>> Col;
		for (const FString& K : GStore.Collapsed) Col.Add(MakeShared<FJsonValueString>(K));
		Settings->SetArrayField(TEXT("collapsed"), Col);
		Root->SetObjectField(TEXT("settings"), Settings);
		TSharedRef<FJsonObject> Worlds = MakeShared<FJsonObject>();
		for (const auto& Pair : GStore.Worlds)
		{
			TSharedRef<FJsonObject> W = MakeShared<FJsonObject>();
			TArray<TSharedPtr<FJsonValue>> F;
			TArray<FString> Ids = Pair.Value.Found.Array();
			Ids.Sort();
			for (const FString& Id : Ids) F.Add(MakeShared<FJsonValueString>(Id));
			W->SetArrayField(TEXT("found"), F);
			if (Pair.Value.bHasBack)
			{
				TArray<TSharedPtr<FJsonValue>> B;
				B.Add(MakeShared<FJsonValueNumber>(FMath::RoundToDouble(Pair.Value.Back.X)));
				B.Add(MakeShared<FJsonValueNumber>(FMath::RoundToDouble(Pair.Value.Back.Y)));
				B.Add(MakeShared<FJsonValueNumber>(FMath::RoundToDouble(Pair.Value.Back.Z)));
				B.Add(MakeShared<FJsonValueNumber>(FMath::RoundToDouble(Pair.Value.BackYaw)));
				W->SetArrayField(TEXT("back"), B);
			}
			Worlds->SetObjectField(Pair.Key, W);
		}
		Root->SetObjectField(TEXT("worlds"), Worlds);
		FString Out;
		const TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&Out);
		FJsonSerializer::Serialize(Root, Writer);
		const FString Tmp = StorePath() + TEXT(".tmp");
		if (!FFileHelper::SaveStringToFile(Out, *Tmp) || !IFileManager::Get().Move(*StorePath(), *Tmp, true, true))
		{
			UE_LOG(LogGamesOMGTeleport, Warning, TEXT("could not save %s"), *StorePath());
			GOMGClient::Toast(FText::FromString(TEXT("Couldn't save your ticks (is the Saved folder read-only?)")), true);
		}
	}

	// ── state ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────
	TSharedPtr<IInputProcessor> GInput;
	TSharedPtr<SGOMGRoot> GRoot;
	TWeakObjectPtr<UWorld> GRootWorld;
	bool GPanelOpen = false;
	TBitArray<> GGameFound;
	bool GHasGameFound = false;
	double GLastFoundRequest = -1000;
	FText GStatus;
	bool GServerMissing = false;

	struct FPending
	{
		int32 Id = 0;
		FText Label;
		FVector OriginFeet = FVector::ZeroVector;
		float OriginYaw = 0;
		bool bIsBack = false;
		bool bHolding = false;
		bool bStreamDoneSent = false;
		bool bIgnoringInput = false;
		double Sent = 0;
	};
	FPending GPending;
	int32 GNextId = 1;

	UWorld* GameWorld()
	{
		if (!GEngine) return nullptr;
		for (const FWorldContext& Ctx : GEngine->GetWorldContexts())
			if ((Ctx.WorldType == EWorldType::Game || Ctx.WorldType == EWorldType::PIE) && Ctx.World() && Ctx.World()->GetGameState<AFGGameState>())
				return Ctx.World();
		return nullptr;
	}

	AFGPlayerController* LocalPC()
	{
		UWorld* W = GameWorld();
		AFGPlayerController* PC = W ? Cast<AFGPlayerController>(W->GetFirstPlayerController()) : nullptr;
		return (PC && PC->IsLocalController()) ? PC : nullptr;
	}

	AFGCharacterPlayer* LocalCharacter()
	{
		AFGPlayerController* PC = LocalPC();
		APawn* Pawn = PC ? PC->GetPawn() : nullptr;
		if (AFGCharacterPlayer* Ch = Cast<AFGCharacterPlayer>(Pawn)) return Ch;
		// in a vehicle the controller's pawn is the vehicle; we are its driver (1.0.0 said "Load into a world first." and listed
		// places with no distances there: 2026-11-23 sitting, in a tractor)
		if (const AFGDriveablePawn* Vehicle = Cast<AFGDriveablePawn>(Pawn)) return Vehicle->GetDriver();
		return nullptr;
	}

	UGOMGTeleportRCO* LocalRco()
	{
		AFGPlayerController* PC = LocalPC();
		return PC ? PC->GetRemoteCallObjectOfClass<UGOMGTeleportRCO>() : nullptr;
	}

	FString WorldKey()
	{
		UWorld* W = GameWorld();
		AFGGameState* GS = W ? W->GetGameState<AFGGameState>() : nullptr;
		FString Name = GS ? GS->GetSessionName() : FString();
		if (Name.IsEmpty()) Name = TEXT("(unnamed session)");
		if (W && W->GetNetMode() == NM_Client) Name += TEXT(" @ ") + W->URL.Host;   // a guest: per host
		return Name;
	}

	FWorldStore& CurrentWorld()
	{
		if (!GStoreLoaded) LoadStore();
		return GStore.Worlds.FindOrAdd(WorldKey());
	}

	FVector FeetOf(const ACharacter* Ch)
	{
		return Ch->GetActorLocation() - FVector(0, 0, Ch->GetCapsuleComponent()->GetScaledCapsuleHalfHeight());
	}

	void SetPanelInput(bool bOpen)
	{
		AFGPlayerController* PC = LocalPC();
		if (!PC) return;
		if (bOpen)
		{
			FInputModeUIOnly Mode;
			if (GRoot.IsValid()) Mode.SetWidgetToFocus(GRoot->GetFocusTarget());
			Mode.SetLockMouseToViewportBehavior(EMouseLockMode::DoNotLock);
			PC->SetInputMode(Mode);
			PC->SetShowMouseCursor(true);
		}
		else
		{
			PC->SetInputMode(FInputModeGameOnly());
			PC->SetShowMouseCursor(false);
		}
	}

	void EnsureRoot()
	{
		UWorld* W = GameWorld();
		if (!W || !GEngine || !GEngine->GameViewport) return;
		if (GRoot.IsValid() && GRootWorld.Get() == W) return;
		if (GRoot.IsValid()) GEngine->GameViewport->RemoveViewportWidgetContent(GRoot.ToSharedRef());
		GRoot = SNew(SGOMGRoot);
		GEngine->GameViewport->AddViewportWidgetContent(GRoot.ToSharedRef(), 50);
		GRootWorld = W;
		GPanelOpen = false;
		GHasGameFound = false;
		GGameFound.Reset();
		GServerMissing = false;
	}

	void RequestFound(bool bForce)
	{
		const double T = FPlatformTime::Seconds();
		if (!bForce && T - GLastFoundRequest < 8.0) return;
		GLastFoundRequest = T;
		if (UGOMGTeleportRCO* Rco = LocalRco()) Rco->Server_RequestFound(GOMGPlaces::ListHash());
	}

	void ReleaseInputHold()
	{
		if (GPending.bIgnoringInput)
		{
			if (AFGPlayerController* PC = LocalPC()) PC->SetIgnoreMoveInput(false);
			GPending.bIgnoringInput = false;
		}
	}

	void Send(const FVector& Target, const FVector* Hint, uint8 Flags, const FText& Label)
	{
		AFGCharacterPlayer* Ch = LocalCharacter();
		UGOMGTeleportRCO* Rco = LocalRco();
		if (!Ch) { GOMGClient::Toast(FText::FromString(TEXT("Load into a world first.")), true); return; }
		if (!Rco)
		{
			GServerMissing = true;
			GOMGClient::Toast(FText::FromString(TEXT("This server doesn't run GamesOMG Teleport, so it can't move you. The host or server needs it too.")), true);
			return;
		}
		if (GPending.Id && FPlatformTime::Seconds() - GPending.Sent < 30.0)
		{
			GOMGClient::Toast(FText::FromString(TEXT("Still moving you: one at a time.")), true);
			return;
		}
		if (Ch->IsDrivingVehicle())
		{
			GOMGClient::Toast(FText::FromString(TEXT("Get out of the vehicle first.")), true);
			return;
		}
		GPending = FPending();
		GPending.Id = GNextId++;
		GPending.Label = Label;
		GPending.OriginFeet = FeetOf(Ch);
		GPending.OriginYaw = Ch->GetActorRotation().Yaw;
		GPending.bIsBack = (Flags & GOMGFlags::IsBack) != 0;
		GPending.Sent = FPlatformTime::Seconds();
		Rco->Server_Teleport(Target, Hint ? *Hint : FVector::ZeroVector, Flags | (Hint ? GOMGFlags::HasHint : 0), GPending.Id);
		GOMGClient::Toast(FText::Format(FText::FromString(TEXT("Going to {0}…")), Label));
		if (GStore.bCloseAfterTeleport) GOMGClient::ClosePanel();
	}

	FText PlaceLabel(int32 Index)
	{
		const FGOMGPlaceDef& P = GOMGPlaces::Get(Index);
		const FText Region = GOMGPlaces::RegionName(P.Region);
		const FText Cat = GOMGPlaces::CatName(P.Cat);
		if (FCString::Strcmp(GOMGPlaces::Cat(P.Cat).Key, TEXT("tapes")) == 0) return GOMGPlaces::PlaceDetail(Index);
		return Region.IsEmpty() ? Cat : FText::Format(FText::FromString(TEXT("{0}, {1}")), Cat, Region);
	}

	// ── keys ──────────────────────────────────────────────────────────────────────────────────────────────────────────────────
	class FGOMGInputProcessor : public IInputProcessor
	{
	public:
		virtual void Tick(const float, FSlateApplication&, TSharedRef<ICursor>) override {}
		virtual const TCHAR* GetDebugName() const { return TEXT("GamesOMGTeleport"); }

		virtual bool HandleKeyDownEvent(FSlateApplication& App, const FKeyEvent& E) override
		{
			if (!LocalPC() || !GRoot.IsValid()) return false;
			const FKey K = E.GetKey();
			if (GPanelOpen)
			{
				if (K == EKeys::Escape || (K == GStore.PanelKey && !E.IsRepeat())) { GOMGClient::ClosePanel(); return true; }
				if (K == EKeys::Up) { GRoot->MoveSelection(-1); return true; }
				if (K == EKeys::Down) { GRoot->MoveSelection(+1); return true; }
				if (K == EKeys::PageUp) { GRoot->MoveSelection(-10); return true; }
				if (K == EKeys::PageDown) { GRoot->MoveSelection(+10); return true; }
				if (K == EKeys::Enter && !E.IsRepeat()) { GRoot->ActivateSelection(); return true; }
				if (K == GStore.BackKey && !E.IsRepeat()) { GOMGClient::GoBack(); return true; }
				return false;   // typing goes to the search box
			}
			if (E.IsRepeat()) return false;
			// someone typing in chat or a sign: F-keys aren't typed, but don't steal them from a text box anyway
			const TSharedPtr<SWidget> Focus = App.GetKeyboardFocusedWidget();
			if (Focus.IsValid() && (Focus->GetType() == FName(TEXT("SEditableText")) || Focus->GetType() == FName(TEXT("SMultiLineEditableText")))) return false;
			if (K == GStore.PanelKey) { GOMGClient::TogglePanel(); return true; }
			if (K == GStore.BackKey) { GOMGClient::GoBack(); return true; }
			return false;
		}
	};
}

namespace GOMGClient
{
	void Startup() {}

	void Shutdown()
	{
		if (GInput.IsValid() && FSlateApplication::IsInitialized()) FSlateApplication::Get().UnregisterInputPreProcessor(GInput);
		GInput.Reset();
		GRoot.Reset();
	}

	void Tick(float)
	{
		if (!GInput.IsValid() && FSlateApplication::IsInitialized())
		{
			GInput = MakeShared<FGOMGInputProcessor>();
			FSlateApplication::Get().RegisterInputPreProcessor(GInput);
			if (!GStoreLoaded) LoadStore();
			UE_LOG(LogGamesOMGTeleport, Log, TEXT("keys: %s opens Places, %s goes back"), *GStore.PanelKey.ToString(), *GStore.BackKey.ToString());
		}
		if (!LocalPC()) return;
		EnsureRoot();
		if (GPanelOpen) RequestFound(false);

		// waiting at the destination: tell the server when OUR copy of the world has streamed in too
		if (GPending.Id && GPending.bHolding && !GPending.bStreamDoneSent)
		{
			AFGPlayerController* PC = LocalPC();
			if (PC && GOMGServer::LevelStreamingDone(PC))
			{
				if (UGOMGTeleportRCO* Rco = LocalRco()) Rco->Server_ClientStreamingDone(GPending.Id);
				GPending.bStreamDoneSent = true;
			}
		}
		if (GPending.Id && FPlatformTime::Seconds() - GPending.Sent > 45.0)   // lost: never leave the player frozen
		{
			ReleaseInputHold();
			GPending = FPending();
			Toast(FText::FromString(TEXT("The server never answered. Nothing moved.")), true);
		}
	}

	void OnTeleportState(UGOMGTeleportRCO*, int32 RequestId, EGOMGResult Result, const FVector& Where)
	{
		if (RequestId != GPending.Id) return;
		AFGPlayerController* PC = LocalPC();
		AFGCharacterPlayer* Ch = LocalCharacter();
		if (Result == EGOMGResult::Holding)
		{
			GPending.bHolding = true;
			if (PC && !GPending.bIgnoringInput) { PC->SetIgnoreMoveInput(true); GPending.bIgnoringInput = true; }
			if (Ch && !Ch->HasAuthority())
			{
				Ch->GetCharacterMovement()->StopMovementImmediately();
				Ch->GetCharacterMovement()->SetMovementMode(MOVE_Flying);
			}
			return;
		}
		ReleaseInputHold();
		if (Ch && !Ch->HasAuthority()) Ch->GetCharacterMovement()->SetMovementMode(MOVE_Walking);
		const FPending Done = GPending;
		GPending = FPending();
		switch (Result)
		{
		case EGOMGResult::Ok:
		{
			FWorldStore& S = CurrentWorld();
			S.bHasBack = true; S.Back = Done.OriginFeet; S.BackYaw = Done.OriginYaw;   // Back swaps: press it again to return
			SaveStore();
			Toast(Done.bIsBack ? FText::FromString(TEXT("Back where you were. Press Back again to return."))
				: FText::Format(FText::FromString(TEXT("You're at {0}.")), Done.Label));
			if (GRoot.IsValid()) GRoot->Rebuild();
			break;
		}
		case EGOMGResult::NoLanding:
			Toast(FText::Format(FText::FromString(TEXT("No safe spot to stand near {0}. You're back where you were.")), Done.Label), true); break;
		case EGOMGResult::Busy: Toast(FText::FromString(TEXT("Still moving you: one at a time.")), true); break;
		case EGOMGResult::NotNow: Toast(FText::FromString(TEXT("Can't move you right now (in a vehicle, down, or respawning).")), true); break;
		case EGOMGResult::BadTarget: Toast(FText::FromString(TEXT("That spot is outside the world.")), true); break;
		case EGOMGResult::Disabled: Toast(FText::FromString(TEXT("This server has teleporting off for guests.")), true); break;
		default: break;
		}
		RequestFound(true);
	}

	void OnFound(uint32 ListHash, const TArray<uint8>& Bits)
	{
		if (ListHash != GOMGPlaces::ListHash() || Bits.Num() * 8 < GOMGPlaces::Num())
		{
			Toast(FText::FromString(TEXT("The server runs a different version of GamesOMG Teleport: ticks from the save are off until they match.")), true);
			return;
		}
		GGameFound.Init(false, GOMGPlaces::Num());
		for (int32 i = 0; i < GOMGPlaces::Num(); ++i) GGameFound[i] = (Bits[i / 8] >> (i % 8)) & 1;
		const bool bFirst = !GHasGameFound;
		GHasGameFound = true;
		if (GRoot.IsValid() && (bFirst || GPanelOpen)) GRoot->Rebuild();
	}

	void TogglePanel() { if (GPanelOpen) ClosePanel(); else
	{
		EnsureRoot();
		if (!GRoot.IsValid()) return;
		GOMGPlaces::WarmNames();
		GPanelOpen = true;
		GRoot->SetPanelOpen(true);
		// a /tp line on the clipboard (the map's Copy Teleport): put it in the box, ready for Enter
		FString Clip;
		FPlatformApplicationMisc::ClipboardPaste(Clip);
		Clip.TrimStartAndEndInline();
		if (IsLine(Clip)) GRoot->PrefillLine(Clip);
		SetPanelInput(true);
		RequestFound(true);
	} }

	void ClosePanel()
	{
		if (!GPanelOpen) return;
		GPanelOpen = false;
		if (GRoot.IsValid()) GRoot->SetPanelOpen(false);
		SetPanelInput(false);
	}

	void GoToPlace(int32 Index)
	{
		if (Index < 0 || Index >= GOMGPlaces::Num()) return;
		if (GOMGPlaces::HasNoLanding(Index))
		{
			Toast(FText::FromString(TEXT("No safe spot to stand there was found, so there's no teleport for it.")), true);
			return;
		}
		const FVector Target = GOMGPlaces::LocationOf(Index);
		const bool bHint = GOMGPlaces::HasCheckedLanding(Index);
		const FVector Hint = GOMGPlaces::LandingOf(Index);
		const FString Key = GOMGPlaces::Cat(GOMGPlaces::Get(Index).Cat).Key;
		// nodes, wells and starts are places you stand on; collectibles you stand beside
		const bool bStandOn = GOMGPlaces::Cat(GOMGPlaces::Get(Index).Cat).Group >= 3;
		Send(Target, bHint ? &Hint : nullptr, bStandOn ? GOMGFlags::ExactFirst : 0, PlaceLabel(Index));
	}

	void GoBack()
	{
		FWorldStore& S = CurrentWorld();
		if (!S.bHasBack) { Toast(FText::FromString(TEXT("Nowhere to go back to yet: teleport somewhere first.")), true); return; }
		Send(S.Back, nullptr, GOMGFlags::ExactFirst | GOMGFlags::IsBack, FText::FromString(TEXT("where you were")));
	}

	bool HasBack() { return CurrentWorld().bHasBack; }

	bool IsLine(const FString& TextIn)
	{
		FString T = TextIn.TrimStartAndEnd();
		if (T.StartsWith(TEXT("/tp ")) || T.StartsWith(TEXT("/tp\t"))) return true;
		return T.Len() == 8 && GOMGPlaces::FindById(T) != INDEX_NONE;
	}

	bool SubmitLine(const FString& TextIn)
	{
		FString T = TextIn.TrimStartAndEnd();
		const int32 ById = T.Len() == 8 ? GOMGPlaces::FindById(T) : INDEX_NONE;
		if (ById != INDEX_NONE) { GoToPlace(ById); return true; }
		if (!T.StartsWith(TEXT("/tp"))) return false;
		T = T.Mid(3).Replace(TEXT(","), TEXT(" ")).TrimStartAndEnd();
		TArray<FString> Parts;
		T.ParseIntoArrayWS(Parts);
		if (Parts.Num() != 3) { Toast(FText::FromString(TEXT("A teleport line is /tp X Y Z: the in-game map's X and Y, and a height (or ~).")), true); return true; }
		double V[3] = { 0, 0, 0 };
		bool bSky = false;
		for (int32 i = 0; i < 3; ++i)
		{
			if (i == 2 && Parts[i] == TEXT("~")) { bSky = true; continue; }
			if (!Parts[i].IsNumeric() && !(Parts[i].StartsWith(TEXT("-")) && Parts[i].Mid(1).IsNumeric()))
			{
				Toast(FText::FromString(TEXT("A teleport line is /tp X Y Z: numbers only (or ~ for the height).")), true);
				return true;
			}
			V[i] = FCString::Atod(*Parts[i]);
		}
		// the in-game map shows world / 100 (metres); so does our web map's Copy Teleport
		const FVector Target(V[0] * 100.0, V[1] * 100.0, V[2] * 100.0);
		Send(Target, nullptr, GOMGFlags::ExactFirst | (bSky ? GOMGFlags::FromSky : 0),
			FText::FromString(FString::Printf(TEXT("%s, %s"), *Parts[0], *Parts[1])));
		return true;
	}

	void ToggleManualFound(int32 Index)
	{
		FWorldStore& S = CurrentWorld();
		const FString Id = GOMGPlaces::IdOf(Index);
		if (S.Found.Contains(Id)) S.Found.Remove(Id); else S.Found.Add(Id);
		SaveStore();
		if (GRoot.IsValid()) GRoot->Rebuild();
	}

	bool IsGameFound(int32 Index) { return GHasGameFound && GGameFound.IsValidIndex(Index) && GGameFound[Index]; }
	bool IsFound(int32 Index) { return IsGameFound(Index) || CurrentWorld().Found.Contains(GOMGPlaces::IdOf(Index)); }
	bool HasGameState() { return GHasGameFound; }

	bool IsCollapsed(const FString& Key, bool bDefault)
	{
		if (!GStoreLoaded) LoadStore();
		return bDefault ? !GStore.Collapsed.Contains(TEXT("!") + Key) : GStore.Collapsed.Contains(Key);
	}

	void SetCollapsed(const FString& Key, bool bCollapsed)
	{
		// stored as the difference from the default, so changing a default later doesn't flip everyone's choices
		GStore.Collapsed.Remove(Key);
		GStore.Collapsed.Remove(TEXT("!") + Key);
		if (bCollapsed) GStore.Collapsed.Add(Key); else GStore.Collapsed.Add(TEXT("!") + Key);
		SaveStore();
	}

	FKey PanelKey() { return GStore.PanelKey; }
	FKey BackKey() { return GStore.BackKey; }

	bool PlayerLocation(FVector& Out)
	{
		if (AFGCharacterPlayer* Ch = LocalCharacter()) { Out = Ch->GetActorLocation(); return true; }
		return false;
	}

	void Toast(const FText& Message, bool bBad)
	{
		GStatus = Message;
		UE_LOG(LogGamesOMGTeleport, Log, TEXT("%s%s"), bBad ? TEXT("[refused] ") : TEXT(""), *Message.ToString());
		if (GRoot.IsValid()) GRoot->ShowToast(Message, bBad);
	}

	FText LastStatus() { return GStatus; }
	bool IsServerMissingMod() { return GServerMissing; }
}

#include "GOMGClient.h"

#include "GOMGPlaces.h"
#include "GOMGServer.h"
#include "GamesOMGTeleportModule.h"
#include "SGOMGPanel.h"

#include "Components/CapsuleComponent.h"
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
#include "HAL/PlatformProcess.h"
#include "HAL/PlatformApplicationMisc.h"

namespace
{
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
	TFunction<void(FKey)> GCapture;

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

	FString SessionName()
	{
		UWorld* W = GameWorld();
		AFGGameState* GS = W ? W->GetGameState<AFGGameState>() : nullptr;
		FString Name = GS ? GS->GetSessionName() : FString();
		if (Name.IsEmpty()) Name = TEXT("(unnamed session)");
		return Name;
	}

	bool IsGuest() { UWorld* W = GameWorld(); return W && W->GetNetMode() == NM_Client; }

	FString WorldKey()
	{
		FString Name = SessionName();
		if (IsGuest()) Name += TEXT(" @ ") + GameWorld()->URL.Host;   // a guest: per host
		return Name;
	}

	FVector FeetOf(const ACharacter* Ch)
	{
		return Ch->GetActorLocation() - FVector(0, 0, Ch->GetCapsuleComponent()->GetScaledCapsuleHalfHeight());
	}

	// map metres, as the in-game map prints them (world / 100, truncated)
	FString MapXY(const FVector& V)
	{
		return FString::Printf(TEXT("%lld, %lld"), (long long)FMath::TruncToDouble(V.X / 100.0), (long long)FMath::TruncToDouble(V.Y / 100.0));
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
		GCapture = nullptr;      // a key capture armed in the old world's Settings must not reach the old panel
		GPending = FPending();   // nor a teleport the old world was waiting on
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
		ReleaseInputHold();   // a lost request's hold (30-45 s old) must not outlive it
		GPending = FPending();
		GPending.Id = GNextId++;
		GPending.Label = Label;
		GPending.OriginFeet = FeetOf(Ch);
		GPending.OriginYaw = Ch->GetActorRotation().Yaw;
		GPending.bIsBack = (Flags & GOMGFlags::IsBack) != 0;
		GPending.Sent = FPlatformTime::Seconds();
		const int32 Id = GPending.Id;
		Rco->Server_Teleport(Target, Hint ? *Hint : FVector::ZeroVector, Flags | (Hint ? GOMGFlags::HasHint : 0), GPending.Id);
		if (GPending.Id != Id) return;   // solo / host: refused at once, and already said why
		if (GOMGStore::Settings().bCloseAfterTeleport) GOMGClient::ClosePanel();
		GOMGClient::Toast(FText::Format(FText::FromString(TEXT("Going to {0}…")), Label));
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
			const FGOMGSettings& S = GOMGStore::Settings();
			if (GCapture)
			{
				// Settings › Controls: the next key is the new one (Esc cancels); mouse buttons and modifiers alone don't count
				if (K.IsMouseButton() || K.IsModifierKey() || E.IsRepeat()) return true;
				TFunction<void(FKey)> Done = MoveTemp(GCapture);
				GCapture = nullptr;
				// a key that types (a letter, Space, the arrows and editing keys) would fire while you type in a box: refused
				const bool bTyping = E.GetCharacter() != 0 || K == EKeys::BackSpace || K == EKeys::Delete || K == EKeys::Left || K == EKeys::Right
					|| K == EKeys::Home || K == EKeys::End || K == EKeys::Tab || K == EKeys::SpaceBar;
				Done(K == EKeys::Escape ? EKeys::Invalid : bTyping ? EKeys::AnyKey : K);
				return true;
			}
			if (GPanelOpen)
			{
				if (K == EKeys::Escape) { if (!GRoot->Back()) GOMGClient::ClosePanel(); return true; }
				if (K == S.PanelKey && !E.IsRepeat()) { GOMGClient::ClosePanel(); return true; }
				if (K == EKeys::Up) { GRoot->MoveFocus(-1); return true; }
				if (K == EKeys::Down) { GRoot->MoveFocus(+1); return true; }
				if (K == EKeys::PageUp) { GRoot->MoveFocus(-10); return true; }
				if (K == EKeys::PageDown) { GRoot->MoveFocus(+10); return true; }
				if (K == EKeys::Enter && !E.IsRepeat()) { GRoot->Enter(); return true; }
				if (K == S.BackKey && !E.IsRepeat()) { GOMGClient::GoBack(); return true; }
				return false;   // typing goes to the text box
			}
			if (E.IsRepeat()) return false;
			// someone typing in chat or a sign: F-keys aren't typed, but don't steal them from a text box anyway
			const TSharedPtr<SWidget> Focus = App.GetKeyboardFocusedWidget();
			if (Focus.IsValid() && (Focus->GetType() == FName(TEXT("SEditableText")) || Focus->GetType() == FName(TEXT("SMultiLineEditableText")))) return false;
			if (K == S.PanelKey) { GOMGClient::TogglePanel(); return true; }
			if (K == S.BackKey) { GOMGClient::GoBack(); return true; }
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
			const FGOMGSettings& S = GOMGStore::Settings();
			UE_LOG(LogGamesOMGTeleport, Log, TEXT("keys: %s opens Places, %s returns"), *S.PanelKey.ToString(), *S.BackKey.ToString());
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
			FGOMGWorld& W = World();
			// Return swaps: the spot you left, named for what stood there (the model's "Return to Iron Ore, Grass Fields")
			W.bHasBack = true; W.Back = Done.OriginFeet; W.BackYaw = Done.OriginYaw;
			W.BackName = NameNear(Done.OriginFeet, 3000.0);
			if (!Done.bIsBack)
			{
				// Recently travelled: where the server stood you (it reports the capsule's centre, 4 cm over the floor)
				const float HalfH = Ch ? Ch->GetCapsuleComponent()->GetScaledCapsuleHalfHeight() : 90.f;
				GOMGStore::AddRecent(W, Done.Label.ToString(), Where - FVector(0, 0, HalfH + 4.f), Ch ? Ch->GetActorRotation().Yaw : 0.f);
			}
			Persist();
			Toast(Done.bIsBack ? FText::FromString(TEXT("Back where you were. Press Return again to swap back."))
				: FText::Format(FText::FromString(TEXT("You're at {0}.")), Done.Label));
			if (GRoot.IsValid()) { GRoot->OnTeleported(); GRoot->Rebuild(); }
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
		TBitArray<> Now(false, GOMGPlaces::Num());
		for (int32 i = 0; i < GOMGPlaces::Num(); ++i) Now[i] = (Bits[i / 8] >> (i % 8)) & 1;
		const bool bFirst = !GHasGameFound;
		const bool bChanged = bFirst || Now != GGameFound;
		GGameFound = MoveTemp(Now);
		GHasGameFound = true;
		if (GRoot.IsValid() && bChanged) GRoot->Rebuild();   // the 8 s re-ask while open rebuilds only when something changed
	}

	void TogglePanel()
	{
		if (GPanelOpen) { ClosePanel(); return; }
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
	}

	void ClosePanel()
	{
		if (!GPanelOpen) return;
		GPanelOpen = false;
		GCapture = nullptr;
		if (GRoot.IsValid()) GRoot->SetPanelOpen(false);
		SetPanelInput(false);
	}

	bool IsPanelOpen() { return GPanelOpen; }

	FText PlaceLabel(int32 Index)
	{
		const FGOMGPlaceDef& P = GOMGPlaces::Get(Index);
		const FText Region = GOMGPlaces::RegionName(P.Region);
		const FText Cat = GOMGPlaces::CatName(P.Cat);
		if (FCString::Strcmp(GOMGPlaces::Cat(P.Cat).Key, TEXT("tapes")) == 0) return GOMGPlaces::PlaceDetail(Index);
		return Region.IsEmpty() ? Cat : FText::Format(FText::FromString(TEXT("{0}, {1}")), Cat, Region);
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
		// nodes, wells and starts are places you stand on; collectibles you stand beside
		const bool bStandOn = GOMGPlaces::Cat(GOMGPlaces::Get(Index).Cat).Group >= 3;
		Send(Target, bHint ? &Hint : nullptr, bStandOn ? GOMGFlags::ExactFirst : 0, PlaceLabel(Index));
	}

	void GoToSpot(const FGOMGSpot& Spot)
	{
		// a place you stood: try exactly there first, as Return does
		Send(Spot.Feet, nullptr, GOMGFlags::ExactFirst, FText::FromString(Spot.Name));
	}

	void GoBack()
	{
		FGOMGWorld& W = World();
		if (!W.bHasBack) { Toast(FText::FromString(TEXT("Nothing to return to yet: a teleport remembers where you stood.")), true); return; }
		Send(W.Back, nullptr, GOMGFlags::ExactFirst | GOMGFlags::IsBack,
			W.BackName.IsEmpty() ? FText::FromString(TEXT("where you were")) : FText::FromString(W.BackName));
	}

	bool HasBack() { return World().bHasBack; }
	bool IsBusy() { return GPending.Id != 0; }

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
		const FString Near = NameNear(Target, 3000.0);
		Send(Target, nullptr, GOMGFlags::ExactFirst | (bSky ? GOMGFlags::FromSky : 0),
			FText::FromString(Near.IsEmpty() ? FString::Printf(TEXT("%s, %s"), *Parts[0], *Parts[1]) : Near));
		return true;
	}

	void FromClipboard()
	{
		FString Clip;
		FPlatformApplicationMisc::ClipboardPaste(Clip);
		Clip.TrimStartAndEndInline();
		if (Clip.IsEmpty()) { Toast(FText::FromString(TEXT("Your clipboard is empty.")), true); return; }
		if (!IsLine(Clip))
		{
			Toast(FText::FromString(TEXT("Your clipboard doesn't hold a /tp line. Press Copy teleport on a pin on the GamesOMG map first.")), true);
			return;
		}
		SubmitLine(Clip);
	}

	void ToggleManualFound(int32 Index)
	{
		FGOMGWorld& W = World();
		const FString Id = GOMGPlaces::IdOf(Index);
		const bool bOn = !W.Found.Contains(Id);
		if (bOn) W.Found.Add(Id); else W.Found.Remove(Id);
		if (Persist()) Toast(FText::Format(FText::FromString(bOn ? TEXT("Found: {0}") : TEXT("Back in the list: {0}")), PlaceLabel(Index)));
		if (GRoot.IsValid()) GRoot->Rebuild();
	}

	bool IsGameFound(int32 Index) { return GHasGameFound && GGameFound.IsValidIndex(Index) && GGameFound[Index]; }
	bool IsFound(int32 Index) { return IsGameFound(Index) || World().Found.Contains(GOMGPlaces::IdOf(Index)); }
	bool HasGameState() { return GHasGameFound; }

	FKey PanelKey() { return GOMGStore::Settings().PanelKey; }
	FKey BackKey() { return GOMGStore::Settings().BackKey; }

	bool PlayerLocation(FVector& Out)
	{
		if (AFGCharacterPlayer* Ch = LocalCharacter()) { Out = Ch->GetActorLocation(); return true; }
		return false;
	}

	bool PlayerFeet(FVector& Out, float& Yaw)
	{
		AFGCharacterPlayer* Ch = LocalCharacter();
		if (!Ch) return false;
		Out = FeetOf(Ch);
		Yaw = Ch->GetActorRotation().Yaw;
		return true;
	}

	void Toast(const FText& Message, bool bBad)
	{
		GStatus = Message;
		UE_LOG(LogGamesOMGTeleport, Log, TEXT("%s%s"), bBad ? TEXT("[refused] ") : TEXT(""), *Message.ToString());
		if (!GRoot.IsValid()) return;
		// the model's footer line while the panel is open; a line at the top of the screen when it isn't
		if (GPanelOpen) GRoot->ShowStatus(Message, bBad);
		else GRoot->ShowToast(Message, bBad);
	}

	FText LastStatus() { return GStatus; }
	bool IsServerMissingMod() { return GServerMissing; }

	FGOMGWorld& World() { return GOMGStore::World(WorldKey()); }

	FString SessionLabel()
	{
		if (!GameWorld()) return FString();
		return IsGuest() ? SessionName() + TEXT(" · co-op") : SessionName();
	}

	FString NameNear(const FVector& Feet, double RadiusCm)
	{
		const FGOMGWorld& W = World();
		const FGOMGSpot* Best = nullptr;
		double BestD = RadiusCm;
		for (const FGOMGSpot& S : W.Saved) { const double D = FVector::Dist2D(S.Feet, Feet); if (D <= BestD) { Best = &S; BestD = D; } }
		if (Best) return Best->Name;
		int32 Near = INDEX_NONE;
		BestD = RadiusCm;
		for (int32 i = 0; i < GOMGPlaces::Num(); ++i)
		{
			const double D = FVector::Dist2D(GOMGPlaces::LocationOf(i), Feet);
			if (D <= BestD && FMath::Abs(GOMGPlaces::LocationOf(i).Z - Feet.Z) < RadiusCm) { Near = i; BestD = D; }
		}
		return Near == INDEX_NONE ? FString() : PlaceLabel(Near).ToString();
	}

	int32 SaveHere()
	{
		FVector Feet; float Yaw = 0;
		if (!PlayerFeet(Feet, Yaw)) { Toast(FText::FromString(TEXT("Load into a world first.")), true); return INDEX_NONE; }
		FGOMGWorld& W = World();
		// the model's name for it: "Near <the map place within 60 m>", else where it is
		FString Name;
		int32 Near = INDEX_NONE;
		double BestD = 6000.0;
		for (int32 i = 0; i < GOMGPlaces::Num(); ++i)
		{
			const double D = FVector::Dist2D(GOMGPlaces::LocationOf(i), Feet);
			if (D <= BestD) { Near = i; BestD = D; }
		}
		if (Near != INDEX_NONE) Name = FText::Format(FText::FromString(TEXT("Near {0}")), PlaceLabel(Near)).ToString();
		else
		{
			// no map place that close: the region of the nearest within 1 km, and the in-game map's X, Y
			BestD = 100000.0;
			for (int32 i = 0; i < GOMGPlaces::Num(); ++i)
			{
				const double D = FVector::Dist2D(GOMGPlaces::LocationOf(i), Feet);
				if (D <= BestD && GOMGPlaces::Get(i).Region >= 0) { Near = i; BestD = D; }
			}
			const FString Region = Near == INDEX_NONE ? FString() : GOMGPlaces::RegionName(GOMGPlaces::Get(Near).Region).ToString();
			Name = Region.IsEmpty() ? MapXY(Feet) : Region + TEXT(" ") + MapXY(Feet);
		}
		FGOMGSpot* S = GOMGStore::AddSaved(W, Name, Feet, Yaw, TEXT(""));
		if (!S) { Toast(FText::FromString(TEXT("This world's list is full. Delete a place first.")), true); return INDEX_NONE; }
		const FString Kept = S->Name;
		const int32 Index = W.Saved.Num() - 1;
		if (Persist()) Toast(FText::Format(FText::FromString(TEXT("Saved “{0}”. Rename it any time.")), FText::FromString(Kept)));
		return Index;
	}

	bool Persist()
	{
		FString Why;
		if (GOMGStore::Save(&Why)) return true;
		Toast(FText::Format(FText::FromString(TEXT("Places not saved: {0}. This session's changes stay until you quit.")), FText::FromString(Why)), true);
		return false;
	}

	FString MapUrl() { return TEXT("https://gamesomg.com/satisfactory-map/"); }

	void OpenUrl(const FString& Url)
	{
		FString Error;
		FPlatformProcess::LaunchURL(*Url, nullptr, &Error);
		if (Error.IsEmpty()) Toast(FText::FromString(TEXT("Opened the GamesOMG map in your browser.")));
		else Toast(FText::FromString(TEXT("Your browser did not open. The map is at gamesomg.com/satisfactory-map.")), true);
	}

	void CaptureKey(TFunction<void(FKey)> Done) { GCapture = MoveTemp(Done); }
	void CancelCapture() { GCapture = nullptr; }
	bool IsCapturing() { return (bool)GCapture; }
}

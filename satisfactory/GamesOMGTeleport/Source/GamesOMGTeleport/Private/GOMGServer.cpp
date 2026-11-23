#include "GOMGServer.h"

#include "GOMGPlaces.h"
#include "GamesOMGTeleportModule.h"

#include "Components/CapsuleComponent.h"
#include "Engine/Engine.h"
#include "HAL/FileManager.h"
#include "HAL/IConsoleManager.h"
#include "GameFramework/GameModeBase.h"
#include "WorldPartition/WorldPartitionSubsystem.h"
#include "Components/WorldPartitionStreamingSourceComponent.h"
#include "Engine/World.h"
#include "EngineUtils.h"
#include "FGCharacterPlayer.h"
#include "FGDropPod.h"
#include "FGItemPickup.h"
#include "FGPlayerController.h"
#include "FGScannableSubsystem.h"
#include "FGSchematicManager.h"
#include "GameFramework/CharacterMovementComponent.h"
#include "Dom/JsonObject.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"
#include "Serialization/JsonWriter.h"

namespace
{
	// how a teleport runs on the server
	enum class EJobState : uint8 { Holding, Done };

	struct FJob
	{
		TWeakObjectPtr<UGOMGTeleportRCO> Rco;
		TWeakObjectPtr<AFGPlayerController> PC;
		TWeakObjectPtr<AFGCharacterPlayer> Character;
		FVector Target = FVector::ZeroVector;
		FVector Hint = FVector::ZeroVector;
		uint8 Flags = 0;
		int32 RequestId = 0;
		FVector OriginLocation = FVector::ZeroVector;
		FRotator OriginRotation = FRotator::ZeroRotator;
		double Started = 0;
		bool bClientDone = false;
		bool bSurvey = false;
		EJobState State = EJobState::Holding;
	};

	TArray<FJob> GJobs;

	// ── the landing survey (a solo world; Saved/GamesOMGTeleport/survey.on turns it on) ──────────────────────────────────────
	struct FSurvey
	{
		bool bActive = false;
		bool bLoaded = false;
		TMap<FString, TOptional<FVector>> Results;   // place id → feet, or unset = no landing
		int32 SinceSave = 0;
		int32 Current = INDEX_NONE;
		double StartedAt = 0;
	};
	FSurvey GSurvey;
	FString SurveyDir() { return FPaths::ProjectSavedDir() / TEXT("GamesOMGTeleport"); }

	constexpr double MinHoldSeconds = 0.6;   // give collision a moment even when streaming reports done at once
	constexpr double MaxHoldSeconds = 25.0;  // a very slow disk; after this we try anyway, then give up
	constexpr float HoldAbove = 250.f;       // where you wait while the destination streams in (cm over the target)

	double Now() { return FPlatformTime::Seconds(); }

	void Reply(UGOMGTeleportRCO* Rco, int32 RequestId, EGOMGResult Result, const FVector& Where)
	{
		if (Rco) Rco->Client_TeleportState(RequestId, Result, Where);
	}

	FCollisionQueryParams Params(const AActor* Ignore)
	{
		FCollisionQueryParams P(SCENE_QUERY_STAT(GOMGLanding), false, Ignore);
		P.bReturnPhysicalMaterial = false;
		return P;
	}

	/** Feet on a floor at (X, Y), searched downward from just above RefZ. */
	bool FloorAt(UWorld* W, const ACharacter* Ch, double X, double Y, double FromZ, double ToZ, FVector& OutFeet, FVector& OutNormal)
	{
		const UCapsuleComponent* Cap = Ch->GetCapsuleComponent();
		FHitResult Hit;
		const FCollisionResponseParams Resp(Cap->GetCollisionResponseToChannels());
		if (!W->LineTraceSingleByChannel(Hit, FVector(X, Y, FromZ), FVector(X, Y, ToZ), Cap->GetCollisionObjectType(), Params(Ch), Resp))
			return false;
		OutFeet = Hit.ImpactPoint;
		OutNormal = Hit.ImpactNormal;
		return true;
	}

	bool RoomToStand(UWorld* W, const ACharacter* Ch, const FVector& Feet)
	{
		const UCapsuleComponent* Cap = Ch->GetCapsuleComponent();
		const float R = Cap->GetScaledCapsuleRadius(), H = Cap->GetScaledCapsuleHalfHeight();
		const FVector Center = Feet + FVector(0, 0, H + 6.f);
		const FCollisionShape Shape = FCollisionShape::MakeCapsule(FMath::Max(R - 2.f, 10.f), FMath::Max(H - 2.f, 20.f));
		return !W->OverlapBlockingTestByChannel(Center, FQuat::Identity, Cap->GetCollisionObjectType(), Shape, Params(Ch),
			FCollisionResponseParams(Cap->GetCollisionResponseToChannels()));
	}

	/** Can you see the place from there? (a wall between = probably the other side of a cave wall) */
	bool Sees(UWorld* W, const ACharacter* Ch, const FVector& Feet, const FVector& Target)
	{
		FHitResult Hit;
		const FVector Eye = Feet + FVector(0, 0, 160.f);
		const FVector To = Target + FVector(0, 0, 40.f);
		if (!W->LineTraceSingleByChannel(Hit, Eye, To, ECC_Visibility, Params(Ch))) return true;
		return FVector::Dist(Hit.ImpactPoint, To) < 150.f;   // the thing itself (a shrine, a pod) is in the way: fine
	}

	FVector ClampToWorldZ(const FVector& V) { return FVector(V.X, V.Y, FMath::Clamp(V.Z, -100000.0, 400000.0)); }
}

namespace GOMGServer
{
	bool LevelStreamingDone(AFGPlayerController* PC)
	{
		if (!PC) return false;
		static const FName Name(TEXT("IsLevelStreamingComplete"));
		UFunction* Fn = PC->FindFunction(Name);
		const FBoolProperty* Ret = Fn ? CastField<FBoolProperty>(Fn->GetReturnProperty()) : nullptr;
		if (!Fn || !Ret)
		{
			static bool bWarned = false;
			if (!bWarned) { bWarned = true; UE_LOG(LogGamesOMGTeleport, Warning, TEXT("the game has no IsLevelStreamingComplete: waiting by time only")); }
			return true;   // the hold still has its minimum time, and a landing needs a floor
		}
		TArray<uint8, TInlineAllocator<16>> Params;
		Params.SetNumZeroed(FMath::Max<int32>(Fn->ParmsSize, 1));
		PC->ProcessEvent(Fn, Params.GetData());
		return Ret->GetPropertyValue_InContainer(Params.GetData());
	}

	bool IsInsideWorld(const FVector& V)
	{
		// the map's playable box (tools/satisfactory-map border, metres x100) plus a margin
		return V.X > -400000.0 && V.X < 500000.0 && V.Y > -420000.0 && V.Y < 400000.0 && V.Z > -100000.0 && V.Z < 400000.0
			&& !V.ContainsNaN();
	}

	bool FindLanding(UWorld* W, ACharacter* Ch, const FVector& TargetIn, const FVector* Hint, uint8 Flags, FVector& OutFeet, FString* OutWhy)
	{
		if (!W || !Ch || !Ch->GetCapsuleComponent()) return false;
		FVector Target = TargetIn;
		FVector Feet, Normal;
		constexpr float MinWalkableZ = 0.70f;   // about 45 degrees
		constexpr double MaxDrop = 800.0;      // a landing more than 8 m under the place is a different level (a cave below it)

		// "~": the top surface at X, Y, then search around that as usual
		if (Flags & GOMGFlags::FromSky)
		{
			if (!FloorAt(W, Ch, Target.X, Target.Y, 400000.0, -100000.0, Feet, Normal)) return false;
			Target.Z = Feet.Z;
		}

		// 1. a landing checked ahead of time (the survey): still re-checked here, the player may have built on it
		if (Hint && FloorAt(W, Ch, Hint->X, Hint->Y, Hint->Z + 120.0, Hint->Z - 400.0, Feet, Normal)
			&& Normal.Z >= MinWalkableZ && RoomToStand(W, Ch, Feet))
		{
			OutFeet = Feet;
			return true;
		}

		// 2. rings beside the place. The trace starts just above the place's own height, never from the sky, so a place in a cave
		//    or under a factory lands on that floor, not on the roof above it.
		//    A collectible: stand beside it (from 2.5 m out); only if nothing beside it will do, on its own spot (a slug on a
		//    ledge). Nodes, wells, starts and pasted lines: the spot itself first.
		const bool bExact = (Flags & GOMGFlags::ExactFirst) != 0;
		const float Radii[] = { 0.f, 150.f, 250.f, 400.f, 600.f, 900.f, 1200.f, 1600.f };
		const int32 NumRadii = UE_ARRAY_COUNT(Radii);
		TArray<int32, TInlineAllocator<10>> Order;
		for (int32 Ring = bExact ? 0 : 2; Ring < NumRadii; ++Ring) Order.Add(Ring);
		if (!bExact) { Order.Add(0); Order.Add(1); }
		const double StartZ = Target.Z + 250.0;
		bool bHaveFallback = false;
		FVector Fallback;
		int32 NoHit = 0, Steep = 0, Drop = 0, NoRoom = 0, Blind = 0;
		for (const int32 Ring : Order)
		{
			const float R = Radii[Ring];
			const int32 Steps = R <= 0.f ? 1 : FMath::Max(12, FMath::CeilToInt(2.0 * PI * R / 250.0));   // a point every 2.5 m at most
			for (int32 S = 0; S < Steps; ++S)
			{
				const double A = (2.0 * PI * S) / Steps;
				const double X = Target.X + R * FMath::Cos(A), Y = Target.Y + R * FMath::Sin(A);
				if (!FloorAt(W, Ch, X, Y, StartZ, Target.Z - MaxDrop - 100.0, Feet, Normal)) { ++NoHit; continue; }
				if (Normal.Z < MinWalkableZ) { ++Steep; continue; }
				if (Target.Z - Feet.Z > MaxDrop) { ++Drop; continue; }
				if (!RoomToStand(W, Ch, Feet)) { ++NoRoom; continue; }
				if (Sees(W, Ch, Feet, Target)) { OutFeet = Feet; return true; }
				++Blind;
				if (!bHaveFallback && FMath::Abs(Feet.Z - Target.Z) <= 300.0) { bHaveFallback = true; Fallback = Feet; }
			}
		}
		if (bHaveFallback) { OutFeet = Fallback; return true; }
		if (OutWhy) *OutWhy = FString::Printf(TEXT("no floor %d, steep %d, >8 m down %d, no room %d, can't see it %d"), NoHit, Steep, Drop, NoRoom, Blind);
		return false;
	}

	static void StartJob(AFGPlayerController* PC, UGOMGTeleportRCO* Rco, FVector Target, FVector Hint, uint8 Flags, int32 RequestId, bool bSurvey);

	void StartTeleport(UGOMGTeleportRCO* Rco, FVector Target, FVector Hint, uint8 Flags, int32 RequestId)
	{
		StartJob(Rco ? Rco->GetOwnerPlayerController() : nullptr, Rco, Target, Hint, Flags, RequestId, false);
	}

	static void StartJob(AFGPlayerController* PC, UGOMGTeleportRCO* Rco, FVector Target, FVector Hint, uint8 Flags, int32 RequestId, bool bSurvey)
	{
		AFGCharacterPlayer* Ch = PC ? Cast<AFGCharacterPlayer>(PC->GetPawn()) : nullptr;
		if (!PC || !Ch) { Reply(Rco, RequestId, EGOMGResult::NotNow, FVector::ZeroVector); return; }
		if (!IsInsideWorld(Target)) { Reply(Rco, RequestId, EGOMGResult::BadTarget, FVector::ZeroVector); return; }
		if (Ch->IsDrivingVehicle() || !Ch->IsAliveAndWell() || Ch->IsRagdolled())
		{
			Reply(Rco, RequestId, EGOMGResult::NotNow, FVector::ZeroVector);
			return;
		}
		for (const FJob& J : GJobs)
			if (J.PC.Get() == PC && J.State == EJobState::Holding) { Reply(Rco, RequestId, EGOMGResult::Busy, FVector::ZeroVector); return; }

		FJob Job;
		Job.Rco = Rco; Job.PC = PC; Job.Character = Ch;
		Job.Target = Target; Job.Hint = Hint; Job.Flags = Flags; Job.RequestId = RequestId;
		Job.OriginLocation = Ch->GetActorLocation(); Job.OriginRotation = Ch->GetActorRotation();
		Job.Started = Now();
		Job.bClientDone = PC->IsLocalController();   // solo / host: one world, one check
		Job.bSurvey = bSurvey;

		// Hold the player at the destination while it streams in, on the server and (through the RCO) on the client.
		UCharacterMovementComponent* Move = Ch->GetCharacterMovement();
		Move->StopMovementImmediately();
		Move->SetMovementMode(MOVE_Flying);
		const FVector HoldAt = ClampToWorldZ((Flags & GOMGFlags::FromSky) ? FVector(Target.X, Target.Y, Target.Z + 2000.0) : Target + FVector(0, 0, HoldAbove));
		Ch->TeleportTo(HoldAt, Ch->GetActorRotation(), false, true);
		GJobs.Add(Job);
		UE_LOG(LogGamesOMGTeleport, Log, TEXT("request %d: holding at %s (flags %d)"), RequestId, *HoldAt.ToCompactString(), Flags);
		Reply(Rco, RequestId, EGOMGResult::Holding, HoldAt);
	}

	void ClientStreamingDone(UGOMGTeleportRCO* Rco, int32 RequestId)
	{
		for (FJob& J : GJobs)
			if (J.Rco.Get() == Rco && J.RequestId == RequestId) J.bClientDone = true;
	}

	static void SaveSurvey(bool bFinal)
	{
		TSharedRef<FJsonObject> Root = MakeShared<FJsonObject>();
		Root->SetStringField(TEXT("listHash"), FString::Printf(TEXT("%08x"), GOMGPlaces::ListHash()));
		TSharedRef<FJsonObject> L = MakeShared<FJsonObject>();
		for (const auto& Pair : GSurvey.Results)
		{
			if (!Pair.Value.IsSet()) { L->SetField(Pair.Key, MakeShared<FJsonValueNull>()); continue; }
			const FVector V = Pair.Value.GetValue();
			TArray<TSharedPtr<FJsonValue>> A;
			A.Add(MakeShared<FJsonValueNumber>(FMath::RoundToDouble(V.X)));
			A.Add(MakeShared<FJsonValueNumber>(FMath::RoundToDouble(V.Y)));
			A.Add(MakeShared<FJsonValueNumber>(FMath::RoundToDouble(V.Z)));
			L->SetArrayField(Pair.Key, A);
		}
		Root->SetObjectField(TEXT("landings"), L);
		FString Out;
		FJsonSerializer::Serialize(Root, TJsonWriterFactory<>::Create(&Out));
		FFileHelper::SaveStringToFile(Out, *(SurveyDir() / TEXT("survey.json")));
		GSurvey.SinceSave = 0;
		if (bFinal) UE_LOG(LogGamesOMGTeleport, Log, TEXT("survey: %d results written to %s"), GSurvey.Results.Num(), *(SurveyDir() / TEXT("survey.json")));
	}

	static void LoadSurvey()
	{
		GSurvey.bLoaded = true;
		FString Text;
		if (!FFileHelper::LoadFileToString(Text, *(SurveyDir() / TEXT("survey.json")))) return;
		TSharedPtr<FJsonObject> Root;
		if (!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Text), Root) || !Root.IsValid()) return;
		const TSharedPtr<FJsonObject>* L;
		if (!Root->TryGetObjectField(TEXT("landings"), L)) return;
		for (const auto& Pair : (*L)->Values)
		{
			const TArray<TSharedPtr<FJsonValue>>* A;
			if (Pair.Value->TryGetArray(A) && A->Num() == 3)
				GSurvey.Results.Add(Pair.Key, FVector((*A)[0]->AsNumber(), (*A)[1]->AsNumber(), (*A)[2]->AsNumber()));
			else
				GSurvey.Results.Add(Pair.Key, TOptional<FVector>());
		}
		UE_LOG(LogGamesOMGTeleport, Log, TEXT("survey: resuming with %d results"), GSurvey.Results.Num());
	}

	/**
	 * The survey on a dedicated server, no client needed: a stand-in character with the player's own capsule and collision
	 * (from the game mode's pawn class) is moved to each place and the same FindLanding runs on it. If the server streams
	 * its world (wp.Runtime.EnableServerStreaming), the stand-in carries a streaming source and each hop waits for it.
	 */
	static TWeakObjectPtr<ACharacter> GProbe;
	static double GProbeArrived = 0;
	static int32 GProbePlace = INDEX_NONE;

	static void SurveyOnServer()
	{
		UWorld* W = nullptr;
		if (GEngine)
			for (const FWorldContext& Ctx : GEngine->GetWorldContexts())
				if (Ctx.WorldType == EWorldType::Game && Ctx.World() && Ctx.World()->GetAuthGameMode() && Ctx.World()->HasBegunPlay())
					W = Ctx.World();
		if (!W) return;
		AGameModeBase* GM = W->GetAuthGameMode();
		static bool bStreams = false;
		if (!GProbe.IsValid())
		{
			const IConsoleVariable* CV = IConsoleManager::Get().FindConsoleVariable(TEXT("wp.Runtime.EnableServerStreaming"));
			bStreams = CV && CV->GetInt() != 0;
			FActorSpawnParameters SP;
			SP.SpawnCollisionHandlingOverride = ESpawnActorCollisionHandlingMethod::AlwaysSpawn;
			ACharacter* Probe = W->SpawnActor<ACharacter>(ACharacter::StaticClass(), FVector(0, 0, 200000), FRotator::ZeroRotator, SP);
			if (!Probe) return;
			const ACharacter* PawnCDO = GM->DefaultPawnClass ? Cast<ACharacter>(GM->DefaultPawnClass->GetDefaultObject()) : nullptr;
			const UCapsuleComponent* Src = PawnCDO ? PawnCDO->GetCapsuleComponent() : nullptr;
			UCapsuleComponent* Cap = Probe->GetCapsuleComponent();
			if (Src)
			{
				Cap->SetCapsuleSize(Src->GetUnscaledCapsuleRadius(), Src->GetUnscaledCapsuleHalfHeight());
				Cap->SetCollisionProfileName(Src->GetCollisionProfileName());
			}
			Probe->GetCharacterMovement()->SetMovementMode(MOVE_None);
			if (bStreams)
			{
				UWorldPartitionStreamingSourceComponent* Source = NewObject<UWorldPartitionStreamingSourceComponent>(Probe);
				Source->RegisterComponent();
			}
			GProbe = Probe;
			GSurvey.bActive = true;
			GSurvey.StartedAt = Now();
			UE_LOG(LogGamesOMGTeleport, Log, TEXT("survey (server): started; stand-in capsule %.0f x %.0f, profile %s; server streaming %s"),
				Cap->GetUnscaledCapsuleRadius(), Cap->GetUnscaledCapsuleHalfHeight(), *Cap->GetCollisionProfileName().ToString(),
				bStreams ? TEXT("ON (waiting per hop)") : TEXT("off (whole world loaded)"));
		}
		ACharacter* Probe = GProbe.Get();

		// whole world loaded: a batch per tick; streaming: one hop at a time, waiting for the cells around the stand-in
		for (int32 Batch = 0; Batch < (bStreams ? 1 : 40); ++Batch)
		{
			if (GProbePlace == INDEX_NONE)
			{
				const FVector From = Probe->GetActorLocation();
				double BestD = TNumericLimits<double>::Max();
				for (int32 i = 0; i < GOMGPlaces::Num(); ++i)
				{
					if (GSurvey.Results.Contains(GOMGPlaces::IdOf(i))) continue;
					const double D = FVector::DistSquared(From, GOMGPlaces::LocationOf(i));
					if (D < BestD) { BestD = D; GProbePlace = i; }
				}
				if (GProbePlace == INDEX_NONE)
				{
					SaveSurvey(true);
					IFileManager::Get().Delete(*(SurveyDir() / TEXT("survey.on")));
					UE_LOG(LogGamesOMGTeleport, Log, TEXT("survey (server): DONE, %d places in %.0f s"), GSurvey.Results.Num(), Now() - GSurvey.StartedAt);
					Probe->Destroy();
					GSurvey.bActive = false;
					return;
				}
				Probe->SetActorLocation(GOMGPlaces::LocationOf(GProbePlace) + FVector(0, 0, HoldAbove), false, nullptr, ETeleportType::TeleportPhysics);
				GProbeArrived = Now();
			}
			if (bStreams)
			{
				const UWorldPartitionSubsystem* WP = W->GetSubsystem<UWorldPartitionSubsystem>();
				UWorldPartitionStreamingSourceComponent* Source = Probe->FindComponentByClass<UWorldPartitionStreamingSourceComponent>();
				const bool bReady = !WP || !Source || WP->IsStreamingCompleted(Source);
				if ((!bReady || Now() - GProbeArrived < MinHoldSeconds) && Now() - GProbeArrived < MaxHoldSeconds) return;
			}
			const int32 i = GProbePlace;
			const bool bStandOn = GOMGPlaces::Cat(GOMGPlaces::Get(i).Cat).Group >= 3;
			FVector Feet;
			FString Why;
			const bool bFound = FindLanding(W, Probe, GOMGPlaces::LocationOf(i), nullptr, bStandOn ? GOMGFlags::ExactFirst : 0, Feet, &Why);
			const FString Id = GOMGPlaces::IdOf(i);
			if (bFound) GSurvey.Results.Add(Id, Feet); else GSurvey.Results.Add(Id, TOptional<FVector>());
			UE_LOG(LogGamesOMGTeleport, Verbose, TEXT("survey %s: %s"), *Id, bFound ? *Feet.ToCompactString() : TEXT("NO LANDING"));
			if (!bFound) UE_LOG(LogGamesOMGTeleport, Log, TEXT("survey %s (%s) at %s: NO LANDING (%s)"), *Id, GOMGPlaces::Cat(GOMGPlaces::Get(i).Cat).Key,
				*GOMGPlaces::LocationOf(i).ToCompactString(), *Why);
			GProbePlace = INDEX_NONE;
			if (++GSurvey.SinceSave >= 50) SaveSurvey(false);
		}
	}

	/** One place at a time, nearest first, through the same hold-and-search a real teleport uses. */
	static void SurveyTick()
	{
		const bool bOn = FPaths::FileExists(SurveyDir() / TEXT("survey.on"));
		if (!bOn)
		{
			if (GSurvey.bActive) { SaveSurvey(true); GSurvey.bActive = false; UE_LOG(LogGamesOMGTeleport, Log, TEXT("survey: stopped")); }
			return;
		}
		if (!GSurvey.bLoaded) LoadSurvey();
		if (IsRunningDedicatedServer()) { SurveyOnServer(); return; }
		UWorld* W = nullptr;
		AFGPlayerController* PC = nullptr;
		if (GEngine)
			for (const FWorldContext& Ctx : GEngine->GetWorldContexts())
				if (Ctx.WorldType == EWorldType::Game && Ctx.World() && Ctx.World()->GetNetMode() == NM_Standalone)
				{
					W = Ctx.World();
					PC = Cast<AFGPlayerController>(W->GetFirstPlayerController());
				}
		AFGCharacterPlayer* Ch = PC ? Cast<AFGCharacterPlayer>(PC->GetPawn()) : nullptr;
		if (!Ch || !LevelStreamingDone(PC)) return;
		for (const FJob& J : GJobs) if (J.State == EJobState::Holding) return;
		if (!GSurvey.bActive) { GSurvey.bActive = true; GSurvey.StartedAt = Now(); UE_LOG(LogGamesOMGTeleport, Log, TEXT("survey: started")); }
		if (GSurvey.SinceSave >= 10) SaveSurvey(false);

		// next: the nearest place not surveyed yet
		const FVector From = Ch->GetActorLocation();
		int32 Best = INDEX_NONE;
		double BestD = TNumericLimits<double>::Max();
		for (int32 i = 0; i < GOMGPlaces::Num(); ++i)
		{
			if (GSurvey.Results.Contains(GOMGPlaces::IdOf(i))) continue;
			const double D = FVector::DistSquared(From, GOMGPlaces::LocationOf(i));
			if (D < BestD) { BestD = D; Best = i; }
		}
		if (Best == INDEX_NONE)
		{
			SaveSurvey(true);
			IFileManager::Get().Delete(*(SurveyDir() / TEXT("survey.on")));
			UE_LOG(LogGamesOMGTeleport, Log, TEXT("survey: DONE, %d places in %.0f s"), GSurvey.Results.Num(), Now() - GSurvey.StartedAt);
			UCharacterMovementComponent* Move = Ch->GetCharacterMovement();
			Move->SetMovementMode(MOVE_Walking);
			GSurvey.bActive = false;
			return;
		}
		const bool bStandOn = GOMGPlaces::Cat(GOMGPlaces::Get(Best).Cat).Group >= 3;
		StartJob(PC, nullptr, GOMGPlaces::LocationOf(Best), FVector::ZeroVector, bStandOn ? GOMGFlags::ExactFirst : 0, Best, true);
	}

	void Tick(float)
	{
		SurveyTick();
		const double T = Now();
		for (FJob& J : GJobs)
		{
			if (J.State != EJobState::Holding) continue;
			AFGPlayerController* PC = J.PC.Get();
			AFGCharacterPlayer* Ch = J.Character.Get();
			UGOMGTeleportRCO* Rco = J.Rco.Get();
			if (!PC || !Ch || PC->GetPawn() != Ch) { J.State = EJobState::Done; continue; }   // left, died, respawned
			UCharacterMovementComponent* Move = Ch->GetCharacterMovement();
			Move->Velocity = FVector::ZeroVector;

			const double Held = T - J.Started;
			const bool bServerReady = LevelStreamingDone(PC);
			const bool bTimedOut = Held > MaxHoldSeconds;
			if (Held < MinHoldSeconds || (!(bServerReady && J.bClientDone) && !bTimedOut)) continue;

			FVector Feet;
			const bool bHint = (J.Flags & GOMGFlags::HasHint) != 0;
			const bool bFound = FindLanding(Ch->GetWorld(), Ch, J.Target, bHint ? &J.Hint : nullptr, J.Flags, Feet);
			const float HalfH = Ch->GetCapsuleComponent()->GetScaledCapsuleHalfHeight();
			if (J.bSurvey)
			{
				// record and move on; the player stays held, the next place starts from here
				const FString Id = GOMGPlaces::IdOf(J.RequestId);
				if (bFound) GSurvey.Results.Add(Id, Feet); else GSurvey.Results.Add(Id, TOptional<FVector>());
				UE_LOG(LogGamesOMGTeleport, Log, TEXT("survey %s: %s (%.1fs%s)"), *Id, bFound ? *Feet.ToCompactString() : TEXT("NO LANDING"), Held,
					bTimedOut ? TEXT(", stream wait timed out") : TEXT(""));
				++GSurvey.SinceSave;
				J.State = EJobState::Done;
				continue;
			}
			if (bFound)
			{
				const FVector Dest = Feet + FVector(0, 0, HalfH + 4.f);
				FRotator Face = (J.Target - Dest).GetSafeNormal2D().Rotation();   // face the place
				if (J.Flags & GOMGFlags::IsBack) Face = J.OriginRotation;
				Ch->TeleportTo(Dest, FRotator(0, Face.Yaw, 0), false, true);
				Move->StopMovementImmediately();
				Move->SetMovementMode(MOVE_Walking);
				PC->ClientSetRotation(FRotator(0, Face.Yaw, 0), true);   // the owning client steers its own view
				UE_LOG(LogGamesOMGTeleport, Log, TEXT("request %d: landed at %s after %.1fs%s"), J.RequestId, *Dest.ToCompactString(), Held,
					bTimedOut ? TEXT(" (stream wait timed out)") : TEXT(""));
				Reply(Rco, J.RequestId, EGOMGResult::Ok, Dest);
			}
			else
			{
				Ch->TeleportTo(J.OriginLocation, J.OriginRotation, false, true);
				Move->StopMovementImmediately();
				Move->SetMovementMode(MOVE_Walking);
				UE_LOG(LogGamesOMGTeleport, Log, TEXT("request %d: no landing near %s, sent back"), J.RequestId, *J.Target.ToCompactString());
				Reply(Rco, J.RequestId, EGOMGResult::NoLanding, J.OriginLocation);
			}
			J.State = EJobState::Done;
		}
		GJobs.RemoveAll([](const FJob& J) { return J.State == EJobState::Done; });
	}

	void Shutdown() { GJobs.Reset(); }

	void ComputeFound(UWorld* World, TArray<uint8>& OutBits)
	{
		const int32 N = GOMGPlaces::Num();
		OutBits.SetNumZeroed((N + 7) / 8);
		if (!World) return;
		AFGScannableSubsystem* Scan = AFGScannableSubsystem::Get(World);
		AFGSchematicManager* Schematics = AFGSchematicManager::Get(World);

		// the save's own record: every level pickup it has destroyed (picked up) and every drop pod looted
		const TSet<FGuid>* Destroyed = nullptr;
		if (Scan)
			if (const FSetProperty* P = FindFProperty<FSetProperty>(AFGScannableSubsystem::StaticClass(), TEXT("mDestroyedPickups")))
				Destroyed = P->ContainerPtrToValuePtr<TSet<FGuid>>(Scan);
		// pickups that are loaded right now and empty (picked up but not destroyed)
		TSet<FGuid> EmptyLoaded;
		for (TActorIterator<AFGItemPickup> It(World); It; ++It)
			if (It->IsPickedUp()) EmptyLoaded.Add(It->GetItemPickupGuid());

		for (int32 i = 0; i < N; ++i)
		{
			bool bFound = false;
			switch (GOMGPlaces::TickOf(i))
			{
			case EGOMGTick::Pickup:
			{
				const FGuid G = GOMGPlaces::GuidOf(i);
				bFound = (Destroyed && Destroyed->Contains(G)) || EmptyLoaded.Contains(G);
				break;
			}
			case EGOMGTick::DropPod:
				bFound = Scan && Scan->HasDropPodBeenLooted(GOMGPlaces::GuidOf(i));
				break;
			case EGOMGTick::Schematic:
				if (Schematics)
					if (UClass* Sch = GOMGPlaces::PathClass(GOMGPlaces::Get(i).Aux1))
						bFound = Schematics->IsSchematicPurchased(Sch);
				break;
			default:
				break;
			}
			if (bFound) OutBits[i / 8] |= (uint8)(1u << (i % 8));
		}
	}
}

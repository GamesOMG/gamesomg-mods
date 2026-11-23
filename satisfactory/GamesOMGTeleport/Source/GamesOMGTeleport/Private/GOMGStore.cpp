#include "GOMGStore.h"

#include "GamesOMGTeleportModule.h"

#include "Dom/JsonObject.h"
#include "HAL/FileManager.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"
#include "Serialization/JsonWriter.h"

namespace
{
	FGOMGSettings GSettings;
	TMap<FString, FGOMGWorld> GWorlds;
	bool GLoaded = false;
	int32 GReadVersion = 0;      // what the file said (0: no file, or not ours)
	FString GBlocked;            // why nothing may be written this session ("" = free to write)
	bool GBackedUpOld = false;   // a version-1 file is copied aside before the first write over it

	double Round1(double V) { return FMath::RoundToDouble(V * 10.0) / 10.0; }

	TSharedRef<FJsonObject> SpotJson(const FGOMGSpot& S)
	{
		TSharedRef<FJsonObject> O = MakeShared<FJsonObject>();
		O->SetStringField(TEXT("n"), S.Name);
		O->SetNumberField(TEXT("x"), Round1(S.Feet.X));
		O->SetNumberField(TEXT("y"), Round1(S.Feet.Y));
		O->SetNumberField(TEXT("z"), Round1(S.Feet.Z));
		O->SetNumberField(TEXT("yaw"), FMath::RoundToDouble(S.Yaw));
		if (!S.Pin.IsEmpty()) O->SetStringField(TEXT("pin"), S.Pin);
		if (S.bFav) O->SetBoolField(TEXT("fav"), true);
		if (!S.Note.IsEmpty()) O->SetStringField(TEXT("note"), S.Note);
		return O;
	}

	bool ReadSpot(const TSharedPtr<FJsonValue>& V, FGOMGSpot& Out)
	{
		const TSharedPtr<FJsonObject>* O;
		if (!V.IsValid() || !V->TryGetObject(O)) return false;
		double X, Y, Z;
		if (!(*O)->TryGetNumberField(TEXT("x"), X) || !(*O)->TryGetNumberField(TEXT("y"), Y) || !(*O)->TryGetNumberField(TEXT("z"), Z)) return false;
		Out = FGOMGSpot();
		Out.Feet = FVector(X, Y, Z);
		double Yaw = 0;
		if ((*O)->TryGetNumberField(TEXT("yaw"), Yaw)) Out.Yaw = (float)Yaw;
		FString S;
		if ((*O)->TryGetStringField(TEXT("n"), S)) Out.Name = GOMGStore::Clean(S, GOMGStore::NameMax);
		if ((*O)->TryGetStringField(TEXT("pin"), S)) Out.Pin = S;
		if ((*O)->TryGetStringField(TEXT("note"), S)) Out.Note = GOMGStore::Clean(S, GOMGStore::NoteMax);
		(*O)->TryGetBoolField(TEXT("fav"), Out.bFav);
		return true;
	}

	void ReadSpots(const TSharedPtr<FJsonObject>& W, const TCHAR* Field, TArray<FGOMGSpot>& Out, int32 Max)
	{
		const TArray<TSharedPtr<FJsonValue>>* Arr;
		if (!W->TryGetArrayField(Field, Arr)) return;
		for (const TSharedPtr<FJsonValue>& V : *Arr)
		{
			FGOMGSpot S;
			if (Out.Num() < Max && ReadSpot(V, S))
			{
				if (S.Name.IsEmpty()) S.Name = FString::Printf(TEXT("Place %d"), Out.Num() + 1);
				Out.Add(MoveTemp(S));
			}
		}
	}

	void StringSet(const TSharedPtr<FJsonObject>& O, const TCHAR* Field, TSet<FString>& Out)
	{
		const TArray<TSharedPtr<FJsonValue>>* Arr;
		if (O->TryGetArrayField(Field, Arr))
			for (const TSharedPtr<FJsonValue>& V : *Arr) { FString S; if (V->TryGetString(S) && !S.IsEmpty()) Out.Add(S); }
	}

	TArray<TSharedPtr<FJsonValue>> SortedSet(const TSet<FString>& In)
	{
		TArray<FString> Ids = In.Array();
		Ids.Sort();
		TArray<TSharedPtr<FJsonValue>> Out;
		for (const FString& Id : Ids) Out.Add(MakeShared<FJsonValueString>(Id));
		return Out;
	}
}

namespace GOMGStore
{
	FGOMGSettings& Settings() { if (!GLoaded) Load(); return GSettings; }
	FGOMGWorld& World(const FString& Key) { if (!GLoaded) Load(); return GWorlds.FindOrAdd(Key); }
	bool IsLoaded() { return GLoaded; }
	FString Path() { return FPaths::ProjectSavedDir() / TEXT("GamesOMGTeleport") / TEXT("places.json"); }

	FString Clean(const FString& In, int32 Max)
	{
		FString Out;
		Out.Reserve(In.Len());
		bool bSpace = false;
		for (const TCHAR C : In)
		{
			if (FChar::IsWhitespace(C) || C < 32) { bSpace = !Out.IsEmpty(); continue; }
			if (bSpace) { Out.AppendChar(TEXT(' ')); bSpace = false; }
			Out.AppendChar(C);
		}
		if (Out.Len() > Max)
		{
			int32 Cut = Max;
			if (Out[Cut - 1] >= 0xD800 && Out[Cut - 1] <= 0xDBFF) --Cut;   // never half a letter (a UTF-16 pair)
			Out = Out.Left(Cut).TrimEnd();
		}
		return Out;
	}

	bool SameName(const FString& A, const FString& B) { return A.Equals(B, ESearchCase::IgnoreCase); }

	FGOMGSpot* AddSaved(FGOMGWorld& W, const FString& NameIn, const FVector& Feet, float Yaw, const FString& DefaultName)
	{
		if (W.Saved.Num() >= SavedMax) return nullptr;
		FString Name = Clean(NameIn, NameMax);
		if (Name.IsEmpty()) Name = Clean(DefaultName, NameMax);
		if (Name.IsEmpty()) Name = FString::Printf(TEXT("Place %d"), W.Saved.Num() + 1);
		FString Unique = Name;
		for (int32 N = 2; W.Saved.ContainsByPredicate([&](const FGOMGSpot& S) { return SameName(S.Name, Unique); }); ++N)
			Unique = FString::Printf(TEXT("%s %d"), *Name, N);
		FGOMGSpot& S = W.Saved.AddDefaulted_GetRef();
		S.Name = Unique;
		S.Feet = FVector(Round1(Feet.X), Round1(Feet.Y), Round1(Feet.Z));
		S.Yaw = Yaw;
		return &S;
	}

	FString Rename(FGOMGWorld& W, int32 Index, const FString& NameIn)
	{
		if (!W.Saved.IsValidIndex(Index)) return FString();
		const FString Name = Clean(NameIn, NameMax);
		if (Name.IsEmpty()) return FString();
		FString Unique = Name;
		for (int32 N = 2; ; ++N)
		{
			bool bTaken = false;
			for (int32 i = 0; i < W.Saved.Num(); ++i) if (i != Index && SameName(W.Saved[i].Name, Unique)) { bTaken = true; break; }
			if (!bTaken) break;
			Unique = FString::Printf(TEXT("%s %d"), *Name, N);
		}
		W.Saved[Index].Name = Unique;
		return Unique;
	}

	void AddRecent(FGOMGWorld& W, const FString& NameIn, const FVector& Feet, float Yaw)
	{
		const FString Name = Clean(NameIn, NameMax);
		W.Recent.RemoveAll([&](const FGOMGSpot& S)
		{
			return SameName(S.Name, Name) || (FVector::Dist2D(S.Feet, Feet) < 300.0 && FMath::Abs(S.Feet.Z - Feet.Z) < 300.0);
		});
		FGOMGSpot S;
		S.Name = Name;
		S.Feet = FVector(Round1(Feet.X), Round1(Feet.Y), Round1(Feet.Z));
		S.Yaw = Yaw;
		W.Recent.Insert(MoveTemp(S), 0);
		if (W.Recent.Num() > RecentMax) W.Recent.SetNum(RecentMax);
	}

	FString SetNote(FGOMGWorld& W, const FString& Pin, const FString& NoteIn)
	{
		const FString Note = Clean(NoteIn, NoteMax);
		if (Pin.IsEmpty()) return FString();
		if (Note.IsEmpty()) W.Notes.Remove(Pin); else W.Notes.Add(Pin, Note);
		return Note;
	}

	FString ToJson()
	{
		TSharedRef<FJsonObject> Root = MakeShared<FJsonObject>();
		Root->SetNumberField(TEXT("version"), FileVersion);
		TSharedRef<FJsonObject> S = MakeShared<FJsonObject>();
		S->SetStringField(TEXT("panelKey"), GSettings.PanelKey.ToString());
		S->SetStringField(TEXT("backKey"), GSettings.BackKey.ToString());
		S->SetBoolField(TEXT("closeAfterTeleport"), GSettings.bCloseAfterTeleport);
		S->SetBoolField(TEXT("clickTeleports"), GSettings.bClickTeleports);
		S->SetNumberField(TEXT("textSize"), GSettings.TextSize);
		S->SetStringField(TEXT("side"), GSettings.bLeft ? TEXT("left") : TEXT("right"));
		S->SetNumberField(TEXT("opacity"), GSettings.Opacity);
		S->SetBoolField(TEXT("hideFound"), GSettings.bHideFound);
		S->SetStringField(TEXT("sort"), GSettings.bByName ? TEXT("az") : TEXT("near"));
		S->SetArrayField(TEXT("flipped"), SortedSet(GSettings.Flipped));
		S->SetArrayField(TEXT("on"), SortedSet(GSettings.On));
		Root->SetObjectField(TEXT("settings"), S);
		TSharedRef<FJsonObject> Worlds = MakeShared<FJsonObject>();
		TArray<FString> Keys;
		GWorlds.GetKeys(Keys);
		Keys.Sort();
		for (const FString& Key : Keys)
		{
			const FGOMGWorld& W = GWorlds[Key];
			if (W.Found.Num() == 0 && !W.bHasBack && W.Saved.Num() == 0 && W.Recent.Num() == 0 && W.Notes.Num() == 0) continue;
			TSharedRef<FJsonObject> O = MakeShared<FJsonObject>();
			O->SetArrayField(TEXT("found"), SortedSet(W.Found));
			if (W.bHasBack)
			{
				TSharedRef<FJsonObject> B = MakeShared<FJsonObject>();
				B->SetNumberField(TEXT("x"), FMath::RoundToDouble(W.Back.X));
				B->SetNumberField(TEXT("y"), FMath::RoundToDouble(W.Back.Y));
				B->SetNumberField(TEXT("z"), FMath::RoundToDouble(W.Back.Z));
				B->SetNumberField(TEXT("yaw"), FMath::RoundToDouble(W.BackYaw));
				if (!W.BackName.IsEmpty()) B->SetStringField(TEXT("n"), W.BackName);
				O->SetObjectField(TEXT("back"), B);
			}
			TArray<TSharedPtr<FJsonValue>> Saved, Recent;
			for (const FGOMGSpot& Sp : W.Saved) Saved.Add(MakeShared<FJsonValueObject>(SpotJson(Sp)));
			for (const FGOMGSpot& Sp : W.Recent) Recent.Add(MakeShared<FJsonValueObject>(SpotJson(Sp)));
			O->SetArrayField(TEXT("saved"), Saved);
			O->SetArrayField(TEXT("recent"), Recent);
			if (W.Notes.Num() > 0)
			{
				TSharedRef<FJsonObject> N = MakeShared<FJsonObject>();
				TArray<FString> Pins;
				W.Notes.GetKeys(Pins);
				Pins.Sort();
				for (const FString& Pin : Pins) N->SetStringField(Pin, W.Notes[Pin]);
				O->SetObjectField(TEXT("notes"), N);
			}
			Worlds->SetObjectField(Key, O);
		}
		Root->SetObjectField(TEXT("worlds"), Worlds);
		FString Out;
		const TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&Out);
		FJsonSerializer::Serialize(Root, Writer);
		return Out;
	}

	bool FromJson(const FString& Text, int32& Version)
	{
		Version = 0;
		TSharedPtr<FJsonObject> Root;
		if (!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Text), Root) || !Root.IsValid()) return false;
		double V = 0;
		if (!Root->TryGetNumberField(TEXT("version"), V) || V < 1) return false;
		Version = (int32)V;
		GSettings = FGOMGSettings();
		GWorlds.Reset();
		const TSharedPtr<FJsonObject>* S;
		if (Root->TryGetObjectField(TEXT("settings"), S))
		{
			FString K;
			if ((*S)->TryGetStringField(TEXT("panelKey"), K) && FKey(*K).IsValid()) GSettings.PanelKey = FKey(*K);
			if ((*S)->TryGetStringField(TEXT("backKey"), K) && FKey(*K).IsValid()) GSettings.BackKey = FKey(*K);
			(*S)->TryGetBoolField(TEXT("closeAfterTeleport"), GSettings.bCloseAfterTeleport);
			(*S)->TryGetBoolField(TEXT("clickTeleports"), GSettings.bClickTeleports);
			double N;
			if ((*S)->TryGetNumberField(TEXT("textSize"), N)) GSettings.TextSize = FMath::Clamp((int32)N, TextMin, TextMax);
			if ((*S)->TryGetNumberField(TEXT("opacity"), N)) GSettings.Opacity = FMath::Clamp((int32)N, OpacityMin, 100);
			if ((*S)->TryGetStringField(TEXT("side"), K)) GSettings.bLeft = K == TEXT("left");
			(*S)->TryGetBoolField(TEXT("hideFound"), GSettings.bHideFound);
			if ((*S)->TryGetStringField(TEXT("sort"), K)) GSettings.bByName = K == TEXT("az");
			StringSet(*S, TEXT("flipped"), GSettings.Flipped);   // 1.0.0's "collapsed" keys name a tree that is gone: left out
			StringSet(*S, TEXT("on"), GSettings.On);
		}
		const TSharedPtr<FJsonObject>* Worlds;
		if (Root->TryGetObjectField(TEXT("worlds"), Worlds))
		{
			for (const auto& Pair : (*Worlds)->Values)
			{
				const TSharedPtr<FJsonObject>* WO;
				if (!Pair.Value.IsValid() || !Pair.Value->TryGetObject(WO)) continue;
				FGOMGWorld& W = GWorlds.Add(Pair.Key);
				StringSet(*WO, TEXT("found"), W.Found);
				const TArray<TSharedPtr<FJsonValue>>* Arr;
				const TSharedPtr<FJsonObject>* B;
				if ((*WO)->TryGetArrayField(TEXT("back"), Arr) && Arr->Num() == 4)   // 1.0.0: [x, y, z, yaw]
				{
					W.bHasBack = true;
					W.Back = FVector((*Arr)[0]->AsNumber(), (*Arr)[1]->AsNumber(), (*Arr)[2]->AsNumber());
					W.BackYaw = (*Arr)[3]->AsNumber();
				}
				else if ((*WO)->TryGetObjectField(TEXT("back"), B))
				{
					double X, Y, Z, Yaw = 0;
					if ((*B)->TryGetNumberField(TEXT("x"), X) && (*B)->TryGetNumberField(TEXT("y"), Y) && (*B)->TryGetNumberField(TEXT("z"), Z))
					{
						W.bHasBack = true;
						W.Back = FVector(X, Y, Z);
						(*B)->TryGetNumberField(TEXT("yaw"), Yaw);
						W.BackYaw = (float)Yaw;
						(*B)->TryGetStringField(TEXT("n"), W.BackName);
					}
				}
				ReadSpots(*WO, TEXT("saved"), W.Saved, SavedMax);
				ReadSpots(*WO, TEXT("recent"), W.Recent, RecentMax);
				const TSharedPtr<FJsonObject>* N;
				if ((*WO)->TryGetObjectField(TEXT("notes"), N))
					for (const auto& NP : (*N)->Values) { FString T; if (NP.Value.IsValid() && NP.Value->TryGetString(T)) SetNote(W, NP.Key, T); }
			}
		}
		return true;
	}

	void ResetForTest()
	{
		GSettings = FGOMGSettings();
		GWorlds.Reset();
		GLoaded = true;
		GReadVersion = 0;
		GBlocked = TEXT("a test is running");   // never write a player's file from a test
	}

	void Load()
	{
		if (GLoaded) return;
		GLoaded = true;
		IFileManager& FM = IFileManager::Get();
		const FString Tmp = Path() + TEXT(".tmp");
		// a write cut short between removing the old file and renaming the new one leaves only places.json.tmp: put it back
		if (!FM.FileExists(*Path()) && FM.FileExists(*Tmp) && FM.Move(*Path(), *Tmp, false, true))
			UE_LOG(LogGamesOMGTeleport, Warning, TEXT("places.json was missing; places.json.tmp put back"));
		if (!FM.FileExists(*Path()))
		{
			Save();   // the defaults, written once, so the key settings are there to find
			return;
		}
		FString Text;
		if (!FFileHelper::LoadFileToString(Text, *Path()))
		{
			// there, but not readable now (another program has it open?): never write defaults over it
			GBlocked = TEXT("your places file could not be opened (another program may have it open)");
			UE_LOG(LogGamesOMGTeleport, Warning, TEXT("places.json exists but could not be read: nothing written this session"));
			return;
		}
		int32 Version = 0;
		if (FromJson(Text, Version))
		{
			GReadVersion = Version;
			if (Version > FileVersion)
			{
				GBlocked = TEXT("your places file is from a newer version of GamesOMG Teleport");
				UE_LOG(LogGamesOMGTeleport, Warning, TEXT("places.json is version %d, newer than this mod's %d: read only this session"), Version, FileVersion);
			}
			return;
		}
		// never throw a player's file away: keep it beside the new one; if even that fails, write nothing over it
		GSettings = FGOMGSettings();
		GWorlds.Reset();
		// its own bytes, under a name no later copy takes
		const FString Bad = Path() + TEXT(".bad-") + FDateTime::Now().ToString(TEXT("%Y%m%d-%H%M%S"));
		if (IFileManager::Get().Copy(*Bad, *Path()) == COPY_OK)
		{
			UE_LOG(LogGamesOMGTeleport, Warning, TEXT("places.json would not read; kept as %s"), *FPaths::GetCleanFilename(Bad));
		}
		else
		{
			GBlocked = TEXT("your places file could not be read or copied aside");
			UE_LOG(LogGamesOMGTeleport, Warning, TEXT("places.json would not read and could not be copied aside: nothing written this session"));
		}
	}

	bool Save(FString* Why)
	{
		if (!GLoaded) Load();
		if (!GBlocked.IsEmpty()) { if (Why) *Why = GBlocked; return false; }
		if (GReadVersion > 0 && GReadVersion < FileVersion && !GBackedUpOld)
		{
			// 1.0.0's file, as it was, before 1.1.0 writes over it (a player who goes back to 1.0.0 can put it back)
			const FString Old = FPaths::GetPath(Path()) / FString::Printf(TEXT("places.v%d.json"), GReadVersion);
			if (!IFileManager::Get().FileExists(*Old) && IFileManager::Get().Copy(*Old, *Path()) != COPY_OK)
			{
				if (Why) *Why = TEXT("the old places file could not be backed up first");
				return false;
			}
			GBackedUpOld = true;
		}
		const FString Tmp = Path() + TEXT(".tmp");
		if (!FFileHelper::SaveStringToFile(ToJson(), *Tmp, FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM) || !IFileManager::Get().Move(*Path(), *Tmp, true, true))
		{
			UE_LOG(LogGamesOMGTeleport, Warning, TEXT("could not save %s"), *Path());
			if (Why) *Why = TEXT("the Saved folder may be read-only");
			return false;
		}
		return true;
	}
}

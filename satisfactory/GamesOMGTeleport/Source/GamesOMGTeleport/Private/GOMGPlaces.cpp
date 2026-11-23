#include "GOMGPlaces.h"

#include "Resources/FGItemDescriptor.h"
#include "Resources/FGResourceNode.h"
#include "FGSchematic.h"
#include "UObject/UnrealType.h"
#include "Internationalization/Culture.h"
#include "Internationalization/Internationalization.h"
#include "Internationalization/TextLocalizationManager.h"

#include "GOMGPlaces.inl"

namespace
{
	// The panel's headings. English: the GamesOMG map's own headings. Any other language: the game's own word for it, from the
	// game's string tables (namespace, key), so the heading is in the player's language. Resource wells have no such word.
	struct FGroupLabel { const TCHAR* English; const TCHAR* Namespace; const TCHAR* Key; };
	const FGroupLabel GROUP_LABEL[] = {
		{ TEXT("Alien artifacts"), TEXT("MAM_Data"), TEXT("Categories/AlienTechnology") },          // "Alien Technology"
		{ TEXT("Hard drives & tapes"), TEXT("MAM_Data"), TEXT("Categories/HardDrive") },             // "Hard Drives"
		{ TEXT("Power Slugs"), TEXT("MAM_Data"), TEXT("Categories/PowerSlugs") },                     // "Power Slugs"
		{ TEXT("Resource nodes"), TEXT("Map_UI"), TEXT("MapMenu/Filter/Categories/Resources") },     // the in-game map's "Resources"
		{ TEXT("Resource wells"), nullptr, nullptr },
		{ TEXT("Starting areas"), TEXT("Options_Data"), TEXT("Multiplayer/StartingArea") },          // "Starting Area"
	};
	const TCHAR* const RESOURCE_NODE_CLASS = TEXT("/Game/FactoryGame/Resource/BP_ResourceNode.BP_ResourceNode_C");

	// weak: leaving a world can unload a Blueprint class (descriptors, map areas). 1.0.0 kept raw pointers here and crashed
	// in UFGItemDescriptor::GetItemName on the next F9 after solo world → main menu → join a server (2026-11-23 sitting).
	TMap<FString, TWeakObjectPtr<UClass>> GClassCache;
	TSet<FString> GClassMissing;
	TMap<FString, int32> GIdIndex;
	FText GPurity[3];
	bool GPurityResolved = false;

	UClass* LoadClassCached(const TCHAR* Path)
	{
		if (!Path || !*Path) return nullptr;
		if (const TWeakObjectPtr<UClass>* Found = GClassCache.Find(Path))
			if (UClass* Cached = Found->Get()) return Cached;
		if (GClassMissing.Contains(Path)) return nullptr;
		UClass* Cls = LoadObject<UClass>(nullptr, Path);
		if (!Cls) { UE_LOG(LogTemp, Warning, TEXT("GamesOMG Teleport: no class at %s"), Path); GClassMissing.Add(Path); return nullptr; }
		GClassCache.Add(Path, Cls);
		return Cls;
	}

	FText ReadTextProperty(UClass* Cls, const TCHAR* Prop)
	{
		if (!Cls) return FText();
		const FTextProperty* P = FindFProperty<FTextProperty>(Cls, Prop);
		if (!P) return FText();
		return P->GetPropertyValue_InContainer(Cls->GetDefaultObject());
	}

	void ResolvePurity()
	{
		if (GPurityResolved) return;
		GPurityResolved = true;
		GPurity[0] = FText::FromString(TEXT("Impure"));
		GPurity[1] = FText::FromString(TEXT("Normal"));
		GPurity[2] = FText::FromString(TEXT("Pure"));
		UClass* Node = LoadClassCached(RESOURCE_NODE_CLASS);
		if (!Node) return;
		const FArrayProperty* P = FindFProperty<FArrayProperty>(Node, TEXT("mPurityTextArray"));
		if (!P) return;
		const TArray<FPurityTextPair>* Pairs = P->ContainerPtrToValuePtr<TArray<FPurityTextPair>>(Node->GetDefaultObject());
		for (const FPurityTextPair& Pair : *Pairs)
		{
			const int32 K = (int32)Pair.Purity.GetValue();
			if (K >= 0 && K < 3 && !Pair.Text.IsEmpty()) GPurity[K] = Pair.Text;
		}
	}

	// The game's purity words are rich text for its own tooltips: "<Bold>(Pure)</>" in English, the same shape in every language.
	// Keep the word: drop the tags, then one pair of brackets around it (ASCII or full-width, for CJK).
	FString PlainWord(const FString& In)
	{
		FString Out;
		Out.Reserve(In.Len());
		bool bTag = false;
		for (const TCHAR C : In)
		{
			if (C == TEXT('<')) bTag = true;
			else if (C == TEXT('>')) bTag = false;
			else if (!bTag) Out.AppendChar(C);
		}
		Out.TrimStartAndEndInline();
		if (Out.Len() >= 2 && ((Out[0] == TEXT('(') && Out[Out.Len() - 1] == TEXT(')')) || (Out[0] == 0xFF08 && Out[Out.Len() - 1] == 0xFF09)))
			Out = Out.Mid(1, Out.Len() - 2).TrimStartAndEnd();
		return Out;
	}

	// read at display time (ToString follows the game's current language), so a language switch mid-game shows up at once
	FText Purity(int32 K) { ResolvePurity(); return FText::FromString(PlainWord(GPurity[FMath::Clamp(K, 0, 2)].ToString())); }
}

namespace GOMGPlaces
{
	int32 Num() { return UE_ARRAY_COUNT(GOMG_PLACES); }
	const FGOMGPlaceDef& Get(int32 Index) { return GOMG_PLACES[Index]; }
	int32 NumCats() { return UE_ARRAY_COUNT(GOMG_CATS); }
	const FGOMGCatDef& Cat(int32 CatIndex) { return GOMG_CATS[CatIndex]; }
	int32 NumGroups() { return UE_ARRAY_COUNT(GOMG_GROUPS); }
	uint32 ListHash() { return GOMG_LIST_HASH; }

	FString IdOf(int32 Index) { return FString(ANSI_TO_TCHAR(GOMG_PLACES[Index].Id)); }

	int32 FindById(const FString& Id)
	{
		if (GIdIndex.Num() == 0)
			for (int32 i = 0; i < Num(); ++i) GIdIndex.Add(IdOf(i), i);
		const int32* Found = GIdIndex.Find(Id.ToLower());
		return Found ? *Found : INDEX_NONE;
	}

	FGuid GuidOf(int32 Index)
	{
		const FGOMGPlaceDef& P = GOMG_PLACES[Index];
		return FGuid(P.Guid[0], P.Guid[1], P.Guid[2], P.Guid[3]);
	}

	FVector LocationOf(int32 Index) { const FGOMGPlaceDef& P = GOMG_PLACES[Index]; return FVector(P.X, P.Y, P.Z); }
	bool HasCheckedLanding(int32 Index) { return GOMG_PLACES[Index].LandKind == 1; }
	bool HasNoLanding(int32 Index) { return GOMG_PLACES[Index].LandKind == 2; }
	FVector LandingOf(int32 Index) { const FGOMGPlaceDef& P = GOMG_PLACES[Index]; return FVector(P.LX, P.LY, P.LZ); }
	EGOMGTick TickOf(int32 Index) { return GOMG_CATS[GOMG_PLACES[Index].Cat].Tick; }

	FText GameText(const TCHAR* Namespace, const TCHAR* Key, const TCHAR* English, const TCHAR* WrongIn)
	{
		const FString Lang = FInternationalization::Get().GetCurrentLanguage()->GetTwoLetterISOLanguageName();
		TArray<FString> Wrong;
		if (WrongIn) FString(WrongIn).ParseIntoArray(Wrong, TEXT(","));
		if (Namespace && Key && Lang != TEXT("en") && !Wrong.Contains(Lang))
		{
			const auto Found = FTextLocalizationManager::Get().FindDisplayString(FTextKey(Namespace), FTextKey(Key));
			if (Found.IsValid() && !Found->IsEmpty()) return FText::FromString(*Found);
		}
		return FText::FromString(English);
	}

	FText GroupName(int32 Group)
	{
		if (Group < 0 || Group >= (int32)UE_ARRAY_COUNT(GROUP_LABEL)) return FText();
		const FGroupLabel& L = GROUP_LABEL[Group];
		return GameText(L.Namespace, L.Key, L.English);
	}

	FText CatName(int32 CatIndex)
	{
		const FGOMGCatDef& C = GOMG_CATS[CatIndex];
		if (FCString::Strcmp(C.Key, TEXT("tapes")) == 0) return FText::FromString(TEXT("Boombox tapes & cosmetics"));
		if (FCString::Strcmp(C.Key, TEXT("start")) == 0) return GroupName(5);
		if (UClass* Cls = LoadClassCached(C.NameClass))
		{
			const FText Name = UFGItemDescriptor::GetItemName(Cls);
			if (!Name.IsEmpty()) return Name;
		}
		return FText::FromString(C.Key);
	}

	FText RegionName(int32 Region)
	{
		if (Region < 0 || Region >= (int32)UE_ARRAY_COUNT(GOMG_REGIONS)) return FText();
		const FText Name = ReadTextProperty(LoadClassCached(GOMG_REGIONS[Region].AreaClass), TEXT("mDisplayName"));
		return Name.IsEmpty() ? FText::FromString(GOMG_REGIONS[Region].English) : Name;
	}

	UClass* PathClass(int32 PathIndex)
	{
		if (PathIndex < 0 || PathIndex >= (int32)UE_ARRAY_COUNT(GOMG_PATHS)) return nullptr;
		return LoadClassCached(GOMG_PATHS[PathIndex]);
	}

	FText PlaceDetail(int32 Index)
	{
		const FGOMGPlaceDef& P = GOMG_PLACES[Index];
		const FString Key = GOMG_CATS[P.Cat].Key;
		if (Key == TEXT("pod"))
		{
			if (P.Aux0 == 1)
			{
				UClass* Item = PathClass(P.Aux1);
				return FText::Format(FText::FromString(TEXT("{0} × {1}")), FText::AsNumber(P.Aux2),
					Item ? UFGItemDescriptor::GetItemName(Item) : FText::FromString(TEXT("?")));
			}
			if (P.Aux0 == 2)
			{
				FNumberFormattingOptions O; O.MaximumFractionalDigits = 1;
				return FText::Format(FText::FromString(TEXT("{0} MW")), FText::AsNumber(P.Aux2 / 10.0, &O));
			}
			return FText::FromString(TEXT("Opens free"));
		}
		if (Key == TEXT("tapes"))
		{
			UClass* Sch = PathClass(P.Aux1);
			return Sch ? UFGSchematic::GetSchematicDisplayName(Sch) : FText();
		}
		if (GOMG_CATS[P.Cat].Group == 3) return Purity(P.Aux0);   // nodes + geysers
		if (GOMG_CATS[P.Cat].Group == 4)                            // wells: the core; its nodes by purity
		{
			TArray<FText> Parts;
			const int32 Count[3] = { P.Aux0, P.Aux1, P.Aux2 };
			for (int32 K = 2; K >= 0; --K)
				if (Count[K] > 0)
					Parts.Add(FText::Format(FText::FromString(TEXT("{0} {1}")), FText::AsNumber(Count[K]), Purity(K)));
			return FText::Join(FText::FromString(TEXT(" · ")), Parts);
		}
		return FText();
	}

	FText PlaceSearchText(int32 Index)
	{
		const FGOMGPlaceDef& P = GOMG_PLACES[Index];
		const FString S = (CatName(P.Cat).ToString() + TEXT(" ") + RegionName(P.Region).ToString() + TEXT(" ")
			+ PlaceDetail(Index).ToString() + TEXT(" ") + IdOf(Index)).ToLower();
		return FText::FromString(S);
	}

	void WarmNames()
	{
		ResolvePurity();
		for (int32 C = 0; C < NumCats(); ++C) CatName(C);
		for (int32 R = 0; R < (int32)UE_ARRAY_COUNT(GOMG_REGIONS); ++R) RegionName(R);
		for (int32 K = 0; K < (int32)UE_ARRAY_COUNT(GOMG_PATHS); ++K) PathClass(K);
	}
}

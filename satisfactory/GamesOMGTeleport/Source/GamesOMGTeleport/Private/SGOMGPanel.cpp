#include "SGOMGPanel.h"

#include "GOMGClient.h"
#include "GOMGPlaces.h"

#include "Brushes/SlateColorBrush.h"
#include "Brushes/SlateNoResource.h"
#include "Brushes/SlateRoundedBoxBrush.h"
#include "Engine/Font.h"
#include "Framework/Application/SlateApplication.h"
#include "Internationalization/Culture.h"
#include "Internationalization/Internationalization.h"
#include "Styling/CoreStyle.h"
#include "Widgets/Input/SButton.h"
#include "Widgets/Input/SEditableTextBox.h"
#include "Widgets/Layout/SBorder.h"
#include "Widgets/Layout/SBox.h"
#include "Widgets/Layout/SSpacer.h"
#include "Widgets/SOverlay.h"
#include "Widgets/Text/STextBlock.h"
#include "Widgets/Views/STableRow.h"

namespace
{
	// GamesOMG's colours, the same as the other GamesOMG Teleport mods: orange for the main action, blue for focus
	FLinearColor Hex(uint32 Rgb, float A = 1.f)
	{
		return FLinearColor(FColor((Rgb >> 16) & 255, (Rgb >> 8) & 255, Rgb & 255)).CopyWithNewOpacity(A);
	}
	const FLinearColor Orange = Hex(0xf08000), Blue = Hex(0x3be3ff);
	const FLinearColor Ink = Hex(0xeef1f5), Muted = Hex(0xbdc6d2), Faint = Hex(0x8f99a8);
	const FLinearColor PanelBg = Hex(0x0e1117, 0.96f), Surface = Hex(0x171b23), Raised = Hex(0x20262f), Line = Hex(0x2b3240);
	const FLinearColor Good = Hex(0x4fd08a), BadText = Hex(0xff8a80);

	struct FTheme
	{
		FSlateRoundedBoxBrush Panel{ PanelBg, 10.f, Line, 1.f };
		FSlateRoundedBoxBrush Toast{ Hex(0x171b23, 0.97f), 8.f, Line, 1.f };
		FSlateRoundedBoxBrush Field{ Surface, 6.f, Line, 1.f };
		FSlateRoundedBoxBrush FieldFocus{ Surface, 6.f, Blue, 1.f };
		FSlateRoundedBoxBrush Heading{ Surface, 6.f };
		FSlateRoundedBoxBrush HeadingHover{ Raised, 6.f };
		FSlateRoundedBoxBrush RowHover{ Raised, 6.f };
		FSlateRoundedBoxBrush RowSelected{ Raised, 6.f, Blue, 1.f };
		FSlateRoundedBoxBrush GoPill{ Orange, 5.f };
		FSlateRoundedBoxBrush GoPillHover{ Hex(0xff9a2e), 5.f };
		FSlateRoundedBoxBrush Button{ Raised, 6.f, Line, 1.f };
		FSlateRoundedBoxBrush ButtonHover{ Hex(0x2a313c), 6.f, Blue, 1.f };
		FSlateRoundedBoxBrush BoxOff{ FLinearColor::Transparent, 4.f, Faint, 1.5f };
		FSlateRoundedBoxBrush BoxOffHover{ FLinearColor::Transparent, 4.f, Blue, 1.5f };
		FSlateRoundedBoxBrush BoxMine{ Blue, 4.f };
		FSlateRoundedBoxBrush BoxGame{ Good, 4.f };
		FSlateNoResource None;
		FButtonStyle Flat, HeadingBtn, Btn, Go, Check;
		FTableRowStyle Row;
		FEditableTextBoxStyle Input;

		FTheme()
		{
			Flat = FButtonStyle().SetNormal(None).SetHovered(RowHover).SetPressed(RowHover).SetDisabled(None)
				.SetNormalPadding(FMargin(0)).SetPressedPadding(FMargin(0));
			HeadingBtn = FButtonStyle().SetNormal(Heading).SetHovered(HeadingHover).SetPressed(HeadingHover).SetDisabled(Heading)
				.SetNormalPadding(FMargin(0)).SetPressedPadding(FMargin(0));
			Btn = FButtonStyle().SetNormal(Button).SetHovered(ButtonHover).SetPressed(ButtonHover).SetDisabled(Heading)
				.SetNormalPadding(FMargin(0)).SetPressedPadding(FMargin(0));
			Go = FButtonStyle().SetNormal(GoPill).SetHovered(GoPillHover).SetPressed(GoPillHover).SetDisabled(None)
				.SetNormalPadding(FMargin(0)).SetPressedPadding(FMargin(0));
			Check = FButtonStyle().SetNormal(BoxOff).SetHovered(BoxOffHover).SetPressed(BoxOffHover).SetDisabled(None)
				.SetNormalPadding(FMargin(0)).SetPressedPadding(FMargin(0));
			Row = FCoreStyle::Get().GetWidgetStyle<FTableRowStyle>("TableView.Row");
			Row.SetEvenRowBackgroundBrush(None).SetOddRowBackgroundBrush(None)
				.SetEvenRowBackgroundHoveredBrush(None).SetOddRowBackgroundHoveredBrush(None)
				.SetActiveBrush(RowSelected).SetActiveHoveredBrush(RowSelected)
				.SetInactiveBrush(RowSelected).SetInactiveHoveredBrush(RowSelected)
				.SetSelectorFocusedBrush(None).SetActiveHighlightedBrush(None).SetInactiveHighlightedBrush(None)
				.SetTextColor(Ink).SetSelectedTextColor(Ink);
			Input = FCoreStyle::Get().GetWidgetStyle<FEditableTextBoxStyle>("NormalEditableTextBox");
			Input.SetBackgroundImageNormal(Field).SetBackgroundImageHovered(Field).SetBackgroundImageFocused(FieldFocus)
				.SetBackgroundImageReadOnly(Field).SetForegroundColor(FSlateColor(Ink)).SetFocusedForegroundColor(FSlateColor(Ink))
				.SetPadding(FMargin(10, 7));
		}
	};
	FTheme& Theme() { static FTheme* T = new FTheme(); return *T; }

	// the game's own font (Noto/Heebo/OpenSans with CJK, Arabic, Thai and Bengali fallbacks), so every language it ships draws
	FSlateFontInfo Font(int32 Size, bool bBold)
	{
		static UObject* GameFont = nullptr;
		static bool bTried = false;
		if (!bTried)
		{
			bTried = true;
			GameFont = LoadObject<UFont>(nullptr, TEXT("/Game/FactoryGame/Interface/Font/DescriptionText.DescriptionText"));
			if (GameFont) GameFont->AddToRoot();
		}
		if (GameFont) return FSlateFontInfo(GameFont, Size, bBold ? FName(TEXT("Bold")) : FName(TEXT("Regular")));
		return FCoreStyle::GetDefaultFontStyle(bBold ? "Bold" : "Regular", Size);
	}

	FText Distance(double Cm)
	{
		if (Cm < 0) return FText();
		FNumberFormattingOptions O; O.MaximumFractionalDigits = 1; O.MinimumFractionalDigits = 0;
		if (Cm < 100000.0) return FText::Format(FText::FromString(TEXT("{0} m")), FText::AsNumber(FMath::RoundToInt(Cm / 100.0)));
		return FText::Format(FText::FromString(TEXT("{0} km")), FText::AsNumber(Cm / 100000.0, &O));
	}

	bool IsTapes(int32 Place) { return FCString::Strcmp(GOMGPlaces::Cat(GOMGPlaces::Get(Place).Cat).Key, TEXT("tapes")) == 0; }
}

void SGOMGRoot::Construct(const FArguments& InArgs)
{
	FTheme& T = Theme();
	SetVisibility(EVisibility::SelfHitTestInvisible);
	ChildSlot
	[
		SNew(SOverlay)
		.Visibility(EVisibility::SelfHitTestInvisible)
		+ SOverlay::Slot().HAlign(HAlign_Center).VAlign(VAlign_Top).Padding(FMargin(0, 96, 0, 0))
		[
			SAssignNew(ToastBox, SBorder)
			.Visibility(EVisibility::Collapsed)
			.BorderImage(&T.Toast)
			.Padding(FMargin(16, 10))
			[
				SAssignNew(ToastText, STextBlock).Font(Font(15, false)).ColorAndOpacity(Ink)
			]
		]
		+ SOverlay::Slot().HAlign(HAlign_Right).VAlign(VAlign_Fill).Padding(FMargin(0, 64, 18, 64))
		[
			SAssignNew(Panel, SBox)
			.Visibility(EVisibility::Collapsed)
			.WidthOverride(470.f)
			[
				SNew(SBorder)
				.BorderImage(&T.Panel)
				.Padding(FMargin(14, 12))
				[
					SNew(SVerticalBox)
					// header: the brand, small; close
					+ SVerticalBox::Slot().AutoHeight().Padding(FMargin(2, 0, 0, 10))
					[
						SNew(SHorizontalBox)
						+ SHorizontalBox::Slot().AutoWidth().VAlign(VAlign_Center)
						[ SNew(STextBlock).Text(FText::FromString(TEXT("GamesOMG"))).Font(Font(17, true)).ColorAndOpacity(Orange) ]
						+ SHorizontalBox::Slot().AutoWidth().VAlign(VAlign_Center).Padding(FMargin(6, 0, 0, 0))
						[ SNew(STextBlock).Text(FText::FromString(TEXT("Teleport"))).Font(Font(17, true)).ColorAndOpacity(Ink) ]
						+ SHorizontalBox::Slot().FillWidth(1.f) [ SNew(SSpacer) ]
						+ SHorizontalBox::Slot().AutoWidth().VAlign(VAlign_Center)
						[
							SNew(SButton).ButtonStyle(&T.Flat).ContentPadding(FMargin(10, 2))
							// the game's words here are read each time they're drawn, so they follow a language switch mid-game
							.ToolTipText_Lambda([]() { return FText::Format(FText::FromString(TEXT("{0} (Esc)")),
								GOMGPlaces::GameText(TEXT("Gamepads_UI"), TEXT("GamepadHints/aCommon/Close"), TEXT("Close"))); })
							.OnClicked_Lambda([]() { GOMGClient::ClosePanel(); return FReply::Handled(); })
							[ SNew(STextBlock).Text(FText::FromString(TEXT("×"))).Font(Font(20, false)).ColorAndOpacity(Muted) ]
						]
					]
					// search, or a /tp line
					+ SVerticalBox::Slot().AutoHeight().Padding(FMargin(0, 0, 0, 8))
					[
						SAssignNew(Search, SEditableTextBox)
						.Style(&T.Input)
						.Font(Font(15, false))
						// the game's own "Search..." in the player's language, then what else the box takes (the same in every language)
						.HintText_Lambda([]() { return FText::Format(FText::FromString(TEXT("{0}   /tp X Y Z")),
							GOMGPlaces::GameText(TEXT("General_UI"), TEXT("GenericUIStrings/Textbox/Search/TextboxHint"), TEXT("Search, or paste"))); })
						.OnTextChanged(this, &SGOMGRoot::OnSearchChanged)
					]
					// Back, and how much of this save is collected
					+ SVerticalBox::Slot().AutoHeight().Padding(FMargin(0, 0, 0, 8))
					[
						SNew(SHorizontalBox)
						+ SHorizontalBox::Slot().AutoWidth()
						[
							SNew(SButton).ButtonStyle(&T.Btn).ContentPadding(FMargin(12, 5))
							.IsEnabled_Lambda([]() { return GOMGClient::HasBack(); })
							.ToolTipText_Lambda([]() { return FText::Format(FText::FromString(TEXT("Back to where you stood before your last teleport ({0}). Press it again to swap back.")), GOMGClient::BackKey().GetDisplayName()); })
							.OnClicked_Lambda([]() { GOMGClient::GoBack(); return FReply::Handled(); })
							[ SNew(STextBlock).Text_Lambda([]() { return GOMGPlaces::GameText(TEXT("Menus_UI"), TEXT("AGS/Gameplay/GiveItems/Back/Button"), TEXT("Back"), TEXT("sv")); }).Font(Font(14, true)).ColorAndOpacity(Ink) ]
						]
						+ SHorizontalBox::Slot().FillWidth(1.f).VAlign(VAlign_Center).HAlign(HAlign_Right)
						[
							SNew(STextBlock).Font(Font(13, false)).ColorAndOpacity(Muted)
							.Text_Lambda([this]() { return FooterText(); })
						]
					]
					+ SVerticalBox::Slot().FillHeight(1.f)
					[
						SAssignNew(List, SListView<TSharedPtr<FGOMGRow>>)
						.ListItemsSource(&Rows)
						.SelectionMode(ESelectionMode::Single)
						.OnGenerateRow(this, &SGOMGRoot::MakeRow)
					]
					+ SVerticalBox::Slot().AutoHeight().Padding(FMargin(2, 8, 0, 0))
					[
						SNew(STextBlock).Font(Font(12, false)).ColorAndOpacity(Faint).AutoWrapText(true)
						.Text_Lambda([]() {
							return FText::Format(FText::FromString(TEXT("Click a place to go · Up/Down + Enter · {0} closes · {1} goes back")),
								GOMGClient::PanelKey().GetDisplayName(), GOMGClient::BackKey().GetDisplayName());
						})
					]
				]
			]
		]
	];
}

TSharedPtr<SWidget> SGOMGRoot::GetFocusTarget() const { return Search; }

void SGOMGRoot::SetPanelOpen(bool bOpen)
{
	Panel->SetVisibility(bOpen ? EVisibility::Visible : EVisibility::Collapsed);
	if (bOpen)
	{
		Rebuild();
		if (FSlateApplication::IsInitialized()) FSlateApplication::Get().SetKeyboardFocus(Search, EFocusCause::SetDirectly);
	}
}

void SGOMGRoot::PrefillLine(const FString& TpLine)
{
	Search->SetText(FText::FromString(TpLine));
	Filter.Reset();   // a line is not a search
	Rebuild();
}

void SGOMGRoot::OnSearchChanged(const FText& Text)
{
	const FString S = Text.ToString().TrimStartAndEnd();
	Filter = GOMGClient::IsLine(S) ? FString() : S.ToLower();
	Rebuild();
}

FText SGOMGRoot::FooterText() const
{
	if (!GOMGClient::HasGameState())
		return GOMGClient::IsServerMissingMod() ? FText::FromString(TEXT("Server has no GamesOMG Teleport")) : FText::FromString(TEXT("Reading this save…"));
	return FText::Format(FText::FromString(TEXT("{0} of {1} collected")), FText::AsNumber(FoundTickable), FText::AsNumber(Tickable));
}

void SGOMGRoot::Rebuild()
{
	const int32 N = GOMGPlaces::Num();
	// names are searched in the language they're shown in: rebuild the search text after a language switch
	const FString Culture = FInternationalization::Get().GetCurrentLanguage()->GetName();
	if (SearchCache.Num() != N || SearchCulture != Culture)
	{
		SearchCulture = Culture;
		SearchCache.SetNum(N);
		for (int32 i = 0; i < N; ++i) SearchCache[i] = GOMGPlaces::PlaceSearchText(i).ToString();
	}
	FVector Me;
	const bool bHaveMe = GOMGClient::PlayerLocation(Me);
	const bool bFiltering = !Filter.IsEmpty();

	// which places show, in which section
	TArray<TArray<int32>> MainByCat, FoundByCat;
	MainByCat.SetNum(GOMGPlaces::NumCats());
	FoundByCat.SetNum(GOMGPlaces::NumCats());
	Tickable = 0; FoundTickable = 0; FoundTotal = 0;
	for (int32 i = 0; i < N; ++i)
	{
		const bool bFound = GOMGClient::IsFound(i);
		if (GOMGPlaces::TickOf(i) != EGOMGTick::Manual) { ++Tickable; if (GOMGClient::IsGameFound(i)) ++FoundTickable; }
		if (bFiltering && !SearchCache[i].Contains(Filter)) continue;
		(bFound ? FoundByCat : MainByCat)[GOMGPlaces::Get(i).Cat].Add(i);
		if (bFound) ++FoundTotal;
	}
	auto Dist = [&](int32 i) { return bHaveMe ? FVector::Dist(Me, GOMGPlaces::LocationOf(i)) : -1.0; };
	auto Sort = [&](TArray<int32>& L)
	{
		if (bHaveMe) L.Sort([&](int32 A, int32 B) { return Dist(A) < Dist(B); });   // nearest first
	};

	const TSharedPtr<FGOMGRow> Selected = List.IsValid() && List->GetNumItemsSelected() > 0 ? List->GetSelectedItems()[0] : nullptr;
	const int32 SelectedPlace = (Selected.IsValid() && Selected->Kind == FGOMGRow::EKind::Place) ? Selected->Index : INDEX_NONE;
	Rows.Reset();

	auto EmitSection = [&](TArray<TArray<int32>>& ByCat, bool bFoundSection)
	{
		const FString P = bFoundSection ? TEXT("F") : TEXT("M");
		for (int32 G = 0; G < GOMGPlaces::NumGroups(); ++G)
		{
			TArray<int32> Cats;
			int32 GroupCount = 0, CatsInGroup = 0;
			for (int32 C = 0; C < GOMGPlaces::NumCats(); ++C)
			{
				if (GOMGPlaces::Cat(C).Group != G) continue;
				++CatsInGroup;
				if (ByCat[C].Num() == 0) continue;
				Cats.Add(C);
				GroupCount += ByCat[C].Num();
			}
			if (GroupCount == 0) continue;   // a heading with nothing under it disappears
			TSharedPtr<FGOMGRow> GR = MakeShared<FGOMGRow>();
			GR->Kind = FGOMGRow::EKind::Group; GR->Index = G; GR->bFound = bFoundSection; GR->Count = GroupCount;
			GR->Key = P + TEXT("g:") + FString::FromInt(G);
			GR->bCollapsed = !bFiltering && GOMGClient::IsCollapsed(GR->Key, false);
			GR->Depth = bFoundSection ? 1 : 0;
			Rows.Add(GR);
			if (GR->bCollapsed) continue;
			for (int32 C : Cats)
			{
				TArray<int32>& L = ByCat[C];
				bool bShowPlaces = true;
				if (CatsInGroup > 1)
				{
					TSharedPtr<FGOMGRow> CR = MakeShared<FGOMGRow>();
					CR->Kind = FGOMGRow::EKind::Cat; CR->Index = C; CR->bFound = bFoundSection; CR->Count = L.Num();
					CR->Key = P + TEXT("c:") + GOMGPlaces::Cat(C).Key;
					CR->bCollapsed = !bFiltering && GOMGClient::IsCollapsed(CR->Key, true);   // categories start closed: open the one you're hunting
					CR->Depth = GR->Depth + 1;
					Rows.Add(CR);
					bShowPlaces = !CR->bCollapsed;
				}
				if (!bShowPlaces) continue;
				Sort(L);
				for (int32 i : L)
				{
					TSharedPtr<FGOMGRow> R = MakeShared<FGOMGRow>();
					R->Kind = FGOMGRow::EKind::Place; R->Index = i; R->bFound = bFoundSection; R->Dist = Dist(i);
					R->Depth = GR->Depth + (CatsInGroup > 1 ? 2 : 1);
					Rows.Add(R);
				}
			}
		}
	};

	EmitSection(MainByCat, false);
	if (FoundTotal > 0)
	{
		TSharedPtr<FGOMGRow> FR = MakeShared<FGOMGRow>();
		FR->Kind = FGOMGRow::EKind::FoundHeader; FR->Count = FoundTotal; FR->Key = TEXT("F:drawer");
		FR->bCollapsed = !bFiltering && GOMGClient::IsCollapsed(FR->Key, true);
		Rows.Add(FR);
		if (!FR->bCollapsed) EmitSection(FoundByCat, true);
	}
	if (Rows.Num() == 0)
	{
		TSharedPtr<FGOMGRow> E = MakeShared<FGOMGRow>();
		E->Kind = FGOMGRow::EKind::Empty;
		Rows.Add(E);
	}
	if (List.IsValid())
	{
		List->RequestListRefresh();
		if (SelectedPlace != INDEX_NONE)
			for (const TSharedPtr<FGOMGRow>& R : Rows)
				if (R->Kind == FGOMGRow::EKind::Place && R->Index == SelectedPlace) { List->SetSelection(R, ESelectInfo::Direct); break; }
	}
}

void SGOMGRoot::ToggleHeading(const TSharedPtr<FGOMGRow>& Row)
{
	if (!Filter.IsEmpty()) return;   // everything is open while searching
	GOMGClient::SetCollapsed(Row->Key, !Row->bCollapsed);
	Rebuild();
}

TSharedRef<ITableRow> SGOMGRoot::MakeRow(TSharedPtr<FGOMGRow> Row, const TSharedRef<STableViewBase>& Owner)
{
	return SNew(STableRow<TSharedPtr<FGOMGRow>>, Owner)
		.Style(&Theme().Row)
		.Padding(FMargin(0, 1))
		[ MakeRowContent(Row) ];
}

TSharedRef<SWidget> SGOMGRoot::MakeRowContent(const TSharedPtr<FGOMGRow>& Row)
{
	FTheme& T = Theme();
	const float Indent = 14.f * Row->Depth;
	switch (Row->Kind)
	{
	case FGOMGRow::EKind::Empty:
		return SNew(SBox).Padding(FMargin(8, 16))
			[ SNew(STextBlock).Font(Font(14, false)).ColorAndOpacity(Muted).AutoWrapText(true)
				.Text(FText::FromString(Filter.IsEmpty() ? TEXT("Nothing to list.") : TEXT("No place matches that."))) ];

	case FGOMGRow::EKind::FoundHeader:
	case FGOMGRow::EKind::Group:
	case FGOMGRow::EKind::Cat:
	{
		const bool bDrawer = Row->Kind == FGOMGRow::EKind::FoundHeader;
		const bool bGroup = Row->Kind == FGOMGRow::EKind::Group;
		const FText Name = bDrawer ? FText::FromString(TEXT("Found"))
			: bGroup ? GOMGPlaces::GroupName(Row->Index) : GOMGPlaces::CatName(Row->Index);
		const FText Count = (Row->bFound || bDrawer) ? FText::AsNumber(Row->Count)
			: FText::Format(FText::FromString(TEXT("{0} left")), FText::AsNumber(Row->Count));
		return SNew(SBox).Padding(FMargin(Indent, bDrawer ? 10.f : (bGroup ? 4.f : 0.f), 0, 0))
		[
			SNew(SButton)
			.ButtonStyle(bGroup || bDrawer ? &T.HeadingBtn : &T.Flat)
			.ContentPadding(FMargin(10, bGroup || bDrawer ? 7 : 5))
			.OnClicked_Lambda([this, Row]() { ToggleHeading(Row); return FReply::Handled(); })
			[
				SNew(SHorizontalBox)
				+ SHorizontalBox::Slot().AutoWidth().VAlign(VAlign_Center).Padding(FMargin(0, 0, 8, 0))
				[ SNew(STextBlock).Font(Font(12, true)).ColorAndOpacity(Faint).Text(FText::FromString(Row->bCollapsed ? TEXT("+") : TEXT("–"))) ]
				+ SHorizontalBox::Slot().FillWidth(1.f).VAlign(VAlign_Center)
				[
					SNew(STextBlock).Font(Font(bGroup || bDrawer ? 15 : 14, true))
					.ColorAndOpacity(bDrawer ? Good : (bGroup ? Ink : Muted))
					.Text(Name).OverflowPolicy(ETextOverflowPolicy::Ellipsis)
				]
				+ SHorizontalBox::Slot().AutoWidth().VAlign(VAlign_Center)
				[ SNew(STextBlock).Font(Font(13, false)).ColorAndOpacity(Faint).Text(Count) ]
			]
		];
	}

	case FGOMGRow::EKind::Place:
	default:
	{
		const int32 i = Row->Index;
		const FGOMGPlaceDef& P = GOMGPlaces::Get(i);
		const bool bTape = IsTapes(i);
		const FText Region = GOMGPlaces::RegionName(P.Region);
		const FText Detail = GOMGPlaces::PlaceDetail(i);
		const FText Primary = bTape ? Detail : (Region.IsEmpty() ? GOMGPlaces::CatName(P.Cat) : Region);
		const FText Secondary = bTape ? Region : Detail;
		const bool bGame = GOMGClient::IsGameFound(i);
		const bool bMine = !bGame && GOMGClient::IsFound(i);
		const bool bNoLanding = GOMGPlaces::HasNoLanding(i);

		TSharedRef<SWidget> Check = bGame
			? StaticCastSharedRef<SWidget>(SNew(SBox).WidthOverride(20).HeightOverride(20).ToolTipText(FText::FromString(TEXT("Collected in this save")))
				[ SNew(SBorder).BorderImage(&T.BoxGame) ])
			: StaticCastSharedRef<SWidget>(SNew(SBox).WidthOverride(20).HeightOverride(20)
				[
					SNew(SButton).ButtonStyle(bMine ? &T.Go : &T.Check).ContentPadding(FMargin(0))
					.ToolTipText(FText::FromString(bMine ? TEXT("Ticked by you: click to put it back in the list") : TEXT("Tick it: moves it to Found")))
					.OnClicked_Lambda([i]() { GOMGClient::ToggleManualFound(i); return FReply::Handled(); })
					[ SNew(SBorder).BorderImage(bMine ? static_cast<const FSlateBrush*>(&T.BoxMine) : static_cast<const FSlateBrush*>(&T.None)) ]
				]);

		return SNew(SBox).Padding(FMargin(Indent, 0, 0, 0))
		[
			SNew(SHorizontalBox)
			+ SHorizontalBox::Slot().AutoWidth().VAlign(VAlign_Center).Padding(FMargin(6, 0, 8, 0)) [ Check ]
			+ SHorizontalBox::Slot().FillWidth(1.f)
			[
				SNew(SButton).ButtonStyle(&T.Flat).ContentPadding(FMargin(6, 5))
				.IsEnabled(!bNoLanding)
				.ToolTipText(bNoLanding ? FText::FromString(TEXT("No safe spot to stand here was found, so no teleport.")) : FText::GetEmpty())
				.OnClicked_Lambda([i]() { GOMGClient::GoToPlace(i); return FReply::Handled(); })
				[
					SNew(SHorizontalBox)
					+ SHorizontalBox::Slot().FillWidth(1.f).VAlign(VAlign_Center)
					[
						SNew(SVerticalBox)
						+ SVerticalBox::Slot().AutoHeight()
						[ SNew(STextBlock).Font(Font(15, false)).ColorAndOpacity(Ink).Text(Primary).OverflowPolicy(ETextOverflowPolicy::Ellipsis) ]
						+ SVerticalBox::Slot().AutoHeight()
						[
							SNew(STextBlock).Font(Font(12, false)).ColorAndOpacity(Muted).Text(Secondary).OverflowPolicy(ETextOverflowPolicy::Ellipsis)
							.Visibility(Secondary.IsEmpty() ? EVisibility::Collapsed : EVisibility::Visible)
						]
					]
					+ SHorizontalBox::Slot().AutoWidth().VAlign(VAlign_Center).Padding(FMargin(8, 0))
					[ SNew(STextBlock).Font(Font(13, false)).ColorAndOpacity(Muted).Text(Distance(Row->Dist)) ]
					+ SHorizontalBox::Slot().AutoWidth().VAlign(VAlign_Center)
					[
						bNoLanding
						? StaticCastSharedRef<SWidget>(SNew(STextBlock).Font(Font(12, false)).ColorAndOpacity(Faint).Text(FText::FromString(TEXT("no landing"))))
						: StaticCastSharedRef<SWidget>(SNew(SBorder).BorderImage(&T.GoPill).Padding(FMargin(10, 3))
							[ SNew(STextBlock).Font(Font(13, true)).ColorAndOpacity(Hex(0x0e1117)).Text(FText::FromString(TEXT("Go"))) ])
					]
				]
			]
		];
	}
	}
}

void SGOMGRoot::MoveSelection(int32 Delta)
{
	if (Rows.Num() == 0 || Delta == 0) return;
	// While searching every heading is open and can't be folded, so the keys step from place to place (Down, Enter goes to the
	// first match). Otherwise headings take a stop too, so Enter can fold them from the keyboard.
	const bool bPlacesOnly = !Filter.IsEmpty();
	auto Stops = [this, bPlacesOnly](int32 i)
	{
		const FGOMGRow::EKind K = Rows[i]->Kind;
		return K == FGOMGRow::EKind::Place || (!bPlacesOnly && K != FGOMGRow::EKind::Empty);
	};
	int32 Cur = INDEX_NONE;
	if (List->GetNumItemsSelected() > 0) Cur = Rows.IndexOfByKey(List->GetSelectedItems()[0]);
	const int32 Step = Delta > 0 ? 1 : -1;
	int32 Left = FMath::Abs(Delta);
	int32 Next = INDEX_NONE;
	for (int32 i = (Cur == INDEX_NONE ? (Step > 0 ? -1 : Rows.Num()) : Cur) + Step; i >= 0 && i < Rows.Num() && Left > 0; i += Step)
		if (Stops(i)) { Next = i; --Left; }
	if (Next == INDEX_NONE) return;   // nothing further that way
	List->SetSelection(Rows[Next], ESelectInfo::OnKeyPress);
	List->RequestScrollIntoView(Rows[Next]);
}

void SGOMGRoot::ActivateSelection()
{
	const FString Text = Search->GetText().ToString();
	if (GOMGClient::IsLine(Text) && GOMGClient::SubmitLine(Text)) { Search->SetText(FText::GetEmpty()); return; }
	if (Text.TrimStartAndEnd().StartsWith(TEXT("/tp"))) { GOMGClient::SubmitLine(Text); return; }   // says what's wrong with it
	if (List->GetNumItemsSelected() == 0) { MoveSelection(+1); return; }
	const TSharedPtr<FGOMGRow> Row = List->GetSelectedItems()[0];
	if (Row->Kind == FGOMGRow::EKind::Place) GOMGClient::GoToPlace(Row->Index);
	else if (Row->Kind != FGOMGRow::EKind::Empty) ToggleHeading(Row);
}

void SGOMGRoot::ShowToast(const FText& Message, bool bBad)
{
	ToastText->SetText(Message);
	ToastText->SetColorAndOpacity(bBad ? BadText : Ink);
	ToastBox->SetVisibility(EVisibility::HitTestInvisible);
	if (ToastTimer.IsValid()) UnRegisterActiveTimer(ToastTimer.ToSharedRef());
	ToastTimer = RegisterActiveTimer(bBad ? 6.f : 4.f, FWidgetActiveTimerDelegate::CreateSP(this, &SGOMGRoot::HideToast));
}

EActiveTimerReturnType SGOMGRoot::HideToast(double, float)
{
	ToastBox->SetVisibility(EVisibility::Collapsed);
	ToastTimer.Reset();
	return EActiveTimerReturnType::Stop;
}

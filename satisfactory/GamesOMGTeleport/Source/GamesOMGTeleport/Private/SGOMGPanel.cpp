#include "SGOMGPanel.h"

#include "GOMGClient.h"
#include "GOMGPlaces.h"
#include "GOMGTheme.h"
#include "GamesOMGTeleportModule.h"

#include "Framework/Application/SlateApplication.h"
#include "Internationalization/Culture.h"
#include "Internationalization/Internationalization.h"
#include "Misc/Paths.h"
#include "Styling/CoreStyle.h"
#include "Widgets/Images/SImage.h"
#include "Widgets/Input/SEditableTextBox.h"
#include "Widgets/Layout/SBorder.h"
#include "Widgets/Layout/SBox.h"
#include "Widgets/Layout/SDPIScaler.h"
#include "Widgets/Layout/SScrollBox.h"
#include "Widgets/Layout/SSpacer.h"
#include "Widgets/Layout/SWidgetSwitcher.h"
#include "Widgets/Layout/SWrapBox.h"
#include "Widgets/SOverlay.h"
#include "Widgets/Text/STextBlock.h"
#include "Widgets/Views/STableRow.h"

using namespace GOMGTheme;

namespace
{
	// Planet Crafter's compact sizes at 1080p (Theme.Build): rows 32, buttons 32 (42 with a second line), tabs 30, fields 30
	constexpr float RowH = 32, ButtonH = 32, ButtonSubH = 42, TabH = 30, FieldH = 30, FoldH = 30, PanelW = 360, InnerW = 328;
	constexpr int32 NearestMax = 10, LayerFirst = 15, LayerMore = 25, FindMax = 60;
	const TCHAR* const Sep = TEXT(" · ");

	FText Tx(const TCHAR* S) { return FText::FromString(S); }
	FText Tx(const FString& S) { return FText::FromString(S); }
	template <typename... TArgs> FText Fmt(const TCHAR* S, TArgs&&... Args) { return FText::Format(FText::FromString(S), Forward<TArgs>(Args)...); }

	FSimpleDelegate Do(TFunction<void()> F) { return FSimpleDelegate::CreateLambda([F = MoveTemp(F)]() { if (F) F(); }); }

	FString Join(const FString& A, const FString& B) { return A.IsEmpty() ? B : B.IsEmpty() ? A : A + Sep + B; }

	FString Distance(double Cm)
	{
		const double M = Cm / 100.0;
		return M < 1000.0 ? FString::Printf(TEXT("%d m"), FMath::RoundToInt(M)) : FString::Printf(TEXT("%.1f km"), M / 1000.0);
	}

	// the model's RowMeta: the whole line if it fits; else without the tail (the height); then the leading parts go one at a
	// time; the last part (the distance) always stays
	FString Fit(const FString& Lead, const FString& Tail, float Max, const FSlateFontInfo& F)
	{
		const FString Full = Join(Lead, Tail);
		if (Measure(Full, F) <= Max || Lead.IsEmpty()) return Full;
		FString Ln = Lead;
		int32 Cut;
		while (Measure(Ln, F) > Max && (Cut = Ln.Find(Sep)) != INDEX_NONE) Ln = Ln.Mid(Cut + FCString::Strlen(Sep));
		return Ln;
	}

	FString UrlPart(const FString& In)
	{
		const FTCHARToUTF8 U(*In);
		FString Out;
		for (int32 i = 0; i < U.Length(); ++i)
		{
			const uint8 C = (uint8)U.Get()[i];
			if ((C < 128 && FChar::IsAlnum((TCHAR)C)) || C == '-' || C == '_' || C == '.' || C == '~') Out.AppendChar((TCHAR)C);
			else Out += FString::Printf(TEXT("%%%02X"), C);
		}
		return Out;
	}

	bool IsTapes(int32 Place) { return FCString::Strcmp(GOMGPlaces::Cat(GOMGPlaces::Get(Place).Cat).Key, TEXT("tapes")) == 0; }

	// a place's name in the list: the region it's in (a tape: the tape's name)
	FString PlaceName(int32 i)
	{
		const FGOMGPlaceDef& P = GOMGPlaces::Get(i);
		if (IsTapes(i)) return GOMGPlaces::PlaceDetail(i).ToString();
		const FText Region = GOMGPlaces::RegionName(P.Region);
		return (Region.IsEmpty() ? GOMGPlaces::CatName(P.Cat) : Region).ToString();
	}

	FVector PlaceFeet(int32 i) { return GOMGPlaces::HasCheckedLanding(i) ? GOMGPlaces::LandingOf(i) : GOMGPlaces::LocationOf(i); }

	// ── a clickable area: a plate that changes on hover, a colour its text and icons take, left and right clicks ──
	class SGOMGHit : public SCompoundWidget
	{
	public:
		SLATE_BEGIN_ARGS(SGOMGHit)
			: _Normal(nullptr), _Hover(nullptr), _Off(nullptr), _Fg(FSlateColor::UseForeground()), _HoverFg(FSlateColor::UseForeground()),
			  _OffFg(FSlateColor::UseForeground()), _Padding(FMargin(0))
		{}
			SLATE_DEFAULT_SLOT(FArguments, Content)
			SLATE_ATTRIBUTE(const FSlateBrush*, Normal)
			SLATE_ATTRIBUTE(const FSlateBrush*, Hover)
			SLATE_ATTRIBUTE(const FSlateBrush*, Off)
			SLATE_ATTRIBUTE(FSlateColor, Fg)
			SLATE_ATTRIBUTE(FSlateColor, HoverFg)
			SLATE_ATTRIBUTE(FSlateColor, OffFg)
			SLATE_ATTRIBUTE(FMargin, Padding)
			SLATE_EVENT(FSimpleDelegate, OnClick)
			SLATE_EVENT(FSimpleDelegate, OnRightClick)
		SLATE_END_ARGS()

		void Construct(const FArguments& A)
		{
			Normal = A._Normal; Hover = A._Hover; Off = A._Off; Fg = A._Fg; HoverFg = A._HoverFg; OffFg = A._OffFg;
			OnClick = A._OnClick; OnRightClick = A._OnRightClick;
			ChildSlot
			[
				SNew(SBorder)
				.BorderImage_Lambda([this]() { return Brush(); })
				.ForegroundColor_Lambda([this]() { return Colour(); })
				.Padding(A._Padding)
				[ A._Content.Widget ]
			];
		}

		const FSlateBrush* Brush() const
		{
			const FSlateBrush* B = !IsEnabled() && Off.Get() ? Off.Get() : (IsEnabled() && IsHovered() && Hover.Get()) ? Hover.Get() : Normal.Get();
			return B ? B : GOMGTheme::None();
		}

		FSlateColor Colour() const
		{
			if (!IsEnabled()) return OffFg.Get();
			return IsHovered() ? HoverFg.Get() : Fg.Get();
		}

		// Slate sends the second press of a double-click here, not to OnMouseButtonDown (SButton does the same)
		virtual FReply OnMouseButtonDoubleClick(const FGeometry& G, const FPointerEvent& E) override { return OnMouseButtonDown(G, E); }

		virtual FReply OnMouseButtonDown(const FGeometry&, const FPointerEvent& E) override
		{
			if (!IsEnabled()) return FReply::Unhandled();
			if (E.GetEffectingButton() == EKeys::LeftMouseButton && OnClick.IsBound()) { OnClick.Execute(); return FReply::Handled(); }
			if (E.GetEffectingButton() == EKeys::RightMouseButton && OnRightClick.IsBound()) { OnRightClick.Execute(); return FReply::Handled(); }
			return FReply::Unhandled();
		}

	private:
		TAttribute<const FSlateBrush*> Normal, Hover, Off;
		TAttribute<FSlateColor> Fg, HoverFg, OffFg;
		FSimpleDelegate OnClick, OnRightClick;
	};

	TSharedRef<STextBlock> Txt(const TAttribute<FText>& T, float Px, bool bBold, const TAttribute<FSlateColor>& C = FSlateColor::UseForeground())
	{
		return SNew(STextBlock).Text(T).Font(Font(Px, bBold)).ColorAndOpacity(C);
	}

	TSharedRef<SWidget> Gap(float H) { return SNew(SSpacer).Size(FVector2D(1, H)); }
	TSharedRef<SWidget> GapW(float W) { return SNew(SSpacer).Size(FVector2D(W, 1)); }

	TSharedRef<SWidget> Img(const TAttribute<const FSlateBrush*>& B, float Size, const TAttribute<FSlateColor>& C = FSlateColor::UseForeground())
	{
		return SNew(SBox).WidthOverride(Size).HeightOverride(Size)[ SNew(SImage).Image(B).ColorAndOpacity(C) ];
	}

	TSharedRef<SWidget> KeyChip(const TAttribute<FText>& Key)
	{
		return SNew(SBox).HeightOverride(18).VAlign(VAlign_Center)
			[ SNew(SBorder).BorderImage(Plate(Raised, 4)).Padding(FMargin(6, 0)).VAlign(VAlign_Center)[ Txt(Key, 11, true, Ink) ] ];
	}

	FEditableTextBoxStyle& InputStyle()
	{
		static FEditableTextBoxStyle* S = nullptr;
		if (!S)
		{
			S = new FEditableTextBoxStyle(FCoreStyle::Get().GetWidgetStyle<FEditableTextBoxStyle>("NormalEditableTextBox"));
			S->SetBackgroundImageNormal(*Plate(Surface, 8)).SetBackgroundImageHovered(*Plate(Surface, 8))
				.SetBackgroundImageFocused(*PlateLined(Raised, 8, Alpha(Blue, .9f), 1.5f)).SetBackgroundImageReadOnly(*Plate(Surface, 8))
				.SetForegroundColor(FSlateColor(Ink)).SetFocusedForegroundColor(FSlateColor(Ink)).SetPadding(FMargin(10, 5));
		}
		return *S;
	}

	FTableViewStyle& ListStyle()
	{
		static FTableViewStyle* S = nullptr;
		if (!S) { S = new FTableViewStyle(FCoreStyle::Get().GetWidgetStyle<FTableViewStyle>("ListView")); S->SetBackgroundBrush(*None()); }
		return *S;
	}

	FTableRowStyle& RowStyle()
	{
		static FTableRowStyle* S = nullptr;
		if (!S)
		{
			S = new FTableRowStyle(FCoreStyle::Get().GetWidgetStyle<FTableRowStyle>("TableView.Row"));
			const FSlateBrush* N = None();
			S->SetEvenRowBackgroundBrush(*N).SetOddRowBackgroundBrush(*N).SetEvenRowBackgroundHoveredBrush(*N).SetOddRowBackgroundHoveredBrush(*N)
				.SetActiveBrush(*N).SetActiveHoveredBrush(*N).SetInactiveBrush(*N).SetInactiveHoveredBrush(*N)
				.SetSelectorFocusedBrush(*N).SetActiveHighlightedBrush(*N).SetInactiveHighlightedBrush(*N)
				.SetTextColor(Ink).SetSelectedTextColor(Ink);
		}
		return *S;
	}
}

// ══ building blocks ══════════════════════════════════════════════════════════════════════════════════════════════════════════

void SGOMGRoot::Later(TFunction<void()> Act) { Pending = MoveTemp(Act); }

// a button: kind 0 = plain, 1 = primary (orange), 2 = danger, 3 = ghost (no plate until hover)
TSharedRef<SWidget> SGOMGRoot::Button(const FText& Label, TOptional<uint8> Icon, int32 Kind, TFunction<void()> Act, TAttribute<bool> Enabled, TAttribute<FText> Sub, float Width)
{
	return ButtonDyn(Label, Icon, Kind, MoveTemp(Act), Enabled, Sub, Width);
}

TSharedRef<SWidget> SGOMGRoot::ButtonDyn(TAttribute<FText> Label, TOptional<uint8> Icon, int32 Kind, TFunction<void()> Act, TAttribute<bool> Enabled, TAttribute<FText> Sub, float Width)
{
	const bool bSub = Sub.IsSet() || Sub.IsBound();
	const FLinearColor Base = Kind == 1 ? Orange : Kind == 2 ? Alpha(Bad, .9f) : Kind == 3 ? Alpha(Raised, 0) : Raised;
	const FLinearColor Hot = Kind == 1 ? OrangeHover : Kind == 2 ? BadText : Kind == 3 ? Raised : Hex(0x2a313c);
	const FLinearColor InkOn = Kind == 1 || Kind == 2 ? OnOrange : Ink;
	TSharedRef<SHorizontalBox> Ln = SNew(SHorizontalBox);
	if (Icon.IsSet())
		Ln->AddSlot().AutoWidth().VAlign(VAlign_Center).Padding(FMargin(0, 0, 8, 0))[ Img(GOMGTheme::Icon((EGOMGIcon)Icon.GetValue()), 18) ];
	Ln->AddSlot().AutoWidth().VAlign(VAlign_Center)
		[ SNew(STextBlock).Text(Label).Font(Font(14.5f, Kind == 1 || Kind == 2)).OverflowPolicy(ETextOverflowPolicy::Ellipsis) ];
	TSharedRef<SVerticalBox> Col = SNew(SVerticalBox)
		+ SVerticalBox::Slot().FillHeight(1.f).HAlign(HAlign_Center).VAlign(VAlign_Center).Padding(FMargin(10, 0))[ Ln ];
	if (bSub)
		Col->AddSlot().AutoHeight().HAlign(HAlign_Center).Padding(FMargin(10, 0, 10, 5))
			[ SNew(STextBlock).Text(Sub).Font(Font(12.5f)).ColorAndOpacity(Muted).OverflowPolicy(ETextOverflowPolicy::Ellipsis) ];
	TSharedRef<SBox> Box = SNew(SBox).HeightOverride(bSub ? ButtonSubH : ButtonH)
		[
			SNew(SGOMGHit).IsEnabled(Enabled)
			.Normal(Plate(Base, 8)).Hover(Plate(Hot, 8)).Off(Plate(Raised, 8))
			.Fg(FSlateColor(InkOn)).HoverFg(FSlateColor(InkOn)).OffFg(FSlateColor(Muted))
			.OnClick(Do([this, Act]() { Later(Act); }))
			[ Col ]
		];
	if (Width > 0) Box->SetWidthOverride(Width);
	return Box;
}

TSharedRef<SWidget> SGOMGRoot::IconButton(TAttribute<uint8> Icon, TAttribute<FText> Tip, TFunction<void()> Act)
{
	return SNew(SBox).WidthOverride(34).HeightOverride(34)
		[
			SNew(SGOMGHit).ToolTipText(Tip)
			.Normal(None()).Hover(Plate(Raised, 8)).Fg(FSlateColor(Muted)).HoverFg(FSlateColor(Ink))
			.OnClick(Do([this, Act]() { Later(Act); }))
			[
				SNew(SBox).HAlign(HAlign_Center).VAlign(VAlign_Center)
				[ Img(TAttribute<const FSlateBrush*>::CreateLambda([Icon]() { return GOMGTheme::Icon((EGOMGIcon)Icon.Get()); }), 18) ]
			]
		];
}

TSharedRef<SWidget> SGOMGRoot::TabButton(const FText& Label, ETab Which)
{
	auto On = [this, Which]() { return Tab == Which; };
	return SNew(SBox).HeightOverride(TabH)
		[
			SNew(SGOMGHit)
			.Normal_Lambda([On]() { return On() ? Plate(Surface, 8) : None(); })
			.Hover_Lambda([On]() { return On() ? Plate(Surface, 8) : Plate(Alpha(Raised, .6f), 8); })
			.Fg_Lambda([On]() { return FSlateColor(On() ? Ink : Muted); })
			.HoverFg_Lambda([On]() { return FSlateColor(On() ? Ink : Muted); })
			.OnClick(Do([this, Which]() { Later([this, Which]() { SetTab(Which); }); }))
			[
				SNew(SOverlay)
				+ SOverlay::Slot().HAlign(HAlign_Center).VAlign(VAlign_Center)
				[ SNew(STextBlock).Text(Label).Font_Lambda([On]() { return Font(14.5f, On()); }) ]
				+ SOverlay::Slot().VAlign(VAlign_Bottom).Padding(FMargin(12, 0))
				[
					SNew(SBox).HeightOverride(3).Visibility_Lambda([On]() { return On() ? EVisibility::HitTestInvisible : EVisibility::Hidden; })
					[ SNew(SImage).Image(Plate(Orange, 0)) ]
				]
			]
		];
}

TSharedRef<SWidget> SGOMGRoot::Chip(TAttribute<FText> Label, TFunction<bool()> On, TFunction<void()> Act, bool bTickIcon)
{
	return SNew(SBox).HeightOverride(FieldH)
		[
			SNew(SGOMGHit)
			.Normal_Lambda([On]() { return On() ? Plate(Alpha(Orange, .22f), 8) : Plate(Surface, 8); })
			.Hover_Lambda([On]() { return On() ? Plate(Alpha(Orange, .22f), 8) : Plate(Raised, 8); })
			.Fg_Lambda([On]() { return FSlateColor(On() ? Ink : Muted); })
			.HoverFg_Lambda([On]() { return FSlateColor(On() ? Ink : Muted); })
			.OnClick(Do([this, Act]() { Later(Act); }))
			[
				SNew(SHorizontalBox)
				+ SHorizontalBox::Slot().AutoWidth().VAlign(VAlign_Center).Padding(FMargin(11, 0, 5, 0))
				[
					SNew(SBox).Visibility_Lambda([On, bTickIcon]() { return bTickIcon && On() ? EVisibility::HitTestInvisible : EVisibility::Collapsed; })
					[ Img(GOMGTheme::Icon(EGOMGIcon::Check), 14, FSlateColor(Orange)) ]
				]
				+ SHorizontalBox::Slot().AutoWidth().VAlign(VAlign_Center).Padding(FMargin(0, 0, 11, 0))
				[ SNew(STextBlock).Text(Label).Font(Font(12.5f)) ]
			]
		];
}

TSharedRef<SWidget> SGOMGRoot::Field(TSharedPtr<SEditableTextBox>& Out, TAttribute<FText> Hint, TFunction<void(const FText&)> Changed)
{
	return SNew(SBox).HeightOverride(FieldH)
		[
			SAssignNew(Out, SEditableTextBox)
			.Style(&InputStyle())
			.Font(Font(14.5f))
			.HintText(Hint)
			.OnTextChanged_Lambda([Changed](const FText& T) { if (Changed) Changed(T); })
		];
}

TSharedRef<SWidget> SGOMGRoot::Heading(const FText& Text)
{
	return SNew(SBox).Padding(FMargin(0, 8, 0, 2))[ Txt(Text.ToUpper(), 11.5f, true, Muted) ];
}

// the model's FlowButtons: side by side while whole labels fit, the rest on the next line, the spare width shared out
TSharedRef<SWidget> SGOMGRoot::FlowButtons(const TArray<TTuple<FText, uint8, TFunction<void()>, bool>>& Items)
{
	TSharedRef<SVerticalBox> Out = SNew(SVerticalBox);
	const float GapW6 = 6;
	TArray<float> Need;
	for (const auto& B : Items) Need.Add(Measure(B.Get<0>().ToString(), Font(14.5f)) + 18 + 8 + 24);
	int32 At = 0;
	while (At < Items.Num())
	{
		int32 End = At;
		float Used = Need[At];
		while (End + 1 < Items.Num() && Used + GapW6 + Need[End + 1] <= InnerW) { ++End; Used += GapW6 + Need[End]; }
		TSharedRef<SHorizontalBox> Ln = SNew(SHorizontalBox);
		for (int32 i = At; i <= End; ++i)
		{
			const auto& B = Items[i];
			Ln->AddSlot().FillWidth(Need[i]).Padding(FMargin(0, 0, i < End ? GapW6 : 0, 0))
				[ Button(B.Get<0>(), B.Get<1>(), 0, B.Get<2>(), B.Get<3>()) ];
		}
		Out->AddSlot().AutoHeight().Padding(FMargin(0, At > 0 ? GapW6 : 0, 0, 0))[ Ln ];
		At = End + 1;
	}
	return Out;
}

// ══ the panel ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════

void SGOMGRoot::Construct(const FArguments&)
{
	SetVisibility(EVisibility::SelfHitTestInvisible);
	ChildSlot
	[
		SNew(SDPIScaler).DPIScale_Lambda([this]() { return Scale; }).Visibility(EVisibility::SelfHitTestInvisible)
		[
			SNew(SOverlay).Visibility(EVisibility::SelfHitTestInvisible)
			+ SOverlay::Slot().HAlign(HAlign_Center).VAlign(VAlign_Top).Padding(FMargin(0, 96, 0, 0))
			[
				SAssignNew(ToastBox, SBorder)
				.Visibility(EVisibility::Collapsed)
				.BorderImage(PlateLined(Alpha(GOMGTheme::Panel, .96f), 10, GOMGTheme::Line, 1))
				.Padding(FMargin(12, 8, 16, 8))
				[
					SNew(SHorizontalBox)
					+ SHorizontalBox::Slot().AutoWidth().VAlign(VAlign_Center).Padding(FMargin(0, 0, 10, 0))[ Img(Mark(), 22, FSlateColor(FLinearColor::White)) ]
					+ SHorizontalBox::Slot().AutoWidth().VAlign(VAlign_Center)[ SAssignNew(ToastText, STextBlock).Font(Font(14.5f)).ColorAndOpacity(Ink) ]
				]
			]
			+ SOverlay::Slot()
			[
				SAssignNew(Holder, SBox).Visibility(EVisibility::Collapsed).Padding(16)
			]
		]
	];

	Panel = SNew(SBorder)
		.BorderImage_Lambda([]() { return Plate(Alpha(GOMGTheme::Panel, GOMGStore::Settings().Opacity / 100.f), 14); })
		.Padding(0)
		[
			SNew(SOverlay)
			// the model's orange line along the top
			+ SOverlay::Slot().VAlign(VAlign_Top).Padding(FMargin(14, 0))
			[ SNew(SBox).HeightOverride(2.5f)[ SNew(SImage).Image(Plate(Orange, 0)) ] ]
			+ SOverlay::Slot().Padding(FMargin(16, 14))
			[
				SNew(SVerticalBox)
				+ SVerticalBox::Slot().AutoHeight()[ MakeHeader() ]
				+ SVerticalBox::Slot().FillHeight(1.f)
				[
					SNew(SWidgetSwitcher).WidgetIndex_Lambda([this]() { return (int32)View; })
					+ SWidgetSwitcher::Slot()[ MakePlaces() ]
					+ SWidgetSwitcher::Slot()[ MakeSettings() ]
					+ SWidgetSwitcher::Slot()[ MakeRename() ]
				]
				+ SVerticalBox::Slot().AutoHeight()[ MakeFooter() ]
			]
		];
	PlacePanel();
}

void SGOMGRoot::PlacePanel()
{
	bLeftNow = GOMGStore::Settings().bLeft;
	TSharedRef<SWidget> Sized = SNew(SBox).WidthOverride(PanelW)[ Panel.ToSharedRef() ];
	if (bLeftNow)
		Holder->SetContent(SNew(SHorizontalBox).Visibility(EVisibility::SelfHitTestInvisible)
			+ SHorizontalBox::Slot().AutoWidth()[ Sized ] + SHorizontalBox::Slot().FillWidth(1.f)[ SNew(SSpacer) ]);
	else
		Holder->SetContent(SNew(SHorizontalBox).Visibility(EVisibility::SelfHitTestInvisible)
			+ SHorizontalBox::Slot().FillWidth(1.f)[ SNew(SSpacer) ] + SHorizontalBox::Slot().AutoWidth()[ Sized ]);
}

TSharedRef<SWidget> SGOMGRoot::MakeHeader()
{
	return SNew(SVerticalBox)
		+ SVerticalBox::Slot().AutoHeight()
		[
			SNew(SHorizontalBox)
			+ SHorizontalBox::Slot().AutoWidth().VAlign(VAlign_Center)[ Img(Mark(), 32, FSlateColor(FLinearColor::White)) ]
			+ SHorizontalBox::Slot().FillWidth(1.f).VAlign(VAlign_Center).Padding(FMargin(10, 0, 6, 0))
			[
				SNew(SVerticalBox)
				+ SVerticalBox::Slot().AutoHeight()
				[ Txt(TAttribute<FText>::CreateLambda([this]() { return Tx(View == EView::Settings ? TEXT("Settings") : TEXT("Places")); }), 19, true, Ink) ]
				+ SVerticalBox::Slot().AutoHeight()
				[
					SNew(STextBlock).Font(Font(12.5f)).ColorAndOpacity(Muted).OverflowPolicy(ETextOverflowPolicy::Ellipsis)
					.Text_Lambda([this]()
					{
						const FString Session = GOMGClient::SessionLabel();
						if (GOMGClient::IsServerMissingMod()) return Tx(Session + TEXT(" · server has no GamesOMG Teleport"));
						if (!GOMGClient::HasGameState()) return Tx(Session + TEXT(" · reading this save…"));
						return Fmt(TEXT("{0} · {1} of {2} collected"), Tx(Session), FoundTickable, Tickable);
					})
				]
			]
			+ SHorizontalBox::Slot().AutoWidth().VAlign(VAlign_Center)
			[
				IconButton(TAttribute<uint8>::CreateLambda([this]() { return (uint8)(View == EView::Places ? EGOMGIcon::Gear : EGOMGIcon::Close); }),
					TAttribute<FText>::CreateLambda([this]() { return Tx(View == EView::Places ? TEXT("Settings") : TEXT("Back to Places")); }),
					[this]() { CancelNote(); GOMGClient::CancelCapture(); View = View == EView::Places ? EView::Settings : EView::Places; RebuildSettings(); RebuildPrompts(); })
			]
			+ SHorizontalBox::Slot().AutoWidth().VAlign(VAlign_Center).Padding(FMargin(4, 0, 0, 0))
			[
				IconButton((uint8)EGOMGIcon::Close, TAttribute<FText>::CreateLambda([]()
					{ return Fmt(TEXT("{0} (Esc)"), GOMGPlaces::GameText(TEXT("Gamepads_UI"), TEXT("GamepadHints/aCommon/Close"), TEXT("Close"))); }),
					[]() { GOMGClient::ClosePanel(); })
			]
		]
		+ SVerticalBox::Slot().AutoHeight()[ Gap(12) ];
}

TSharedRef<SWidget> SGOMGRoot::MakePlaces()
{
	return SNew(SVerticalBox)
		// main actions
		+ SVerticalBox::Slot().AutoHeight()
		[
			SNew(SHorizontalBox)
			+ SHorizontalBox::Slot().FillWidth(1.f).Padding(FMargin(0, 0, 4, 0))
			[ Button(Tx(TEXT("From clipboard")), (uint8)EGOMGIcon::Clipboard, 0, []() { GOMGClient::FromClipboard(); }) ]
			+ SHorizontalBox::Slot().FillWidth(1.f).Padding(FMargin(4, 0, 0, 0))
			[ Button(Tx(TEXT("Save here")), (uint8)EGOMGIcon::Save, 0, [this]() { DoSaveHere(); }) ]
		]
		+ SVerticalBox::Slot().AutoHeight()[ Gap(8) ]
		+ SVerticalBox::Slot().AutoHeight()
		[
			ButtonDyn(TAttribute<FText>::CreateLambda([]()
				{
					const FGOMGWorld& W = GOMGClient::World();
					if (!W.bHasBack) return Tx(TEXT("Return to previous spot"));
					const FString To = !W.BackName.IsEmpty() ? W.BackName
						: FString::Printf(TEXT("%lld, %lld"), (long long)FMath::TruncToDouble(W.Back.X / 100.0), (long long)FMath::TruncToDouble(W.Back.Y / 100.0));
					return Fmt(TEXT("Return to {0}"), Tx(To));
				}),
				(uint8)EGOMGIcon::Return, 0, []() { GOMGClient::GoBack(); },
				TAttribute<bool>::CreateLambda([]() { return GOMGClient::HasBack(); }),
				TAttribute<FText>::CreateLambda([]()
				{
					return GOMGClient::HasBack() ? Fmt(TEXT("Swaps between two spots, not a history. Key: {0}"), GOMGClient::BackKey().GetDisplayName())
						: Tx(TEXT("Nothing yet: a teleport here remembers where you stood."));
				}))
		]
		+ SVerticalBox::Slot().AutoHeight()[ Gap(12) ]
		// My places | Map places
		+ SVerticalBox::Slot().AutoHeight()
		[
			SNew(SHorizontalBox)
			+ SHorizontalBox::Slot().FillWidth(1.f).Padding(FMargin(0, 0, 2, 0))[ TabButton(Tx(TEXT("My places")), ETab::Mine) ]
			+ SHorizontalBox::Slot().FillWidth(1.f).Padding(FMargin(2, 0, 0, 0))[ TabButton(Tx(TEXT("Map places")), ETab::Map) ]
		]
		+ SVerticalBox::Slot().AutoHeight()[ Gap(8) ]
		// search and filters
		+ SVerticalBox::Slot().AutoHeight()
		[
			SNew(SHorizontalBox)
			+ SHorizontalBox::Slot().FillWidth(1.f)
			[
				Field(Search, TAttribute<FText>::CreateLambda([]()
				{
					// the game's own "Search..." in the player's language, then the other thing the box takes
					return Fmt(TEXT("{0}   /tp X Y Z"), GOMGPlaces::GameText(TEXT("General_UI"), TEXT("GenericUIStrings/Textbox/Search/TextboxHint"), TEXT("Search")));
				}), [this](const FText& T)
				{
					const FString S = T.ToString().TrimStartAndEnd();
					const FString Was = Filter;
					Filter = GOMGClient::IsLine(S) ? FString() : S.ToLower();   // a line is not a search
					if (Filter != Was) { FocusKey.Reset(); Rebuild(); }
				})
			]
			+ SHorizontalBox::Slot().AutoWidth().Padding(FMargin(6, 0, 0, 0))
			[
				SNew(SBox).Visibility_Lambda([this]() { return Tab == ETab::Map ? EVisibility::Visible : EVisibility::Collapsed; })
				[ Chip(Tx(TEXT("Hide found")), []() { return GOMGStore::Settings().bHideFound; },
					[this]() { GOMGStore::Settings().bHideFound = !GOMGStore::Settings().bHideFound; GOMGClient::Persist(); Rebuild(); }, true) ]
			]
			+ SHorizontalBox::Slot().AutoWidth().Padding(FMargin(6, 0, 0, 0))
			[
				Chip(TAttribute<FText>::CreateLambda([]() { return Tx(GOMGStore::Settings().bByName ? TEXT("A–Z") : TEXT("Nearest")); }), []() { return false; },
					[this]() { GOMGStore::Settings().bByName = !GOMGStore::Settings().bByName; GOMGClient::Persist(); Rebuild(); }, false)
			]
		]
		+ SVerticalBox::Slot().AutoHeight()[ Gap(6) ]
		// rows
		+ SVerticalBox::Slot().FillHeight(1.f)
		[
			SAssignNew(List, SListView<TSharedPtr<FGOMGRow>>)
			.ListViewStyle(&ListStyle())
			.ListItemsSource(&Rows)
			.SelectionMode(ESelectionMode::None)
			.OnGenerateRow(this, &SGOMGRoot::MakeRow)
		]
		// the selected place's actions
		+ SVerticalBox::Slot().AutoHeight()[ SAssignNew(SelectedBox, SBox) ];
}

TSharedRef<SWidget> SGOMGRoot::MakeRename()
{
	return SNew(SVerticalBox)
		+ SVerticalBox::Slot().AutoHeight()[ Txt(Tx(TEXT("Rename")), 14.5f, true, Ink) ]
		+ SVerticalBox::Slot().AutoHeight()[ Gap(8) ]
		+ SVerticalBox::Slot().AutoHeight()[ Field(RenameField, Tx(TEXT("A name for this place")), [this](const FText& T) { RenameText = T.ToString(); }) ]
		+ SVerticalBox::Slot().AutoHeight()[ Gap(8) ]
		+ SVerticalBox::Slot().AutoHeight()
		[
			SNew(SHorizontalBox)
			+ SHorizontalBox::Slot().FillWidth(1.f).Padding(FMargin(0, 0, 4, 0))[ Button(Tx(TEXT("Save name")), (uint8)EGOMGIcon::Check, 1, [this]() { DoRename(); }) ]
			+ SHorizontalBox::Slot().FillWidth(1.f).Padding(FMargin(4, 0, 0, 0))[ Button(Tx(TEXT("Cancel")), TOptional<uint8>(), 0, [this]() { Back(); }) ]
		]
		+ SVerticalBox::Slot().FillHeight(1.f)[ SNew(SSpacer) ];
}

TSharedRef<SWidget> SGOMGRoot::MakeFooter()
{
	return SNew(SVerticalBox)
		+ SVerticalBox::Slot().AutoHeight()[ Gap(8) ]
		+ SVerticalBox::Slot().AutoHeight()
		[
			SNew(SBox)
			.Visibility_Lambda([this]() { return FPlatformTime::Seconds() < StatusUntil ? EVisibility::Visible : EVisibility::Collapsed; })
			.Padding(FMargin(0, 0, 0, 6))
			[
				SNew(SHorizontalBox)
				+ SHorizontalBox::Slot().FillWidth(1.f).VAlign(VAlign_Center)
				[
					SNew(STextBlock).Font(Font(13)).AutoWrapText(true)
					.Text_Lambda([this]() { return Status; })
					.ColorAndOpacity_Lambda([this]() { return FSlateColor(bStatusBad ? BadText : Good); })
				]
				+ SHorizontalBox::Slot().AutoWidth().VAlign(VAlign_Center).Padding(FMargin(6, 0, 0, 0))
				[
					SNew(SBox).Visibility_Lambda([this]() { return Undone.IsSet() && View == EView::Places ? EVisibility::Visible : EVisibility::Collapsed; })
					[ Button(Tx(TEXT("Undo")), (uint8)EGOMGIcon::Undo, 0, [this]() { DoUndo(); }, true, TAttribute<FText>(), 96) ]
				]
			]
		]
		+ SVerticalBox::Slot().AutoHeight()[ SAssignNew(PromptsBox, SBox) ];
}

void SGOMGRoot::RebuildPrompts()
{
	if (!PromptsBox.IsValid()) return;
	TArray<TPair<FText, FText>> Keys;
	auto Add = [&Keys](const FText& K, const TCHAR* V) { Keys.Add(TPair<FText, FText>(K, Tx(V))); };
	if (GOMGClient::IsCapturing()) Add(Tx(TEXT("Esc")), TEXT("Cancel"));
	else if (!NoteKey.IsEmpty()) { Add(Tx(TEXT("Enter")), TEXT("Save")); Add(Tx(TEXT("Esc")), TEXT("Cancel")); }
	else if (View == EView::Rename) { Add(Tx(TEXT("Enter")), TEXT("Choose")); Add(Tx(TEXT("Esc")), TEXT("Cancel")); }
	else if (View == EView::Settings) Add(Tx(TEXT("Esc")), TEXT("Back"));
	else
	{
		Add(Tx(TEXT("Enter")), TEXT("Teleport"));
		Add(Tx(TEXT("↑↓")), TEXT("Move"));
		Add(Fmt(TEXT("Esc / {0}"), GOMGClient::PanelKey().GetDisplayName()), TEXT("Close"));
		if (GOMGStore::Settings().bClickTeleports) Add(Tx(TEXT("Right-click")), TEXT("Select"));
		else Add(Tx(TEXT("Double-click")), TEXT("Teleport"));
	}
	TSharedRef<SWrapBox> Wrap = SNew(SWrapBox).UseAllottedSize(true);
	for (const TPair<FText, FText>& K : Keys)
		Wrap->AddSlot().Padding(FMargin(0, 0, 12, 4))
			[
				SNew(SHorizontalBox)
				+ SHorizontalBox::Slot().AutoWidth().VAlign(VAlign_Center)[ KeyChip(K.Key) ]
				+ SHorizontalBox::Slot().AutoWidth().VAlign(VAlign_Center).Padding(FMargin(5, 0, 0, 0))[ Txt(K.Value, 12, false, Muted) ]
			];
	PromptsBox->SetContent(Wrap);
}

// ══ state ═════════════════════════════════════════════════════════════════════════════════════════════════════════════════════

TSharedPtr<SWidget> SGOMGRoot::GetFocusTarget() const { return Search; }

void SGOMGRoot::Tick(const FGeometry& AllottedGeometry, const double InCurrentTime, const float InDeltaTime)
{
	SCompoundWidget::Tick(AllottedGeometry, InCurrentTime, InDeltaTime);
	// the model's scale: screen height / 1080 × Text size, whatever the game's own UI scale makes of 1080
	const float Outer = FMath::Max(AllottedGeometry.Scale, 0.01f);   // the game's own UI scale (our scaler is inside this widget)
	const float PixelH = AllottedGeometry.GetLocalSize().Y * AllottedGeometry.Scale;
	const float Want = FMath::Clamp(PixelH / 1080.f, .7f, 3.f) * FMath::Clamp(GOMGStore::Settings().TextSize, GOMGStore::TextMin, GOMGStore::TextMax) / 100.f;
	Scale = Want / Outer;
	if (GOMGStore::Settings().bLeft != bLeftNow) PlacePanel();
	if (Slide < 1.f && Panel.IsValid())
	{
		Slide = FMath::Min(1.f, Slide + InDeltaTime / .18f);
		const float Eased = 1.f - (1.f - Slide) * (1.f - Slide);
		Panel->SetRenderTransform(FSlateRenderTransform(FVector2f((1.f - Eased) * (PanelW + 16) * (bLeftNow ? -1.f : 1.f), 0.f)));
	}
	if (bFocusNote && NoteField.IsValid())
	{
		bFocusNote = false;
		FSlateApplication::Get().SetKeyboardFocus(NoteField, EFocusCause::SetDirectly);
	}
	if (Pending)
	{
		TFunction<void()> Act = MoveTemp(Pending);
		Pending = nullptr;
		Act();
	}
}

void SGOMGRoot::SetPanelOpen(bool bOpenNow)
{
	bOpen = bOpenNow;
	Holder->SetVisibility(bOpen ? EVisibility::SelfHitTestInvisible : EVisibility::Collapsed);
	if (bOpen)
	{
		View = EView::Places;
		Slide = 0.f;
		ToastBox->SetVisibility(EVisibility::Collapsed);
		Rebuild();
		RebuildPrompts();
		if (FSlateApplication::IsInitialized()) FSlateApplication::Get().SetKeyboardFocus(Search, EFocusCause::SetDirectly);
	}
	else
	{
		NoteKey.Reset();
		StatusUntil = 0;
	}
}

void SGOMGRoot::PrefillLine(const FString& TpLine)
{
	Search->SetText(FText::FromString(TpLine));
	Filter.Reset();
	Rebuild();
}

void SGOMGRoot::ShowToast(const FText& Message, bool bBad)
{
	ToastText->SetText(Message);
	ToastText->SetColorAndOpacity(bBad ? BadText : Ink);
	ToastBox->SetVisibility(EVisibility::HitTestInvisible);
	if (ToastTimer.IsValid()) UnRegisterActiveTimer(ToastTimer.ToSharedRef());
	ToastTimer = RegisterActiveTimer(bBad ? 6.f : 4.f, FWidgetActiveTimerDelegate::CreateLambda([this](double, float)
	{
		ToastBox->SetVisibility(EVisibility::Collapsed);
		ToastTimer.Reset();
		return EActiveTimerReturnType::Stop;
	}));
}

void SGOMGRoot::ShowStatus(const FText& Message, bool bBad)
{
	Status = Message;
	bStatusBad = bBad;
	StatusUntil = FPlatformTime::Seconds() + (bBad ? 6.0 : 4.0);
	Undone.Reset();
}

bool SGOMGRoot::IsOpen(const FString& Key, bool bDefault) const
{
	return GOMGStore::Settings().Flipped.Contains(Key) ? !bDefault : bDefault;
}

void SGOMGRoot::Flip(const FString& Key)
{
	TSet<FString>& F = GOMGStore::Settings().Flipped;
	if (F.Contains(Key)) F.Remove(Key); else F.Add(Key);
	GOMGClient::Persist();
}

FText SGOMGRoot::CountText(int32 NFound, int32 Total) const
{
	// the one place a heading's count is worded: "145/200 found", as every GamesOMG Teleport mod and the Planet Crafter mod's
	// tree.foundOf (Jason 11-23 14:24 via the Bridge: "the widely most used easiest to understand at a glance"); nothing found yet:
	// a layer says how many it has, a group says nothing (the model's rule)
	return NFound > 0 ? Fmt(TEXT("{0}/{1} found"), NFound, Total) : FText::AsNumber(Total);
}

void SGOMGRoot::SetTab(ETab T)
{
	if (Tab == T) return;
	Tab = T;
	FocusKey.Reset();
	CancelNote();
	Rebuild();
}

FGOMGSpot* SGOMGRoot::SpotOf(const FGOMGRow& R) const
{
	FGOMGWorld& W = GOMGClient::World();
	TArray<FGOMGSpot>& L = R.bRecent ? W.Recent : W.Saved;
	return L.IsValidIndex(R.Index) ? &L[R.Index] : nullptr;
}

FGOMGSpot* SGOMGRoot::SelSpot() const
{
	if (Sel != ESel::Spot) return nullptr;
	FGOMGWorld& W = GOMGClient::World();
	TArray<FGOMGSpot>& L = bSelRecent ? W.Recent : W.Saved;
	return L.IsValidIndex(SelIndex) ? &L[SelIndex] : nullptr;
}

TSharedPtr<FGOMGRow> SGOMGRoot::Focused() const
{
	if (FocusKey.IsEmpty()) return nullptr;
	for (const TSharedPtr<FGOMGRow>& R : Rows) if (R->Key == FocusKey) return R;
	return nullptr;
}

FString SGOMGRoot::Meta(const FVector& Target, const FVector& Me, bool bMe, const FString& Lead, FString& Tail) const
{
	Tail.Reset();
	if (!bMe) return Lead;
	const double Flat = FVector::Dist2D(Target, Me);
	const double Dy = (Target.Z - Me.Z) / 100.0;
	// the model's RowMeta.ShowsHeight: only when the place is mostly straight up or down from you
	if (FMath::Abs(Dy) >= 20.0 && FMath::Abs(Dy) >= Flat / 100.0 * .5)
		Tail = FString::Printf(TEXT("%d m %s you"), FMath::RoundToInt(FMath::Abs(Dy)), Dy > 0 ? TEXT("above") : TEXT("below"));
	return Join(Lead, Distance(Flat));
}

// ══ the rows ══════════════════════════════════════════════════════════════════════════════════════════════════════════════════

void SGOMGRoot::OnTeleported() { if (Sel == ESel::Spot && bSelRecent) ClearSelection(); }

void SGOMGRoot::Rebuild()
{
	// the row with an open note box is made again below: focus its new box, not the old one
	if (!NoteKey.IsEmpty()) { bFocusNote = true; NoteField.Reset(); }
	const int32 N = GOMGPlaces::Num();
	// names are searched in the language they're shown in: rebuild the search text after a language switch
	const FString Culture = FInternationalization::Get().GetCurrentLanguage()->GetName();
	if (SearchCache.Num() != N || SearchCulture != Culture)
	{
		SearchCulture = Culture;
		SearchCache.SetNum(N);
		for (int32 i = 0; i < N; ++i) SearchCache[i] = GOMGPlaces::PlaceSearchText(i).ToString();
	}
	float Yaw = 0;
	bHaveMe = GOMGClient::PlayerFeet(MeFeet, Yaw);
	Tickable = 0; FoundTickable = 0;
	for (int32 i = 0; i < N; ++i)
		if (GOMGPlaces::TickOf(i) != EGOMGTick::Manual) { ++Tickable; if (GOMGClient::IsGameFound(i)) ++FoundTickable; }
	Rows.Reset();
	if (Tab == ETab::Mine) BuildMine(MeFeet, bHaveMe); else BuildMap(MeFeet, bHaveMe);
	if (!Focused().IsValid()) FocusKey.Reset();
	if (List.IsValid()) List->RequestListRefresh();
	RebuildSelected();
}

void SGOMGRoot::BuildMine(const FVector& Me, bool bMe)
{
	FGOMGWorld& W = GOMGClient::World();
	const bool bByName = GOMGStore::Settings().bByName;
	auto Matches = [this](const FGOMGSpot& S) { return Filter.IsEmpty() || S.Name.ToLower().Contains(Filter); };
	auto Sorted = [&](TArray<FGOMGSpot>& L, bool bSort)
	{
		TArray<int32> Out;
		for (int32 i = 0; i < L.Num(); ++i) if (Matches(L[i])) Out.Add(i);
		if (!bSort) return Out;
		if (bByName) Out.StableSort([&](int32 A, int32 B) { return L[A].Name.Compare(L[B].Name, ESearchCase::IgnoreCase) < 0; });
		else if (bMe) Out.StableSort([&](int32 A, int32 B) { return FVector::Dist2D(L[A].Feet, Me) < FVector::Dist2D(L[B].Feet, Me); });
		return Out;
	};
	auto AddNote = [this](const TCHAR* Text)
	{
		TSharedPtr<FGOMGRow> R = MakeShared<FGOMGRow>();
		R->Kind = FGOMGRow::EKind::Note; R->Text = Tx(Text); R->Key = FString::Printf(TEXT("note:%d"), Rows.Num());
		Rows.Add(R);
	};
	auto Head = [this](const TCHAR* Text)
	{
		TSharedPtr<FGOMGRow> R = MakeShared<FGOMGRow>();
		R->Kind = FGOMGRow::EKind::Head; R->Text = Tx(Text); R->Key = FString(TEXT("h:")) + Text;
		Rows.Add(R);
	};
	auto Spot = [this](int32 i, bool bRecent)
	{
		TSharedPtr<FGOMGRow> R = MakeShared<FGOMGRow>();
		R->Kind = FGOMGRow::EKind::Spot; R->Index = i; R->bRecent = bRecent;
		R->Key = FString::Printf(TEXT("%s:%d"), bRecent ? TEXT("t") : TEXT("m"), i);
		Rows.Add(R);
	};
	const TArray<int32> Saved = Sorted(W.Saved, true);
	if (W.Saved.Num() == 0)
	{
		TSharedPtr<FGOMGRow> R = MakeShared<FGOMGRow>();
		R->Kind = FGOMGRow::EKind::Note; R->Key = TEXT("note:empty");
		R->Text = Fmt(TEXT("Nothing saved yet. Save here keeps where you stand. {0} closes this."), GOMGClient::PanelKey().GetDisplayName());
		Rows.Add(R);
	}
	else if (Saved.Num() == 0) AddNote(TEXT("None of your places in this world has that in its name."));
	bool bAny = false;
	for (int32 i : Saved) if (W.Saved[i].bFav) { if (!bAny) Head(TEXT("Favourites")); bAny = true; Spot(i, false); }
	bAny = false;
	for (int32 i : Saved) if (!W.Saved[i].bFav) { if (!bAny) Head(TEXT("My places")); bAny = true; Spot(i, false); }
	bAny = false;
	for (int32 i : Sorted(W.Recent, false)) { if (!bAny) Head(TEXT("Recently travelled")); bAny = true; Spot(i, true); }
}

void SGOMGRoot::BuildMap(const FVector& Me, bool bMe)
{
	const FGOMGSettings& S = GOMGStore::Settings();
	const int32 N = GOMGPlaces::Num(), NC = GOMGPlaces::NumCats();
	const bool bFilter = !Filter.IsEmpty();
	TBitArray<> Found(false, N);
	TArray<int32> CatAll, CatFound;
	CatAll.SetNumZeroed(NC); CatFound.SetNumZeroed(NC);
	int32 FoundTotal = 0;
	for (int32 i = 0; i < N; ++i)
	{
		const int32 C = GOMGPlaces::Get(i).Cat;
		++CatAll[C];
		if (GOMGClient::IsFound(i)) { Found[i] = true; ++CatFound[C]; ++FoundTotal; }
	}
	const bool bDrawer = !S.bHideFound && FoundTotal > 0;
	auto Sort = [&](TArray<int32>& L, bool bName)
	{
		if (bName)
		{
			TArray<FString> Names;
			Names.SetNum(N);
			for (int32 i : L) Names[i] = PlaceName(i);
			L.StableSort([&](int32 A, int32 B)
			{
				const int32 C = Names[A].Compare(Names[B], ESearchCase::IgnoreCase);
				return C != 0 ? C < 0 : (bMe && FVector::Dist2D(PlaceFeet(A), Me) < FVector::Dist2D(PlaceFeet(B), Me));
			});
		}
		else if (bMe) L.StableSort([&](int32 A, int32 B) { return FVector::Dist2D(PlaceFeet(A), Me) < FVector::Dist2D(PlaceFeet(B), Me); });
	};
	auto Add = [this](FGOMGRow::EKind K, int32 Index, int32 Depth, bool bInFound, const FString& Key) -> TSharedPtr<FGOMGRow>
	{
		TSharedPtr<FGOMGRow> R = MakeShared<FGOMGRow>();
		R->Kind = K; R->Index = Index; R->Depth = Depth; R->bInFound = bInFound; R->Key = Key;
		Rows.Add(R);
		return R;
	};
	auto AddNote = [&](const FText& T, int32 Depth, bool bInFound)
	{
		Add(FGOMGRow::EKind::Note, INDEX_NONE, Depth, bInFound, FString::Printf(TEXT("note:%d"), Rows.Num()))->Text = T;
	};
	auto Place = [&](int32 i, int32 Depth, bool bInFound, const TCHAR* Prefix)
	{
		Add(FGOMGRow::EKind::Place, i, Depth, bInFound, FString(Prefix) + GOMGPlaces::IdOf(i));
	};

	if (bFilter)
	{
		// a search: one flat list of matches across every layer, nearest first; the found ones in the drawer, open
		TArray<int32> Hits, FHits;
		for (int32 i = 0; i < N; ++i)
			if (SearchCache[i].Contains(Filter)) (Found[i] ? FHits : Hits).Add(i);
		Sort(Hits, S.bByName);
		const int32 Total = Hits.Num();
		for (int32 k = 0; k < FMath::Min(Total, FindMax); ++k) Place(Hits[k], 0, false, TEXT("s:"));
		if (Total == 0 && (FHits.Num() == 0 || !bDrawer)) AddNote(Tx(TEXT("Nothing in this world by that name.")), 0, false);
		else if (Total > FindMax) AddNote(Fmt(TEXT("The nearest {0} of {1}. A longer name narrows it."), FindMax, Total), 0, false);
		if (bDrawer && FHits.Num() > 0)
		{
			TSharedPtr<FGOMGRow> F = Add(FGOMGRow::EKind::Found, INDEX_NONE, 0, true, TEXT("found"));
			F->bOpen = true; F->Count = FHits.Num();
			Sort(FHits, S.bByName);
			for (int32 k = 0; k < FMath::Min(FHits.Num(), FindMax); ++k) Place(FHits[k], 1, true, TEXT("d:"));
			if (FHits.Num() > FindMax) AddNote(Fmt(TEXT("The nearest {0} of {1}. A longer name narrows it."), FindMax, FHits.Num()), 1, true);
		}
		return;
	}

	// Nearest, on top: the nearest places on the layers ticked on
	{
		TSharedPtr<FGOMGRow> R = Add(FGOMGRow::EKind::Nearest, INDEX_NONE, 0, false, TEXT("near"));
		R->bOpen = IsOpen(TEXT("near"), true);
		int32 On = 0;
		for (int32 C = 0; C < NC; ++C) if (S.On.Contains(GOMGPlaces::Cat(C).Key)) ++On;
		R->Count = On;
		if (R->bOpen)
		{
			if (On == 0) AddNote(Tx(TEXT("Tick a layer below and its nearest places show here.")), 1, false);
			else
			{
				TArray<int32> Near;
				for (int32 i = 0; i < N; ++i) if (!Found[i] && S.On.Contains(GOMGPlaces::Cat(GOMGPlaces::Get(i).Cat).Key)) Near.Add(i);
				Sort(Near, false);   // Nearest stays nearest whatever the sort
				for (int32 k = 0; k < FMath::Min(Near.Num(), NearestMax); ++k) Place(Near[k], 1, false, TEXT("n:"));
				if (Near.Num() == 0) AddNote(Tx(TEXT("Every place on your ticked layers is marked found.")), 1, false);
			}
		}
	}

	// each group folds; each layer opens its own places right under it
	for (int32 G = 0; G < GOMGPlaces::NumGroups(); ++G)
	{
		TArray<int32> Left;
		int32 GAll = 0, GFound = 0;
		for (int32 C = 0; C < NC; ++C)
		{
			if (GOMGPlaces::Cat(C).Group != G) continue;
			GAll += CatAll[C]; GFound += CatFound[C];
			if (CatAll[C] > CatFound[C]) Left.Add(C);
		}
		if (Left.Num() == 0) continue;   // a group with every place found leaves the list
		const FString GKey = FString::Printf(TEXT("g:%d"), G);
		TSharedPtr<FGOMGRow> GR = Add(FGOMGRow::EKind::Group, G, 0, false, GKey);
		GR->bOpen = IsOpen(GKey, false); GR->Count = GAll; GR->NFound = GFound;
		if (!GR->bOpen) continue;
		for (int32 C : Left)
		{
			const FString CK = GOMGPlaces::Cat(C).Key;
			TSharedPtr<FGOMGRow> LR = Add(FGOMGRow::EKind::Layer, C, 1, false, TEXT("l:") + CK);
			LR->bOpen = IsOpen(TEXT("l:") + CK, false); LR->Count = CatAll[C]; LR->NFound = CatFound[C]; LR->bOn = S.On.Contains(CK);
			if (!LR->bOpen) continue;
			TArray<int32> L;
			for (int32 i = 0; i < N; ++i) if (!Found[i] && GOMGPlaces::Get(i).Cat == C) L.Add(i);
			Sort(L, S.bByName);
			const int32* CapAt = Cap.Find(CK);
			const int32 Show = FMath::Min(L.Num(), CapAt ? *CapAt : LayerFirst);
			for (int32 k = 0; k < Show; ++k) Place(L[k], 2, false, TEXT("p:"));
			if (L.Num() > Show) { TSharedPtr<FGOMGRow> M = Add(FGOMGRow::EKind::More, C, 2, false, TEXT("more:") + CK); M->Left = L.Num() - Show; }
		}
	}

	// the Found drawer, at the bottom: the same headings, only those with a found place under them
	if (!bDrawer) return;
	TSharedPtr<FGOMGRow> F = Add(FGOMGRow::EKind::Found, INDEX_NONE, 0, true, TEXT("found"));
	F->bOpen = IsOpen(TEXT("found"), false); F->Count = FoundTotal;
	if (!F->bOpen) return;
	for (int32 G = 0; G < GOMGPlaces::NumGroups(); ++G)
	{
		TArray<int32> Has;
		int32 InGroup = 0;
		for (int32 C = 0; C < NC; ++C) if (GOMGPlaces::Cat(C).Group == G && CatFound[C] > 0) { Has.Add(C); InGroup += CatFound[C]; }
		if (Has.Num() == 0) continue;
		const FString GKey = FString::Printf(TEXT("fg:%d"), G);
		TSharedPtr<FGOMGRow> GR = Add(FGOMGRow::EKind::Group, G, 1, true, GKey);
		GR->bOpen = IsOpen(GKey, true); GR->Count = InGroup;
		if (!GR->bOpen) continue;
		for (int32 C : Has)
		{
			const FString CK = GOMGPlaces::Cat(C).Key;
			TSharedPtr<FGOMGRow> LR = Add(FGOMGRow::EKind::Layer, C, 2, true, TEXT("fl:") + CK);
			LR->bOpen = IsOpen(TEXT("fl:") + CK, true); LR->Count = CatFound[C];
			if (!LR->bOpen) continue;
			TArray<int32> L;
			for (int32 i = 0; i < N; ++i) if (Found[i] && GOMGPlaces::Get(i).Cat == C) L.Add(i);
			Sort(L, S.bByName);
			const int32* CapAt = Cap.Find(TEXT("f") + CK);
			const int32 Show = FMath::Min(L.Num(), CapAt ? *CapAt : LayerFirst);
			for (int32 k = 0; k < Show; ++k) Place(L[k], 3, true, TEXT("d:"));
			if (L.Num() > Show) { TSharedPtr<FGOMGRow> M = Add(FGOMGRow::EKind::More, C, 3, true, TEXT("fmore:") + CK); M->Left = L.Num() - Show; }
		}
	}
}

TSharedRef<ITableRow> SGOMGRoot::MakeRow(TSharedPtr<FGOMGRow> Row, const TSharedRef<STableViewBase>& Owner)
{
	return SNew(STableRow<TSharedPtr<FGOMGRow>>, Owner).Style(&RowStyle()).Padding(FMargin(0)).ShowSelection(false)
		[ MakeRowContent(Row) ];
}

TSharedRef<SWidget> SGOMGRoot::MakeRowContent(const TSharedPtr<FGOMGRow>& Row)
{
	switch (Row->Kind)
	{
	case FGOMGRow::EKind::Head: return Heading(Row->Text);
	case FGOMGRow::EKind::Note:
		return SNew(SBox).Padding(FMargin(Row->Depth > 0 ? 30 : 4, 4, 4, 4))
			[ SNew(STextBlock).Text(Row->Text).Font(Font(12.5f)).ColorAndOpacity(Muted).AutoWrapText(true) ];
	case FGOMGRow::EKind::More:
	{
		const FString CK = (Row->bInFound ? TEXT("f") : TEXT("")) + FString(GOMGPlaces::Cat(Row->Index).Key);
		return SNew(SBox).Padding(FMargin(Row->bInFound ? 42 : 30, 0, 0, 0))
			[
				Button(Fmt(TEXT("Show {0} more · {1} left"), FMath::Min(LayerMore, Row->Left), Row->Left), TOptional<uint8>(), 3,
					[this, CK]() { int32& C = Cap.FindOrAdd(CK, LayerFirst); C += LayerMore; Rebuild(); })
			];
	}
	case FGOMGRow::EKind::Spot:
	case FGOMGRow::EKind::Place: return PlaceRow(Row);
	default: return FoldRow(Row);
	}
}

// a group / layer header: a chevron, the name, a count; a layer in the list has its "on" box (it feeds Nearest)
TSharedRef<SWidget> SGOMGRoot::FoldRow(const TSharedPtr<FGOMGRow>& Row)
{
	const bool bStrong = Row->Kind != FGOMGRow::EKind::Layer;
	const float Indent = Row->Kind == FGOMGRow::EKind::Layer ? (Row->bInFound ? 24 : 12) : (Row->bInFound && Row->Kind == FGOMGRow::EKind::Group ? 12 : 0);
	FText Label, Count;
	switch (Row->Kind)
	{
	case FGOMGRow::EKind::Nearest:
		Label = Tx(TEXT("Nearest"));
		if (Row->Count == 1) Count = Tx(TEXT("on your ticked layer"));
		else if (Row->Count > 1) Count = Fmt(TEXT("on your {0} ticked layers"), Row->Count);
		break;
	case FGOMGRow::EKind::Found:
		Label = Tx(TEXT("Found"));
		Count = Filter.IsEmpty() ? FText::AsNumber(Row->Count) : Fmt(TEXT("{0} matching"), Row->Count);
		break;
	case FGOMGRow::EKind::Group:
		Label = GOMGPlaces::GroupName(Row->Index);
		Count = Row->bInFound ? FText::AsNumber(Row->Count) : Row->NFound > 0 ? CountText(Row->NFound, Row->Count) : FText::GetEmpty();
		break;
	default:
		Label = GOMGPlaces::CatName(Row->Index);
		Count = Row->bInFound ? FText::AsNumber(Row->Count) : CountText(Row->NFound, Row->Count);
		break;
	}
	const FString Key = Row->Key;
	TSharedRef<SHorizontalBox> Ln = SNew(SHorizontalBox);
	Ln->AddSlot().AutoWidth()[ GapW(8 + Indent) ];
	if (Row->Kind == FGOMGRow::EKind::Layer && !Row->bInFound)
	{
		const FString CK = GOMGPlaces::Cat(Row->Index).Key;
		const bool bOn = Row->bOn;
		Ln->AddSlot().AutoWidth().VAlign(VAlign_Center).Padding(FMargin(0, 0, 8, 0))
			[
				SNew(SBox).WidthOverride(22).HeightOverride(22).ToolTipText(Tx(TEXT("On: its nearest places show under Nearest")))
				[
					SNew(SGOMGHit)
					.Normal(bOn ? Plate(Alpha(Orange, .3f), 5) : Plate(Surface, 5)).Hover(bOn ? Plate(Alpha(Orange, .3f), 5) : Plate(Raised, 5))
					.OnClick(Do([this, CK]()
					{
						Later([this, CK]()
						{
							// ticking a layer on also opens it; off leaves it as it is
							TSet<FString>& On = GOMGStore::Settings().On;
							if (On.Contains(CK)) On.Remove(CK);
							else { On.Add(CK); if (!IsOpen(TEXT("l:") + CK, false)) Flip(TEXT("l:") + CK); }
							GOMGClient::Persist();
							Rebuild();
						});
					}))
					[ SNew(SBox).HAlign(HAlign_Center).VAlign(VAlign_Center).Visibility(bOn ? EVisibility::HitTestInvisible : EVisibility::Hidden)
						[ Img(GOMGTheme::Icon(EGOMGIcon::Check), 14, FSlateColor(Orange)) ] ]
				]
			];
	}
	Ln->AddSlot().AutoWidth().VAlign(VAlign_Center).Padding(FMargin(0, 0, 8, 0))
		[ Img(GOMGTheme::Icon(Row->bOpen ? EGOMGIcon::ChevronDown : EGOMGIcon::ChevronRight), 14, FSlateColor(Muted)) ];
	Ln->AddSlot().FillWidth(1.f).VAlign(VAlign_Center)
		[
			SNew(STextBlock).Text(bStrong ? Label.ToUpper() : Label).Font(Font(bStrong ? 11.5f : 14.5f, bStrong))
			.ColorAndOpacity(bStrong ? Muted : Ink).OverflowPolicy(ETextOverflowPolicy::Ellipsis)
		];
	Ln->AddSlot().AutoWidth().VAlign(VAlign_Center).Padding(FMargin(8, 0, 10, 0))[ Txt(Count, 12, false, Muted) ];
	TSharedRef<SWidget> Fold = SNew(SOverlay)
		+ SOverlay::Slot()
		[
			SNew(SBox).HeightOverride(FoldH)
			[
				SNew(SGOMGHit).Normal(None()).Hover(Plate(Alpha(Raised, .7f), 8))
				.OnClick(Do([this, Row]() { Later([this, Row]() { FocusKey = Row->Key; Toggle(Row); }); }))
				[ Ln ]
			]
		]
		+ SOverlay::Slot()
		[
			SNew(SBorder).BorderImage(Ring(Blue, 2, 9)).Visibility_Lambda([this, Key]() { return FocusKey == Key ? EVisibility::HitTestInvisible : EVisibility::Collapsed; })
		];
	if (Row->Kind == FGOMGRow::EKind::Found) return SNew(SVerticalBox) + SVerticalBox::Slot().AutoHeight()[ Gap(10) ] + SVerticalBox::Slot().AutoHeight()[ Fold ];
	return Fold;
}

// one place: [found box, or star / clock / dot] name · meta [+]. Click teleports (or selects: Settings), right-click selects.
// A noted row shows its note under the name in yellow, with a yellow edge, so it stands out while you scroll.
TSharedRef<SWidget> SGOMGRoot::PlaceRow(const TSharedPtr<FGOMGRow>& Row)
{
	const bool bPlace = Row->Kind == FGOMGRow::EKind::Place;
	const int32 i = Row->Index;
	FGOMGSpot* Spot = bPlace ? nullptr : SpotOf(*Row);
	if (!bPlace && !Spot) return SNew(SSpacer);
	const bool bNoteable = bPlace || !Row->bRecent;
	const FString NoteNow = NoteOf(Row);
	const bool bEditing = bNoteable && NoteKey == Row->Key;
	const bool bHasNote = !NoteNow.IsEmpty() && !bEditing;
	const bool bCanGo = bPlace ? !GOMGPlaces::HasNoLanding(i) : true;
	const bool bGameFound = bPlace && GOMGClient::IsGameFound(i);
	const bool bFound = bPlace && GOMGClient::IsFound(i);
	const float Indent = Row->Depth >= 3 ? 42 : Row->Depth == 2 ? 30 : Row->Depth == 1 ? 12 : 0;
	const FString Key = Row->Key;

	FString Name, Lead, Tail, MetaLine;
	FVector Target;
	if (bPlace)
	{
		const FGOMGPlaceDef& P = GOMGPlaces::Get(i);
		Name = PlaceName(i);
		Target = PlaceFeet(i);
		FString Detail = IsTapes(i) ? GOMGPlaces::RegionName(P.Region).ToString() : GOMGPlaces::PlaceDetail(i).ToString();
		if (Row->Depth < 2 && !IsTapes(i)) Lead = Join(GOMGPlaces::CatName(P.Cat).ToString(), Detail); else Lead = Detail;
		Lead = bCanGo ? Meta(Target, MeFeet, bHaveMe, Lead, Tail) : FString(TEXT("no checked landing yet"));
	}
	else
	{
		Name = Spot->Name;
		Target = Spot->Feet;
		Lead = Meta(Target, MeFeet, bHaveMe, FString(), Tail);
	}
	// the meta line takes at most 55 % of what's left beside the lead mark, losing the height first, then the layer
	const float Room = InnerW - 12 /* the list's scroll bar */ - (bNoteable ? 30 : 0) - (10 + Indent + 28);
	MetaLine = Fit(Lead, Tail, Room * .55f, Font(12));
	const FString FullMeta = Join(Lead, Tail);

	// the lead mark
	TSharedRef<SWidget> LeadMark = SNullWidget::NullWidget;
	float LeadPad = 10 + Indent;
	if (bPlace)
	{
		LeadPad = 6 + Indent;
		LeadMark = SNew(SBox).WidthOverride(26).HeightOverride(26)
			.ToolTipText(Tx(bGameFound ? TEXT("Collected in this save") : bFound ? TEXT("Marked found by you: click to put it back in the list") : TEXT("Mark found: moves it to Found")))
			[
				SNew(SGOMGHit).IsEnabled(!bGameFound)
				.Normal(bFound ? Plate(Alpha(Good, .22f), 6) : Plate(Surface, 6)).Hover(bFound ? Plate(Alpha(Good, .3f), 6) : Plate(Raised, 6))
				.Off(Plate(Alpha(Good, .22f), 6))
				.OnClick(Do([this, i]() { Later([i]() { GOMGClient::ToggleManualFound(i); }); }))
				[ SNew(SBox).HAlign(HAlign_Center).VAlign(VAlign_Center).Visibility(bFound ? EVisibility::HitTestInvisible : EVisibility::Hidden)
					[ Img(GOMGTheme::Icon(EGOMGIcon::Check), 16, FSlateColor(Good)) ] ]
			];
	}
	else if (Row->bRecent || Spot->bFav)
		LeadMark = Img(GOMGTheme::Icon(Row->bRecent ? EGOMGIcon::Clock : EGOMGIcon::StarFilled), 16, FSlateColor(Row->bRecent ? Faint : Orange));
	else
		LeadMark = SNew(SBox).WidthOverride(16).HeightOverride(16).HAlign(HAlign_Center).VAlign(VAlign_Center)
			[ SNew(SBox).WidthOverride(9).HeightOverride(9)[ SNew(SImage).Image(Plate(Orange, 4.5f)) ] ];

	// the main line: lead, name, meta, the + for a note
	TSharedRef<SHorizontalBox> Main = SNew(SHorizontalBox)
		+ SHorizontalBox::Slot().AutoWidth().VAlign(VAlign_Center).Padding(FMargin(LeadPad, 0, bPlace ? 2 : 10, 0))[ LeadMark ]
		+ SHorizontalBox::Slot().FillWidth(1.f).VAlign(VAlign_Center)
		[
			SNew(STextBlock).Text(Tx(Name)).Font(Font(15)).ColorAndOpacity(bCanGo ? Ink : Muted).OverflowPolicy(ETextOverflowPolicy::Ellipsis)
		]
		+ SHorizontalBox::Slot().AutoWidth().VAlign(VAlign_Center).Padding(FMargin(12, 0, 4, 0))
		[
			SNew(SOverlay)
			+ SOverlay::Slot().VAlign(VAlign_Center)[ Txt(Tx(MetaLine), 12, false, Muted) ]
			// the focused row says what Enter does
			+ SOverlay::Slot().HAlign(HAlign_Right).VAlign(VAlign_Top).Padding(FMargin(0, 3, 0, 0))
			[
				SNew(SBox).HeightOverride(16).Visibility_Lambda([this, Key, bCanGo]() { return bCanGo && FocusKey == Key ? EVisibility::HitTestInvisible : EVisibility::Collapsed; })
				[ SNew(SBorder).BorderImage(Plate(Alpha(Blue, .25f), 4)).Padding(FMargin(4, 0)).VAlign(VAlign_Center)[ Txt(Tx(TEXT("Enter")), 11, true, Ink) ] ]
			]
		];
	if (bNoteable)
	{
		Main->AddSlot().AutoWidth().VAlign(VAlign_Center).Padding(FMargin(0, 0, 4, 0))
			[
				SNew(SBox).WidthOverride(24).HeightOverride(24).ToolTipText(Tx(NoteNow.IsEmpty() ? TEXT("Add a short note") : TEXT("Change the note")))
				[
					SNew(SGOMGHit)
					.Normal(bEditing ? Plate(Raised, 6) : Plate(Alpha(Raised, .45f), 6)).Hover(Plate(Raised, 6))
					.Fg(FSlateColor(bEditing ? Ink : !NoteNow.IsEmpty() ? GOMGTheme::Note : Muted)).HoverFg(FSlateColor(Ink))
					.OnClick(Do([this, Row, bEditing]() { Later([this, Row, bEditing]() { if (bEditing) CancelNote(); else OpenNote(Row); }); }))
					[
						SNew(SBox).HAlign(HAlign_Center).VAlign(VAlign_Center)
						[
							!NoteNow.IsEmpty()
							? Img(GOMGTheme::Icon(EGOMGIcon::Pencil), 14)
							: StaticCastSharedRef<SWidget>(SNew(STextBlock).Text(Tx(TEXT("+"))).Font(Font(17, true)))
						]
					]
				]
			];
	}

	TSharedRef<SVerticalBox> Body = SNew(SVerticalBox)
		+ SVerticalBox::Slot().AutoHeight()
		[
			SNew(SOverlay)
			+ SOverlay::Slot()
			[
				SNew(SBox).HeightOverride(RowH)
				[
					SNew(SGOMGHit).ToolTipText(Tx(FullMeta))
					.Normal(None()).Hover_Lambda([this, Row]() { return (Sel == ESel::Place && Row->Kind == FGOMGRow::EKind::Place && SelIndex == Row->Index)
						|| (Sel == ESel::Spot && Row->Kind == FGOMGRow::EKind::Spot && SelIndex == Row->Index && bSelRecent == Row->bRecent) ? None() : Plate(Alpha(Raised, .8f), 8); })
					.OnClick(Do([this, Row]() { Later([this, Row]() { FocusKey = Row->Key; Activate(Row); }); }))
					.OnRightClick(Do([this, Row]() { Later([this, Row]() { FocusKey = Row->Key; Select(Row); }); }))
					[ Main ]
				]
			]
			+ SOverlay::Slot()
			[
				SNew(SBorder).BorderImage(Ring(Blue, 2, 9)).Visibility_Lambda([this, Key]() { return FocusKey == Key ? EVisibility::HitTestInvisible : EVisibility::Collapsed; })
			]
		];
	if (bHasNote)
	{
		Body->AddSlot().AutoHeight().Padding(FMargin(LeadPad + (bPlace ? 28 : 26), 0, 34, 4))
			[
				SNew(SGOMGHit).OnClick(Do([this, Row]() { Later([this, Row]() { OpenNote(Row); }); }))
				[
					SNew(SHorizontalBox)
					+ SHorizontalBox::Slot().AutoWidth().VAlign(VAlign_Center).Padding(FMargin(0, 0, 4, 0))[ Img(GOMGTheme::Icon(EGOMGIcon::Pencil), 12, FSlateColor(GOMGTheme::Note)) ]
					+ SHorizontalBox::Slot().FillWidth(1.f).VAlign(VAlign_Center)
					[ SNew(STextBlock).Text(Tx(NoteNow)).Font(Font(12.5f)).ColorAndOpacity(GOMGTheme::Note).OverflowPolicy(ETextOverflowPolicy::Ellipsis) ]
				]
			];
	}
	if (bEditing)
	{
		// the note box: one line (Enter saves, Esc cancels, empty deletes), then two ready-made notes, and Delete when there is one
		TSharedRef<SHorizontalBox> Presets = SNew(SHorizontalBox);
		for (const TCHAR* P : { TEXT("Come back later"), TEXT("Locked") })
		{
			const FString Text = P;
			Presets->AddSlot().AutoWidth().Padding(FMargin(0, 0, 6, 0))
				[
					SNew(SBox).HeightOverride(26)
					[
						SNew(SGOMGHit).Normal(Plate(Surface, 6)).Hover(Plate(Raised, 6)).Padding(FMargin(8, 0)).Fg(FSlateColor(Ink))
						.OnClick(Do([this, Text]() { Later([this, Text]() { SaveNote(Text); }); }))
						[ SNew(SBox).VAlign(VAlign_Center)[ Txt(Tx(Text), 12.5f, false) ] ]
					]
				];
		}
		if (!NoteNow.IsEmpty())
			Presets->AddSlot().AutoWidth()
				[
					SNew(SBox).HeightOverride(26)
					[
						SNew(SGOMGHit).Normal(Plate(Surface, 6)).Hover(Plate(Raised, 6)).Padding(FMargin(8, 0)).Fg(FSlateColor(BadText))
						.OnClick(Do([this]() { Later([this]() { SaveNote(FString()); }); }))
						[ SNew(SBox).VAlign(VAlign_Center)[ Txt(Tx(TEXT("Delete note")), 12.5f, false) ] ]
					]
				];
		TSharedPtr<SEditableTextBox> Box;
		Body->AddSlot().AutoHeight().Padding(FMargin(LeadPad, 2, 4, 6))
			[
				SNew(SVerticalBox)
				+ SVerticalBox::Slot().AutoHeight()
				[
					SNew(SBox).HeightOverride(FieldH)
					[
						SAssignNew(Box, SEditableTextBox).Style(&InputStyle()).Font(Font(14.5f)).Text(Tx(NoteText))
						.HintText(Tx(TEXT("A short note (Enter saves, Esc cancels)")))
						.OnTextChanged_Lambda([this](const FText& T) { NoteText = T.ToString(); })
					]
				]
				+ SVerticalBox::Slot().AutoHeight().Padding(FMargin(0, 6, 0, 0))[ Presets ]
			];
		NoteField = Box;
	}

	return SNew(SBorder)
		.BorderImage_Lambda([this, Row, bHasNote]()
		{
			const bool bSel = (Sel == ESel::Place && Row->Kind == FGOMGRow::EKind::Place && SelIndex == Row->Index)
				|| (Sel == ESel::Spot && Row->Kind == FGOMGRow::EKind::Spot && SelIndex == Row->Index && bSelRecent == Row->bRecent);
			return bSel ? Plate(Alpha(Orange, .16f), 8) : bHasNote ? Plate(Alpha(GOMGTheme::Note, .07f), 8) : None();
		})
		.Padding(0)
		[
			SNew(SOverlay)
			+ SOverlay::Slot()[ Body ]
			// the selected row's orange edge; a noted row's yellow one
			+ SOverlay::Slot().HAlign(HAlign_Left).Padding(FMargin(0, 6))
			[
				SNew(SBox).WidthOverride(3)
				.Visibility_Lambda([this, Row, bHasNote]()
				{
					const bool bSel = (Sel == ESel::Place && Row->Kind == FGOMGRow::EKind::Place && SelIndex == Row->Index)
						|| (Sel == ESel::Spot && Row->Kind == FGOMGRow::EKind::Spot && SelIndex == Row->Index && bSelRecent == Row->bRecent);
					return bSel || bHasNote ? EVisibility::HitTestInvisible : EVisibility::Collapsed;
				})
				[
					SNew(SImage).Image_Lambda([this, Row]()
					{
						const bool bSel = (Sel == ESel::Place && Row->Kind == FGOMGRow::EKind::Place && SelIndex == Row->Index)
							|| (Sel == ESel::Spot && Row->Kind == FGOMGRow::EKind::Spot && SelIndex == Row->Index && bSelRecent == Row->bRecent);
						return Plate(bSel ? Orange : GOMGTheme::Note, 1.5f);
					})
				]
			]
		];
}

// ══ the selected place's actions, below the list ═════════════════════════════════════════════════════════════════════════════

void SGOMGRoot::RebuildSelected()
{
	if (!SelectedBox.IsValid()) return;
	FGOMGSpot* Spot = SelSpot();
	const bool bPlace = Sel == ESel::Place && SelIndex >= 0 && SelIndex < GOMGPlaces::Num();
	if (!Spot && !bPlace) { Sel = ESel::None; SelectedBox->SetContent(SNullWidget::NullWidget); return; }
	FString Name, Detail;
	bool bCanGo = true;
	TArray<TTuple<FText, uint8, TFunction<void()>, bool>> More;
	auto Act = [&More](const TCHAR* Label, EGOMGIcon Icon, TFunction<void()> F, bool bOn = true)
	{
		More.Add(MakeTuple(Tx(Label), (uint8)Icon, MoveTemp(F), bOn));
	};
	if (Spot)
	{
		Name = Spot->Name;
		if (!bSelRecent)
		{
			Act(Spot->bFav ? TEXT("Unfavourite") : TEXT("Favourite"), Spot->bFav ? EGOMGIcon::StarFilled : EGOMGIcon::Star, [this]() { DoFav(); });
			Act(TEXT("Rename"), EGOMGIcon::Pencil, [this]() { OpenRename(); });
		}
	}
	else
	{
		const int32 i = SelIndex;
		Name = PlaceName(i);
		const FGOMGPlaceDef& P = GOMGPlaces::Get(i);
		Detail = Join(GOMGPlaces::CatName(P.Cat).ToString(), IsTapes(i) ? GOMGPlaces::RegionName(P.Region).ToString() : GOMGPlaces::PlaceDetail(i).ToString());
		bCanGo = !GOMGPlaces::HasNoLanding(i);
		if (!GOMGClient::IsGameFound(i))
			Act(GOMGClient::IsFound(i) ? TEXT("Unmark") : TEXT("Mark found"), EGOMGIcon::Found, [this]() { DoFound(); });
		Act(TEXT("Save to My places"), EGOMGIcon::Star, [this]() { DoFav(); }, bCanGo);
	}
	Act(TEXT("Show on map"), EGOMGIcon::Map, [this]() { DoShowOnMap(); });
	if (Spot) Act(TEXT("Delete"), EGOMGIcon::Trash, [this]() { DoDelete(); });

	TSharedRef<SVerticalBox> Box = SNew(SVerticalBox)
		+ SVerticalBox::Slot().AutoHeight()[ Gap(8) ]
		+ SVerticalBox::Slot().AutoHeight()[ SNew(SBox).HeightOverride(1)[ SNew(SImage).Image(Plate(GOMGTheme::Line, 0)) ] ]
		+ SVerticalBox::Slot().AutoHeight()[ Gap(8) ]
		+ SVerticalBox::Slot().AutoHeight()[ SNew(STextBlock).Text(Tx(Name)).Font(Font(14.5f, true)).ColorAndOpacity(Ink).AutoWrapText(true) ];
	if (!Detail.IsEmpty())
		Box->AddSlot().AutoHeight()[ SNew(STextBlock).Text(Tx(Detail)).Font(Font(12.5f)).ColorAndOpacity(Muted).AutoWrapText(true) ];
	Box->AddSlot().AutoHeight()[ Gap(6) ];
	Box->AddSlot().AutoHeight()
		[
			ButtonDyn(TAttribute<FText>::CreateLambda([]() { return Tx(GOMGClient::IsBusy() ? TEXT("Preparing destination…") : TEXT("Teleport")); }),
				(uint8)EGOMGIcon::Go, 1, [this]() { DoTeleport(); },
				TAttribute<bool>::CreateLambda([bCanGo]() { return bCanGo && !GOMGClient::IsBusy(); }))
		];
	Box->AddSlot().AutoHeight()[ Gap(6) ];
	Box->AddSlot().AutoHeight()[ FlowButtons(More) ];
	SelectedBox->SetContent(Box);
}

// ══ actions ═══════════════════════════════════════════════════════════════════════════════════════════════════════════════════

void SGOMGRoot::Go(const TSharedPtr<FGOMGRow>& Row)
{
	if (Row->Kind == FGOMGRow::EKind::Place)
	{
		if (GOMGPlaces::HasNoLanding(Row->Index)) { ShowStatus(Tx(TEXT("This place has no checked landing yet, so no teleport. Pick one nearby.")), true); return; }
		GOMGClient::GoToPlace(Row->Index);
	}
	else if (const FGOMGSpot* S = SpotOf(*Row))
	{
		const int32 Pin = S->Pin.IsEmpty() ? INDEX_NONE : GOMGPlaces::FindById(S->Pin);
		if (Pin != INDEX_NONE) GOMGClient::GoToPlace(Pin);   // saved from the map: its checked landing, as the map place
		else GOMGClient::GoToSpot(*S);
	}
}

void SGOMGRoot::Activate(const TSharedPtr<FGOMGRow>& Row)
{
	if (Row->Kind == FGOMGRow::EKind::Place || Row->Kind == FGOMGRow::EKind::Spot)
	{
		const double Now = FPlatformTime::Seconds();
		const bool bAgain = LastClickKey == Row->Key && Now - LastClickAt < .4;
		LastClickKey = Row->Key;
		LastClickAt = Now;
		Select(Row);
		if (bAgain || GOMGStore::Settings().bClickTeleports) Go(Row);
		return;
	}
	if (Row->Kind == FGOMGRow::EKind::More)
	{
		const FString CK = (Row->bInFound ? TEXT("f") : TEXT("")) + FString(GOMGPlaces::Cat(Row->Index).Key);
		int32& C = Cap.FindOrAdd(CK, LayerFirst);
		C += LayerMore;
		Rebuild();
		return;
	}
	Toggle(Row);
}

void SGOMGRoot::Select(const TSharedPtr<FGOMGRow>& Row)
{
	if (Row->Kind == FGOMGRow::EKind::Place) { Sel = ESel::Place; SelIndex = Row->Index; bSelRecent = false; }
	else if (Row->Kind == FGOMGRow::EKind::Spot) { Sel = ESel::Spot; SelIndex = Row->Index; bSelRecent = Row->bRecent; }
	else return;
	RebuildSelected();
}

void SGOMGRoot::ClearSelection() { Sel = ESel::None; SelIndex = INDEX_NONE; RebuildSelected(); }

void SGOMGRoot::Toggle(const TSharedPtr<FGOMGRow>& Row)
{
	if (!Filter.IsEmpty()) return;   // a search lists matches flat
	switch (Row->Kind)
	{
	case FGOMGRow::EKind::Nearest: case FGOMGRow::EKind::Found: case FGOMGRow::EKind::Group: Flip(Row->Key); break;
	case FGOMGRow::EKind::Layer:
		Flip(Row->Key);
		if (Row->bOpen) Cap.Remove((Row->bInFound ? TEXT("f") : TEXT("")) + FString(GOMGPlaces::Cat(Row->Index).Key));   // folding resets Show more
		break;
	default: return;
	}
	Rebuild();
}

void SGOMGRoot::MoveFocus(int32 Delta)
{
	if (Rows.Num() == 0 || Delta == 0 || View != EView::Places || !NoteKey.IsEmpty()) return;
	auto Stops = [this](int32 k)
	{
		const FGOMGRow::EKind K = Rows[k]->Kind;
		return K != FGOMGRow::EKind::Head && K != FGOMGRow::EKind::Note;
	};
	int32 Cur = INDEX_NONE;
	for (int32 k = 0; k < Rows.Num(); ++k) if (Rows[k]->Key == FocusKey) { Cur = k; break; }
	const int32 Step = Delta > 0 ? 1 : -1;
	int32 Left = FMath::Abs(Delta), Next = INDEX_NONE;
	for (int32 k = (Cur == INDEX_NONE ? (Step > 0 ? -1 : Rows.Num()) : Cur) + Step; k >= 0 && k < Rows.Num() && Left > 0; k += Step)
		if (Stops(k)) { Next = k; --Left; }
	if (Next == INDEX_NONE) return;
	FocusKey = Rows[Next]->Key;
	List->RequestScrollIntoView(Rows[Next]);
}

void SGOMGRoot::Enter()
{
	if (GOMGClient::IsCapturing()) return;
	if (View == EView::Rename) { DoRename(); return; }
	if (!NoteKey.IsEmpty()) { SaveNote(NoteText); return; }
	if (View != EView::Places) return;
	const FString Text = Search->GetText().ToString();
	if (GOMGClient::IsLine(Text) && GOMGClient::SubmitLine(Text)) { Search->SetText(FText::GetEmpty()); return; }
	if (Text.TrimStartAndEnd().StartsWith(TEXT("/tp"))) { GOMGClient::SubmitLine(Text); return; }   // says what's wrong with it
	const TSharedPtr<FGOMGRow> F = Focused();
	if (!F.IsValid())
	{
		// typed a name and pressed Enter: the first match goes
		if (!Filter.IsEmpty())
			for (const TSharedPtr<FGOMGRow>& R : Rows)
				if (R->Kind == FGOMGRow::EKind::Place || R->Kind == FGOMGRow::EKind::Spot) { FocusKey = R->Key; Select(R); Go(R); return; }
		MoveFocus(+1);
		return;
	}
	if (F->Kind == FGOMGRow::EKind::Place || F->Kind == FGOMGRow::EKind::Spot) { Select(F); Go(F); }
	else Activate(F);
}

bool SGOMGRoot::Back()
{
	if (!NoteKey.IsEmpty()) { CancelNote(); return true; }
	if (View == EView::Rename) { View = EView::Places; RebuildPrompts(); FSlateApplication::Get().SetKeyboardFocus(Search, EFocusCause::SetDirectly); return true; }
	if (View == EView::Settings) { GOMGClient::CancelCapture(); View = EView::Places; RebuildPrompts(); return true; }
	return false;
}

void SGOMGRoot::DoSaveHere()
{
	CancelNote();   // an open note is known by its row, and rows move
	const int32 Index = GOMGClient::SaveHere();
	if (Index == INDEX_NONE) return;
	Tab = ETab::Mine;
	Sel = ESel::Spot; SelIndex = Index; bSelRecent = false;
	FocusKey = FString::Printf(TEXT("m:%d"), Index);
	Rebuild();
	for (const TSharedPtr<FGOMGRow>& R : Rows) if (R->Key == FocusKey) { List->RequestScrollIntoView(R); break; }
}

void SGOMGRoot::DoTeleport()
{
	if (FGOMGSpot* S = SelSpot())
	{
		TSharedPtr<FGOMGRow> R = MakeShared<FGOMGRow>();
		R->Kind = FGOMGRow::EKind::Spot; R->Index = SelIndex; R->bRecent = bSelRecent;
		Go(R);
	}
	else if (Sel == ESel::Place)
	{
		TSharedPtr<FGOMGRow> R = MakeShared<FGOMGRow>();
		R->Kind = FGOMGRow::EKind::Place; R->Index = SelIndex;
		Go(R);
	}
}

void SGOMGRoot::DoFav()
{
	FGOMGWorld& W = GOMGClient::World();
	if (FGOMGSpot* S = SelSpot())
	{
		S->bFav = !S->bFav;
		if (GOMGClient::Persist()) ShowStatus(Fmt(S->bFav ? TEXT("“{0}” is a favourite now.") : TEXT("“{0}” is not a favourite any more."), Tx(S->Name)), false);
		Rebuild();
		return;
	}
	if (Sel != ESel::Place) return;
	// a map place to My places, as a favourite: it goes by the map place's own checked landing
	const FString Id = GOMGPlaces::IdOf(SelIndex);
	for (FGOMGSpot& S : W.Saved)
		if (S.Pin == Id) { S.bFav = true; if (GOMGClient::Persist()) ShowStatus(Fmt(TEXT("“{0}” is a favourite now."), Tx(S.Name)), false); Rebuild(); return; }
	if (GOMGPlaces::HasNoLanding(SelIndex)) { ShowStatus(Tx(TEXT("This place has no checked landing yet, so no teleport. Pick one nearby.")), true); return; }
	FGOMGSpot* S = GOMGStore::AddSaved(W, GOMGClient::PlaceLabel(SelIndex).ToString(), PlaceFeet(SelIndex), 0, TEXT(""));
	if (!S) { ShowStatus(Tx(TEXT("This world's list is full. Delete a place first.")), true); return; }
	S->Pin = Id;
	S->bFav = true;
	if (GOMGClient::Persist()) ShowStatus(Fmt(TEXT("“{0}” is in My places, as a favourite."), Tx(S->Name)), false);
	Rebuild();
}

void SGOMGRoot::DoFound()
{
	if (Sel != ESel::Place) return;
	GOMGClient::ToggleManualFound(SelIndex);
}

void SGOMGRoot::DoShowOnMap()
{
	FString Url = GOMGClient::MapUrl();
	if (Sel == ESel::Place) Url += TEXT("#pin=") + GOMGPlaces::IdOf(SelIndex);
	else if (const FGOMGSpot* S = SelSpot())
	{
		if (!S->Pin.IsEmpty() && GOMGPlaces::FindById(S->Pin) != INDEX_NONE) Url += TEXT("#pin=") + S->Pin;
		else Url += FString::Printf(TEXT("#spot=%lld,%lld,"), (long long)FMath::TruncToDouble(S->Feet.X / 100.0), (long long)FMath::TruncToDouble(S->Feet.Y / 100.0)) + UrlPart(S->Name);
	}
	GOMGClient::OpenUrl(Url);
}

void SGOMGRoot::DoDelete()
{
	CancelNote();
	FGOMGSpot* S = SelSpot();
	if (!S) return;
	FGOMGWorld& W = GOMGClient::World();
	const FGOMGSpot Gone = *S;
	(bSelRecent ? W.Recent : W.Saved).RemoveAt(SelIndex);
	const bool bOk = GOMGClient::Persist();
	if (bOk)
	{
		ShowStatus(Fmt(TEXT("Deleted “{0}”."), Tx(Gone.Name)), false);
		Undone = Gone; UndoneIndex = SelIndex; bUndoneRecent = bSelRecent; UndoneWorld = GOMGClient::SessionLabel();
	}
	ClearSelection();
	Rebuild();
}

void SGOMGRoot::DoUndo()
{
	CancelNote();
	if (!Undone.IsSet() || UndoneWorld != GOMGClient::SessionLabel()) { Undone.Reset(); return; }
	FGOMGWorld& W = GOMGClient::World();
	TArray<FGOMGSpot>& L = bUndoneRecent ? W.Recent : W.Saved;
	const FGOMGSpot Again = Undone.GetValue();
	Undone.Reset();
	if (!bUndoneRecent && L.ContainsByPredicate([&](const FGOMGSpot& S) { return GOMGStore::SameName(S.Name, Again.Name); }))
	{
		ShowStatus(Fmt(TEXT("“{0}” can't come back: that name is in use again. Nothing changed."), Tx(Again.Name)), true);
		return;
	}
	const int32 At = FMath::Clamp(UndoneIndex, 0, L.Num());
	L.Insert(Again, At);
	if (GOMGClient::Persist()) ShowStatus(Fmt(TEXT("“{0}” is back."), Tx(Again.Name)), false);
	Sel = ESel::Spot; SelIndex = At; bSelRecent = bUndoneRecent;
	Rebuild();
}

void SGOMGRoot::OpenRename()
{
	const FGOMGSpot* S = SelSpot();
	if (!S || bSelRecent) return;
	RenameText = S->Name;
	RenameField->SetText(Tx(S->Name));
	View = EView::Rename;
	RebuildPrompts();
	FSlateApplication::Get().SetKeyboardFocus(RenameField, EFocusCause::SetDirectly);
}

void SGOMGRoot::DoRename()
{
	if (Sel != ESel::Spot || bSelRecent) { Back(); return; }
	const FString Got = GOMGStore::Rename(GOMGClient::World(), SelIndex, RenameText);
	if (Got.IsEmpty()) { ShowStatus(Tx(TEXT("A name needs at least one letter.")), true); return; }
	Back();
	if (GOMGClient::Persist()) ShowStatus(Fmt(TEXT("Renamed to “{0}”."), Tx(Got)), false);
	Rebuild();
}

FString SGOMGRoot::NoteOf(const TSharedPtr<FGOMGRow>& Row) const
{
	if (Row->Kind == FGOMGRow::EKind::Place)
	{
		const FString* N = GOMGClient::World().Notes.Find(GOMGPlaces::IdOf(Row->Index));
		return N ? *N : FString();
	}
	if (Row->Kind == FGOMGRow::EKind::Spot && !Row->bRecent) if (const FGOMGSpot* S = SpotOf(*Row)) return S->Note;
	return FString();
}

void SGOMGRoot::OpenNote(const TSharedPtr<FGOMGRow>& Row)
{
	Select(Row);
	NoteField.Reset();
	NoteKey = Row->Key;
	NoteText = NoteOf(Row);
	FocusKey = Row->Key;
	bFocusNote = true;
	RebuildPrompts();
	Rebuild();
}

void SGOMGRoot::CancelNote()
{
	if (NoteKey.IsEmpty()) return;
	NoteKey.Reset();
	NoteField.Reset();
	RebuildPrompts();
	Rebuild();
	if (FSlateApplication::IsInitialized() && Search.IsValid()) FSlateApplication::Get().SetKeyboardFocus(Search, EFocusCause::SetDirectly);
}

void SGOMGRoot::SaveNote(const FString& Text)
{
	TSharedPtr<FGOMGRow> Row = Focused();
	if (!Row.IsValid() || Row->Key != NoteKey) { CancelNote(); return; }
	FGOMGWorld& W = GOMGClient::World();
	FString Kept, Name;
	if (Row->Kind == FGOMGRow::EKind::Place) { Kept = GOMGStore::SetNote(W, GOMGPlaces::IdOf(Row->Index), Text); Name = PlaceName(Row->Index); }
	else if (FGOMGSpot* S = SpotOf(*Row)) { S->Note = GOMGStore::Clean(Text, GOMGStore::NoteMax); Kept = S->Note; Name = S->Name; }
	CancelNote();
	if (GOMGClient::Persist()) ShowStatus(Fmt(Kept.IsEmpty() ? TEXT("Note deleted from “{0}”.") : TEXT("Note kept on “{0}”."), Tx(Name)), false);
}

// ══ Settings ══════════════════════════════════════════════════════════════════════════════════════════════════════════════════

TSharedRef<SWidget> SGOMGRoot::MakeSettings()
{
	auto SectionTab = [this](const TCHAR* Label, ESection Which)
	{
		auto On = [this, Which]() { return Section == Which; };
		return SNew(SBox).HeightOverride(TabH)
			[
				SNew(SGOMGHit)
				.Normal_Lambda([On]() { return On() ? Plate(Surface, 8) : None(); })
				.Hover_Lambda([On]() { return On() ? Plate(Surface, 8) : Plate(Alpha(Raised, .6f), 8); })
				.Fg_Lambda([On]() { return FSlateColor(On() ? Ink : Muted); }).HoverFg_Lambda([On]() { return FSlateColor(On() ? Ink : Muted); })
				.OnClick(Do([this, Which]() { Later([this, Which]() { GOMGClient::CancelCapture(); Section = Which; RebuildSettings(); RebuildPrompts(); }); }))
				[
					SNew(SOverlay)
					+ SOverlay::Slot().HAlign(HAlign_Center).VAlign(VAlign_Center)[ SNew(STextBlock).Text(Tx(Label)).Font_Lambda([On]() { return Font(12.5f, On()); }) ]
					+ SOverlay::Slot().VAlign(VAlign_Bottom).Padding(FMargin(8, 0))
					[ SNew(SBox).HeightOverride(3).Visibility_Lambda([On]() { return On() ? EVisibility::HitTestInvisible : EVisibility::Hidden; })[ SNew(SImage).Image(Plate(Orange, 0)) ] ]
				]
			];
	};
	return SNew(SVerticalBox)
		+ SVerticalBox::Slot().AutoHeight()
		[
			SNew(SHorizontalBox)
			+ SHorizontalBox::Slot().FillWidth(8.f)[ SectionTab(TEXT("Controls"), ESection::Controls) ]
			+ SHorizontalBox::Slot().FillWidth(10.f)[ SectionTab(TEXT("Appearance"), ESection::Appearance) ]
			+ SHorizontalBox::Slot().FillWidth(9.f)[ SectionTab(TEXT("Behaviour"), ESection::Behaviour) ]
			+ SHorizontalBox::Slot().FillWidth(5.f)[ SectionTab(TEXT("Help"), ESection::Help) ]
		]
		+ SVerticalBox::Slot().AutoHeight()[ Gap(10) ]
		+ SVerticalBox::Slot().FillHeight(1.f)
		[
			SNew(SScrollBox)
			+ SScrollBox::Slot()[ SAssignNew(SettingsBox, SBox) ]
		];
}

void SGOMGRoot::RebuildSettings()
{
	if (!SettingsBox.IsValid()) return;
	FGOMGSettings& S = GOMGStore::Settings();
	TSharedRef<SVerticalBox> Box = SNew(SVerticalBox);
	auto Para = [&Box](const FText& T, float Px = 12.5f, const FLinearColor& C = Muted)
	{
		Box->AddSlot().AutoHeight().Padding(FMargin(0, 0, 0, 6))[ SNew(STextBlock).Text(T).Font(Font(Px)).ColorAndOpacity(C).AutoWrapText(true) ];
	};
	auto Label = [&Box](const TCHAR* Name, const TCHAR* Hint)
	{
		Box->AddSlot().AutoHeight().Padding(FMargin(0, 4, 0, 0))[ Txt(Tx(Name), 14.5f, false, Ink) ];
		if (Hint) Box->AddSlot().AutoHeight().Padding(FMargin(0, 0, 0, 4))[ SNew(STextBlock).Text(Tx(Hint)).Font(Font(12.5f)).ColorAndOpacity(Muted).AutoWrapText(true) ];
	};
	auto Changed = [this]() { GOMGClient::Persist(); ShowStatus(Tx(TEXT("Changed.")), false); RebuildSettings(); RebuildPrompts(); };
	auto Segmented = [this, &Box, Changed](TArray<const TCHAR*> Labels, int32 At, TFunction<void(int32)> Set)
	{
		TSharedRef<SHorizontalBox> Ln = SNew(SHorizontalBox);
		for (int32 k = 0; k < Labels.Num(); ++k)
		{
			const bool bOn = k == At;
			Ln->AddSlot().FillWidth(1.f).Padding(FMargin(k > 0 ? 4 : 0, 0, 0, 0))
				[
					SNew(SBox).HeightOverride(ButtonH)
					[
						SNew(SGOMGHit).Normal(bOn ? Plate(Alpha(Orange, .22f), 8) : Plate(Surface, 8)).Hover(bOn ? Plate(Alpha(Orange, .22f), 8) : Plate(Raised, 8))
						.Fg(FSlateColor(bOn ? Ink : Muted)).HoverFg(FSlateColor(Ink))
						.OnClick(Do([this, k, Set, Changed]() { Later([k, Set, Changed]() { Set(k); Changed(); }); }))
						[ SNew(SBox).HAlign(HAlign_Center).VAlign(VAlign_Center)[ SNew(STextBlock).Text(Tx(Labels[k])).Font(Font(14, bOn)) ] ]
					]
				];
		}
		Box->AddSlot().AutoHeight().Padding(FMargin(0, 2, 0, 10))[ Ln ];
	};
	auto Stepper = [this, &Box, Changed](const FString& Value, TFunction<void()> Down, TFunction<void()> Up)
	{
		Box->AddSlot().AutoHeight().Padding(FMargin(0, 2, 0, 10))
			[
				SNew(SHorizontalBox)
				+ SHorizontalBox::Slot().AutoWidth()[ Button(Tx(TEXT("−")), TOptional<uint8>(), 0, [Down, Changed]() { Down(); Changed(); }, true, TAttribute<FText>(), 44) ]
				+ SHorizontalBox::Slot().FillWidth(1.f).HAlign(HAlign_Center).VAlign(VAlign_Center)[ Txt(Tx(Value), 14.5f, true, Ink) ]
				+ SHorizontalBox::Slot().AutoWidth()[ Button(Tx(TEXT("+")), TOptional<uint8>(), 0, [Up, Changed]() { Up(); Changed(); }, true, TAttribute<FText>(), 44) ]
			];
	};
	auto Head = [&Box](const TCHAR* T) { Box->AddSlot().AutoHeight().Padding(FMargin(0, 8, 0, 4))[ Txt(Tx(T).ToUpper(), 11.5f, true, Muted) ]; };

	switch (Section)
	{
	case ESection::Controls:
	{
		Para(Tx(TEXT("Change a key, then press the new one. Another mod, or the game, can still use the same key: if a key does two things, change one here.")));
		auto KeyRow = [&](const TCHAR* Name, const TCHAR* Hint, bool bPanel)
		{
			Label(Name, Hint);
			const FKey Now = bPanel ? S.PanelKey : S.BackKey;
			const bool bWaiting = GOMGClient::IsCapturing() && CaptureFor.ToString() == Name;
			Box->AddSlot().AutoHeight().Padding(FMargin(0, 2, 0, 10))
				[
					SNew(SHorizontalBox)
					+ SHorizontalBox::Slot().FillWidth(1.f).VAlign(VAlign_Center)
					[ bWaiting ? StaticCastSharedRef<SWidget>(Txt(Tx(TEXT("Press the new key. Esc cancels.")), 12.5f, false, Blue)) : KeyChip(Now.GetDisplayName()) ]
					+ SHorizontalBox::Slot().AutoWidth()
					[
						Button(Tx(TEXT("Change")), (uint8)EGOMGIcon::Keyboard, 0, [this, Name, bPanel]()
						{
							CaptureFor = Tx(Name);
							GOMGClient::CaptureKey([this, bPanel](FKey K)
							{
								FGOMGSettings& St = GOMGStore::Settings();
								const FKey Other = bPanel ? St.BackKey : St.PanelKey;
								if (!K.IsValid()) ShowStatus(Tx(TEXT("Nothing changed.")), false);
								else if (K == EKeys::AnyKey) ShowStatus(Tx(TEXT("That key types in the panel's boxes. Pick one that doesn't, like F6 or Insert. Nothing changed.")), true);
								else if (K == Other) ShowStatus(Fmt(TEXT("That's already {0}. Change that one first. Nothing changed."), Tx(bPanel ? TEXT("Return") : TEXT("Open Places"))), true);
								else if (K == EKeys::Enter || K == EKeys::Up || K == EKeys::Down || K == EKeys::PageUp || K == EKeys::PageDown)
									ShowStatus(Tx(TEXT("The panel uses that key itself. Pick another. Nothing changed.")), true);
								else
								{
									(bPanel ? St.PanelKey : St.BackKey) = K;
									GOMGClient::Persist();
									ShowStatus(Fmt(TEXT("{0} is now {1}."), Tx(bPanel ? TEXT("Open Places") : TEXT("Return to previous spot")), K.GetDisplayName()), false);
								}
								CaptureFor = FText();
								RebuildSettings();
								RebuildPrompts();
							});
							RebuildSettings();
							RebuildPrompts();
						}, true, TAttribute<FText>(), 120)
					]
				];
		};
		KeyRow(TEXT("Open Places"), TEXT("Opens and closes this panel."), true);
		KeyRow(TEXT("Return to previous spot"), TEXT("Back to where you stood before your last teleport. Again swaps back."), false);
		Box->AddSlot().AutoHeight().Padding(FMargin(0, 6, 0, 0))
			[
				Button(Tx(TEXT("Restore control defaults")), (uint8)EGOMGIcon::Undo, 0, [this, Changed]()
				{
					GOMGStore::Settings().PanelKey = EKeys::F9; GOMGStore::Settings().BackKey = EKeys::F10; Changed();
				}, true, Tx(TEXT("F9 and F10. Your places and found marks are not touched.")))
			];
		break;
	}
	case ESection::Appearance:
		Label(TEXT("Text and panel size"), TEXT("Bigger is easier to read on a TV."));
		Stepper(FString::Printf(TEXT("%d %%"), S.TextSize),
			[]() { int32& V = GOMGStore::Settings().TextSize; V = FMath::Max(GOMGStore::TextMin, V - 10); },
			[]() { int32& V = GOMGStore::Settings().TextSize; V = FMath::Min(GOMGStore::TextMax, V + 10); });
		Label(TEXT("Panel side"), nullptr);
		Segmented({ TEXT("Left"), TEXT("Right") }, S.bLeft ? 0 : 1, [](int32 K) { GOMGStore::Settings().bLeft = K == 0; });
		Label(TEXT("Background"), TEXT("Lower shows more of the game behind it. Text always stays solid."));
		Stepper(FString::Printf(TEXT("%d %%"), S.Opacity),
			[]() { int32& V = GOMGStore::Settings().Opacity; V = FMath::Max(GOMGStore::OpacityMin, V - 5); },
			[]() { int32& V = GOMGStore::Settings().Opacity; V = FMath::Min(100, V + 5); });
		Box->AddSlot().AutoHeight().Padding(FMargin(0, 6, 0, 0))
			[
				Button(Tx(TEXT("Restore appearance defaults")), (uint8)EGOMGIcon::Undo, 0, [Changed]()
				{
					FGOMGSettings& St = GOMGStore::Settings(); const FGOMGSettings D; St.TextSize = D.TextSize; St.bLeft = D.bLeft; St.Opacity = D.Opacity; Changed();
				})
			];
		break;
	case ESection::Behaviour:
		Label(TEXT("Clicking a place"), TEXT("Enter always teleports. Right-click a place to select it without going."));
		Segmented({ TEXT("Selects it"), TEXT("Teleports at once") }, S.bClickTeleports ? 1 : 0, [](int32 K) { GOMGStore::Settings().bClickTeleports = K == 1; });
		Label(TEXT("Close after a teleport"), TEXT("Off: Places stays open where you land."));
		Segmented({ TEXT("Off"), TEXT("On") }, S.bCloseAfterTeleport ? 1 : 0, [](int32 K) { GOMGStore::Settings().bCloseAfterTeleport = K == 1; });
		Box->AddSlot().AutoHeight().Padding(FMargin(0, 6, 0, 0))
			[
				Button(Tx(TEXT("Restore behaviour defaults")), (uint8)EGOMGIcon::Undo, 0, [Changed]()
				{
					FGOMGSettings& St = GOMGStore::Settings(); const FGOMGSettings D; St.bClickTeleports = D.bClickTeleports; St.bCloseAfterTeleport = D.bCloseAfterTeleport; Changed();
				})
			];
		break;
	case ESection::Help:
	{
		const FText P = S.PanelKey.GetDisplayName(), B = S.BackKey.GetDisplayName();
		Head(TEXT("Your keys"));
		Para(Fmt(TEXT("{0} opens Places, {1} returns to where you were. In Places: ↑ ↓ move, Enter teleports, Esc goes back, right-click selects a place."), P, B), 13.5f, Ink);
		Head(TEXT("How it works"));
		Para(Fmt(TEXT("On the GamesOMG map, press Copy teleport on a pin, come back and press {0}: the line waits in the search box, press Enter. Or pick any place here. You land beside it, after a check for a floor and room to stand."), P), 13.5f, Ink);
		Head(TEXT("Good to know"));
		Para(Tx(TEXT("An open panel doesn't pause the game. In multiplayer the world keeps going around you.")));
		Para(Tx(TEXT("A green box is collected in this save: the game says so. Mark found is yours to set; teleporting somewhere never marks it found.")));
		Para(Tx(TEXT("Your places, notes and found marks are kept per world, in the file below.")));
		Para(Tx(TEXT("In multiplayer the host or server needs the mod too. It moves only you.")));
		Head(TEXT("About"));
		Para(Fmt(TEXT("GamesOMG Teleport {0} · tested on game build CL 520140"), Tx(FGamesOMGTeleportModule::VersionString())));
		Para(Fmt(TEXT("Places file: {0}"), Tx(FPaths::ConvertRelativePathToFull(GOMGStore::Path()))));
		Box->AddSlot().AutoHeight().Padding(FMargin(0, 4, 0, 8))[ Button(Tx(TEXT("Open the GamesOMG map")), (uint8)EGOMGIcon::Map, 0, []() { GOMGClient::OpenUrl(GOMGClient::MapUrl()); }) ];
		Para(Tx(TEXT("Free, from gamesomg.com. Not made or endorsed by Coffee Stain Studios.")));
		break;
	}
	}
	SettingsBox->SetContent(Box);
}

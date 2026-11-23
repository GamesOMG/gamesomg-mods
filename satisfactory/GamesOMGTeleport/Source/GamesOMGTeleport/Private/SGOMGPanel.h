// GamesOMG Teleport for Satisfactory: the Places panel and the toast line, drawn with Slate.
// 1.1.0 (Jason 2026-11-23, after playing 1.0.0: "the text and whatnot are a lot bigger than the others, the logo and name are not
// the same, there is no way to save a spot. The drop downs do not look the same ... Planet Crafter one should be the model"):
// rebuilt on the Planet Crafter mod's Panel.cs, top to bottom:
//   header           gear-G mark · Places · this session's name · Settings · Close
//   main actions     From clipboard · Save here; Return to <the spot you left>
//   navigation       My places | Map places
//   search, filters  search (or a /tp line) · Hide found · Sort (Nearest / A–Z)
//   rows             My places: Favourites, My places, Recently travelled. Map places: Nearest (from the layers you tick),
//                    then GROUP → layer → places, 15 and "Show 25 more", and the Found drawer at the bottom
//   selected place   Teleport · Favourite / Save to My places · Mark found · Rename · Show on map · Delete
//   footer           what just happened (with Undo), and the keys
// The Settings view (Controls / Appearance / Behaviour / Help) is the same panel's other side.
#pragma once

#include "CoreMinimal.h"
#include "GOMGStore.h"
#include "Widgets/SCompoundWidget.h"
#include "Widgets/Views/SListView.h"

class SEditableTextBox;
class STextBlock;
class SBorder;
class SBox;

struct FGOMGRow
{
	enum class EKind : uint8 { Head, Spot, Nearest, Group, Layer, Place, More, Note, Found };
	EKind Kind = EKind::Place;
	int32 Index = INDEX_NONE;   // Spot: in Saved (or Recent); Group: group; Layer / More: layer; Place: place
	bool bRecent = false;
	bool bInFound = false;      // the Found drawer and everything in it
	bool bOpen = false;
	bool bOn = false;           // Layer: ticked on (feeds Nearest)
	int32 Count = 0, NFound = 0, Left = 0;
	int32 Depth = 0;
	FText Text;                 // Head / Note
	FString Key;                // stable across rebuilds: the keyboard focus and the open note box follow it
};

class SGOMGRoot : public SCompoundWidget
{
public:
	SLATE_BEGIN_ARGS(SGOMGRoot) {}
	SLATE_END_ARGS()

	void Construct(const FArguments& InArgs);
	virtual void Tick(const FGeometry& AllottedGeometry, const double InCurrentTime, const float InDeltaTime) override;

	void SetPanelOpen(bool bOpen);
	void Rebuild();
	void ShowToast(const FText& Message, bool bBad);
	void ShowStatus(const FText& Message, bool bBad);
	void MoveFocus(int32 Delta);
	void Enter();
	bool Back();                // Esc: out of a note, a dialog or Settings; false when there's nothing to step out of
	void PrefillLine(const FString& TpLine);
	void OnTeleported();        // a Recently travelled row picked before the trip may now be another one: let it go
	TSharedPtr<SWidget> GetFocusTarget() const;

private:
	friend class FGOMGPanelShotTest;   // the offscreen render check (GOMGPanelShotTest.cpp)
	enum class ETab : uint8 { Mine, Map };
	enum class EView : uint8 { Places, Settings, Rename };
	enum class ESection : uint8 { Controls, Appearance, Behaviour, Help };
	enum class ESel : uint8 { None, Spot, Place };

	// building blocks (the model's Button / IconButton / TabButton / Chip / Row / FoldRow)
	TSharedRef<SWidget> Button(const FText& Label, TOptional<uint8> Icon, int32 Kind, TFunction<void()> Act,
		TAttribute<bool> Enabled = true, TAttribute<FText> Sub = TAttribute<FText>(), float Width = -1);
	TSharedRef<SWidget> ButtonDyn(TAttribute<FText> Label, TOptional<uint8> Icon, int32 Kind, TFunction<void()> Act,
		TAttribute<bool> Enabled = true, TAttribute<FText> Sub = TAttribute<FText>(), float Width = -1);
	TSharedRef<SWidget> IconButton(TAttribute<uint8> Icon, TAttribute<FText> Tip, TFunction<void()> Act);
	TSharedRef<SWidget> TabButton(const FText& Label, ETab Which);
	TSharedRef<SWidget> Chip(TAttribute<FText> Label, TFunction<bool()> On, TFunction<void()> Act, bool bTickIcon);
	TSharedRef<SWidget> Field(TSharedPtr<SEditableTextBox>& Out, TAttribute<FText> Hint, TFunction<void(const FText&)> Changed);
	TSharedRef<SWidget> Heading(const FText& Text);
	TSharedRef<SWidget> FlowButtons(const TArray<TTuple<FText, uint8, TFunction<void()>, bool>>& Items);

	TSharedRef<SWidget> MakeHeader();
	TSharedRef<SWidget> MakePlaces();
	TSharedRef<SWidget> MakeSettings();
	TSharedRef<SWidget> MakeRename();
	TSharedRef<SWidget> MakeFooter();
	void RebuildSelected();
	void RebuildPrompts();
	void RebuildSettings();
	void PlacePanel();

	TSharedRef<ITableRow> MakeRow(TSharedPtr<FGOMGRow> Row, const TSharedRef<STableViewBase>& Owner);
	TSharedRef<SWidget> MakeRowContent(const TSharedPtr<FGOMGRow>& Row);
	TSharedRef<SWidget> PlaceRow(const TSharedPtr<FGOMGRow>& Row);
	TSharedRef<SWidget> FoldRow(const TSharedPtr<FGOMGRow>& Row);
	void BuildMine(const FVector& Me, bool bHaveMe);
	void BuildMap(const FVector& Me, bool bHaveMe);

	// actions
	void Later(TFunction<void()> Act);
	void SetTab(ETab T);
	void Activate(const TSharedPtr<FGOMGRow>& Row);
	void Go(const TSharedPtr<FGOMGRow>& Row);
	FGOMGSpot* SpotOf(const FGOMGRow& Row) const;
	TSharedPtr<FGOMGRow> Focused() const;
	void Select(const TSharedPtr<FGOMGRow>& Row);
	void ClearSelection();
	void Toggle(const TSharedPtr<FGOMGRow>& Row);
	void DoSaveHere();
	void DoTeleport();
	void DoFav();
	void DoFound();
	void DoShowOnMap();
	void DoDelete();
	void DoUndo();
	void OpenRename();
	void DoRename();
	void OpenNote(const TSharedPtr<FGOMGRow>& Row);
	void SaveNote(const FString& Text);
	void CancelNote();
	FString NoteOf(const TSharedPtr<FGOMGRow>& Row) const;
	FGOMGSpot* SelSpot() const;
	bool IsOpen(const FString& Key, bool bDefault) const;
	void Flip(const FString& Key);
	FString Meta(const FVector& Target, const FVector& Me, bool bHaveMe, const FString& Lead, FString& Tail) const;
	FText CountText(int32 NFound, int32 Total) const;
	void SetLook();

	TSharedPtr<SBorder> ToastBox;
	TSharedPtr<STextBlock> ToastText;
	TSharedPtr<SBox> Holder;
	TSharedPtr<SWidget> Panel;
	TSharedPtr<SEditableTextBox> Search, RenameField, NoteField;
	TSharedPtr<SListView<TSharedPtr<FGOMGRow>>> List;
	TSharedPtr<SBox> SelectedBox, PromptsBox, SettingsBox;
	TArray<TSharedPtr<FGOMGRow>> Rows;
	TSharedPtr<FActiveTimerHandle> ToastTimer;

	ETab Tab = ETab::Mine;
	EView View = EView::Places;
	ESection Section = ESection::Controls;
	ESel Sel = ESel::None;
	int32 SelIndex = INDEX_NONE;
	bool bSelRecent = false;
	FString FocusKey;
	FString Filter;
	TArray<FString> SearchCache;
	FString SearchCulture;
	TMap<FString, int32> Cap;   // per layer: how many places show ("Show 25 more"), this game session only
	FString NoteKey;
	FString NoteText;
	bool bFocusNote = false;
	FString RenameText;
	FText Status;
	bool bStatusBad = false;
	double StatusUntil = 0;
	TOptional<FGOMGSpot> Undone;
	int32 UndoneIndex = INDEX_NONE;
	bool bUndoneRecent = false;
	FString UndoneWorld;
	FText CaptureFor;
	TFunction<void()> Pending;
	FVector MeFeet = FVector::ZeroVector;
	bool bHaveMe = false;
	float Scale = 1.f;
	float Slide = 1.f;
	bool bOpen = false;
	bool bLeftNow = false;
	int32 Tickable = 0, FoundTickable = 0;
	double LastClickAt = 0;
	FString LastClickKey;
};

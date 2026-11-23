// GamesOMG Teleport for Satisfactory: the Places panel and the toast line, drawn with Slate (no assets of our own).
#pragma once

#include "CoreMinimal.h"
#include "Widgets/SCompoundWidget.h"
#include "Widgets/Views/SListView.h"

class SEditableTextBox;
class STextBlock;
class SBorder;
class SBox;

struct FGOMGRow
{
	enum class EKind : uint8 { Group, Cat, Place, FoundHeader, Empty };
	EKind Kind = EKind::Place;
	int32 Index = 0;          // group, cat or place index
	bool bFound = false;      // in the Found drawer
	int32 Count = 0;
	FString Key;              // collapse key for headings
	bool bCollapsed = false;
	double Dist = -1;
	int32 Depth = 0;
};

class SGOMGRoot : public SCompoundWidget
{
public:
	SLATE_BEGIN_ARGS(SGOMGRoot) {}
	SLATE_END_ARGS()

	void Construct(const FArguments& InArgs);

	void SetPanelOpen(bool bOpen);
	void Rebuild();
	void ShowToast(const FText& Message, bool bBad);
	void MoveSelection(int32 Delta);
	void ActivateSelection();
	void PrefillLine(const FString& TpLine);
	TSharedPtr<SWidget> GetFocusTarget() const;

private:
	TSharedRef<ITableRow> MakeRow(TSharedPtr<FGOMGRow> Row, const TSharedRef<STableViewBase>& Owner);
	TSharedRef<SWidget> MakeRowContent(const TSharedPtr<FGOMGRow>& Row);
	void OnSearchChanged(const FText& Text);
	void ToggleHeading(const TSharedPtr<FGOMGRow>& Row);
	FText FooterText() const;
	EActiveTimerReturnType HideToast(double, float);

	TSharedPtr<SBox> Panel;
	TSharedPtr<SBorder> ToastBox;
	TSharedPtr<STextBlock> ToastText;
	TSharedPtr<SEditableTextBox> Search;
	TSharedPtr<SListView<TSharedPtr<FGOMGRow>>> List;
	TArray<TSharedPtr<FGOMGRow>> Rows;
	FString Filter;
	TArray<FString> SearchCache;
	FString SearchCulture;
	TSharedPtr<FActiveTimerHandle> ToastTimer;
	int32 FoundTotal = 0;
	int32 Tickable = 0;
	int32 FoundTickable = 0;
};

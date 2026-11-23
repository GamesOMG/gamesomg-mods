#include "GOMGTheme.h"

#include "Brushes/SlateDynamicImageBrush.h"
#include "Brushes/SlateNoResource.h"
#include "Brushes/SlateRoundedBoxBrush.h"
#include "Engine/Font.h"
#include "Fonts/FontMeasure.h"
#include "Framework/Application/SlateApplication.h"
#include "IImageWrapper.h"
#include "IImageWrapperModule.h"
#include "Modules/ModuleManager.h"
#include "Rendering/SlateRenderer.h"
#include "Styling/CoreStyle.h"

#include "GOMGArt.inl"

namespace GOMGTheme
{
	FLinearColor Hex(uint32 Rgb, float A)
	{
		return FLinearColor(FColor((Rgb >> 16) & 255, (Rgb >> 8) & 255, Rgb & 255)).CopyWithNewOpacity(A);
	}
	FLinearColor Alpha(const FLinearColor& C, float A) { return C.CopyWithNewOpacity(A); }

	// Planet Crafter's Theme.cs, colour for colour
	const FLinearColor Orange = Hex(0xf08000), Blue = Hex(0x3be3ff);
	const FLinearColor Ink = Hex(0xeef1f5), Muted = Hex(0xbdc6d2), Faint = Hex(0x8f99a8);
	const FLinearColor Panel = Hex(0x0e1117), Surface = Hex(0x171b23), Raised = Hex(0x20262f), Line = Hex(0x2b3240);
	const FLinearColor Good = Hex(0x4fd08a), Bad = Hex(0xff6b5e), BadText = Hex(0xff8a80), Note = Hex(0xfacc15);
	const FLinearColor OrangeHover = Hex(0xff9a2e), OnOrange = Hex(0x15100a);

	FSlateFontInfo Font(float Px, bool bBold)
	{
		static UObject* GameFont = nullptr;
		static bool bTried = false;
		if (!bTried)
		{
			bTried = true;
			GameFont = LoadObject<UFont>(nullptr, TEXT("/Game/FactoryGame/Interface/Font/DescriptionText.DescriptionText"));
			if (GameFont) GameFont->AddToRoot();
		}
		const float Size = Px * 0.75f;   // pixels → Slate points (96 DPI)
		if (GameFont) return FSlateFontInfo(GameFont, Size, bBold ? FName(TEXT("Bold")) : FName(TEXT("Regular")));
		return FCoreStyle::GetDefaultFontStyle(bBold ? "Bold" : "Regular", Size);
	}

	namespace
	{
		TMap<FString, TUniquePtr<FSlateBrush>>& Cache() { static TMap<FString, TUniquePtr<FSlateBrush>> C; return C; }

		FString KeyOf(const TCHAR* Kind, const FLinearColor& A, float R, const FLinearColor& B, float W)
		{
			const FColor X = A.ToFColor(false), Y = B.ToFColor(false);
			return FString::Printf(TEXT("%s%08x/%.1f/%08x/%.1f"), Kind, X.DWColor(), R, Y.DWColor(), W);
		}

		TSharedPtr<FSlateDynamicImageBrush> Decode(const uint8* Png, int32 Bytes, const TCHAR* Name)
		{
			if (!FSlateApplication::IsInitialized()) return nullptr;
			IImageWrapperModule& Module = FModuleManager::LoadModuleChecked<IImageWrapperModule>(TEXT("ImageWrapper"));
			const TSharedPtr<IImageWrapper> Wrapper = Module.CreateImageWrapper(EImageFormat::PNG);
			TArray64<uint8> Raw;
			if (!Wrapper.IsValid() || !Wrapper->SetCompressed(Png, Bytes) || !Wrapper->GetRaw(ERGBFormat::BGRA, 8, Raw)) return nullptr;
			TArray<uint8> Data(Raw.GetData(), (int32)Raw.Num());
			return FSlateDynamicImageBrush::CreateWithImageData(FName(Name), FVector2D(Wrapper->GetWidth(), Wrapper->GetHeight()), Data);
		}

		TSharedPtr<FSlateDynamicImageBrush> GMark, GIcons;
		bool GArtTried = false;
		FSlateBrush GIcon[(int32)EGOMGIcon::Count];

		void LoadArt()
		{
			if (GArtTried || !FSlateApplication::IsInitialized()) return;
			GArtTried = true;
			GMark = Decode(GOMG_MARK_PNG, sizeof(GOMG_MARK_PNG), TEXT("GamesOMGTeleport.Mark"));
			GIcons = Decode(GOMG_ICONS_PNG, sizeof(GOMG_ICONS_PNG), TEXT("GamesOMGTeleport.Icons"));
			if (!GIcons.IsValid()) return;
			const int32 N = (int32)EGOMGIcon::Count;
			for (int32 i = 0; i < N; ++i)
			{
				GIcon[i] = *GIcons;   // the same texture, one square of it
				GIcon[i].SetUVRegion(FBox2f(FVector2f((float)i / N, 0.f), FVector2f((float)(i + 1) / N, 1.f)));
				GIcon[i].SetImageSize(FVector2f(16.f, 16.f));
			}
		}
	}

	const FSlateBrush* Plate(const FLinearColor& Fill, float Radius)
	{
		const FString K = KeyOf(TEXT("p"), Fill, Radius, FLinearColor::Transparent, 0);
		if (TUniquePtr<FSlateBrush>* B = Cache().Find(K)) return B->Get();
		return Cache().Add(K, MakeUnique<FSlateRoundedBoxBrush>(Fill, Radius)).Get();
	}

	const FSlateBrush* Ring(const FLinearColor& Colour, float Width, float Radius)
	{
		const FString K = KeyOf(TEXT("r"), FLinearColor::Transparent, Radius, Colour, Width);
		if (TUniquePtr<FSlateBrush>* B = Cache().Find(K)) return B->Get();
		return Cache().Add(K, MakeUnique<FSlateRoundedBoxBrush>(FLinearColor::Transparent, Radius, Colour, Width)).Get();
	}

	const FSlateBrush* PlateLined(const FLinearColor& Fill, float Radius, const FLinearColor& Outline, float Width)
	{
		const FString K = KeyOf(TEXT("l"), Fill, Radius, Outline, Width);
		if (TUniquePtr<FSlateBrush>* B = Cache().Find(K)) return B->Get();
		return Cache().Add(K, MakeUnique<FSlateRoundedBoxBrush>(Fill, Radius, Outline, Width)).Get();
	}

	const FSlateBrush* None() { static FSlateNoResource N; return &N; }

	const FSlateBrush* Mark() { LoadArt(); return GMark.IsValid() ? GMark.Get() : None(); }

	const FSlateBrush* Icon(EGOMGIcon Which)
	{
		LoadArt();
		return GIcons.IsValid() ? &GIcon[(int32)Which] : None();
	}

	float Measure(const FString& Text, const FSlateFontInfo& InFont)
	{
		if (!FSlateApplication::IsInitialized()) return Text.Len() * InFont.Size;
		return (float)FSlateApplication::Get().GetRenderer()->GetFontMeasureService()->Measure(Text, InFont).X;
	}
}

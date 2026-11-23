// GamesOMG Teleport for Satisfactory: the Places panel drawn offscreen at 1920x1080, without the game, so its look can be held up
// against the Planet Crafter mod's before anyone plays it. Needs a renderer (not -nullrhi):
//   UnrealEditor-Cmd.exe <SML>\FactoryGame.uproject -ExecCmds="Automation RunTests GamesOMG.PanelShot;Quit" -unattended -RenderOffscreen -nosplash
// PNGs land in <SML>\Saved\GamesOMGPanelShots. The starter project has no game assets, so names fall back to the map's English
// and the font to the engine's; sizes, spacing, colours, the mark and the icons are the real ones.
#include "SGOMGPanel.h"

#include "GOMGPlaces.h"
#include "GOMGStore.h"

#include "Engine/TextureRenderTarget2D.h"
#include "ImageUtils.h"
#include "Misc/AutomationTest.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "RenderingThread.h"
#include "Slate/WidgetRenderer.h"
#include "TextureResource.h"

#if WITH_DEV_AUTOMATION_TESTS

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FGOMGPanelShotTest, "GamesOMG.PanelShot", EAutomationTestFlags::EditorContext | EAutomationTestFlags::EngineFilter)

namespace
{
	bool Shot(FAutomationTestBase& Test, const TSharedRef<SWidget>& Widget, const FString& Name, int32 W, int32 H)
	{
		// a float target, read back linear and turned sRGB here, as the game's back buffer does (8-bit targets came out a shade
		// light with the renderer's gamma step, and crushed to black without it)
		FWidgetRenderer* R = new FWidgetRenderer(false, true);   // cleared every frame
		UTextureRenderTarget2D* RT = NewObject<UTextureRenderTarget2D>();
		RT->RenderTargetFormat = RTF_RGBA16f;
		RT->ClearColor = FLinearColor(0.10f, 0.127f, 0.09f, 1.f);   // a grey-green stand-in for the game behind the panel
		RT->InitAutoFormat(W, H);
		RT->UpdateResourceImmediate(true);
		RT->AddToRoot();
		for (int32 k = 0; k < 8; ++k) { R->DrawWidget(RT, Widget, FVector2D(W, H), 0.1f); FlushRenderingCommands(); }
		TArray<FLinearColor> Lin;
		const bool bRead = RT->GameThread_GetRenderTargetResource()->ReadLinearColorPixels(Lin, FReadSurfaceDataFlags(RCM_UNorm, CubeFace_MAX));
		RT->RemoveFromRoot();
		BeginCleanup(R);
		if (!bRead || Lin.Num() != W * H) { Test.AddError(TEXT("could not read the render back")); return false; }
		TArray<FColor> Px;
		Px.Reserve(Lin.Num());
		for (const FLinearColor& C : Lin) { FColor O = C.ToFColor(true); O.A = 255; Px.Add(O); }
		TArray64<uint8> Png;
		FImageUtils::PNGCompressImageArray(W, H, TArrayView64<const FColor>(Px.GetData(), Px.Num()), Png);
		const FString Path = FPaths::ProjectSavedDir() / TEXT("GamesOMGPanelShots") / Name;
		const bool bSaved = FFileHelper::SaveArrayToFile(Png, *Path);
		Test.AddInfo(FString::Printf(TEXT("%s %s"), bSaved ? TEXT("wrote") : TEXT("could not write"), *FPaths::ConvertRelativePathToFull(Path)));
		return bSaved;
	}

	int32 FirstOf(const TCHAR* Cat, int32 Skip = 0)
	{
		for (int32 i = 0; i < GOMGPlaces::Num(); ++i)
			if (FCString::Strcmp(GOMGPlaces::Cat(GOMGPlaces::Get(i).Cat).Key, Cat) == 0 && Skip-- == 0) return i;
		return 0;
	}
}

bool FGOMGPanelShotTest::RunTest(const FString&)
{
	if (!FApp::CanEverRender()) { AddError(TEXT("needs a renderer: run without -nullrhi")); return false; }
	GOMGStore::ResetForTest();
	FGOMGWorld& Wd = GOMGStore::World(TEXT("(unnamed session)"));   // the key the panel uses with no game world
	const FVector Me = GOMGPlaces::LocationOf(FirstOf(TEXT("oreiron")));
	auto At = [&Me](double Dx, double Dy, double Dz) { return Me + FVector(Dx, Dy, Dz); };
	GOMGStore::AddSaved(Wd, TEXT("Main base"), At(42000, 9000, 800), 0, TEXT(""));
	GOMGStore::AddSaved(Wd, TEXT("Near Iron Ore, Grass Fields"), At(1500, -800, 0), 0, TEXT(""))->bFav = true;
	GOMGStore::AddSaved(Wd, TEXT("Oil outpost"), At(-180000, 64000, -2500), 0, TEXT(""))->Note = TEXT("Come back later");
	GOMGStore::AddSaved(Wd, TEXT("Spire Coast -1234, 2567"), At(-95000, -230000, 4200), 0, TEXT(""));
	GOMGStore::AddRecent(Wd, TEXT("Somersloop, Rocky Desert"), At(120000, -40000, -3000), 0);
	GOMGStore::AddRecent(Wd, TEXT("Iron Ore, Grass Fields"), At(600, 200, 0), 0);
	Wd.bHasBack = true; Wd.Back = At(1500, -800, 0); Wd.BackName = TEXT("Near Iron Ore, Grass Fields");
	FGOMGSettings& S = GOMGStore::Settings();
	S.On.Add(TEXT("somersloop")); S.On.Add(TEXT("slug-purple"));
	S.Flipped.Add(TEXT("g:0")); S.Flipped.Add(TEXT("g:3")); S.Flipped.Add(TEXT("l:oreiron"));
	for (int32 k = 0; k < 3; ++k) Wd.Found.Add(GOMGPlaces::IdOf(FirstOf(TEXT("somersloop"), k)));
	Wd.Notes.Add(GOMGPlaces::IdOf(FirstOf(TEXT("oreiron"), 2)), TEXT("Locked"));

	TSharedRef<SGOMGRoot> Root = SNew(SGOMGRoot);
	Root->SetPanelOpen(true);
	auto Use = [&](SGOMGRoot::ETab Tab)
	{
		// the editor has no player: stand the panel at a place, as the game would
		Root->Tab = Tab;
		Root->Rebuild();
		Root->MeFeet = Me; Root->bHaveMe = true;
		Root->Rows.Reset();
		if (Tab == SGOMGRoot::ETab::Mine) Root->BuildMine(Me, true); else Root->BuildMap(Me, true);
		Root->List->RequestListRefresh();
		Root->RebuildSelected();
	};

	Root->Sel = SGOMGRoot::ESel::Spot; Root->SelIndex = 0; Root->bSelRecent = false;
	Use(SGOMGRoot::ETab::Mine);
	Root->ShowStatus(FText::FromString(TEXT("Saved “Main base”. Rename it any time.")), false);
	bool bOk = Shot(*this, Root, TEXT("1-my-places-1080.png"), 1920, 1080);

	Root->Sel = SGOMGRoot::ESel::Place; Root->SelIndex = FirstOf(TEXT("oreiron"), 1);
	Root->FocusKey = TEXT("p:") + GOMGPlaces::IdOf(FirstOf(TEXT("oreiron"), 3));
	Root->StatusUntil = 0;
	Use(SGOMGRoot::ETab::Map);
	bOk &= Shot(*this, Root, TEXT("2-map-places-1080.png"), 1920, 1080);

	Root->View = SGOMGRoot::EView::Settings; Root->Section = SGOMGRoot::ESection::Appearance;
	Root->RebuildSettings(); Root->RebuildPrompts();
	bOk &= Shot(*this, Root, TEXT("3-settings-1080.png"), 1920, 1080);

	Root->View = SGOMGRoot::EView::Places;
	Root->RebuildPrompts();
	bOk &= Shot(*this, Root, TEXT("4-map-places-1440.png"), 2560, 1440);
	return bOk;
}

#endif

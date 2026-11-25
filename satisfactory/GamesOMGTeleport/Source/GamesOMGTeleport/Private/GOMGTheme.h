// GamesOMG Teleport for Satisfactory 1.1.0: how the panel looks, copied from the Planet Crafter mod's Theme.cs (the model:
// Jason 2026-11-23, "Planet Crafter one should be the model where we can").
//   · the same palette: orange #f08000 for the main action, blue #3be3ff for focus, a dark panel
//   · the same text sizes. Planet Crafter's sizes are pixels (Unity IMGUI); Slate's are points at 96 DPI, so a Slate size
//     draws 4/3 as tall. 1.0.0 used Slate sizes as if they were pixels, so every word was a third bigger than the model's.
//     Font(Px) takes the model's pixel size and does the conversion.
//   · the same G Emblem mark and icon sheet (Planet Crafter's mark.png and icons.png, embedded: GOMGArt.inl)
//   · the game's own font, as the model uses its game's (Satisfactory's covers CJK, Arabic, Thai and Bengali)
#pragma once

#include "CoreMinimal.h"
#include "Fonts/SlateFontInfo.h"
#include "Styling/SlateBrush.h"

// Planet Crafter's Icon enum, in its order (one 64 px square each in icons.png)
enum class EGOMGIcon : uint8
{
	Check, ChevronRight, ChevronDown, Star, StarFilled, Gear, Close, Return, Clipboard, Save, Map, Trash, Search, Undo,
	Pencil, Go, Clock, Warn, Found, Pad, Keyboard, Folder, Help, Count
};

namespace GOMGTheme
{
	FLinearColor Hex(uint32 Rgb, float A = 1.f);
	FLinearColor Alpha(const FLinearColor& C, float A);

	extern const FLinearColor Orange, Blue, Ink, Muted, Faint, Panel, Surface, Raised, Line, Good, Bad, BadText, Note, OrangeHover, OnOrange;

	/** The game's font at Planet Crafter's pixel size (12.5 px there draws 12.5 px here) */
	FSlateFontInfo Font(float Px, bool bBold = false);

	/** A rounded plate, cached: the same colour and radius give the same brush */
	const FSlateBrush* Plate(const FLinearColor& Fill, float Radius);
	/** A rounded outline with a clear middle (the blue focus ring) */
	const FSlateBrush* Ring(const FLinearColor& Colour, float Width, float Radius);
	/** A rounded plate with an outline */
	const FSlateBrush* PlateLined(const FLinearColor& Fill, float Radius, const FLinearColor& Outline, float Width);
	const FSlateBrush* None();

	/** The gear-G mark, and one icon from the sheet (white: tint it with the SImage's colour) */
	const FSlateBrush* Mark();
	const FSlateBrush* Icon(EGOMGIcon Which);

	/** How wide a line of text draws in this font, in layout units (for the row's meta line, which drops parts that don't fit) */
	float Measure(const FString& Text, const FSlateFontInfo& Font);
}

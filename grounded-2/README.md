# GamesOMG Teleport for Grounded 2

Copy a spot on the [GamesOMG Grounded 2 map](https://gamesomg.com/grounded-2-map/), press **F7** in game, paste it
(**Ctrl+V**), press **Enter**. You're standing there. **F8** takes you back. Free, no app running beside the game, no
account.

Or skip the browser: **F7**, start typing a place (*pergola*, *network*, *dozer*) and press **Enter**. The list has the
outposts, labs, MIX.Rs, wonders and bosses, in the game's own names.

> Yes, AI helped make these. No, we're not sorry. They're free, they work, and they get patched faster than you can write
> the angry comment. Not for you? The back button still works.

The code was written with Anthropic's Claude and OpenAI's Codex and ChatGPT, directed by us and tested in the game.

**Tested in game** on Grounded 2 0.6.0.5 (Steam build 26399478), hosting solo. **Languages:** the panel and the place
list are in English (the game's English names). A pasted `/tp` line is only numbers, so it works whatever language your
game is set to.

## Install

Needs Grounded 2 on Steam (Windows). It runs on [UE4SS](https://github.com/UE4SS-RE/RE-UE4SS) 4.0.0, the mod loader
Grounded 2's mods use.

**Easy (no UE4SS yet):** download `Grounded2-GamesOMG-Teleport-<version>-Easy-Install.zip` from
[Releases](https://github.com/GamesOMG/gamesomg-mods/releases). It holds UE4SS 4.0.0 (without its console and cheat
add-ons) and this mod.

1. In Steam, right-click Grounded 2 → Manage → Browse local files, then open `Augusta\Binaries\Win64`
   (`Grounded2Steam-Win64-Shipping.exe` is in it).
2. Open the zip, select everything in it and drag it into that folder: `dwmapi.dll` and the `ue4ss` folder end up next to
   the exe.
3. Start the game from Steam as normal and load a world. Press **F7**: the GamesOMG Teleport panel opens.

**Already run UE4SS 4?** Take `…-Mod-Only.zip` and drag its `Augusta` folder into your Grounded 2 folder (Steam: Manage,
Browse local files), letting Windows merge it. It adds one mod folder (`Augusta\Binaries\Win64\ue4ss\Mods\GamesOMGTeleport`,
with an `enabled.txt` so it loads) and touches nothing else. It's laid out from the game's root so mod managers place it.

## Use it

| Key | What it does |
|---|---|
| **F7** | Opens the panel. Paste a `/tp` line copied from the map and press Enter, or type a place and press Enter (↑ ↓ to pick, or click one). Esc or F7 closes it. |
| **F8** | Back to where you stood before your last teleport. Press it again to swap back. |

Keys live in `ue4ss\Mods\GamesOMGTeleport\settings.txt`. Change one if another mod already uses it, then restart the game.

## Read this before you complain

- **It lands you beside the place, not on it.** Every place in the list and every map pin with a Copy Teleport button
  lands where we stood next to it in the game, underground outposts at their own floor, not on the lawn above them.
- **It watches the landing.** If you keep falling, or the area never finishes loading, it puts you back where you were.
- **The Abyss is under water.** Bring your diving gear, or the trip back is short.
- **On foot only.** Get off your mount or out of the buggy first.
- **Found ticks are per world, your saved spots are not.** Tick a place off and it moves to the Found drawer in that
  world only. Saved spots (My places) show in every world: it's the same yard.
- **Co-op:** it moves only you, never anyone else. Tested hosting solo; a guest's teleport goes through the host the way
  the game's own tubes do, but that path hasn't been played with a second person yet.
- It's a teleport and nothing else: no item spawns, no unlocks, no console, no cheat menu. It makes no network calls and
  never reads your clipboard: you paste into the box yourself.

## Uninstall

- **This mod only:** delete `ue4ss\Mods\GamesOMGTeleport`.
- **Everything the Easy zip added:** delete `dwmapi.dll` and the `ue4ss` folder from `Augusta\Binaries\Win64`. Your saves
  aren't in there.

## Bugs and questions

[Open an issue](https://github.com/GamesOMG/gamesomg-mods/issues) with your game version, the mod version and the line or
place you used. `ue4ss\UE4SS.log` helps (lines starting `[GamesOMG Teleport]`).

Not made or endorsed by Obsidian Entertainment or Xbox Game Studios. MIT licence; UE4SS is MIT too (its licence ships in
the zip).

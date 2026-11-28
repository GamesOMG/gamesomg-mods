# GamesOMG Teleport for Lord of Undead

Copy a pin on the [GamesOMG Lord of Undead map](https://gamesomg.com/lord-of-undead-map/), press **F7** in game. You're
standing beside it, and the minions following you come too. **F10** takes you back. Free, no app running beside the game,
no account.

Or skip the browser: **F7**, start typing a place (*copper mine*, *graveyard*, *rare chest*) and press **Enter**. Every
pin on the map is in the list: places, chests, ore, plants, camps and the enemies they hold.

> Yes, AI helped make these. No, we're not sorry. They're free, they work, and they get patched faster than you can write
> the angry comment. Not for you? The back button still works.

The code was written with Anthropic's Claude, directed by us. The game has no teleport of its own (its hidden developer
console spawns items and switches god mode on, nothing more), so this is the first.

> ⚠ **Not released yet.** 0.1.0 is built and its offline checks pass; the in-game proof (a survey that stands on every
> landing) has not run. There is no download until it has.

**Built for** Lord of Undead Early Access, Steam build 26969408 (Update #4), on BepInEx 6.0.0-be.793. **Languages:** the
panel and the place names are in English (the map's names). A `/tp` line is only numbers, so it works whatever language
your game is set to.

## Install

Needs Lord of Undead on Steam (Windows). It runs on [BepInEx 6](https://github.com/BepInEx/BepInEx) (the IL2CPP build),
the mod loader proven on this game.

**Easy (no BepInEx yet):** download `LordOfUndead-GamesOMG-Teleport-<version>-Easy-Install.zip` from
[Releases](https://github.com/GamesOMG/gamesomg-mods/releases). It holds BepInEx 6.0.0-be.793 exactly as its authors
publish it, and this mod.

1. In Steam, right-click Lord of Undead → Manage → Browse local files. That's the game folder (`Lord of Undead.exe` is in it).
2. Open the zip, select everything in it and drag it into that folder: `winhttp.dll`, `doorstop_config.ini`, `dotnet` and
   `BepInEx` end up next to the exe.
3. Start the game from Steam as normal. **The first start takes about a minute longer** while BepInEx builds its files for
   your copy of the game (it also downloads Unity's base libraries once, from BepInEx's own server). After that it starts
   at normal speed, until the game updates.
4. Load a world and press **F7**: the GamesOMG Teleport box opens.

**Already run BepInEx 6 (IL2CPP)?** Take `…-Plugin-Only.zip` and drop its `BepInEx` folder into the game folder. It adds
one folder (`BepInEx\plugins\GamesOMGTeleport`) and touches nothing else.

## Use it

| Key | What it does |
|---|---|
| **F7** | A `/tp` line on your clipboard (the map's **Copy Teleport Command**): you go straight there. Nothing copied (or you're already standing there): the box opens. Paste a line (Ctrl+V) or type a place, ↑ ↓ to pick, Enter goes. Esc or F7 closes it. |
| **F10** | Back to where you stood before your last teleport. Press it again to swap back. |

The box also has **Copy where I stand**: it puts your own spot on the clipboard as a `/tp` line, for a friend or for later.

Keys live in `BepInEx\config\com.gamesomg.lordofundead.teleport.cfg`. Change one if another mod already uses it, then
restart the game. The game itself uses F1 to F4 and F8 (photo mode), so leave those alone.

## Read this before you complain

- **It lands you beside the pin, not on it.** Every pin's line is a spot a few metres off it, on open ground at its own
  level: a cave's pin at the cave floor, a pin standing over water on the thing it stands on.
- **It checks the landing first.** The spot is snapped to the game's own walkable ground (the NavMesh its minions walk
  on, the one its Teleport Stone uses). No walkable ground within 4 m: it says so and moves nothing.
- **It watches the landing.** If you drop more than 8 m in the first 3 seconds, or hit the game's own fall-death zone, it
  puts you back where you stood.
- **Your squad comes along.** Minions following you (your current squad) within 40 m land beside you, each where the
  game itself would put them. Minions on guard at your base or out on patrol stay put. `BringSquad = false` in the
  config turns it off.
- **Not while the game has your movement locked** (a menu, an action), or while you're dead: it says so and moves nothing.
- **The box takes the keyboard and mouse while it's open,** so typing "wood" never walks you forward. Close it and the
  game has them back.
- It's a teleport and nothing else: no items, no unlocks, no cheats, and achievements are untouched. It makes no network
  calls and reads your clipboard only when you press F7 (or Ctrl+V in its box), and never keeps it.

## Uninstall

- **This mod only:** delete `BepInEx\plugins\GamesOMGTeleport`.
- **Everything the Easy zip added:** delete `BepInEx`, `dotnet`, `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`
  and `changelog.txt` from the game folder. Your saves aren't in there.

## Bugs and questions

[Open an issue](https://github.com/GamesOMG/gamesomg-mods/issues) with your game's Steam build, the mod version and the
line or place you used. `BepInEx\LogOutput.log` helps (lines with `GamesOMG Teleport`).

## Building it

Windows, the .NET SDK (8 or newer), Lord of Undead installed.
1. `powershell -ExecutionPolicy Bypass -File fetch-dependency.ps1` checks and unpacks the pinned BepInEx be.793 into `.deps\`.
2. `dotnet run -c Release --project tools\interop-gen -- "<Lord of Undead folder>" .deps\interop` builds the game's interop
   assemblies offline from `GameAssembly.dll` and `global-metadata.dat`, the way BepInEx does on the game's first start
   (the game never runs). They are the game's own stripped API: anything the mod calls is in the game's build.
3. `dotnet build -c Release` → `bin\Release\net6.0\GamesOMG.LordOfUndeadTeleport.dll`. `dotnet run --project tests` runs
   the checks that need no game (the `/tp` parser, the place list, the landing rule over every place).
4. `data\places.tsv` (every pin and its landing) comes from the site: `gamesomg-site\tools\lord-of-undead-map\tp\landings.py`.

Not made or endorsed by Megame. MIT licence (this mod). BepInEx and the parts it ships carry their own licences, listed
in the Easy zip's `THIRD-PARTY-NOTICES.txt`.

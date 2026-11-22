# GamesOMG Teleport for The Planet Crafter

Copy a spot on the [GamesOMG Planet Crafter map](https://gamesomg.com/planet-crafter-map/), go back to the game, press **F7**.
You're standing there. No app running in the background, no account, no menu to dig through, free.

It checks the landing before it moves you: a floor under your feet and room to stand (what that can't see is below).
**F3** (or **View / Share** on a controller) opens Places: the map's crates, wrecks, keys and the rest for the planet
you're on, plus your own saved spots. **F10** takes you back to where you were.

**Installed it with a mod manager?** Start the game with the manager's **Start modded** button. A normal Steam launch skips it.

> Yes, AI helped make these. No, we're not sorry. They're free, they work, and they get patched faster than you can write
> the angry comment. Not for you? The back button still works.

The code was written with OpenAI's Codex and ChatGPT and Anthropic's Claude, directed by us and tested in the game.

**Tested in game** on v2.104 (Steam build 26411416): Prime and both DLC worlds, Humble and Toxicity, single player and
as the co-op host. **Languages:** the panel is in English. A `/tp` line is only numbers, so teleporting works whatever
language your game is set to.

Full guide: **[gamesomg.com/planet-crafter-teleport-mod](https://gamesomg.com/planet-crafter-teleport-mod/)**

## Install

**Easy (no mod manager):** download `PlanetCrafter-GamesOMG-Teleport-<version>-Easy-Install.zip` from
[Releases](https://github.com/GamesOMG/gamesomg-mods/releases). It holds the BepInEx 5 mod loader and this mod.

1. In Steam, right-click The Planet Crafter → Manage → Browse local files. That's the game folder (`Planet Crafter.exe` is in it).
2. Open the zip, select everything in it and drag it into that folder. Everything goes next to `Planet Crafter.exe`, not into a new subfolder.
3. Start the game from Steam as normal. Load a world: a tip headed "GamesOMG Teleport is ready", with your keys, means it worked
   (the first time; after that it's a short "Ready." line).

**Already run BepInEx 5?** Take `…-Plugin-Only.zip` instead and drop its `BepInEx` folder into the game folder. It never touches your loader.

**Mod manager (r2modman, Gale, Thunderstore Mod Manager):** search for GamesOMG Teleport and install it.
⚠ **Then start the game with the manager's "Start modded" button.** A normal Steam launch skips every mod the manager installed.

## Use it

1. On the map, pick the same planet you're playing. Click a pin and press **Copy Teleport Command** (it copies a line like `/tp 785 31 795`).
2. Back in the game, on foot, with no menu open: press **F7**.

| Key | Controller | What it does |
|---|---|---|
| **F7** | (none) | Teleport to the `/tp` line on your clipboard. Nothing copied? A box opens to paste or type one. |
| **F3** | **View** (Xbox) / **Share** (PlayStation) | **Places**: Save here, My places (saved and recently travelled), Map places (the map's POIs for this planet, nearest first), Return, Settings. |
| **F10** | (none: set one in Settings) | **Return to previous spot**: back to where you stood before your last teleport. Press it again to swap back (two spots, not a history). |

View / Share is the one controller button the game leaves free while you play, so it's ours by default. Every other
button already does something in the game, which is why Return has no controller button until you pick one.

**Inside Places:** arrows, D-pad or left stick move · **Enter / A** teleports to the selected place · **Esc / B** goes
back · **Tab** moves on · **LB / RB** switch tabs · **X** saves where you stand · **Y** opens a place's actions (rename,
favourite, Mark found, Show on map, delete, with Undo). A mouse click on a place teleports there; right-click selects
it without going (Settings → Behaviour → "Selects it" makes a click select instead).

**Notes:** the small **+** at the right of a place opens a one-line note (Enter saves, Esc cancels, an empty note is
deleted); **→** (keyboard) or **D-pad →** opens it too, and a controller picks **Come back later**, **Locked** or **Delete
note**. A noted place shows its note in yellow under its name and stays where it is in the list: a note never moves it
to Found, and marking it found keeps the note. Notes are kept per save and travel with Export / Import.

**Settings** (Places → Settings) changes the same values as `BepInEx/config/gamesomg.planetcrafter.teleport.cfg`:
- **Controls:** change any key or button. One that clashes with ours or with the game's own controls is refused and
  says what it clashes with. Restore control defaults puts F3, F7, F10 and View / Share back.
- **Appearance:** text and panel size (70-160 %, bigger reads better on a TV), left or right, row height, background
  opacity (85-100 %: any lower and a bright scene behind the panel makes the text hard to read), reduced motion.
- **Behaviour:** what a click does, close after a teleport, sounds for done and refused (off by default; the text line
  always shows), the first-run tip again.
- **Data:** export a backup file, import one (a preview shows what's new, what's already there and what clashes before
  anything changes: merge by default, replace only if you pick it, your settings only if you tick them), restore an
  automatic backup, clear one list on one planet, paste a map link, open the mod's folder.
- **Help:** your keys, the "good to know" list below, and **Copy diagnostics** for a bug report (versions, settings and
  recent errors; never your clipboard, place names or positions).

**Map links: a transfer, not a sync.** The map's **Send to game** button copies one link with your map spots and found
marks: copy it, press F7 in game, and the box opens with the link in it (or Settings → Data → Paste a map link). The
panel's **Copy found marks for the map** does the reverse. Both ways only add. Moving everything to another PC is a
backup file (Settings → Data → Export), not a link.

## Read this before you complain

- **Same planet only.** A `/tp` line has no planet in it, so the mod can't tell which world you copied it from. Pick the
  same world on the map. It never moves you to another planet.
- **`~` means "find the ground from above".** Spots you drop on the map have no height, so they copy as `/tp x ~ z` and land
  you on the top surface. That's useless inside a cave: use the cave pin's own numbers instead.
- **Map and game don't sync.** A link is a transfer: you copy it one way and paste it the other. Nothing updates by itself.
- **Links only add.** Pasting a map link adds places and found marks. It never deletes a place or unmarks anything.
- **Mark found is yours.** You mark places found yourself (the box on its row, or a place's actions); it moves to the
  **Found** drawer at the bottom of Places, under the same headings. Teleporting somewhere never marks it, and
  the mod doesn't know what you picked up. Crate and ore marks stay in the game: the map's link only carries the
  collectibles it tracks (keys, chips, cards, golden crates and so on).
- **It checks the landing, not the neighbourhood.** Floor and room to stand: yes. Oxygen, toxic water, heat, enemies, or a
  floor that moves after you land: that's still your problem. Under water is allowed (you're wearing a space suit).
- **What the landing check can't see:** a pocket sealed inside solid rock, with ground under it, can pass as "room to
  stand". The map's pins copy landings we checked ahead of time; a line you type yourself, or a spot you drop
  on the map, gets only the live check. Landed somewhere wrong? F10.
- **The panel doesn't pause the game.** Single player or co-op, the world keeps going while Places is open, and you can
  still be hurt.
- **Co-op:** it moves only you, never another player. Tested as the host. A guest's places are kept per host world (its
  seed), so each host's world has its own list.
- **Achievements stay on.** The game turns them off only for worlds with Free Craft or Everything Unlocked, mods or not.
- **Your places are kept per save** in `BepInEx/config/gamesomg.planetcrafter.teleport.places.json`. A new world started
  under an old save's name starts clean; the old world's places stay, as "<name> (old world <seed>)".
- **Your places file is never thrown away.** It's backed up before an import, a restore or a clear (the last 10, in
  `BepInEx/config/gamesomg-teleport/backups/`). A file that won't read is kept as `….bad` beside a new one; a file from a
  newer version of the mod is used read-only until you update.
- **No network.** The mod makes no network calls. Show on map opens your browser only when you press it. The clipboard is
  read only when you press F7 or paste, and never stored.

The numeric lines (`/tp x y z`) also paste into akarnokd's
[Command Console](https://github.com/akarnokd/ThePlanetCrafterMods#feat-command-console). The `~` lines only work with this mod.

## Uninstall

- **Turn all mods off:** delete `winhttp.dll` from the game folder. Put it back to turn them on.
- **Remove this mod only:** delete `BepInEx/plugins/GamesOMGTeleport`. Your places, backups and exports stay in
  `BepInEx/config/` (`gamesomg.planetcrafter.teleport.*` and the `gamesomg-teleport` folder) for a later reinstall; delete
  those too to remove every trace. `BepInEx/cache/gamesomg-teleport/`, if 0.3.0 or 0.3.1 left one, only holds their font.
- **Remove everything the Easy zip added:** delete `BepInEx`, `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version` and
  `changelog.txt` from the game folder. Your saves are not in there; they're untouched.

## Bugs and questions

[Open an issue](https://github.com/GamesOMG/gamesomg-mods/issues) and say your game version, the mod version, and the
`/tp` line or place you used. Places → Settings → Help → **Copy diagnostics** puts the versions, settings and recent errors
on your clipboard to paste in. `BepInEx/LogOutput.log` helps too; neither holds your clipboard or your places.

## Build from source

You need the .NET SDK, Python 3 and your own copy of the game (its DLLs are compile references only; none is ever copied):

```powershell
./fetch-dependency.ps1
./release.ps1 -GameManaged '<game folder>/Planet Crafter_Data/Managed' -DryRun
```

`release.ps1` runs the checks, builds the DLL, makes the three zips and checks each against an exact file list. A real
release builds only from a tag (`planet-crafter-teleport-v<version>`).

## Licence and credits

MIT (see `LICENSE`). The panel uses the game's own font (0.3.0 and 0.3.1 carried Manrope; 0.3.2 dropped it).
The Easy zip bundles [BepInEx 5.4.23.5](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5)
unmodified, with its licences in `BepInEx/licenses/` (BepInEx, HarmonyX, MonoMod and Mono.Cecil are MIT; UnityDoorstop is
LGPL-2.1). The Planet Crafter is made by Miju Games; this mod is not made or endorsed by them.

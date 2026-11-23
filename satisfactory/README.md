# GamesOMG Teleport for Satisfactory

Every Somersloop, Mercer Sphere, hard drive, Power Slug, boombox tape, resource node, geyser and resource well from the
[GamesOMG Satisfactory map](https://gamesomg.com/satisfactory-map/), as a list in game. Press **F9**, click one, and you're
standing beside it. At its own height: inside the cave, under the overhang, under your factory, not on the roof above it.
**F10** takes you back.

The list reads your save. Slugs, Somersloops and Mercer Spheres you've picked up, hard drives you've taken and tapes you've
unlocked move into a **Found** drawer by themselves. Nodes, wells and starting areas you tick yourself.

Free. No app running in the background, no account, no time limit.

> Yes, AI helped make these. No, we're not sorry. They're free, they work, and they get patched faster than you can write
> the angry comment. Not for you? The back button still works.

The code was written with Anthropic's Claude, directed by us and tested in the game.

**Tested in game** on Satisfactory 1.2.5.1 (build 520140) with SML 3.12.1, 23 Nov 2026: solo (the host path: a cave, an
overhang, under a factory, a far hard drive, Back, a pasted /tp line, from a vehicle, German and Japanese, switching worlds)
and as a guest on a dedicated server (moved by the server, Found from the server's save, guests turned off, a server without
the mod).

**Languages:** the list speaks your game's language, in the game's own words. Every place, region, item, tape and purity
name comes from the game itself, and so do the headings and the Back, Close and Search buttons: set Satisfactory to German
and you get *Elektroschnecken*, *Festplatten*, *Zurück*; Japanese, *パワー・スラッグ*, *ハードドライブ*, *戻る*. That covers every
language the game translates (Chinese, Japanese, Korean, Russian, Polish, French, Spanish, Portuguese, Turkish, Arabic and the
rest). What the game has no word for stays in English: the **Found** drawer, the hints and the messages, and the "Resource
wells" heading. A `/tp` line is only numbers, so it works in any language.

## Install

Needs Satisfactory on Windows (Steam or Epic) and SML 3.12.1 or newer, the mod loader made by the Satisfactory Modding team.
Both go in the game's `FactoryGame\Mods` folder (Steam: right-click Satisfactory → Manage → Browse local files, then open
`FactoryGame`; make the `Mods` folder if it isn't there).

1. **SML:** download `SML-Windows.zip` from [SML 3.12.1](https://github.com/satisfactorymodding/SatisfactoryModLoader/releases/tag/v3.12.1)
   and unzip it into `FactoryGame\Mods\SML`. Already run mods with Satisfactory Mod Manager? It has put SML there: skip this.
2. **This mod:** download `Satisfactory-GamesOMG-Teleport-<version>-Windows.zip` from
   [Releases](https://github.com/GamesOMG/gamesomg-mods/releases) and unzip it into `FactoryGame\Mods\GamesOMGTeleport`.
3. Start the game, load a world, press **F9**.

Satisfactory Mod Manager only removes mod folders it installed itself, so it leaves this one alone.

**Multiplayer:** the host or the dedicated server needs the mod, and so does everyone who joins: the server is what moves
you, so the teleport works the same on a dedicated server as in single player. A dedicated server takes `SML-WindowsServer.zip`
or `SML-LinuxServer.zip` and our `-WindowsServer` or `-LinuxServer` zip, into the same two folders under its own
`FactoryGame/Mods`.

## Use it

| Key | What it does |
|---|---|
| **F9** | Opens **Places**. Click a place to go there. Up/Down and Enter work too. Esc or F9 closes it. |
| **F10** | **Back**: to where you stood before your last teleport. Press it again to swap back. |

Other keys: set `panelKey` and `backKey` (Unreal key names, like `F6` or `Insert`) under `settings` in
`%LOCALAPPDATA%\FactoryGame\Saved\GamesOMGTeleport\places.json` (paste that into the Explorer address bar), then restart
the game.

**Inside Places:**
- Headings are the map's: Alien artifacts, Hard drives & tapes, Power Slugs, Resource nodes, Resource wells, Starting areas
  (in another language, the game's own word for each).
  Click a heading to open it. Places are listed nearest first, with the region, the distance, and what matters about each
  (a hard drive's unlock cost, a node's purity, a well's nodes).
- The box at the left of a place ticks it: it moves to **Found**, under the same heading. Click it again to put it back.
  A green box means the game says it's collected; that one stays in Found.
- The search box filters by name, region or cost. Paste a **`/tp X Y Z`** line into it (the map's **Copy Teleport
  Command** gives you one) and press Enter to go there. If a `/tp` line is on your clipboard when you press F9, it's already
  in the box: just press Enter.

## How the landing works (read this before you complain)

- **Beside the place, at its height.** It looks for floor in rings around the place, starting just above the place's own
  height and looking down. That's how a cave Somersloop lands you in the cave and not on the hill over it. It wants floor
  you can stand on, room for you, and a clear view of the place.
- **It waits for the world to load.** Far trips hold you at the destination for a moment while the area streams in (on the
  server and on your side), then put you down. You can't move during that.
- **No safe spot, no teleport.** If there's nothing to stand on, you're put straight back where you were and told so. A place
  the survey found no landing for says "no landing" and can't be clicked.
- **It checks the landing, not the neighbourhood.** Gas, radiation, Stingers, a drop you walk off after landing: still your
  problem.
- **Not from a vehicle**, not while down or respawning.
- **The panel doesn't pause the game.**

## Dedicated servers

The server keeps one setting in `server.json`, in the server's own install folder under
`FactoryGame/Saved/GamesOMGTeleport/` (a game you host from your own PC keeps it beside your `places.json`):

```json
{ "allowGuests": true }
```

`false` lets only the host teleport (on a dedicated server, nobody). Restart the server after changing it.

## Where your ticks live

`%LOCALAPPDATA%\FactoryGame\Saved\GamesOMGTeleport\places.json`, per session (and per host, as a guest). Ticks the game
makes for you aren't stored there: they're read from the save each time. The mod makes no network calls of its own.

## Uninstall

Delete `FactoryGame\Mods\GamesOMGTeleport`. To remove every trace, also delete `%LOCALAPPDATA%\FactoryGame\Saved\GamesOMGTeleport\`.
The mod keeps nothing in your world. The mod loader does note which mods a save was played with, so the first time you load
that save without it you get a "Modded Save" warning naming GamesOMG Teleport: press Confirm and nothing goes missing.

## Source and licence

MIT. Source: [github.com/GamesOMG/gamesomg-mods](https://github.com/GamesOMG/gamesomg-mods) (folder `satisfactory`).
Bug reports: [issues](https://github.com/GamesOMG/gamesomg-mods/issues).

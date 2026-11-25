# GamesOMG Teleport for Satisfactory

Every Somersloop, Mercer Sphere, hard drive, Power Slug, boombox tape, resource node, geyser and resource well from the
[GamesOMG Satisfactory map](https://gamesomg.com/satisfactory-map/), as a list in game. Press **F9**, click one, and you're
standing beside it. At its own height: inside the cave, under the overhang, under your factory, not on the roof above it.
**F10** takes you back. **Save here** keeps your own spots (your base, that cave entrance) in **My places**, to go back to.

The list reads your save. Slugs, Somersloops and Mercer Spheres you've picked up, hard drives you've taken and tapes you've
unlocked move into a **Found** drawer by themselves. Nodes, wells and starting areas you tick yourself.

Free. No app running in the background, no account, no time limit.

> Yes, AI helped make these. No, we're not sorry. They're free, they work, and they get patched faster than you can write
> the angry comment. Not for you? The back button still works.

The code was written with Anthropic's Claude, directed by us and tested in the game.

**Tested in game** on Satisfactory 1.2.5.1 (build 520140) with SML 3.12.1, 23 Nov 2026: solo (the host path: a cave, an
overhang, under a factory, a far hard drive, Back, a pasted /tp line, from a vehicle, German and Japanese, switching worlds)
and as a guest on a dedicated server (moved by the server, Found from the server's save, guests turned off, a server without
the mod). That was 1.0.0. 1.1.0 kept its teleport and save reading and brought the new panel (My places, Settings),
run in game on 24 Nov 2026: Save here, Rename, a map place, a saved spot, Return both ways, Recently travelled, and a key
changed in Settings (F9 to F6 and back). 1.1.1 fixes what those runs found; its places file and key rules are checked by
tests outside the game.

**Languages:** the list speaks your game's language, in the game's own words. Every place, region, item, tape and purity
name comes from the game itself, and so do the headings, the search box's hint and Close: set Satisfactory to German, and
the headings say *Elektroschnecken* and *Festplatten*. That covers every language the game translates (Chinese, Japanese,
Korean, Russian, Polish, French, Spanish, Portuguese, Turkish, Arabic and the rest). What the game has no word for stays in
English: the buttons (Save here, Return, Teleport), the tabs, the **Found** drawer, Settings, the hints and the messages, and
the "Resource wells" heading. A `/tp` line is only numbers, so it works in any language.

## Install

Needs Satisfactory on Windows (Steam or Epic) and SML 3.12.1 or newer, the mod loader made by the Satisfactory Modding team.
Satisfactory Mod Manager can't install this mod yet, and you don't need it.

**Easy install** (the mod and SML in one zip):
1. Download `Satisfactory-GamesOMG-Teleport-<version>-Easy-Install.zip` from
   [Releases](https://github.com/GamesOMG/gamesomg-mods/releases).
2. Open your Satisfactory folder, the one with `FactoryGame` in it (Steam: right-click Satisfactory → Manage → Browse local
   files; Epic: Library → the three dots on Satisfactory → Manage → the folder icon beside Installation).
3. Open the zip, select everything in it and drag it onto an empty spot in that folder. You now have
   `FactoryGame\Mods\SML` and `FactoryGame\Mods\GamesOMGTeleport`.
4. Start the game, load a world, press **F9**.

The Easy-Install zip is built by `easy-install.ps1` from the two published zips, each file byte for byte: SML 3.12.1's
`SML-Windows.zip` (GPL-3.0; its licence and source link ride along in the zip) and our `-Windows.zip`.

**Already have SML** (you run mods through Satisfactory Mod Manager): download `Satisfactory-GamesOMG-Teleport-<version>-Windows.zip`
and unzip it into `FactoryGame\Mods\GamesOMGTeleport`, so your SML stays the version the manager chose. The manager only
removes mod folders it installed itself, so it leaves this one alone.

**SML by hand:** `SML-Windows.zip` from [SML 3.12.1](https://github.com/satisfactorymodding/SatisfactoryModLoader/releases/tag/v3.12.1),
unzipped into `FactoryGame\Mods\SML`.

**Multiplayer:** the host or the dedicated server needs the mod, and so does everyone who joins: the server is what moves
you, so the teleport works the same on a dedicated server as in single player. A dedicated server takes `SML-WindowsServer.zip`
or `SML-LinuxServer.zip` and our `-WindowsServer` or `-LinuxServer` zip, into the same two folders under its own
`FactoryGame/Mods`.

## Use it

| Key | What it does |
|---|---|
| **F9** | Opens **Places**. Click a place to go there. Up/Down and Enter work too. Esc or F9 closes it. |
| **F10** | **Return to previous spot**: where you stood before your last teleport. Press it again to swap back. |

Other keys: **Settings** (the gear at the top of Places) → **Controls** → Change, then press the new key.

**Inside Places** (the Planet Crafter mod's panel: the same layout and sizes):
- **From clipboard** goes to a `/tp` line or place id you copied. **Save here** keeps where you stand in **My places**.
  **Return to …** goes back to the spot you left, and says which one.
- **My places**: your favourites first, then your saved places, then **Recently travelled** (your last five trips).
- **Map places**: **Nearest** at the top lists the nearest places on the layers you tick. Under it are the map's headings
  (Alien artifacts, Hard drives & tapes, Power Slugs, Resource nodes, Resource wells, Starting areas; in another language,
  the game's own word for each). A heading opens to its layers, a layer to its places, nearest first, 15 at a time.
  Each row says what matters about the place (a hard drive's unlock cost, a node's purity, a well's nodes) and how far it is.
- The box at the left of a place marks it found: it moves to **Found**, under the same heading. Click it again to put it
  back. A green box the game ticked means the save says it's collected; that one stays in Found. **Hide found** hides the
  drawer; the **Nearest / A–Z** chip sorts.
- **+** on a row adds a short note ("Come back later", "Locked", or your own words). It never marks the place found.
- Right-click a place to pick it without going: Teleport, Favourite (or Save to My places), Mark found, Rename, Show on
  map (the GamesOMG map, at that pin), Delete (with Undo).
- The search box filters by name, region or cost. Paste a **`/tp X Y Z`** line into it (the map's **Copy Teleport
  Command** gives you one) and press Enter to go there. If a `/tp` line is on your clipboard when you press F9, it's already
  in the box: just press Enter.
- **Settings**: Controls (keys), Appearance (text and panel size, side, background), Behaviour (what a click does, close
  after a teleport), Help.

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

## Where your places live

`%LOCALAPPDATA%\FactoryGame\Saved\GamesOMGTeleport\places.json` (paste that into the Explorer address bar): your places,
notes, Recently travelled, found marks and settings, per session (and per host, as a guest). Ticks the game makes for you
aren't stored there: they're read from the save each time. `places.v2.json` beside it is the same file, kept where 1.0.0
never writes: go back to 1.0.0 and return, and your places come back from it. `places.v1.json` is your 1.0.0 file as it
was before 1.1 first wrote over it. Show on map opens your browser only when you press it; the mod
makes no network calls of its own.

## Uninstall

Delete `FactoryGame\Mods\GamesOMGTeleport`. To remove every trace, also delete `%LOCALAPPDATA%\FactoryGame\Saved\GamesOMGTeleport\`.
The mod keeps nothing in your world. The mod loader does note which mods a save was played with, so the first time you load
that save without it you get a "Modded Save" warning naming GamesOMG Teleport: press Confirm and nothing goes missing.

## Source and licence

MIT. Source: [github.com/GamesOMG/gamesomg-mods](https://github.com/GamesOMG/gamesomg-mods) (folder `satisfactory`).
Bug reports: [issues](https://github.com/GamesOMG/gamesomg-mods/issues).

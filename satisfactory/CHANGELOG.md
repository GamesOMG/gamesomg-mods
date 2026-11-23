# Changelog: GamesOMG Teleport for Satisfactory

## 1.1.0
Made to look and work like the Planet Crafter mod's panel, the model for every GamesOMG Teleport mod.
- **Save a spot.** Save here keeps where you stand in **My places** (named for the nearest map place: "Near Iron Ore, Grass
  Fields"). Rename it, star it as a favourite, give it a short note, delete it (with Undo), go back to it any time.
  **Recently travelled** keeps your last five trips. Each world has its own list.
- **My places | Map places**, as two tabs. Map places is the Planet Crafter tree: **Nearest** at the top (tick a layer and its
  nearest places show there), then each heading folds open to its layers, and each layer to its places, 15 at a time with
  "Show 25 more". Chevrons, not + and –. The Found drawer stays at the bottom. Headings count what you've found, "3/48 found"
  (1.0.0 said "45 left"), the same way in every GamesOMG Teleport mod.
- **Smaller text and a narrower panel**, the same sizes as the Planet Crafter mod: 1.0.0 drew every word a third bigger.
  A **Text and panel size** setting goes from 70 % to 200 %.
- The gear-G mark and **Places** in the header, the orange line along the top, the same colours and icons.
- Pick a place (right-click it, or set clicks to select) and its actions show under the list: Teleport, Favourite or Save to
  My places, Mark found, Rename, Show on map (opens the GamesOMG map at that pin), Delete.
- **From clipboard** goes to a `/tp` line or place id you copied. **Return to <where you were>** says where Return goes.
- **Settings** (the gear): change the Places and Return keys in game (no file editing, no restart), text size, panel side,
  background, what a click does, close after a teleport; Help says what everything does.
- Every place row has a **+** for a short note of your own ("Come back later", "Locked", or your own words).
- What happened (saved, deleted, refused) shows at the bottom of the panel while it's open; the line at the top of the
  screen is for when it's closed.
- `places.json` is version 2. A 1.0.0 file reads as it is (keys, ticks, Return) and is copied to `places.v1.json` before
  1.1.0 first writes over it.

## 1.0.0
First release.
- 23 Nov 2026, same day: an **Easy-Install** zip joins the release, the mod and SML 3.12.1 (unmodified) laid out as the
  game folder, so one download installs both. The mod's files are the `-Windows` zip's, byte for byte.
- **Places** (F9): every Somersloop, Mercer Sphere, hard drive, Power Slug, boombox tape, resource node, geyser, resource well
  and starting area from the GamesOMG Satisfactory map, by the map's headings, nearest first.
- Ticks from your save: picked-up slugs, Somersloops and Mercer Spheres, looted hard drives and unlocked tapes move to a
  **Found** drawer by themselves; tick the rest yourself.
- Click a place: you land beside it at its own height (caves, overhangs, under your factory), after the area has loaded.
  **Back** (F10) returns you; press it again to swap back.
- Paste a `/tp X Y Z` line from the map's Copy Teleport Command into the search box.
- Works solo, hosting, as a guest and on dedicated servers (the server moves you).
- In your game's language: names, headings and buttons come from the game's own text.

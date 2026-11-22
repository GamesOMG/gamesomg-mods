# Changelog: GamesOMG Teleport for Grounded 2

## 0.2.1
- **Found ticks really are per world now.** 0.2.0 looked for the world's id in the wrong place, found nothing on game
  0.6.0.5, and kept one list for every world. 0.2.1 reads the id the game gives the loaded world (the same one its save
  folders carry, `(ID-…)`), so each world has its own `ticks-<that id>.txt`. A 0.2.0 `ticks.txt` carries into the first
  world you open the panel in (the old file is kept as `ticks-carried-into-<id>.txt`).
- Long place names wrap inside their own column instead of running into the PTS / N · E / UNDER WATER text on the right.
  The search box's hint fits the box, the map link at the bottom stays inside the panel, and the group headings' fold
  marks are + and – (the font has no ▸ ▾, so 0.2.0 showed a box there).
- Typing a place's name and pressing Enter goes to that place, even when it's in the Found drawer. 0.2.0 picked the first
  place in the same area instead ("pergola" with Pergola found went to the Crow's Nest).
- A name you type for Save here is cut at 48 letters, never through the middle of one (names typed in any language).

## 0.2.0 (unreleased)
- The panel slides in from the right side, full height and see-through, like the Planet Crafter mod's.
- **My places:** Save here keeps where you stand (the name you typed, or "Near <place>"); × removes one. Kept in
  `my-places.txt` next to the mod, so they hold across your worlds (one yard).
- **Groups fold:** My places, Outposts, Labs, MIX.R, Wonders, Bosses are drop-downs (click, Enter, or ← →); typing opens
  every group with a match.
- **Tick places off:** the box on each row; each group counts "found / all". Meant to be kept per world; on game 0.6.0.5
  every world shared one `ticks.txt` (fixed in 0.2.1).
- An F8 Back button in the panel too.
- **Found drawer:** a ticked place leaves its group and drops into a closed **Found** drawer at the
  bottom, under the same heading and in the same order; untick it and it goes back. A heading with nothing left above
  disappears; the drawer shows only when something is in it.
- Abyss outposts (and The Wall) land inside at their own tube door; places that land you under water say UNDER WATER.

## 0.1.0 (unreleased)
- First build. **F7** opens the panel: paste a `/tp` line copied from a pin on the GamesOMG Grounded 2 map and press
  Enter, or type a place's name and pick it from the list (outposts, labs, MIX.R, wonders, bosses, in the game's own
  names). **F8** takes you back to where you were; press it again to swap back.
- Moves you with the game's own fast-travel call (the one the PTS uses), the engine's teleport as a fallback.
- Watches every landing: if you keep falling, or the area never finishes loading, it puts you back where you were.
- Each listed place lands where our in-game survey stood beside it (underground outposts at their own floor), never on it.

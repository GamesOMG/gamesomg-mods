# Changelog: GamesOMG Teleport for Grounded 2

## 0.2.0
- The panel slides in from the right side, full height and see-through, like the Planet Crafter mod's.
- **My places:** Save here keeps where you stand (the name you typed, or "Near <place>"); × removes one. Kept in
  `my-places.txt` next to the mod, so they hold across your worlds (one yard).
- **Groups fold:** My places, Outposts, Labs, MIX.R, Wonders, Bosses are drop-downs (click, Enter, or ← →); typing opens
  every group with a match.
- **Tick places off:** the box on each row; each group counts "found / all". Kept per world (`ticks-<world id>.txt`, the
  game's own id for the loaded world); an older single `ticks.txt` carries into the first world you load.
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

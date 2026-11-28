# Changelog: GamesOMG Teleport for Lord of Undead

## 0.1.0 (not released yet: waits on the in-game survey)
- First build, for Lord of Undead build 26969408 (Update #4) on BepInEx 6.0.0-be.793.
- **F7** with a `/tp` line on the clipboard (the map's Copy Teleport Command) teleports straight there. With none, it
  opens the box (so does F7 when you're already standing at the copied spot): paste a line, or type a place from the map's own list (every pin: places, chests, ore, plants, camps,
  the enemies they hold), ↑ ↓ to pick, Enter goes. **F10** takes you back, and again swaps back.
- Moves you with the game's own teleport (`PlayerRespawn.TeleportTo`, the one its Teleport Stone uses), to a spot
  snapped to the game's walkable NavMesh within 4 m of the line, on the same level. No walkable ground: refused, nothing
  moves.
- Watches every landing for 3 s: a drop of more than 8 m, or the game's fall-death zone, puts you back where you stood.
- The minions following you within 40 m come along, each to a spot the game picks beside you.
- **Copy where I stand** puts your own spot on the clipboard as a `/tp` line.
- While the box is open the game gets no keyboard or mouse, so typing a place never moves you.

# Changelog

## Next (not released yet)

- **The new GamesOMG logo.** The header's mark is the G Emblem the site has worn since 11-22, not the old gear.
- **Return names the place you went to.** Floating Rocks, then an Offline drone, and the Return button said "Return to
  Osmium": it named the nearest map place to where you stood, and an Osmium vein sat closer than the rocks' own pin. Now
  the place you teleported to names it while you're still within 30 m of where it landed you; walk off and it's the
  nearest place, as before.

## 0.3.3

- **Distance first, height only when it matters.** 0.3.0 to 0.3.2 added "N m above / below you" to every place 3 m or more
  higher or lower than you, which was most rows. Now a row says how far, and adds the height only when the place is
  mostly straight up or down from you (20 m or more, and at least half as far up or down as it is across), or when it is a
  cave or underground place (from 3 m). When a one-line row is short of room, the height goes first, then the category;
  the distance always stays.
- **Easier to read.** Section headings, the layer groups in Map places, places with no checked landing yet, the search
  box's hint and greyed-out buttons were dark grey on a dark panel. They now use the same lighter grey as the distance
  lines, and that grey is lighter too. Every piece of text measures at least 4.5:1 against what's behind it (the WCAG
  level for body text), even with the panel at its most see-through over a bright, snowy scene.
- **Background opacity now goes from 85 % to 100 %** (it was 60 % to 100 %). Below 85 %, a bright scene showing through
  made even white text hard to read. A setting under 85 % reads as 85 %.
- **A key the game already uses is refused** when you set one in Settings → Controls, as this changelog promised in
  0.3.0: F8 (the game's Show feedback), F4 (Unstuck), F2 (Photo mode) and the rest of its play keys. The panel says
  which game action has it, and nothing changes. The defaults (F3, F7, F10 and View / Share) are unaffected.
- Error messages in the panel are a lighter red.

## 0.3.2

- **Compact by default.** Rows are 32 px at 1080p (Comfortable is still in Settings → Appearance), and the panel is
  narrower: 360 px at 1080p, down from 420 (0.2.6 was 380). Buttons, fields and tabs are shorter in Compact too.
- **Easier to read.** The panel uses the game's own font again, as 0.2.6 did. Manrope is gone from the DLL.
- **One click teleports again**, as in 0.2.x. Right-click a place to select it without going (for Mark found, Rename,
  Show on map and the rest). Enter / A still teleport. "Selects it" is still in Settings → Behaviour.
- **Notes.** Every place has a small **+** on the right. Click it and type one line: Enter saves, Esc cancels, and an empty
  box deletes the note. A place with a note shows it in yellow under its name, with a yellow edge, so it stands out while
  you scroll. It stays where it is in the list; a note never moves it to Found, and marking it found keeps the note. On the
  keyboard, → on a place opens its note; on a controller, D-pad →, then **Come back later**, **Locked** or **Delete note**
  (a controller can't type). Notes are kept per save, beside your found marks, and travel with Export / Import.
- **Your settings:** if your `.cfg` still had 0.3.0/0.3.1's defaults (Comfortable rows, click selects), they move to the new
  ones once. Anything you changed stays as you set it.
- The selected place's buttons keep their whole labels and wrap onto a second line when the panel is narrow, and a
  place's distance line drops its category first rather than being cut off.
- **Keyboard fix (0.3.0's):** after typing in the search box, Esc now really leaves it. Before, Enter, Esc and the Places
  key kept typing into the box until you clicked somewhere.
- A refused teleport now writes what the landing check met (each thing the ray hit, its scene and height) to
  `BepInEx/LogOutput.log`, so "No safe ground was found" says why.
- The places file is schema 4 (notes). Your 0.3.x file is backed up once before the first write
  (`BepInEx/config/gamesomg-teleport/backups/`). 0.3.0 and 0.3.1 read it but never write over it.

## 0.3.1

- **Found moves.** A place you mark found leaves the list and goes into a **Found** drawer at the bottom of Places. Before,
  it stayed where it was, greyed out. The drawer is closed until you open it, says how many are inside, and keeps the same
  headings: each found place sits under its own group and layer, in the order it had above. Unmark one and it goes back.
- A layer with every place found leaves the list, and so does a group with nothing left in it. Nearest skips found places.
- **Hide found** now hides the Found drawer. A search shows the found matches in the drawer, open.
- Your marks from earlier versions are in Found after the update. Nothing in the places file changes.

## 0.3.0

The Places panel, rebuilt: it looks like the map, works on a controller, and has settings you can change in game.

- **New look.** The GamesOMG colours and font (Manrope), bigger rows (Compact in Settings fits more), drawn icons. Names in
  a script Manrope lacks fall back to the game's own font.
- **Controller.** View (Xbox) / Share (PlayStation) opens Places: the one button the game leaves free while you play. In
  the panel: D-pad or stick to move, A teleports, B goes back, LB/RB switch tabs, X saves where you stand, Y opens the
  actions for a place. Keyboard: arrows, Enter, Esc, Tab.
- **Settings in game** (Places → Settings), the same values as the `.cfg` file:
  - Controls: change any key or button. One that clashes with ours or with the game's own controls is refused, with
    what it clashes with. Return to previous spot can get a controller button (none by default).
  - Appearance: text and panel size (70-160 %), left or right, row height, background opacity, reduced motion.
  - Behaviour: clicking a place selects it or teleports at once (Enter / A always teleports), close after a teleport,
    sounds for done and refused (off by default), show the first-run tip again.
  - Data: export a backup file and import one on another PC (a preview shows what is new, what is already there and what
    clashes, before anything changes), restore an automatic backup, clear one list on one planet, open the mod's folder.
  - Help: your keys, what the mod does and doesn't promise, Copy diagnostics (versions, settings and recent errors; never
    your clipboard, place names or positions).
- **Backups.** The places file is backed up before anything big (import, clear, restore); the last 10 are kept in
  `BepInEx/config/gamesomg-teleport/backups/`. Deleting a place has Undo.
- **Save here** names the spot after the nearest map place within 60 m ("Near Golden crate"), else after the planet and
  the coordinates. Rename it later.
- **F10 is now "Return to previous spot"** and shows where it goes. Same key, same two-spot swap.
- **A refused teleport from Places** reopens the panel on the same place with the reason, so you can pick another.
- **First-run tip** with your keys, once.
- **Your places file is never thrown away:**
  - a damaged file is kept beside the new one (`.bad`, `.bad2`…, never written over), and the panel says so;
  - a places file from a newer version of the mod is used read-only: nothing is saved over it until you update.
- **A new world on an old save's name starts clean.** Same name, different world seed: the old world's places are kept as
  "<name> (old world <seed>)", and Settings → Data can export them. Before, the new world inherited the old one's places
  and ticks.
- **Landings see far platforms.** Some of the game's floors (launch pads, lab floors) switch off while you're far away. The
  landing check now switches on the ones you'd find there (and off again if it refuses), so a good platform no longer
  reads as "no ground" or drops you on the floor below it.
- **Closing Places while a game window is open** (inventory, chat, a machine) leaves that window in charge of your
  controls. Before, closing the panel could hand your movement back underneath it.
- Places, ticks and Return are still kept per save. In co-op, a guest's places are kept per host world seed.

## 0.2.7

First public release.

- **F7** teleports to a `/tp x y z` line copied from the GamesOMG Planet Crafter map, after checking the landing (a floor,
  room to stand). `~` as the height finds the ground from above. Nothing copied: F7 opens a box to paste or type a line.
- **F3** places panel: save where you stand, your saved and recent spots, and Places (the map's POIs for the planet you're
  on, grouped as on the map, nearest first, with ✓ ticks you set yourself).
- **F10** Back: to where you stood before your last teleport, and again to swap back.
- Map links: **Send to game** (spots and Found ticks) pastes into the panel's top line; **Copy for the map** sends your
  ticks back. Both only add.
- Places, ticks and Back are kept per save.
- Water landings are allowed: it lands you on the bed below.

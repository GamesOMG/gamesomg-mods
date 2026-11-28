# GamesOMG Item Spawner for The Planet Crafter

Search any item by the name you see in the game, pick it from a grid of the game's own icons, and it goes straight into your
backpack. No ids to look up, nothing rolling down a hill.

**Installed it with a mod manager?** Start the game with the manager's **Start modded** button. A normal Steam launch skips it.

> Yes, AI helped make these. No, we're not sorry. They're free, they work, and they get patched faster than you can write
> the angry comment. Not for you? The back button still works.

The code was written with Anthropic's Claude, directed by us and tested in the game.

Full guide: **[gamesomg.com/planet-crafter-item-spawner](https://gamesomg.com/planet-crafter-item-spawner/)**

- **Press F6** (or **LB + RB** together on a controller). Type part of a name: "iron", "alloy", "oxyg". It searches the
  game's own item names in whatever language your game is set to, plus the English names and the game's ids.
- **Enter** adds the amount in the box (1 to start). **×1**, **×10** and **Fill** are one click (X and Y on a controller).
- **Backpack full?** What fits goes in, the rest isn't made, and the panel says how many didn't fit. Nothing is ever dropped
  on the ground.
- **Decoration** (LB/RB to the second tab): every effigy, poster and spacesuit skin the game hands out in crates, and the
  game's furniture that only changes how a base looks (beds, sofas, tables, lamps, the arcade machine…). Effigies, posters and
  furniture are buildings in the game, so they go into your backpack and you put them down the way you would a found one:
  right-click it in your inventory, no recipe or unlock needed. Take a piece of furniture down and you get its parts back
  (and, for one the game unlocks from wrecks, its blueprint chip while it's still locked). **One of each** (the button above
  the grid, or Y on a controller) adds one of every decoration shown that your world doesn't have yet (not on you, in a
  container or built). Search first to narrow it ("poster"). It follows the same backpack rule as everything else: what
  doesn't fit isn't made, so put some away and press it again for the rest.
- **Blueprints** (LB/RB, the tab wears the game's own word for it): every blueprint chip, under the name of the building or
  item it unlocks, as the game itself shows them. Search by that name, or "blueprint". Decode one at the game's Blueprint
  screen and that blueprint is unlocked for good; the line under it says what it unlocks, or that your world already has it.
  There's no "one of each" here: it would be an unlock-everything button.
- **Recent** (LB/RB) holds what you spawned last. The open key then Enter adds your last item again.
- **Esc** clears the search, then closes. **B** closes on a controller. The panel holds your character still while it's
  open, like any text box in the game; the world keeps going (it's not a pause).
- **Pick an item and see what it does**, under its name: the game's own description, and for a seed, larva or egg its
  multiplier and where it counts ("Oxygen multiplier: 600% (×6). Counts in: Tree Spreader. In a pot it adds nothing."),
  for a fuse its boost and each optimizer's reach ("+500% per fuse; fuses add up. Nearest machines: 5 within 120 m (Machine
  optimizer) · 8 within 250 m (T2 Machine Optimizer)"), for food and water what it restores ("Food +12"). Every number is
  read from your game when you pick the item, so a patch that changes one changes it here too.

## Languages

- **Item names, descriptions, building names and the game's labels** ("Oxygen multiplier", "Free build") are the game's own,
  in whatever language your game is set to. Search matches the names in your language, the English names and the game's
  ids, and ignores accents and capitals: in French, "Fer", "fer" and "iron" all find Iron; in German, "dunger" finds Dünger.
- **The mod's own words** (its buttons, notices, the info line's wording and Help) are GamesOMG's translations, in every
  language the game itself ships: English, French, Spanish, Brazilian Portuguese, German, Simplified Chinese, Traditional
  Chinese, Japanese, Korean, Polish, Russian, Turkish and Italian. They follow the game's language setting, and fall back to
  English if a future game update adds a language we don't have yet.

## Achievements stay on

The game switches achievements off for exactly two world settings: Free build and All unlocked. This mod never turns
either on (its build checks that the DLL doesn't even reference them). One thing to know: the game counts an item as
*found* the first time your world has one, however it got there, so spawning Iridium, Osmium, a Golden Seed and a few
others in a world that never had them unlocks their "found" achievement.

## Co-op

Solo and as the host, items are made and saved by your game. As a guest, the host's game makes each item (the same request
crafting uses), so it saves in their world; the host doesn't need this mod. It's their world, though:
ask before you fill it with Super Alloy.

## Install

**Easy (no mod manager):** download `PlanetCrafter-GamesOMG-Item-Spawner-<version>-Easy-Install.zip` from
[Releases](https://github.com/GamesOMG/gamesomg-mods/releases). It holds the BepInEx 5 mod loader and this mod.

1. In Steam, right-click The Planet Crafter → Manage → Browse local files. That's the game folder (`Planet Crafter.exe` is in it).
2. Open the zip, select everything in it and drag it into that folder. Everything goes next to `Planet Crafter.exe`, not into a new subfolder.
3. Start the game from Steam as normal, load a world, press **F6**.

**Already run BepInEx 5** (for GamesOMG Teleport, say)? Take `…-Plugin-Only.zip` instead and drop its `BepInEx` folder into
the game folder. It never touches your loader. The two GamesOMG mods use different keys and sit side by side.

**Mod manager (r2modman, Gale, Thunderstore Mod Manager):** search for GamesOMG Item Spawner and install it.
⚠ **Then start the game with the manager's "Start modded" button.** A normal Steam launch skips every mod the manager installed.

## Uninstall

- **Remove this mod only:** delete `BepInEx/plugins/GamesOMGSpawn`. Its settings file
  `BepInEx/config/gamesomg.planetcrafter.spawn.cfg` can go too.
- **Turn all mods off:** delete `winhttp.dll` from the game folder. Put it back to turn them on.
- Items you spawned stay in your save, like anything you crafted.

## Settings

`BepInEx/config/gamesomg.planetcrafter.spawn.cfg` (or your mod manager's config editor):

| Setting | Default | |
|---|---|---|
| Controls · Key | `<Keyboard>/f6` | Opens and closes the panel |
| Controls · PadKey | `<Gamepad>/leftShoulder + <Gamepad>/rightShoulder` | Buttons joined by `+` are pressed together; empty = none |
| Appearance · TextSize | 100 | Text and panel size, percent (70–200) |

F6 was picked because the game doesn't use it (build 26411416), nor does GamesOMG Teleport (F3, F7, F10, View) or Spawn
Object (End). When you load a world, the mod checks its keys against the game's own bindings, including your rebinds, and
says so if one clashes. It can't see other mods' keys.

## How it works

Items go in through `InventoriesHandler.AddItemToInventory`, the call the game itself uses to put a crafted item into your
backpack, so they sync in co-op and save like anything you crafted. When the backpack refuses one, the game has already made
the item behind the scenes; the mod deletes that leftover with the game's own call so your save doesn't keep an item that is
nowhere. It works with stacking mods: the game's answer decides what fits, not a slot count.

The panel is opaque and every text colour is at least 4.5:1 against what it sits on (WCAG AA), measured by the build.

Tested in game on The Planet Crafter v2.104 (Steam build 26411416) with BepInEx 5.4.23.5, single player. The co-op guest
path is the game's own crafting request; it has not been played with a second person yet. Free, from
[GamesOMG](https://gamesomg.com/mods/). Not made or endorsed by Miju Games.

## Bugs and questions

[Open an issue](https://github.com/GamesOMG/gamesomg-mods/issues) and say your game version, the mod version, your game's
language and the item you searched for. `BepInEx/LogOutput.log` helps.

## AI disclosure

This mod was written with the help of generative AI (Anthropic Claude), directed and tested in game by GamesOMG. The DLL
says so in its metadata too.

## Licence

MIT (LICENSE). The Easy zip bundles [BepInEx 5.4.23.5](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5)
unmodified, with its licences in `BepInEx/licenses/` (BepInEx, HarmonyX, MonoMod and Mono.Cecil are MIT; UnityDoorstop is
LGPL-2.1). The Planet Crafter is made by Miju Games; this mod is not made or endorsed by them.

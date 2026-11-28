# Changelog

## 0.4.0 (not released yet)

- **Furniture on the Decoration tab.** Beds, sofas, chairs, tables, lamps, the arcade machine, the pool table, the planet
  viewer: the game's furniture that only changes how a base looks, under the game's own names. Each piece goes into your
  backpack and you put it down like an effigy: right-click it in your inventory, no recipe and no unlock needed. Take one down
  and you get its parts back; for furniture the game unlocks from wrecks, you also get its blueprint chip while it's still
  locked. One of each covers furniture too.
- Furniture that does something stays out: storage, pots and planters, display cases, anything that makes heat, oxygen or
  plants, and anything the game unlocks with a blueprint chip of its own (that comes as its chip, below).
- **Blueprints tab: every blueprint chip, named.** The game gives its chips no name of their own and shows each one under the
  name of what it unlocks; the panel now does the same (before, a chip only came up when you searched its id). Search by that
  name, or "blueprint". Decode one at the game's Blueprint screen and that blueprint is unlocked for good. The line under it
  says what it unlocks, or that your world already has it. No "one of each" here: it would be an unlock-everything button.
- All, Decoration, Blueprints and Recent (LB/RB step through them). The Blueprints tab wears the game's own word for it, in
  your language.
- **Fixed:** an empty Recent tab said `Nothing matches ""` instead of "Items you spawn show up here."
- Nothing is a fixed list: furniture and chips come from the game's own data when a world loads, so new ones in a game update
  show up without a mod update.
- Help's Decoration section covers furniture, and there's a Blueprints section. Every new line is in every game language.

## 0.3.0 (2026-11-28)

- **Fixed: effigies and posters were missing.** An item spawner that couldn't spawn them: the game files effigies and posters
  as buildings, not items, so 0.2.x's list never had them (a player on Nexus found the gap). They're in now. They go into your
  backpack and you put them down the same way as one you found: right-click it in your inventory.
- **Decoration tab: effigies, posters and spacesuits.** All, Decoration and Recent (LB/RB step through them). Decoration holds
  every effigy, poster and spacesuit skin the game hands out in crates, under the game's own names, searchable like All.
- **One of each.** The button above the Decoration grid (Y on a controller) adds one of every decoration shown that your world
  doesn't have yet: not on you, not in a container, not built. Search first to narrow it ("poster", "effigy"). Same rules as
  Add: what fits goes in, the rest isn't made, nothing is dropped. Put some away and press it again for the rest.
- Nothing is a fixed list: the mod picks decorations from the game's own data when a world loads, so new effigies and skins in a
  game update show up without a mod update.
- Help has a Decoration section. Every new line is in all 13 game languages.
- Tested in game on v2.104 (Steam build 26411416), on this release's code: the Decoration tab against the game's own list, a
  poster into the backpack, a spawned golden effigy put down from the backpack, One of each into a part-full backpack, a full
  one and again after making room (each decoration once, nothing dropped), every check of 0.2.1 again, every game language,
  achievements on.

## 0.2.1 (2026-11-27)

- **The new GamesOMG logo.** The header's mark is the G Emblem the site has worn since 2026-11-22, not the old gear.
  Nothing else changed: same search, same backpack rules, same keys, same settings file.
- Tested in game on v2.104 (Steam build 26411416) from this release's own zip: search by name, Enter / ×10 / Fill into
  the backpack, a full and a part-full backpack with nothing dropped, every game language, achievements on.

## 0.2.0 (2026-11-21)

- **Pick an item and see what it does**, in small print under its name: the game's own description in your game's
  language, and one line on what the item does. Every number in it is read from your game when you pick the item, never
  typed in by us, so a patch that changes a value changes it here too.
  - **Seeds, larvae, eggs:** the multiplier as the game shows it and as × ("Oxygen multiplier: 600% (×6)"), and the
    buildings it counts in ("Vegetube, Flower Spreader"). A seed a pot also takes says "In a pot it adds nothing." A 0 %
    seed reads "none (×1)", the game's own rule.
  - **Fuses:** the boost per fuse, that fuses add up, and how many machines each optimizer reaches from where you stand
    ("5 within 120 m (Machine optimizer) · 8 within 250 m (T2 Machine Optimizer)").
  - **Food, water, oxygen, toxins:** the gauge and the amount ("Food +12", "Hydration +30").
- **The mod's own words in every language the game ships:** English, French, Spanish, Brazilian Portuguese, German,
  Simplified and Traditional Chinese, Japanese, Korean, Polish, Russian, Turkish and Italian. They follow the game's
  language setting and fall back to English. Counts read "{n} × {item}", so no language has to bend a plural around an
  item name.
- Help explains the item info and says which words are the game's and which are ours. It scrolls (wheel, Up / Down) when a
  language needs more room. The status line holds two lines.
- **Fix:** in the second world you load in one game session, "In your backpack" could read 0. The item list now follows
  the game's own list every time a world loads.
- The planet-wide multipliers that rockets fill are never listed as a place a seed counts in.
- Tested in game on v2.104 (Steam build 26411416): every check of 0.1.0 again, the item info on seeds, larvae, fuses, food
  and water, and all 13 languages walked (the longest item names, every result line, an empty search, Help) with nothing
  overflowing.

## 0.1.0 (2026-11-21)

First release.

- A grid of the game's item icons with a search box. Search matches the game's own item names in whatever language your
  game is set to, plus the English names and the ids. Case, accents and separators don't matter (in French "Fer" and
  "iron" both find Iron). The best match is first, so Enter takes it.
- Items go straight into the backpack the way crafting puts them there: solo, co-op host, and co-op guest (the host's game
  makes them). Nothing is ever dropped on the ground. A full backpack takes what fits and the panel says how many didn't.
- Amount box + Add, ×1, ×10, Fill (stops at 999). A Recent tab. The last item is selected when the panel opens.
- F6, or LB + RB on a controller; both checked against the game's own key bindings (yours included) when a world loads.
- Achievements stay on: the mod never touches Free build or All unlocked (the build fails if the DLL even mentions them).
- An opaque panel; every text style is at least 4.5:1 against what it sits on.
- Items the game never names (the growing and hatched states of seeds and eggs, some blueprint chips) are left out of
  browsing and come up only when a search matches their id.
- Tested in game on v2.104 (Steam build 26411416): keys free, every English name finds its item, achievements on before
  and after, a full and a part-full backpack with nothing on the ground, French "Fer" finds Iron, keyboard and controller.

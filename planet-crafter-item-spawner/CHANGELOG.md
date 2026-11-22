# Changelog

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

# Nexus page fields

What the Nexus page (https://www.nexusmods.com/graveyardkeeper2/mods/206, author LeBetoven) holds besides
`description.bbcode`, `summary.txt` and `thumbnail/thumbnail.png`.

The description's two GIFs are hosted in the mod's own gallery on Nexus (not on GitHub), so the page works while the
code isn't public. The editor is SCEditor: paste the BBCode in its "View source" mode.

## Mod

- **Name:** Instant Transitions
- **Category:** Utilities
- **Tags:** Performance Optimization, Quality of Life, AI Assisted
- **Version:** 0.9.0
- **Language:** English

## Gallery (in this order)

1. `thumbnail/thumbnail.png` (the mod's thumbnail): "Instant Transitions: doors and map travel without the freeze"
2. `docs/images/doors-vs-vanilla.gif`: "Side by side: a door and a map trip, without mods and with Instant Transitions"
3. `docs/images/loading.gif`: "The loading screen: the game loads, then Instant Transitions prepares every place on the map"

## Requirements

Mod requirements (legacy), external resource:

- **Name:** BepInEx 5.4.23.x
- **Link:** https://github.com/BepInEx/BepInEx/releases
- **Notes:** Already included in the "with BepInEx" file. Only needed if you download the mod-only file.

## Permissions and credits

Closed for now (decided 2026-09-28): the code isn't on GitHub yet. Nexus's "Use recommended settings": re-upload
and conversion not allowed; modification and asset use, ask the author; donation points and monetisation for others,
not allowed. When the code goes public, switch to the MIT text Crafting Queue uses and link the repo.

- **Third-party content:** Yes (I have permission). Only BepInEx, inside the "with BepInEx" file; its LGPL-2.1
  license allows bundling it unmodified.
- **Credits:**

> BepInEx by the BepInEx team (LGPL-2.1): https://github.com/BepInEx/BepInEx - bundled unmodified in the "with BepInEx" file, as its license allows, and it keeps its own license. Everything else in Instant Transitions is original work.

- **Donations:** donation button on (General → Extra options), like Crafting Queue. Tagged "Nexus Mods Turns 25"
  (the 25th Anniversary Charity Mod Drive: the mod's Donation Points go to Doctors Without Borders, matched by Nexus);
  the mod only earns them once it's opted into the mod rewards program (the "Opt-in" link on the mod page).

Published 2026-09-28.

## Files (Main files)

| File | Name on Nexus | Primary | Description |
|---|---|---|---|
| `InstantTransitions-0.9.0-with-BepInEx.zip` | Instant Transitions (with BepInEx) | yes | Pick this one if you're not sure: it includes BepInEx 5.4.23.5. Extract the zip into your game folder and choose Replace if Windows asks. |
| `InstantTransitions-0.9.0.zip` | Instant Transitions | no | Only the mod, for players who already have BepInEx 5. Extract the zip into your game folder and choose Replace if Windows asks. |

Future versions: upload each zip with "Update existing file" on its counterpart, so Nexus keeps the history.

## Changelog 0.9.0

- First public beta: doors and map travel in about 0.3 s, a map preload while a save loads, and Ctrl + Shift + O to turn it on and off.

## Changelog 0.9.1

- Loads on older versions of the game (a player on 1.004.2 got an error and the whole mod stopped): anything the game doesn't have yet now turns itself off, with a line in the log, instead of stopping the mod.
- The log's first line now says the game version.

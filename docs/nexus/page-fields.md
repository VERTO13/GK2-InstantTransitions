# Nexus page fields

What the Nexus page (https://www.nexusmods.com/graveyardkeeper2/mods/206, author LeBetoven) holds besides
`description.bbcode`, `summary.txt` and `thumbnail/thumbnail.png`.

The description's two GIFs are hosted in the mod's own gallery on Nexus (not on GitHub), so the page works while the
code isn't public. The editor is SCEditor: paste the BBCode in its "View source" mode.

## Mod

- **Name:** Instant Transitions
- **Category:** Utilities
- **Tags:** Performance Optimization, Quality of Life, AI Assisted
- **Version:** 0.10.1
- **Language:** English

## Gallery (in this order)

1. `thumbnail/thumbnail.png` (the mod's thumbnail): "Instant Transitions: walk into doors, no black screen"
2. `docs/images/walk-in.gif` (0.10): "Walking through doors: without mods, then with Instant Transitions 0.10"
3. `docs/images/loading.gif`: "The loading screen: the game loads, then Instant Transitions prepares every place on the map"

The 0.9 thumbnail and `docs/images/doors-vs-vanilla.gif` were deleted from Nexus on 2026-10-01 (they showed the black
that 0.10 removed). The GIF is still here; the old thumbnail is in the 0.9.0 commit.

## Requirements

Mod requirements (legacy), external resource (the same as Crafting Queue since its 0.5.1):

- **Name:** BepInEx 5.4.23.x
- **Link:** https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5
- **Notes:** The mod loader. Download BepInEx_win_x64_5.4.23.5.zip and extract it into your game folder first. Skip it if you already have BepInEx 5.

Until 0.9.0 the notes said it came in the "with BepInEx" file; since 0.9.1 BepInEx isn't bundled (OrionAF asked for it,
and Crafting Queue did the same).

## Permissions and credits

Closed for now (decided 2026-09-28): the code isn't on GitHub yet. Nexus's "Use recommended settings": re-upload
and conversion not allowed; modification and asset use, ask the author; donation points and monetisation for others,
not allowed. When the code goes public, switch to the MIT text Crafting Queue uses and link the repo.

- **Third-party content:** Yes (I have permission). Only BepInEx, inside the "with BepInEx" 0.9.0 file (now in Old
  files, still downloadable, so this stays); its LGPL-2.1 license allows bundling it unmodified.
- **Credits:**

> BepInEx by the BepInEx team (LGPL-2.1): https://github.com/BepInEx/BepInEx - bundled unmodified in the "with BepInEx" file, as its license allows, and it keeps its own license. Everything else in Instant Transitions is original work.

- **Donations:** donation button on (General → Extra options), like Crafting Queue. Tagged "Nexus Mods Turns 25"
  (the 25th Anniversary Charity Mod Drive: the mod's Donation Points go to Doctors Without Borders, matched by Nexus);
  the mod only earns them once it's opted into the mod rewards program (the "Opt-in" link on the mod page).

Published 2026-09-28.

## Files (Main files)

Since 0.9.1, one file:

| File | Name on Nexus | Primary | Description |
|---|---|---|---|
| `InstantTransitions-0.10.1.zip` | Instant Transitions | yes | Only the mod. Requires BepInEx 5 (installed separately, see the description). Extract into your game folder, next to GraveyardKeeper2.exe, and choose Replace if Windows asks. |

Upload each version with "Update existing file" on "Instant Transitions", so Nexus keeps the history. The 0.9.0
"Instant Transitions (with BepInEx)" file goes to **Old files** (edit it and change its category), like Crafting
Queue's 0.4.16 files.

Until 0.9.0 there were two files: "Instant Transitions (with BepInEx)" (primary, BepInEx 5.4.23.5 included) and
"Instant Transitions" (mod only).

## Changelog 0.9.0

- First public beta: doors and map travel in about 0.3 s, a map preload while a save loads, and Ctrl + Shift + O to turn it on and off.

**0.9.1 published 2026-09-30** with Nexus's new editor (`/games/graveyardkeeper2/mods/206/edit/general`, `…/files`,
`…/requirements`): the zip went in with "Update existing file" on "Instant Transitions" (without "Archive existing
file", so 0.9.0 went to Old files), set as primary, with the changelog in the upload form ("One line per entry"). The
"with BepInEx" 0.9.0 file: ⋮ → Edit → File category "Old". Notes: the counters under the text fields show the
characters *left*, and if the Chrome window is hidden the page stops rendering and doesn't take what's typed; bring it
to the front first. The public page shows requirement changes a while later (cache).

## Changelog 0.9.1

- Doors never run the game's full memory clean-up any more: every third door removes one kind of editor-only component (about 30 ms), and a door unloads unused assets every 10 minutes or when memory grows by 300 MB (about 0.3 s). This fixes the long pauses at some doors and the lag that built up when the clean-up was pushed back. If you raised FullCleanupEveryMinutes, you can set it back to 10.
- No fades by default: doors take about 0.1 s. If your settings had the old default (0.15), it moves to 0; set FadeSeconds to bring a fade back.
- Loads on older versions of the game (a player on 1.004.2 got an error and the whole mod stopped): anything the game doesn't have yet now turns itself off, with a line in the log, instead of stopping the mod.
- No more "[PlayerController]: no active game scene found" warnings from this mod while a save loads.
- Scripted doors (like the exit of the tower above the base) are quick too, while the game moving you on its own (quests, story scenes, dialogs) keeps its own timing.
- One download: BepInEx is no longer included. Install it separately (see the description).
- The log's first line says the game version, and each door line says why a door kept the game's timing.

## Changelog 0.10.0

- Doors and map travel inside the same scene (the whole map is one) cut straight to the other side: no black at all, about three frames.
- Walk into a door to go through it, no key needed. Where the floor ends before a door, your character walks on to it (or down the stairs) before the cut. Floor hatches and ladders you climb keep the key.
- The "[E] Enter" prompt over doors you can walk into is hidden; Ctrl + Shift + H shows it again, and Ctrl + Shift + C turns walking in on and off.
- The door you just came through waits until you let go of the movement key, turn around or walk away, so you don't bounce back.
- Near a door, its other side loads ahead of time, so even a first visit doesn't stall the cut.
- Doors with the cut only unload unused assets when memory grew a lot, so that 0.3 s never shows; doors through black and loading a save still do it.
- During fights, doors only work with the key.

**0.10.0 published 2026-10-01**, the same way as 0.9.1 ("Update existing file" without archiving, so 0.9.1 went to the
previous files; changelog in the upload form). Media: "Add image(s)" for the thumbnail and the GIF, then each card's ⋮
→ Edit title and Set as thumbnail. Reordering the gallery: a plain mouse drag drops the card on itself; it only moved
with pointer events sent from the page (pointerdown on the card's grip, several pointermove, pointerup), then Save. The
walk-in GIF is https://staticdelivery.nexusmods.com/mods/10208/images/206/206-1790845933-28489691.gif. On saving, the
editor drops the blank line after each `[/list]` (it did in 0.9.1 too); the page looks the same.

## Changelog 0.10.1

- Fixed: loading a save made the night before a morning scene (like Jack's first visit about his boat) could leave the screen black, and Jack never went to his boat. The map preload now runs before the game starts those scenes.
- If it still happens with 0.10.1, another mod is keeping the loading screen up longer at the very end. The description says exactly when it happens and why.

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

Until 0.9.0 the notes said it came in the "with BepInEx" file; since 0.9.1 BepInEx isn't bundled (a player asked for
it, and Crafting Queue did the same).

## Permissions and credits

Closed on Nexus (decided 2026-09-28, while the code wasn't on GitHub). Nexus's "Use recommended settings": re-upload
and conversion not allowed; modification and asset use, ask the author; donation points and monetisation for others,
not allowed. The code is public since 2026-10-02 (https://github.com/VERTO13/GK2-InstantTransitions, MIT); switching
the Nexus page to the MIT text Crafting Queue uses, and linking the repo there, is the author's call and isn't done yet.

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

- The map preload now runs before the game starts its morning scenes. Before, loading a save made the night before one (like Jack's first visit about his boat) could leave the screen black, and Jack never went to his boat.
- It can still happen if another mod keeps the loading screen up longer at the very end. The description says exactly when it happens and why.

**0.10.1 published 2026-10-01** (zip in `dist/`, built with `System.IO.Compression` so the paths inside use `/`;
`Compress-Archive` writes `\`). The description got the "Black screen after loading, or Jack missing at his boat?"
section. Other mods are not named there: only the condition (a mod that keeps the loading screen up longer at the end).

That time Chrome was on the other computer with its window hidden (`document.visibilityState` "hidden"): clicks by
position and typing did nothing, screenshots timed out. What worked, checking the page text after each step:
- buttons: `button.click()` from the page (Update, Add changelog, Save file, Save);
- the zip: the extension's file upload on the row's file input; text fields: its form fill (version, changelog);
- the description: `sceditor.instance(textarea)`, `sourceMode(true)`, `val(text)`, and then the form's own handler,
  because the editor's events don't mark the form as changed: from the hidden textarea's React fiber, go up to the
  component whose props have `onChange` and `value`, and call `onChange(text)`. Save turns on.
The text was compared by SHA-256 before saving and after reloading (the saved one drops the line break after `[/list]`).

Later that day the wording was softened at the author's request: nothing on the page says "fixed", because another mod
can still cause the same thing ("since 0.10.1 this mod no longer causes it"). The description also got a
**Release notes** section at the end (the same text as `CHANGELOG.md`): add each new version on top. The changelog of
a file that is already uploaded is edited in the Files step: the row's ⋮ → "Edit changelog" (the menu opens with
pointer events, not with a plain click).

## Game 1.008 (2026-10-02): texts only

No new version and no media. The game stopped running its full memory clean-up at doors inside the same scene: it now
only does it when a door leads to another scene, and when a save loads. Its own doors went from 1.4–1.9 s to about a
second. Measured on 1.008 with the test probe (`doors=8` and `mapTravel=4`, with the mod and without it): a door takes
0.92–1.09 s through black without the mod and about 0.1 s with it; map travel takes 0.90–1.37 s without and
0.07–0.33 s with. 0.10.1 works on 1.008 as it is.

The description and both READMEs say so now: the intro, the caption of the comparison GIF (it was recorded before
1.008 and stays), the two first lines of "Measured", "No freeze at doors" and "Tested with 1.008".

A saving trap found that day: **every save removes one line break right after each `[/list]`.** Pasting the repo's
text, which has a blank line there, gives the usual result. Editing the text that is already on the page and saving
it again eats one more each time, until the lists touch the next heading. To change a sentence in place, put one line
break back after every `[/list]` before saving (`text.replace(/\[\/list\]/g, '[/list]\n')`): the saved text is then
the same as before, except for that sentence.

# Instant Transitions for Graveyard Keeper 2

**English** | [Español](README.es.md)

Every door in Graveyard Keeper 2 freezes the game with the screen black for 1 to 2 seconds, and longer the more mods you have. **Instant Transitions** takes doors and map travel down to about **0.1 seconds**, and makes the first visit to a place nearly as quick.

> ⚠️ **Beta (0.9.1).** Doors and loading are measured and tested. Long play sessions are still being tested, so if anything feels off, please [report it](#reporting-bugs) with your log. It really helps.

![A door and a map trip side by side: 1.5 s without mods, 0.35 s with Instant Transitions](docs/images/doors-vs-vanilla.gif)

*Recorded with 0.9.0, when doors still faded for 0.15 s. Since 0.9.1 there's no fade, so doors are quicker still.*

---

## What it does

| | Without mods | With Instant Transitions |
|---|---|---|
| A door (home ↔ yard) | 1.4–1.9 s (up to 2.6 s with many mods) | **~0.1 s** |
| Map travel | ~1.8 s | **~0.1 s** |
| First visit to a place | +0.3–1.3 s while it loads | **almost the same as any other visit** |
| Loading a save | as usual | about 5 s longer, with a progress line |

**The freeze at every door.** With the screen black, the game runs a full memory clean-up on every door: it unloads unused assets, collects garbage and searches everything loaded, 24 times over, for editor-only components. That's most of the wait. This mod never does it all at once: every third door removes one of the 24 kinds of editor-only components (about 30 ms), so nothing piles up, and a door unloads unused assets every 10 minutes or when memory grows by 300 MB (about 0.3 s). The game's garbage collector already works in small steps while you play. While a save loads, the full clean-up runs as usual.

**No fades.** On doors you use and on map travel, the two 0.3 s fades and the extra 0.3 s pause in black are gone: the new place just appears. Fights and story scenes keep the game's own timing. If you'd like a soft fade back, set `FadeSeconds`.

**Fast first visits.** While a save loads, the game preloads a fixed list of the pieces places are drawn with. Anything not on that list (for example, what you built in your yard) loads from disk the first time you see it, in one frozen frame. Instant Transitions preloads the rest of the map at the end of the loading screen, several at a time, so first visits are nearly as quick as the next ones. A line under the loading bar shows the progress. This uses about 500 MB more memory and is skipped on PCs with less than 7 GB of RAM.

![The loading screen: the game loads, then Instant Transitions prepares every place on the map](docs/images/loading.gif)

**Compare it yourself.** Press **Ctrl + Shift + O** while playing to turn the mod on or off. The change applies from the next door, and a notice shows the new state.

---

## Installation (about 2 minutes)

You only do this once. Close the game before you start.

### Step 1: Download two files

1. **BepInEx 5**, the mod loader (skip it if you already play with other BepInEx mods): from its [official page](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5), download **`BepInEx_win_x64_5.4.23.5.zip`**.
2. **Instant Transitions**: from [Releases](../../releases) or the [Nexus Mods page](https://www.nexusmods.com/graveyardkeeper2/mods/206), download **`InstantTransitions-x.y.z.zip`**.

### Step 2: Copy the address of your game folder

1. Open **Steam** and go to your **Library**.
2. Right-click **Graveyard Keeper 2** → **Manage** → **Browse local files**. A folder opens; this is your *game folder*.
3. Click the **address bar** at the top of that folder, then press **Ctrl + C** to copy it.

### Step 3: Extract both files into that folder

Do this first with BepInEx, then with Instant Transitions:

1. Find the file you downloaded (usually in **Downloads**).
2. Right-click it → **Extract All…**
3. Delete the path in the box, then press **Ctrl + V** to paste your game folder's address.
4. Click **Extract**. If Windows asks about replacing files, choose **Replace**.

### Step 4: Check that it worked

Start the game and load your save. Near the end of the loading screen you'll see *"Preparing places so doors are quick…"* under the bar. Then walk through any door. 🎉

<details>
<summary><b>Update or uninstall</b></summary>

- **Update:** extract the new version the same way, choosing **Replace**.
- **Uninstall:** delete the folder `BepInEx\plugins\InstantTransitions` inside your game folder. Your saves are never touched.

</details>

---

## Settings

Settings live in `BepInEx\config\verto13.gk2.instanttransitions.cfg` (created the first time you play; open it with Notepad while the game is closed). Every setting is explained inside:

| Setting | Default | What it does |
|---|---|---|
| `Enabled` | `true` | `false` = everything as in the unmodded game; the mod only measures doors. |
| `ToggleKey` | `Ctrl + Shift + O` | Turns the mod on and off while playing. |
| `FullCleanupEveryMinutes` | `10` | How often a door unloads unused assets (about 0.3 s). Doors never run the game's full clean-up. |
| `FullCleanupWhenMemoryGrowsMB` | `300` | Or sooner, once memory has grown this much. |
| `FadeSeconds` | `0` | Length of each fade on doors and map travel. `0` = no fade (the game: 0.3). |
| `BlackPauseSeconds` | `0` | Pause in black before you're moved (the game: 0.3). |
| `PreloadPlaces` | `true` | Preload the whole map while a save loads. |
| `PreloadMaxSeconds` | `20` | The preload never makes loading longer than this. |

## What it changes / compatibility

- It doesn't touch your saves or change any balance. Turn it off (or uninstall it) at any time.
- Use only **one** mod that changes doors or the game's memory clean-up; two of them would fight over the same thing.
- Tested with Graveyard Keeper 2 1.007.1 and BepInEx 5.4.23.5, alongside a dozen other mods. It also loads on older versions of the game (1.004.2 and up): anything the game doesn't have yet turns itself off, with a line in the log.

## Reporting bugs

Open an [issue](../../issues/new) and include:

1. What you did and what happened.
2. The file `BepInEx\LogOutput.log` from your game folder. Every door, loading screen and clean-up is written there (`[Door]`, `[Load]`, `[Preload]` and `[Clean-up]` lines), which makes problems easy to find.

## License

[MIT](LICENSE). Made by VERTO13, with AI assistance.

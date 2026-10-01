# Release notes

The same notes are at the end of the mod's page on Nexus (`docs/nexus/description.bbcode`).

## 0.10.1

- The map preload now runs before the game starts its morning scenes. Before, loading a save made the night before one (like Jack's first visit) could leave the screen black, and Jack never went to his boat. Another mod that makes the end of loading longer can still cause it.

## 0.10.0

- Doors and map travel cut straight to the other side: no black at all.
- Walk into a door to go through it, no key needed. Hatches and ladders you climb keep the key.
- The "[E] Enter" prompt over those doors is hidden.
- New keys: Ctrl + Shift + C for walking in, Ctrl + Shift + H for the prompts.
- Near a door, its other side loads ahead of time.

## 0.9.1

- Doors never run the game's full memory clean-up: no more long pauses at some doors.
- No fades by default: doors take about 0.1 s.
- Loads on older versions of the game.
- BepInEx is no longer included in the download.

## 0.9.0

- First public beta: quick doors and map travel, a map preload while a save loads, and Ctrl + Shift + O to turn it on and off.

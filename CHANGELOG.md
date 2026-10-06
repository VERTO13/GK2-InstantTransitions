# Release notes

The same notes are at the end of the mod's page on Nexus (`docs/nexus/description.bbcode`).

## 0.10.2

- The gate of the zombie resurrection room (and any other gate or door that opens with an animation) could end up closed, blocking the way, while your save said it was open, so the game didn't even offer to open it. The map preload made the game hide it again right after opening it, and it showed up closed later. Since 0.10.2 the mod shows it again and sends its saved "open" order again. If it is closed in a game you already started, update and load your save again.
- Unloading unused assets no longer freezes the picture at a door. In a long game it takes 1 to 3 seconds, and it used to happen at a cut door about once an hour. Now it only happens under a black screen: when you go to sleep, at a door that goes through black, or while a save loads. Doors and map travel stay instant: only if you go a long time without any of those, memory has grown 1200 MB and your PC is really running short of it (the game holds more than 60 % of your RAM, or less than 1.5 GB is free), one door goes through black once and does it there. New settings `FullCleanupAtAnyDoorWhenMemoryGrowsMB` (1200) and `FullCleanupAtAnyDoorWhenGameUsesPercentOfRam` (60).

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

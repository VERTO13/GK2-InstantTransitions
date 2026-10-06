# Discord post

For the official Lazy Bear Games Discord, forum `#gk2-modding`
(https://discord.com/channels/367278385686380545/1552972336486162452), where Crafting Queue has its thread.

Posts there run 600 to 1,300 characters: a short pitch, 3 to 6 bullets, a line about saves and compatibility, the
Nexus link and an image or GIF. The forum asks for no tags. Its pinned rules: no malicious code, no player data sent
anywhere, no game files, nothing that breaks other mods on purpose; say if saves are touched and note known conflicts.

**Status:** posted by the author on 2026-10-01, with the GIF attached:
https://discord.com/channels/367278385686380545/1555274708033732608. Updates go as replies in that thread. The text
below is what is posted (the author took "(Claude)" out of the AI line).

## Title

Instant Transitions: walk through doors with no black screen

## Message

```
Every door in the game freezes on a black screen for 1 to 2 seconds. With Instant Transitions the whole map feels like one place: walk into a door and you're on the other side in the same instant, still walking.

• No black screen: doors and map travel cut straight to the other side
• No need to press E, and the "[E] Enter" prompt is hidden
• You never bounce back through the door you just used
• First visits are just as quick: the map is prepared while your save loads (about 5 s longer)

Keys (you can change them in the settings):
• Ctrl + Shift + O: turns the whole mod on and off, to compare
• Ctrl + Shift + C: turns walking into doors on and off
• Ctrl + Shift + H: shows or hides the "[E] Enter" prompts

Good to know:
• Requires BepInEx 5.4.23.x
• No balance or gameplay changes, and your save is never touched
• Use only one mod that changes doors or the game's memory clean-up
• No network access, no data collection, no game files or assets included
• Still in beta. Made with the help of AI

I would love to know how it feels on your PC!
Download: https://www.nexusmods.com/graveyardkeeper2/mods/206
```

## Attachment

`docs/images/walk-in.gif` (6.5 MB; Discord takes up to 10 MB without Nitro).

## Update 0.10.2 (reply in the same thread)

Posted by the extension on 2026-10-05 as a new message in the thread. What worked in Discord web with the window hidden: the composer is the `[role=textbox]` whose aria-label starts with "Enviar un mensaje en" (the first Slate editor on the page is the server search box, not it); a synthetic `paste` event with the text in a `DataTransfer` fills it keeping the line breaks, and a dispatched Enter (keydown, keypress, keyup) sends it.

```
Instant Transitions 0.10.2 is out:

• The gate of the zombie resurrection room (or any gate that opens with an animation) could end up closed and block the way while your save said it was open. Since 0.10.2 the mod puts it back. If yours is closed, update and load the save again.
• Unloading unused memory no longer freezes the picture at a door (1 to 3 s in a long game). It now happens while you sleep, at a door through black, or while a save loads.

Download: https://www.nexusmods.com/graveyardkeeper2/mods/206
```

# Live Farm Map
Adds a full-screen map of your farm as it really is right now, with your buildings, crops, animals and everything you've placed.

Not released yet. Once it is, unzip it into your game's `Mods` folder. It needs [SMAPI](https://smapi.io).

- **Your real farm:** the vanilla world map shows the same picture of your farm type all game. This map draws the farm as it actually is, shrunk to fit the screen, without night or weather so it's always easy to read.
- **Live:** while it's open, the map is redrawn every second (you can change how often), so you can watch other players and animals move around in multiplayer.
- **People icons:** like [NPC Map Locations](https://www.nexusmods.com/stardewvalley/mods/239) on the world map, every player on the farm gets a small portrait and every villager a head icon, so you can spot them at a glance. They move smoothly between redraws, and pointing at one shows their name. Animals don't get icons.
- **Easy to open:** click the farm on the world map (or select it with a controller), or press <kbd>Left Shift</kbd> + <kbd>M</kbd> from anywhere. Close it with <kbd>Esc</kbd>, the map key, right-click or the close button. If you opened it from the world map you go back there.

## Settings
Settings are in `config.json` (created on first launch), and can be edited in-game if [Generic Mod Config Menu](https://www.nexusmods.com/stardewvalley/mods/5098) is installed.

| Setting | Default | Effect |
| --- | --- | --- |
| `ModEnabled` | `true` | Whether the farm map can be opened. |
| `OpenKey` | `LeftShift + M` | The keys which open the farm map, from the world or the world map. |
| `RefreshSeconds` | `1` | How many seconds between redraws while the map is open, from 1 to 10. |
| `ShowPeople` | `true` | Whether to show icons for the players and villagers on the farm. |

---
Part of [stardew-valley-mods](../README.md), which has how to build and contribute.

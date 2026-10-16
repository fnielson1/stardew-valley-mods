# Live Farm Map
Adds a full-screen map of your farm as it really is right now, with your buildings, crops, animals and everything you've placed.

Not released yet. Once it is, unzip it into your game's `Mods` folder. It needs [SMAPI](https://smapi.io).

- **Your real farm:** the vanilla world map shows the same picture of your farm type all game. This map draws the farm as it actually is, shrunk to fit the screen, without night or weather so it's always easy to read.
- **Live:** while it's open, the map is redrawn every 5 seconds (you can change how often), so you can watch other players and animals move around in multiplayer.
- **People icons:** like [NPC Map Locations](https://www.nexusmods.com/stardewvalley/mods/239) on the world map, every player on the farm gets a small portrait and every villager a head icon, so you can spot them at a glance. They move smoothly between redraws, and pointing at one shows their name. Animals don't get icons.
- **Easy to open:** click the farm on the world map (or select it with a controller), click the **Farm map** button in the world map's top-left corner, or press <kbd>Left Shift</kbd> + <kbd>M</kbd> from anywhere. The farm map has a **World map** button in the same corner to switch back. Close it with <kbd>Esc</kbd>, the map key, right-click or the close button. If you opened it from the world map you go back there.

## Settings
Settings are in `config.json` (created on first launch), and can be edited in-game if [Generic Mod Config Menu](https://www.nexusmods.com/stardewvalley/mods/5098) is installed.

| Setting | Default | Effect |
| --- | --- | --- |
| `ModEnabled` | `true` | Whether the farm map can be opened. |
| `OpenKey` | `LeftShift + M` | The keys which open the farm map, from the world or the world map. |
| `RefreshSeconds` | `5` | How many seconds between redraws while the map is open, from 1 to 10. |
| `ShowPeople` | `true` | Whether to show icons for the players and villagers on the farm. |

## For mod authors
Other mods can read the farm's buildings and mini-obelisks through Live Farm Map's API. Copy [`ILiveFarmMapApi.cs`](ILiveFarmMapApi.cs) and [`IFarmBuilding.cs`](IFarmBuilding.cs) into your mod (any namespace), then:

```c#
ILiveFarmMapApi? api = this.Helper.ModRegistry.GetApi<ILiveFarmMapApi>("fnielson.LiveFarmMap");
foreach (IFarmBuilding building in api?.GetBuildings() ?? [])
    this.Monitor.Log($"{building.DisplayName} at ({building.TileX}, {building.TileY})");
```

`GetBuildings()` returns what's on the farm right now, whether or not the map is open: every building (including the farmhouse, greenhouse and shipping bin), then every mini-obelisk. Each has its `Kind` (`"building"` or `"mini-obelisk"`), `Id` (the building type, or `(BC)238` for a mini-obelisk), translated `DisplayName`, the tile position and size of its footprint, and whether it's `IsUnderConstruction`. It returns an empty list until a save is loaded.

To add your own things to the farm map:

- `IsMapOpen()` says whether the farm map is open right now.
- `OpenMap()` opens it (closing it goes back to the world). It returns false if no save is loaded or the player turned the map off.
- `TryGetTileScreenArea(tileX, tileY, out Rectangle area)` gives where a farm tile is drawn on screen while the map is open, in UI pixels like `Game1.getMouseX()`. Use it to draw over the map in SMAPI's `RenderedActiveMenu` event, and to check clicks in `ButtonPressed` (suppress the button if you handle it).

---
Part of [stardew-valley-mods](../README.md), which has how to build and contribute.

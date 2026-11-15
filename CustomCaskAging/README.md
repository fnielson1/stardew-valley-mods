# Custom Cask Aging
**[Download the latest version](https://www.nexusmods.com/stardewvalley/mods/55589)**, then unzip it into your game's `Mods` folder.

Makes everything a cask takes age faster or slower by the same multiplier: wine, cheese, goat cheese, mead, pale ale and beer. Set it to 2x and things age twice as fast, so gold wine becomes iridium in 14 days instead of 28.

It needs [SMAPI](https://smapi.io) and Stardew Valley 1.6. It works with or without [Custom Cask Mod](https://www.nexusmods.com/stardewvalley/mods/2642): this mod only changes the base game's cask rules, so anything Custom Cask Mod adds, or that you set in its `CaskData.json`, keeps its own rate.

## Settings
Edit `config.json` in the mod's folder, or use [Generic Mod Config Menu](https://www.nexusmods.com/stardewvalley/mods/5098) in game.

| Setting | Default | What it does |
| --- | --- | --- |
| Aging speed (`AgingMultiplier`) | 1 | How many times faster than the base game everything ages, from 0.1 to 100. 1 is the base game's speed, 2 takes half as long, 2.25 takes a little under half the time, and 0.5 takes twice as long. |

## Things to know
- **It multiplies each item's own speed.** The base game doesn't age everything equally: cheese ages 4 times as fast as wine, for example. At 2x each one is twice as fast as before, so cheese is still 4 times as fast as wine. At the default of 1x nothing changes.
- **It speeds up what's already in your casks.** A cask remembers its rate when you put something in, so the mod brings every cask which already has something in it up to date when a save loads, before each night's aging, and when you save the setting in Generic Mod Config Menu. If you edit `config.json` by hand, restart the game or wait for the next night.
- **Custom Cask Mod's own rates aren't multiplied.** If its `CaskData.json` lists an item or category, that rate is used for it as written.

---
Part of [stardew-valley-mods](../README.md), which has how to build and contribute.

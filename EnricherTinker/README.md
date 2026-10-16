# Enricher Tinker
Lets a sprinkler's Enricher hold several fertilizers, and optionally never use them up.

**[Download the latest version](https://github.com/fnielson1/stardew-valley-mods/releases?q=enricher-tinker)** (the `EnricherTinker-<version>.zip` file), then unzip it into your game's `Mods` folder. It needs [SMAPI](https://smapi.io).

- **More slots:** an Enricher holds 9 stacks of fertilizer in a 3×3 grid instead of one. Enrichers you already placed get the extra slots too.
- **Several fertilizers per seed:** with [Ultimate Fertilizer](https://www.nexusmods.com/stardewvalley/mods/21318) installed and its Fertilizer Mode set to `multi-fertilizer-stack`, every fertilizer in the Enricher which can go on the tile is applied when you plant a seed in range, in slot order. So one Enricher can apply Speed-Gro, Quality Fertilizer and Retaining Soil all at once. Otherwise a tile only takes one fertilizer, so only the first one which can go on it is applied, and the other stacks are used once it runs out.
- **Infinite fertilizer:** optionally, fertilizer in an Enricher is never used up.

## Settings
Settings are in `config.json` (created on first launch), and can be edited in-game if [Generic Mod Config Menu](https://www.nexusmods.com/stardewvalley/mods/5098) is installed. In multiplayer, the player planting the seed uses their own settings.

| Setting | Default | Effect |
| --- | --- | --- |
| `ModEnabled` | `true` | Whether Enrichers have 9 slots and use the settings below. If it's off they work as in the base game, but an Enricher already holding several stacks still shows them all. |
| `ApplyAllFertilizers` | `true` | Apply every fertilizer in the Enricher which can go on the tile. If it's off, only the first one which can is applied, so the other stacks are only used once it runs out or can't go on the tile. This only has an effect with Ultimate Fertilizer set to `multi-fertilizer-stack`; otherwise it's greyed out in Generic Mod Config Menu, with a tooltip saying what it needs. |
| `InfiniteFertilizer` | `false` | Fertilizer in an Enricher is never used up. |

---
Part of [stardew-valley-mods](../README.md), which has how to build and contribute.

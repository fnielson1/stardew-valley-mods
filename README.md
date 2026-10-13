# stardew-valley-mods
A collection of mods for Stardew Valley

## Mods

### Mod Sync Check
Blocks a player from joining a multiplayer game until their mods and mod versions match the host's.

- **Host has the mod:** the host checks the joining player's mod list before the game approves them, using the host's settings. A mismatched player never enters the world; the host gets a HUD notice, and the player sees the list of mismatches on their farmhand selection screen even if they don't have the mod.
- **Joining player has the mod:** they're disconnected and shown the list in a dialog on the title screen. If the host doesn't have the mod, the joining player checks the host's mod list themselves and leaves if it doesn't match.
- **Both have the mod:** the joining player is offered a download of the mods they're missing or have at a different version, sent directly by the host. Mods are only added or updated, never removed; a replaced version is moved to `Mods/.ModSyncCheck/backup`, and the player's `config.json` is never changed. The game must be restarted afterwards.
- The full list is always written to the SMAPI log on whichever side has the mod.

Settings are in `config.json` (created on first launch), and can be edited in-game if [Generic Mod Config Menu](https://www.nexusmods.com/stardewvalley/mods/5098) is installed. Only the host's settings are used when the host has the mod.

| Setting | Default | Effect |
| --- | --- | --- |
| `Enabled` | `true` | When hosting, whether to check joining players at all. Turn it off to let everyone join as if the mod wasn't installed; it can be changed while hosting and applies to players who join afterwards. |
| `RequireThisMod` | `true` | When hosting, block players who don't have Mod Sync Check installed, even if their other mods match. They're shown where to download it. |
| `IgnoredModIds` | common client-only mods | Mod IDs which don't need to match. In Generic Mod Config Menu this is the first checkbox next to each installed mod: checked means required, unchecked means ignored. |
| `MaxModsInPopup` | `10` | How many mismatched mods to list in-game. |
| `AllowModDownloads` | `true` | When hosting, let blocked players download the mods they need from you. |
| `UnsyncedConfigModIds` | none | When hosting, the required mods for which players keep their own settings. For every other required mod, players who have this mod use your `config.json` settings while they're in your game. In Generic Mod Config Menu this is the "My settings (when host)" checkbox next to each mod: checked means players use your settings. Their own settings files aren't changed, and they get their own settings back when they leave. Content packs and a few mods can't be changed while the game is running, so those keep the player's settings. |
| `SyncedConfigModIds` | none | When hosting, the ignored mods for which players use your settings anyway, if they have the mod. This is the same checkbox, for a mod which isn't required. |
| `NeverSyncedConfigModIds` | Custom Cask Mod, TimeSpeed, Content Patcher, Console Commands | Mods whose settings are never sent to players, whatever the other settings say. Their "My settings (when host)" checkbox is greyed out; this list can only be changed by editing `config.json`. |

Mods which only the joining player has are always allowed.

The host's settings stay in force for the whole session. If a player changes one of those settings mid-game it's changed back within a few seconds, and if the mod saved its settings in the meantime the player's `config.json` is put back as it was. If the host changes its own settings mid-game, the players in the game switch to the new ones.

## Building
Requires the .NET SDK and a Stardew Valley install with SMAPI.

```
dotnet build    # builds and copies each mod into the game's Mods folder
dotnet test
```

# stardew-valley-mods
A collection of mods for Stardew Valley

## Mods

### Mod Sync Check
Blocks a player from joining a multiplayer game until their mods and mod versions match the host's.

- **Host has the mod:** the host checks the joining player's mod list before the game approves them, using the host's settings. A mismatched player never enters the world; the host gets a HUD notice, and the player sees the list of mismatches on their farmhand selection screen even if they don't have the mod.
- **Joining player has the mod:** they're disconnected and shown the list in a dialog on the title screen. If the host doesn't have the mod, the joining player checks the host's mod list themselves and leaves if it doesn't match.
- **Both have the mod:** the joining player is offered a download of the mods they're missing or have at a different version, sent directly by the host. Mods are only added or updated, never removed; a replaced version is moved to `Mods/.ModSyncCheck/backup` and its `config.json` is kept. The game must be restarted afterwards.
- The full list is always written to the SMAPI log on whichever side has the mod.

Settings are in `config.json` (created on first launch):

| Setting | Default | Effect |
| --- | --- | --- |
| `IgnoredModIds` | common client-only mods | Mod IDs which don't need to match. |
| `MaxModsInPopup` | `10` | How many mismatched mods to list in-game. |
| `AllowModDownloads` | `true` | When hosting, let blocked players download the mods they need from you. |

## Building
Requires the .NET SDK and a Stardew Valley install with SMAPI.

```
dotnet build    # builds and copies each mod into the game's Mods folder
dotnet test
```

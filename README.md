# stardew-valley-mods
A collection of mods for Stardew Valley. Each mod needs [SMAPI](https://smapi.io).

## Mods

| Mod | What it does | Download |
| --- | --- | --- |
| [Mod Sync Check](ModSyncCheck/README.md) | Blocks a player from joining a multiplayer game until their mods and mod versions match the host's, and can send them the host's mods. | [Nexus Mods](https://www.nexusmods.com/stardewvalley/mods/53850) |
| [Enricher Tinker](EnricherTinker/README.md) | Gives a sprinkler's Enricher a 3×3 grid of fertilizer slots, applies several fertilizers per seed with Ultimate Fertilizer, and can make the fertilizer infinite. | [GitHub releases](https://github.com/fnielson1/stardew-valley-mods/releases?q=enricher-tinker) |

Each mod's README has what it does in detail and its settings.

## Contributing

### Building and testing
You need the [.NET SDK](https://dotnet.microsoft.com/download) (10 or later, for the tests) and Stardew Valley with SMAPI installed.

```
dotnet build    # builds every mod and copies it into the game's Mods folder
dotnet test     # runs every mod's tests
```

The build finds the game through [Pathoschild.Stardew.ModBuildConfig](https://www.nuget.org/packages/Pathoschild.Stardew.ModBuildConfig); if your game is in an unusual place, see its docs for setting `GamePath`. A running game locks the mod's DLL, so close it before building.

### Layout
Each mod is a top-level folder with the same name as its project, next to its tests:

```
ModSyncCheck/                the mod: ModSyncCheck.csproj, manifest.json, i18n/, README.md
ModSyncCheck.Tests/          its tests: ModSyncCheck.Tests.csproj
EnricherTinker/
EnricherTinker.Tests/
openspec/                    plans and specs for larger changes (see below)
```

The test projects don't reference the game or SMAPI. They compile the mod's game-free source files directly (listed in the `.Tests.csproj`), so put logic you want to test in classes which don't use game types.

### Adding a mod
1. Create a `<ModName>/` folder with `<ModName>.csproj` (copy an existing one), `manifest.json`, an `i18n/default.json` for player-facing text, and a `README.md`.
2. Optionally add a `<ModName>.Tests/` project.
3. Add both to `StardewValleyMods.sln` (`dotnet sln add ...`) and a row to the table above.

The release workflow finds a mod by that folder and project name, so there's nothing to add to the workflows.

### Making a change
1. Make a branch from `main`, and open a pull request into `main`. Only the repo admin can merge.
2. Run `dotnet build` and `dotnet test`, and try the change in the game.
3. Keep each mod's `README.md` up to date with what changed for players, including any new settings.
4. For a larger change, you can plan it first with [OpenSpec](https://github.com/Fission-AI/OpenSpec) in `openspec/changes/`; finished changes are archived in `openspec/changes/archive/`.

### Releasing
To release a mod, bump `Version` in its `manifest.json` in the pull request. When it's merged:

- [`release.yml`](.github/workflows/release.yml) builds and tests every mod. Each mod whose version has no release yet gets a GitHub release, tagged with its folder name in kebab case plus the version (e.g. `mod-sync-check-v1.7.0`), with `<ModName>-<version>.zip` attached.
- [`nexus-sync.yml`](.github/workflows/nexus-sync.yml) then uploads that zip to the mod's file on Nexus Mods. It needs the `NEXUSMODS_API_KEY` secret, and a repo variable `NEXUS_<MOD>_FILE_ID` with the file's ID (e.g. `NEXUS_MOD_SYNC_CHECK_FILE_ID`). Until that variable is set it skips the upload, so a new mod's first release can be used to create its Nexus Mods page by hand.

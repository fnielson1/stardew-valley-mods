# stardew-valley-mods
A collection of mods for Stardew Valley. Each mod needs [SMAPI](https://smapi.io).

## Mods

| Mod | What it does | Download |
| --- | --- | --- |
| [Mod Sync Check](ModSyncCheck/README.md) | Blocks a player from joining a multiplayer game until their mods and mod versions match the host's, and the host's mods can be sent. | [Nexus Mods](https://www.nexusmods.com/stardewvalley/mods/53850) |
| [Enricher Tinker](EnricherTinker/README.md) | Gives a sprinkler's Enricher a 3×3 grid of fertilizer slots, applies several fertilizers per seed with Ultimate Fertilizer, and can make the fertilizer infinite. | [Nexus Mods](https://www.nexusmods.com/stardewvalley/mods/53985) |
| [Live Farm Map](LiveFarmMap/README.md) | Adds a full-screen map of your farm as it really is right now, opened by clicking the farm on the world map. | [Nexus Mods](https://www.nexusmods.com/stardewvalley/mods/53998) |
| [Multiple Mini-Obelisks Mapped](MultipleMiniObelisksMapped/README.md) | Lets you place as many mini-obelisks on your farm as you like, and teleport to any of them from Live Farm Map's farm map. | [Nexus Mods](https://www.nexusmods.com/stardewvalley/mods/54000) |
| [Smaller Junimo Hut](SmallerJunimoHut/README.md) | Makes the Junimo Hut 2×2 tiles instead of 3×2, with its sprite shrunk to fit. | Not on Nexus Mods yet |

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
LiveFarmMap/
LiveFarmMap.Tests/
MultipleMiniObelisksMapped/
MultipleMiniObelisksMapped.Tests/
SmallerJunimoHut/
SmallerJunimoHut.Tests/
Shared/                      source files compiled into more than one mod
openspec/                    plans and specs for larger changes (see below)
```

The test projects don't reference the game or SMAPI. They compile the mod's game-free source files directly (listed in the `.Tests.csproj`), so put logic you want to test in classes which don't use game types.

The settings every mod or test project has in common, like the target framework, are in [`Directory.Build.props`](Directory.Build.props), and every package version is in [`Directory.Packages.props`](Directory.Packages.props).

Code used by more than one mod goes in `Shared/`, and each mod which uses a file lists it in its `.csproj`, so it's compiled into that mod. There's no shared DLL, since mods loaded by SMAPI could then clash over its version. Live Farm Map's API files stay in `LiveFarmMap/`, since other mods' authors copy them from there.

### Making a change
1. Make a branch from `main`, and open a pull request into `main`. Only the repo admin can merge.
2. Run `dotnet build` and `dotnet test`, and try the change in the game.
3. Keep each mod's `README.md` up to date with what changed for players, including any new settings.
4. For a larger change, you can plan it first with [OpenSpec](https://github.com/Fission-AI/OpenSpec) in `openspec/changes/`; finished changes are archived in `openspec/changes/archive/`.

### Releasing
To release a mod, bump `Version` in its `manifest.json` in the pull request. When it's merged:

- [`release.yml`](.github/workflows/release.yml) builds and tests every mod. Each mod whose version has no release yet gets a GitHub release, tagged with its folder name in kebab case plus the version (e.g. `mod-sync-check-v1.7.0`), with `<ModName>-<version>.zip` attached.
- [`nexus-sync.yml`](.github/workflows/nexus-sync.yml) then uploads that zip to the mod's file on Nexus Mods. It needs the `NEXUSMODS_API_KEY` secret, and a repo variable `NEXUS_<MOD>_FILE_ID` with the file's ID (e.g. `NEXUS_MOD_SYNC_CHECK_FILE_ID`).

A new mod's first release isn't uploaded, since it has no Nexus Mods page yet. Once it's released:

1. Create its page on Nexus Mods with the zip from its GitHub release.
2. Set the repo variable `NEXUS_<MOD>_FILE_ID` to the file's ID, shown under "Advanced" on the page's Files tab.
3. In a pull request, add `"UpdateKeys": [ "Nexus:<mod id>" ]` to its `manifest.json`, link its README's download to the page, and bump its version. That release is uploaded, and it's the first one SMAPI can check for updates.

# Proposal

## Why

In 1.6.0 a single trust checkbox switched the download between "only the mods with original Nexus Mods files" and "everything", which didn't make clear which mods a player was getting, and a host's changed copy of a mod was refused even when the player trusted the host. This change records the 1.7.0 behavior (already implemented in PR #20) so the `mod-downloads` spec matches what ships.

## What Changes

- The download prompt has two checkboxes: one for the verifiable mods (ticked to start with), and one for the unverifiable mods, which says the player trusts the host. Any combination can be downloaded, and OK is disabled with neither ticked. The trust warning shows only when the second is ticked.
- The list of missing mods is split into "Verifiable" and "Unverifiable" sections.
- When a file the host sends as an original archive doesn't match the mod's files on Nexus Mods, the host changed it:
  - without trust, it's deleted without being unpacked, and the player sees a warning naming those mods with how to get the host's versions
  - the player remembers that mod version for that host, so on the next join with the same host it's listed as unverifiable
  - with trust, it's installed with a warning (**BREAKING** for the 1.6.0 spec, which said trusted downloads behaved as before and refused such files)

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `mod-downloads`: the trust-free download requirement changes from a single checkbox to two, the "Trusted downloads are unchanged" requirement is replaced since trusted downloads now install changed files, and new requirements cover the list sections and changed-file handling.

## Impact

- Client download prompt and result dialog (`ClientSync`, `SyncDialog`), download selection (`DownloadPlan`), the archive check decision (`ArchiveCheckPolicy`), list text (`ComparisonText`) and translations.
- New per-player data: a list of host ID, mod ID and version for files a host changed, stored in SMAPI's global mod data.
- No change to what the host sends; hosts and players on 1.6.0 still work with 1.7.0.

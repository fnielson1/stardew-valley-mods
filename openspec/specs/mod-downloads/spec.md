# mod-downloads Specification

## Purpose

Lets a player who is blocked from joining get the mods they need from the host, and keeps them safe by checking original Nexus Mods archives before anything is unpacked or installed.

## Requirements

### Requirement: Host lists the mods it will send as original archives
When the host blocks a player who has Mod Sync Check and lets them download mods, it SHALL tell them which of the mods they need it has an original archive for, with each archive's size in bytes.

#### Scenario: Some needed mods have archives
- **WHEN** a player is blocked for missing mods A and B, and the host's archives folder has an archive whose manifest matches A's ID and version but none for B
- **THEN** the blocked message lists A with its archive size and doesn't list B

#### Scenario: Downloads not allowed
- **WHEN** the host's settings don't let players download mods
- **THEN** the blocked message lists no archives

### Requirement: Player can get only the mods available as original archives without trusting the host
When the host lists an archive for at least one needed mod with a Nexus Mods ID, the download dialog SHALL let the player confirm without ticking the trust checkbox, and doing so SHALL download only those mods.

#### Scenario: Archived mod without a Nexus Mods ID
- **WHEN** the host lists an archive for a mod whose manifest has no usable Nexus Mods update key
- **THEN** that mod isn't included without the checkbox, since it can't be confirmed as a file from its own page, and is named among the mods left out

#### Scenario: Confirm without ticking the checkbox
- **WHEN** the host lists archives for 22 of the 26 mods the player needs and the player confirms without ticking the checkbox
- **THEN** only those 22 mods are requested from the host, and the prompt before confirming gives that count and the archives' total size

#### Scenario: Confirm with the checkbox ticked
- **WHEN** the player ticks the trust checkbox and confirms
- **THEN** all 26 needed mods are requested, as without this change, and the prompt gives that count and total size

#### Scenario: No archives listed
- **WHEN** the host lists no archives, or sends no archive list at all
- **THEN** the dialog behaves as without this change: the player must tick the checkbox to confirm

### Requirement: Archives on the trust-free path are checked before they're used
On the trust-free path, a downloaded mod SHALL NOT be unpacked, installed or read for anything other than its MD5 hash until Nexus Mods confirms the hash belongs to a file on that mod's own page.

#### Scenario: Archive confirmed
- **WHEN** Nexus Mods reports the archive's hash as a file on the mod's own page
- **THEN** only the mod's own folder is unpacked from the archive and installed when the game closes, like other downloaded mods

#### Scenario: Mod has no Nexus Mods ID
- **WHEN** a mod's manifest has no usable Nexus Mods update key (like `Nexus:???` or `-1`), so its own page isn't known, and it reaches the trust-free path anyway
- **THEN** it's treated as unverified: deleted without being unpacked and listed as not installed

#### Scenario: Archive not confirmed
- **WHEN** Nexus Mods doesn't know the hash, or reports it only for other mods' pages
- **THEN** the archive is deleted without being unpacked and the mod is listed as not installed

#### Scenario: Nexus Mods unreachable
- **WHEN** Nexus Mods can't be asked, e.g. because the player is offline
- **THEN** the archive is deleted without being unpacked and the mod is listed as not installed, rather than installed without a check

#### Scenario: Host sends a packaged folder instead
- **WHEN** the host sends a package of its mod folder instead of the original archive for a mod requested on the trust-free path
- **THEN** the package is deleted without being unpacked and the mod is listed as not installed

### Requirement: Player sees which mods they still need after a trust-free download
After a trust-free download, the result SHALL list the needed mods which weren't installed, so the player can get them elsewhere.

#### Scenario: Some mods left out
- **WHEN** the player downloaded the 22 archived mods and 1 of them failed the check
- **THEN** the result says 21 will be installed, names the failed one and why, and names the 4 mods the host had no archive for, each linking to its Nexus Mods page where it has one

### Requirement: Trusted downloads are unchanged
When the player ticks the trust checkbox, downloading and installing SHALL behave as without this change, including installing an original archive without a check when Nexus Mods can't be reached.

#### Scenario: Nexus Mods unreachable on the trusted path
- **WHEN** the player ticked the checkbox and Nexus Mods can't be reached while checking an archive
- **THEN** the mod is installed and the player is told it wasn't checked

### Requirement: Mixed versions keep working
A host or player without this change SHALL still be able to block, offer and download mods with one that has it.

#### Scenario: Older host
- **WHEN** a player with this change is blocked by a host that sends no archive list
- **THEN** the player sees the dialog without this change, requiring the checkbox

#### Scenario: Older player
- **WHEN** a host with this change blocks a player without it
- **THEN** the player ignores the archive list and sees their usual dialog

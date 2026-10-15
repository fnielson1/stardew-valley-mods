# Spec Delta

## RENAMED Requirements

- FROM: `### Requirement: Trusted downloads are unchanged`
- TO: `### Requirement: Trusted downloads install the host's files`

## MODIFIED Requirements

### Requirement: Player can get only the mods available as original archives without trusting the host
When the host lists an archive for at least one needed mod with a Nexus Mods ID, the download dialog SHALL show one checkbox for those verifiable mods, ticked to start with, and one for the other needed mods, which says the player trusts the host. Confirming SHALL download the mods whose checkbox is ticked, and OK SHALL be disabled while neither is ticked.

#### Scenario: Archived mod without a Nexus Mods ID
- **WHEN** the host lists an archive for a mod whose manifest has no usable Nexus Mods update key
- **THEN** that mod isn't one of the verifiable mods, since it can't be confirmed as a file from its own page, and is counted with the unverifiable mods

#### Scenario: Confirm without ticking the checkbox
- **WHEN** the host lists archives for 20 of the 26 mods the player needs and the player confirms with only the first checkbox ticked, as it starts
- **THEN** only those 20 mods are requested from the host, the first checkbox gives that count and the archives' total size, and no trust warning is shown

#### Scenario: Confirm with the checkbox ticked
- **WHEN** the player ticks the trust checkbox, leaving the first one ticked, and confirms
- **THEN** all 26 needed mods are requested, and the warning that the host's mods can run any code is shown

#### Scenario: Confirm with only the unverifiable mods ticked
- **WHEN** the player unticks the first checkbox, ticks the second and confirms
- **THEN** only the 6 unverifiable mods are requested, the trust warning is shown, and the result lists the 20 verifiable mods as still needed

#### Scenario: Neither ticked
- **WHEN** the player unticks both checkboxes
- **THEN** OK is greyed out and clicking it does nothing

#### Scenario: All needed mods are verifiable
- **WHEN** every needed mod is verifiable
- **THEN** only the first checkbox is shown

#### Scenario: No archives listed
- **WHEN** the host lists no archives, or sends no archive list at all
- **THEN** the dialog shows a single trust checkbox, which the player must tick to confirm, as in 1.5.0

### Requirement: Trusted downloads install the host's files
When the player trusts the host for a download, a mod SHALL be installed whatever the result of checking it with Nexus Mods, and the result SHALL tell the player about any mod which couldn't be checked or which the host changed.

#### Scenario: Nexus Mods unreachable on the trusted path
- **WHEN** the player trusts the host and Nexus Mods can't be reached while checking an archive
- **THEN** the mod is installed and the player is told it wasn't checked

#### Scenario: Host changed the file on the trusted path
- **WHEN** the player trusts the host and the archive the host sent doesn't match the mod's files on Nexus Mods
- **THEN** the mod is installed, and the result warns that the host changed it, listing each such mod on its own line

## ADDED Requirements

### Requirement: Missing mods are listed as verifiable or unverifiable
The dialog shown to a blocked player SHALL list the mods they need in a "Verifiable" section, for the mods offered as original archives with a Nexus Mods ID, followed by an "Unverifiable" section for the rest, leaving out a section with no mods.

#### Scenario: Both kinds of mods needed
- **WHEN** the player needs 20 verifiable mods and 6 unverifiable ones
- **THEN** the dialog shows "Verifiable (20)" with a note that each file is checked against the mod's own Nexus Mods page before it's installed, then "Unverifiable (6)" with a note that these can't be checked

#### Scenario: No mods offered as original archives
- **WHEN** the host offers no original archives, or doesn't allow downloads
- **THEN** every needed mod is listed under "Unverifiable" and there's no "Verifiable" section

### Requirement: Player is warned when the host sent a changed file
When a mod downloaded without trusting the host fails its check because Nexus Mods doesn't know the file, or knows it only for another mod's page, the result SHALL warn that the host changed it, listing each such mod on its own line, and SHALL say how to get the host's version.

#### Scenario: Changed file without trust
- **WHEN** the player gets only the verifiable mods and the host's archive for Nozzle and Enricher doesn't match the mod's files on Nexus Mods
- **THEN** it's deleted without being unpacked, the other mods are installed, and the result warns that the host changed Nozzle and Enricher, names it on its own line, and says to join again and tick the trust checkbox to get the host's version

#### Scenario: Nexus Mods unreachable without trust
- **WHEN** Nexus Mods can't be reached while checking an archive downloaded without trust
- **THEN** the mod isn't installed and is listed among the mods which couldn't be installed, not as changed by the host

### Requirement: A changed file is remembered for that host
When the host sent a changed file for a mod version, the player SHALL remember that mod version for that host, and on later joins with the same host SHALL list that mod as unverifiable with a note that the host's file was changed, so getting it needs the trust checkbox. The record SHALL NOT affect other hosts.

#### Scenario: Joining the same host again
- **WHEN** the host sent a changed file for Nozzle and Enricher 0.2.1 and the player joins the same host again, after restarting the game
- **THEN** Nozzle and Enricher is listed under "Unverifiable", the prompt names it on its own line as changed by the host, and ticking the trust checkbox includes it in the download

#### Scenario: Joining another host
- **WHEN** the player joins a different host whose needed Nozzle and Enricher 0.2.1 is offered as an original archive
- **THEN** it's listed under "Verifiable" as usual, and its file is checked with Nexus Mods

#### Scenario: Host has a different version
- **WHEN** the player joins the same host, which now has a different version of the mod
- **THEN** the record doesn't apply, and the mod is listed by the usual rules

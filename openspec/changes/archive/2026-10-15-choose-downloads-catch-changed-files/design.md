# Design

## Context

1.6.0 (see the archived `download-nexus-verified-mods` change) lets a blocked player download the mods the host has original Nexus Mods archives for without trusting the host. Each archive is checked by MD5 with Nexus Mods before it's unpacked. The dialog had one checkbox, which switched between those mods and everything. A trusted download refused an archive Nexus Mods didn't know. The client only learns that an archive is changed after downloading and checking it; the host sends archive sizes, not hashes.

## Goals / Non-Goals

**Goals:**
- Make the choice of mods explicit, without changing what the host sends, so 1.6.0 hosts and players keep working.
- Never install a changed file without trust, and tell the player plainly when the host changed one.
- Let a player who trusts the host get the host's version on the next join, without the client having to recheck everything.

**Non-Goals:**
- Detecting a changed file before downloading it. That would need the host to send hashes, and the client would still have to check the downloaded file.
- Clearing a remembered changed file when the host puts back the real one at the same version.

## Decisions

**A list of checkboxes in the dialog, with a rule for OK.** The dialog takes any number of labelled checkboxes, their starting states, a function deciding whether OK is allowed, and a function giving the text for the current ticks. The text is measured for every combination so the dialog doesn't resize when a box is ticked. The alternative, a dedicated two-checkbox dialog, would have duplicated layout, scrolling and controller navigation, and the single trust checkbox still uses the same code with "all must be ticked".

**The plan is picked from the ticks.** Verifiable only is 1.6.0's trust-free plan; both is the trusted "everything" plan; unverifiable only is a new trusted plan of the mods left out of the verifiable one, with the verifiable mods recorded as left out so the result lists them as still needed. The host reports one size for all mods, so the unverifiable checkbox shows no size rather than a wrong one.

**"Changed" means Nexus Mods doesn't match the file to the mod.** A file Nexus Mods doesn't know, or knows only for another mod's page, is treated as changed by the host. A file that couldn't be checked (Nexus Mods unreachable) isn't, since nothing is known about it. Trusting the host means taking its files unchecked, so a changed file is installed with a warning rather than refused; refusing it would leave a trusting player no way to get the host's version.

**Remember changed files by host, mod ID and version.** The record is saved in SMAPI's global mod data, so it survives the restart after installing, and is keyed by the host's player ID. On the next join the archive sizes for remembered mods are dropped before planning, so they fall into the unverifiable group. Alternatives: remembering by mod and version only made the mod unverifiable for every host, including ones with the real file; remembering in memory only was lost on the restart that follows installing; remembering by file hash needs the host to send hashes.

## Risks / Trade-offs

- [The record outlives a fixed host] → A host that puts back the real file at the same version stays unverifiable for that mod until its version changes. This only makes the client stricter.
- [The host ID is per save] → A host joined from another save is a different host, so a changed file there is caught again by the Nexus Mods check rather than remembered.
- [The host can claim any ID] → A host could claim another host's ID, but the record only ever makes the client stricter, and every file downloaded without trust is still checked.
- ["Changed" is inferred] → Nexus Mods only says the file isn't one of the mod's files. The warning says the host changed it, which is the likely cause; the file isn't installed without trust either way.

## Migration Plan

Released as 1.7.0 by merging PR #20. Nothing in the protocol changes. Rollback is installing 1.6.0; a leftover record file is ignored by 1.6.0.

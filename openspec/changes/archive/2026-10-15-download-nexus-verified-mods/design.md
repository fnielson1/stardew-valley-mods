# Design

## Context

- The host already finds original archives, but only when it opens the download session for a blocked player (`HostSync.Open` → `FindArchive`), after the `BlockedMessage` has been sent. The player learns a mod came as an archive only from `ChunkResponse.IsArchive` during the download.
- The player checks an archive in `ClientSync.CheckArchive` during `StartInstall`, before `ModFiles.Stage` unpacks it. Today `NexusCheckResult.Unavailable` installs the mod anyway and adds it to `Unverified`.
- `SyncDialog` is a confirmation dialog whose optional checkbox hides the OK button until it's ticked. It can take a `getText` callback for text that changes while it's open, but it's sized by its initial text.
- Messages are serialized by SMAPI with Newtonsoft.Json. Unknown properties are ignored and missing optional record parameters get their defaults, which is how `BlockedMessage.ConfigHashes` was added without breaking older peers.

## Goals / Non-Goals

**Goals:**
- One dialog, with the checkbox choosing between the two download sets rather than gating OK, so there's no new menu to lay out or navigate with a controller.
- Keep the trust decision and the check policy in small pure functions that can be unit tested without the game.

**Non-Goals:**
- Sending an archive once when several needed mods share it (like Stardew Valley Expanded's three parts). Each is still downloaded separately, as today.
- Checking hashes with Nexus Mods before downloading. The check stays at install time, where it already runs on the bytes actually received.
- Changing anything for settings-only syncs.

## Decisions

**1. Add `ArchiveSizes` to `BlockedMessage`.** Add an optional `Dictionary<string, long>? ArchiveSizes = null` (unique mod ID → archive size in bytes, case-insensitive keys). `null` means a host without this change. An empty map means the host has no archives, or doesn't allow downloads. Alternative: a separate message type sent after `Blocked`. Rejected because the player would have to wait for a second message before showing the dialog, and older players would log an unknown message type.

**2. The host finds archives when it blocks the player.** Add `ModArchives.GetSizes(folder, mods)`, which looks up each needed mod's archive and returns the file sizes. Wrap it in `HostSync.GetArchiveSizes(ModComparison)`, which handles errors. `ModEntry` calls it next to `GetDownloadSize` when downloads are allowed. `ModArchives` caches each archive's contents by size and last write time, so the second lookup in `Open` is cheap. `GetSizes` doesn't log each mod, so the host log keeps one "Found the original archive" line per mod, from `Open`. Alternative: open the session first and reuse its `SendableMod.ArchivePath`. Rejected because `Open` runs later, from a separate SMAPI hook (`ModEntry.cs:322`).

**3. The checkbox picks the set.** When `ArchiveSizes` has at least one entry for a needed mod, `SyncDialog` gets a new `checkboxGatesConfirm: false` mode. OK stays visible, and `getText` swaps the prompt between `client.download-prompt-nexus` (unticked: count and size of the archived mods, no warning about running code, and the remaining mods named) and the existing `client.download-prompt` (ticked: all mods, with the warning). The dialog is sized by the longer of the two texts. When there are no archived mods, the dialog is unchanged. Alternative: a third button. Rejected because `ConfirmationDialog`'s two-button layout and controller neighbour IDs would need reworking for little gain.

**4. Choosing targets and the check policy are pure functions.**
- `DownloadPlan.SelectTargets(IEnumerable<ModInfo> needed, IReadOnlyDictionary<string, long>? archiveSizes, bool trustHost)` returns the targets and total size.
- `ArchiveCheckPolicy.Decide(NexusCheckResult? result, bool trustHost, bool hasNexusId)` returns `Install`, `InstallUnchecked` or `Reject`. A `null` result means the host sent a packaged folder rather than an archive. When untrusted, it returns `Reject` for everything except `Verified` from a mod with a Nexus Mods ID: without an ID, `NexusFileCheck` can only say the file is from some mod's page. When trusted, it keeps today's behaviour.
- `DownloadPlan` also leaves mods without a Nexus Mods ID out of the untrusted set, so they aren't downloaded only to be rejected.
- `ClientSync` keeps a `TrustHost` flag from confirm until the result dialog, and `CheckArchive` and `StartInstall` act on the policy's answer. A rejected download is deleted before `ModFiles.Stage` is called, so it's never unpacked.

**5. The result dialog names what's left.** On the untrusted path, the summary adds `client.still-needed` with the mods that weren't installed: failed checks and mods with no archive. It's shown through `CreateMismatchDialog`-style links, so each name opens its Nexus Mods page.

## Risks / Trade-offs

- [The host's archive is moved or replaced between the blocked message and the download] → The host sends a packaged folder or a different file. The packaged folder is rejected by policy, and a different file fails the hash check, so nothing unverified is installed. The player sees it in the result.
- [Nexus Mods is down] → Untrusted downloads install nothing and the player can retry or tick the checkbox. That is the point of the trust-free path, and the result says why.
- [A player assumes "from Nexus Mods" means safe] → The unticked prompt says the files are checked against Nexus Mods, not that the mods themselves are safe, matching what downloading from Nexus Mods yourself means.

## Migration Plan

Ship in a normal version bump. There's no data or config migration. Mixed versions fall back to today's dialog (see the spec's "Mixed versions keep working"). Rolling back means installing the previous release.

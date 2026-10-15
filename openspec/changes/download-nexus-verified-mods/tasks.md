# Tasks

## 1. Host lists its archives

- [x] 1.1 Add the optional `ArchiveSizes` parameter to `BlockedMessage` in `SyncMessages.cs`, and verify with `SyncMessagesTests` that a message round-trips with it, and that JSON without it (an older host) deserializes to `null`.
- [x] 1.2 Add `HostSync.GetArchiveSizes(ModComparison)` (backed by `ModArchives.GetSizes`, which doesn't log per mod), then verify in `dotnet test` that a needed mod with a matching archive is listed with its size and one without isn't (extend `ModArchivesTests` or add a host test using a temp archives folder).
- [x] 1.3 Send the map from `ModEntry` when blocking a player who has the mod and may download, and an empty map when downloads aren't allowed; verify the build passes (the once-per-join log check is part of 5.2).

## 2. Player chooses what to download

- [x] 2.1 Add `DownloadPlan.Select` with unit tests: no map gives all mods with the checkbox required; trusted gives all mods; untrusted gives only the mods in the map, with their total size and the names of the rest. Verify with `dotnet test`.
- [x] 2.2 Add a mode to `SyncDialog` where the checkbox doesn't hide OK and `getText` follows the checkbox, sized by the longer text; verify by building and checking the existing dialogs still hide OK until the checkbox is ticked.
- [x] 2.3 Use them in `ClientSync.ShowModMismatch` and `StartDownload`, keeping the `TrustHost` choice until the result, and add `client.download-prompt-nexus` to `i18n/default.json`; verify with `dotnet build` and by reading the prompt strings for both checkbox states.

## 3. Check before unpacking

- [x] 3.1 Add `ArchiveCheckPolicy.Decide` with unit tests for every `NexusCheckResult` plus a packaged folder (`null`), both trusted and untrusted; verify with `dotnet test` that untrusted rejects everything except `Verified` and trusted matches today.
- [x] 3.2 Make `ClientSync.CheckDownload` (was `CheckArchive`)/`StartInstall` follow the policy, deleting a rejected download before `ModFiles.Stage` and recording why it failed; verify with `dotnet build` and by checking that nothing opens the zip before the check.
- [x] 3.3 Add `client.still-needed` to the result dialog on the untrusted path, with Nexus Mods links for the names; verify the strings in `i18n/default.json` and that the trusted result text is unchanged.

## 4. Docs

- [x] 4.1 Update the README's "Original downloads, checked against Nexus Mods" and "Both have the mod" sections for the two choices and the stricter check; verify they match the spec scenarios.

## 5. Integration

- [x] 5.1 Run the full `dotnet test` suite and verify it passes.
- [ ] 5.2 Run a host with `Mods` and a client with `--mods-path "Mods-TestClient"`. Join without ticking, and verify the host log has one "Found the original archive" line per mod per join, and verify in both SMAPI logs that only archived mods are sent and each logs a Nexus Mods match before being installed. Then repeat offline, and verify they're deleted rather than installed.

## Workflow follow-up

- Bump the version and archive the change once it has been reviewed and merged.

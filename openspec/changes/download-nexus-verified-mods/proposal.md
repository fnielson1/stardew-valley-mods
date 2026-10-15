# Proposal

## Why

A blocked player who wants mods from the host must tick "I know the host personally and trust them" before they can download anything, even when every mod the host would send is an original Nexus Mods archive that the player checks against Nexus Mods before installing. A file confirmed to be from the mod's own Nexus Mods page is as safe as downloading it there yourself, so the player shouldn't have to vouch for the host to get those, and they should be able to get just those while leaving out anything the host packaged from its own mod folders.

## What Changes

- The host tells a blocked player which of the mods they need it will send as an original archive, and the size of those archives.
- The download dialog lets the player get just those mods without ticking the trust checkbox. Ticking it still gets every mod they need, as today, and the prompt says which of the two will happen.
- On the trust-free path, each archive is downloaded but not unpacked, installed or otherwise used until Nexus Mods confirms its MD5 hash belongs to a file on that mod's own page. An archive that can't be confirmed for any reason (unknown hash, a different mod's file, Nexus Mods unreachable, or the host sending a packaged mod folder instead of the archive) is deleted and listed as not installed.
- The trusted path (checkbox ticked) is unchanged, including installing an archive without the check when Nexus Mods can't be reached.
- After a trust-free download, the result dialog lists the mods still needed, which the player can get from Nexus Mods with the existing links.
- A host without this change sends no archive list, so a newer player sees today's dialog; an older player ignores the new field and sees today's dialog too.

## Capabilities

### New Capabilities
- `mod-downloads`: how a blocked player is offered the host's mods and how downloaded mods are checked and installed, including the trust-free path for original Nexus Mods archives.

### Modified Capabilities
(none; there are no existing specs)

## Impact

- `SyncMessages.cs`: `BlockedMessage` gains an optional per-mod archive size list.
- `HostSync.cs`, `ModEntry.cs`: the host finds archives when blocking a player rather than only when opening the download session, and sends the list.
- `ClientSync.cs`: dialog choice, choosing download targets, the stricter check before unpacking on the trust-free path, and the result summary.
- `SyncDialog.cs`: the checkbox no longer has to gate the OK button, and the prompt text can follow the checkbox.
- `i18n/default.json`: new prompt and result strings.
- `README.md`: the "Original downloads, checked against Nexus Mods" section.
- Tests in `ModSyncCheck.Tests` for the message round trip and the target selection.

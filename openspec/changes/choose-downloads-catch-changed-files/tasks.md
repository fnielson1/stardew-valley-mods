# Tasks

The code for this change is already on branch `fix/shorten-nexus-download-prompt` (PR #20). Each task confirms that part matches the spec, fixing it if not.

## 1. Checkboxes in the dialog

- [x] 1.1 Confirm `SyncDialog` takes a list of checkbox labels with starting states, an OK rule and checkbox-dependent text, and doesn't resize when a box is ticked; verify by building and by the in-game check in 4.1.
- [x] 1.2 Confirm `DownloadPlan.SelectUnverifiable` returns the mods left out of the verifiable plan as targets, the verifiable mods as left out, and trusts the host; verify with the `SelectUnverifiable_GetsOnlyTheModsWhichCantBeChecked_AndTrustsTheHost` test in `dotnet test`.
- [x] 1.3 Confirm `ClientSync.ShowModMismatch` shows one checkbox per non-empty group (verifiable ticked), picks the plan from the ticks, shows the trust warning only with the second ticked, and keeps the single trust checkbox when nothing is verifiable; verify by reading the code against the "Player can get only the mods available as original archives without trusting the host" scenarios.
- [x] 1.4 Confirm the result lists the left-out verifiable mods as still needed after an unverifiable-only download, with a Nexus Mods link for every listed mod that has one; verify by reading `ClientSync.Finish` and the `GetAllNexusLinks` use.

## 2. Verifiable and Unverifiable sections

- [x] 2.1 Confirm `ComparisonText` lists "Verifiable" then "Unverifiable", leaves out an empty section, and lists every mod as unverifiable when nothing is verifiable; verify with the `BuildWarning_*` tests in `ComparisonTextTests` passing.
- [x] 2.2 Confirm the README's "Getting mods without trusting the host" bullet describes the sections and both checkboxes; verify by reading it.

## 3. Changed files from the host

- [x] 3.1 Confirm `ArchiveCheckPolicy.Decide` installs with a warning for an unknown or wrong-mod file when trusted, rejects it when not, and `IsModified` is true only for those results; verify with `ArchiveCheckPolicyTests` in `dotnet test`.
- [x] 3.2 Confirm a rejected changed file is reported in its own warning, each mod on its own line followed by how to get the host's version, and not in the generic failure list, while an unreachable Nexus Mods is still a generic failure; verify by reading `ClientSync.CheckDownload` and `Finish`.
- [x] 3.3 Confirm changed files are saved by host ID, mod ID and version in `modified-archives` global data, and only a matching host and version moves a mod to unverifiable with the note; verify by reading `GetKnownModified` and `RememberModified`.
- [x] 3.4 Confirm the README's "Changed files from the host" bullet matches the behavior; verify by reading it.

## 4. In-game checks

- [x] 4.1 With a host and a client using `Mods-TestClient`, check every checkbox combination: verifiable only, both, unverifiable only, and neither (OK greyed out); verify by screenshots and the client's SMAPI log.
- [x] 4.2 With a changed archive for a mod in the host's archive folder, get only the verifiable mods and check the warning; rejoin after the restart and check the mod is unverifiable with the note; then tick trust and check it's installed with a warning; verify by screenshots and the client's SMAPI log.
- [x] 4.3 Point the saved record at another host ID and check the mod is verifiable again; verify by screenshot.

## Workflow follow-up

- Archive the change after PR #20 is merged and 1.7.0 is released.

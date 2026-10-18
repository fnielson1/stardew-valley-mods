using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StardewModdingAPI;

namespace ModSyncCheck;

/// <summary>The installed mods whose settings don't match the host's.</summary>
/// <param name="Live">The mods whose settings can be changed while the game is running.</param>
/// <param name="Restart">The mods whose settings are only read when the game starts, so the player's settings files must be replaced and the game restarted.</param>
internal record SettingsToSync(List<ModInfo> Live, List<ModInfo> Restart);

/// <summary>When joining a host which requires its mod settings, makes the running mods use those settings and keeps them that way, without changing the player's own settings files. Mods which only read their settings when the game starts have their settings files replaced instead, if the player agrees.</summary>
internal class HostSettings
{
    /*********
    ** Fields
    *********/
    /// <summary>The folder within this mod's folder used to hold the host's settings files while they're in use.</summary>
    private const string TempFolderName = "host-settings";

    /// <summary>The number of checks in a row which can find a mod's settings changed before it's left alone.</summary>
    private const int MaxRevertsInARow = 5;

    /// <summary>The SMAPI API for this mod.</summary>
    private readonly IModHelper Helper;

    /// <summary>Writes messages to the SMAPI log.</summary>
    private readonly IMonitor Monitor;

    /// <summary>The running mods which must keep using the host's settings, by unique mod ID.</summary>
    private readonly Dictionary<string, TrackedMod> Tracked = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The unique ID of the host whose settings are in <see cref="Tracked"/>.</summary>
    private long? TrackedHostId;

    /// <summary>The hash of the host's settings for each mod returned by the last call to <see cref="GetModsToSync"/>, by unique mod ID.</summary>
    private readonly Dictionary<string, string> PendingHashes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The names of the mods which are using the player's own settings because the host's couldn't be applied.</summary>
    private readonly SortedSet<string> SkippedNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The unique IDs of the mods whose settings files were replaced with the host's since the game started, which they won't use until it restarts.</summary>
    private readonly HashSet<string> ReplacedIds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The number used to name the next copy of the host's settings.</summary>
    private int NextFileIndex;


    /*********
    ** Accessors
    *********/
    /// <summary>The folder to download the host's settings into.</summary>
    public string DownloadDir => Path.Combine(this.TempDir, "downloads");

    /// <summary>The names of the mods which are using the player's own settings because the host's couldn't be applied.</summary>
    public IReadOnlyCollection<string> Skipped => this.SkippedNames;


    /*********
    ** Public methods
    *********/
    /// <summary>Construct an instance.</summary>
    /// <param name="helper">The SMAPI API for this mod.</param>
    /// <param name="monitor">Writes messages to the SMAPI log.</param>
    public HostSettings(IModHelper helper, IMonitor monitor)
    {
        this.Helper = helper;
        this.Monitor = monitor;
    }

    /// <summary>Get the installed mods whose settings need to be downloaded from the host and applied.</summary>
    /// <param name="hostId">The host's unique player ID.</param>
    /// <param name="hostHashes">A hash of each of the host's settings files, by unique mod ID and then by file path relative to the mod folder.</param>
    public SettingsToSync GetModsToSync(long hostId, IReadOnlyDictionary<string, Dictionary<string, string>> hostHashes)
    {
        // settings from another host are no use here
        if (this.TrackedHostId != hostId)
            this.RestoreOwnSettings();

        this.PendingHashes.Clear();
        this.SkippedNames.Clear();

        List<ModInfo> live = new();
        List<ModInfo> restart = new();
        foreach ((string modId, Dictionary<string, string> hashes) in hostHashes)
        {
            if (this.Helper.ModRegistry.Get(modId) is not { } mod || SmapiInternals.GetDirectoryPath(mod) is not { } modDir)
                continue;
            ModInfo info = new(mod.Manifest.UniqueID, mod.Manifest.Name, mod.Manifest.Version.ToString());

            // the files match the host's now, but the mod is still using the settings it read when the game started
            if (this.ReplacedIds.Contains(modId))
            {
                restart.Add(info);
                continue;
            }

            bool needsRestart = false;
            foreach ((string relativePath, string hostHash) in hashes)
            {
                // only a code mod's own settings can be changed while the game is running
                bool isModSettings = !mod.IsContentPack && string.Equals(relativePath, ModFiles.ConfigFileName, StringComparison.OrdinalIgnoreCase);
                bool canChange = isModSettings && SmapiInternals.CanReloadConfig(mod, out _);

                // already in use
                if (isModSettings && this.Tracked.TryGetValue(modId, out TrackedMod? tracked))
                {
                    if (tracked.HostHash == hostHash)
                        continue;
                }
                else if (this.FileMatches(modDir, relativePath, hostHash))
                {
                    // our own settings are the same as the host's, but they still need to stay that way
                    if (canChange)
                        this.TrackOwnSettings(hostId, mod, modDir, hostHash);
                    continue;
                }

                if (canChange)
                {
                    live.Add(info);
                    this.PendingHashes[modId] = hostHash;
                }
                else
                    needsRestart = true;
            }

            // replacing the settings files and restarting covers all of them
            if (needsRestart)
            {
                live.RemoveAll(other => string.Equals(other.Id, info.Id, StringComparison.OrdinalIgnoreCase));
                this.PendingHashes.Remove(modId);
                restart.Add(info);
            }
        }
        return new SettingsToSync(live, restart);
    }

    /// <summary>Replace the player's settings files with the host's for mods which only read them when the game starts, keeping a copy of the player's own.</summary>
    /// <param name="downloads">The downloaded settings for the mods in <see cref="SettingsToSync.Restart"/>.</param>
    /// <param name="backupRoot">The folder to copy the player's settings files into, in a subfolder for each mod.</param>
    /// <returns>Returns the names of the mods whose settings were replaced, and a reason for each mod whose settings couldn't be.</returns>
    public (List<string> Replaced, List<string> Failures) ReplaceOwnSettings(IEnumerable<DownloadedMod> downloads, string backupRoot)
    {
        List<string> replaced = new();
        List<string> failures = new();
        foreach (DownloadedMod download in downloads)
        {
            ModInfo mod = download.Mod;
            string? error = null;
            try
            {
                if (this.Helper.ModRegistry.Get(mod.Id) is not { } installed || SmapiInternals.GetDirectoryPath(installed) is not { } modDir)
                    error = "it isn't installed";
                else
                {
                    string backupDir = Path.Combine(backupRoot, Path.GetFileName(modDir.TrimEnd('\\', '/')));
                    if (ModFiles.ReplaceConfigFiles(download.ZipPath, modDir, backupDir).Count == 0)
                        error = "the host didn't send its settings";
                    else
                    {
                        this.ReplacedIds.Add(mod.Id);
                        this.Monitor.Log($"{mod.Name} will use the host's settings once the game restarts. Your own settings were copied to '{backupDir}'.", LogLevel.Info);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                error = ex.Message;
            }

            if (error == null)
                replaced.Add(mod.Name);
            else
            {
                this.Monitor.Log($"Couldn't replace your settings for {mod.Name} with the host's: {error}.", LogLevel.Warn);
                failures.Add($"{mod.Name}: {error}");
            }
        }

        this.DeleteDownloads();
        return (replaced, failures);
    }

    /// <summary>Get whether a mod's settings files were replaced with the host's since the game started.</summary>
    /// <param name="modId">The unique mod ID.</param>
    public bool IsReplaced(string modId)
    {
        return this.ReplacedIds.Contains(modId);
    }

    /// <summary>Empty the folder which the host's settings are downloaded into.</summary>
    public void PrepareDownloadDir()
    {
        this.DeleteDownloads();
        Directory.CreateDirectory(this.DownloadDir);
    }

    /// <summary>Make the running mods use the settings downloaded from the host.</summary>
    /// <param name="hostId">The host's unique player ID.</param>
    /// <param name="downloads">The downloaded settings for the mods returned by <see cref="GetModsToSync"/>.</param>
    public void Apply(long hostId, IEnumerable<DownloadedMod> downloads)
    {
        foreach (DownloadedMod download in downloads)
        {
            ModInfo mod = download.Mod;
            string fileName = this.GetNextFileName();
            string? error;
            try
            {
                if (this.Helper.ModRegistry.Get(mod.Id) is not { } installed)
                    error = "it isn't installed";
                else if (!ModFiles.TryExtractConfig(download.ZipPath, Path.Combine(this.TempDir, fileName)))
                    error = "the host didn't send its settings";
                else if (SmapiInternals.TryApplyConfigFile(installed, this.Helper, $"{HostSettings.TempFolderName}/{fileName}", out error))
                    this.Track(hostId, installed, this.PendingHashes.GetValueOrDefault(mod.Id, ""), fileName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                error = ex.Message;
            }

            if (error == null)
                this.Monitor.Log($"{mod.Name} is using the host's settings until you leave. Your settings file wasn't changed.", LogLevel.Info);
            else
            {
                this.Monitor.Log($"Couldn't apply the host's settings for {mod.Name}: {error}.", LogLevel.Warn);
                this.SkippedNames.Add(mod.Name);
            }
        }

        this.DeleteDownloads();
    }

    /// <summary>Record that a mod is using the player's own settings because the host's couldn't be downloaded.</summary>
    /// <param name="modName">The mod's display name.</param>
    public void MarkSkipped(string modName)
    {
        this.SkippedNames.Add(modName);
    }

    /// <summary>Undo any change made to the settings which must match the host's since they were applied, both in the running mods and in the player's settings files.</summary>
    /// <returns>Returns the names of the mods whose settings were changed back to the host's.</returns>
    /// <remarks>A mod which is told to save its settings writes whatever it's using at the time, which would replace the player's own settings with the host's. So a settings file which changes while the host's settings are in use is put back as it was.</remarks>
    public List<string> UndoChanges()
    {
        List<string> reverted = new();
        foreach ((string modId, TrackedMod tracked) in this.Tracked)
        {
            if (this.Helper.ModRegistry.Get(modId) is not { } mod)
                continue;

            this.RestoreOwnFile(mod, tracked);

            if (tracked.Snapshot == null || SmapiInternals.GetConfigSnapshot(mod) is not { } current)
                continue;
            if (current == tracked.Snapshot)
            {
                tracked.RevertsInARow = 0;
                continue;
            }

            // a mod which changes its own settings as it runs would be undone forever
            if (++tracked.RevertsInARow > HostSettings.MaxRevertsInARow)
            {
                this.Monitor.Log($"The settings for {mod.Manifest.Name} keep changing, so they won't be set back to the host's anymore.", LogLevel.Warn);
                tracked.Snapshot = null;
                continue;
            }

            if (SmapiInternals.TryApplyConfigFile(mod, this.Helper, $"{HostSettings.TempFolderName}/{tracked.HostFileName}", out string? error))
            {
                this.Monitor.Log($"The settings for {mod.Manifest.Name} were changed, so they were set back to the host's.", LogLevel.Info);
                tracked.Snapshot = SmapiInternals.GetConfigSnapshot(mod);
                reverted.Add(mod.Manifest.Name);
            }
            else
            {
                this.Monitor.Log($"The settings for {mod.Manifest.Name} were changed and couldn't be set back to the host's ({error}), so they won't be checked again.", LogLevel.Warn);
                tracked.Snapshot = null;
            }
        }
        return reverted;
    }

    /// <summary>Make the running mods use the player's own settings again.</summary>
    public void RestoreOwnSettings()
    {
        foreach ((string modId, TrackedMod tracked) in this.Tracked)
        {
            if (this.Helper.ModRegistry.Get(modId) is not { } mod)
                continue;

            this.RestoreOwnFile(mod, tracked);
            if (SmapiInternals.TryReloadConfig(mod, out string? error))
                this.Monitor.Log($"{mod.Manifest.Name} is using your own settings again.", tracked.IsOwnSettings ? LogLevel.Trace : LogLevel.Info);
            else
                this.Monitor.Log($"Couldn't switch {mod.Manifest.Name} back to your own settings ({error}). It'll use the host's until the game restarts; your settings file wasn't changed.", LogLevel.Warn);
        }

        this.Tracked.Clear();
        this.TrackedHostId = null;
        this.NextFileIndex = 0;
        this.DeleteTempFiles();
    }

    /// <summary>Delete the host's settings files which were downloaded but not applied.</summary>
    public void DeleteDownloads()
    {
        this.DeleteFolder(this.DownloadDir);
    }


    /*********
    ** Private methods
    *********/
    /// <summary>The full path to the folder used to hold the host's settings files while they're in use.</summary>
    private string TempDir => Path.Combine(this.Helper.DirectoryPath, HostSettings.TempFolderName);

    /// <summary>Get the name to use for the next copy of the host's settings.</summary>
    private string GetNextFileName()
    {
        return $"{this.NextFileIndex++}.json";
    }

    /// <summary>Keep a mod on the settings it has now, which are the same as the host's.</summary>
    /// <param name="hostId">The host's unique player ID.</param>
    /// <param name="mod">The mod metadata.</param>
    /// <param name="modDir">The player's folder for the mod.</param>
    /// <param name="hostHash">The hash of the host's settings.</param>
    private void TrackOwnSettings(long hostId, IModInfo mod, string modDir, string hostHash)
    {
        try
        {
            string fileName = this.GetNextFileName();
            Directory.CreateDirectory(this.TempDir);
            File.Copy(Path.Combine(modDir, ModFiles.ConfigFileName), Path.Combine(this.TempDir, fileName), overwrite: true);
            this.Track(hostId, mod, hostHash, fileName, isOwnSettings: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            this.Monitor.Log($"Couldn't keep a copy of the settings for {mod.Manifest.Name}, so changes to them while you're in the host's game won't be undone:\n{ex}");
        }
    }

    /// <summary>Start keeping a mod on the host's settings, which it's using now.</summary>
    /// <param name="hostId">The host's unique player ID.</param>
    /// <param name="mod">The mod metadata.</param>
    /// <param name="hostHash">The hash of the host's settings.</param>
    /// <param name="hostFileName">The name of the file in the temporary folder which has the host's settings.</param>
    /// <param name="isOwnSettings">Whether the player's own settings were already the same as the host's.</param>
    private void Track(long hostId, IModInfo mod, string hostHash, string hostFileName, bool isOwnSettings = false)
    {
        string modId = mod.Manifest.UniqueID;
        if (!this.Tracked.TryGetValue(modId, out TrackedMod? tracked))
        {
            // the player's own settings file as it is before anything can change it
            string? ownPath = SmapiInternals.GetDirectoryPath(mod) is { } modDir ? Path.Combine(modDir, ModFiles.ConfigFileName) : null;
            byte[]? ownContent = null;
            try
            {
                if (ownPath != null && File.Exists(ownPath))
                    ownContent = File.ReadAllBytes(ownPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ownPath = null; // can't tell what to put back
            }
            this.Tracked[modId] = tracked = new TrackedMod(ownPath, ownContent);
        }

        tracked.HostHash = hostHash;
        tracked.HostFileName = hostFileName;
        tracked.IsOwnSettings = isOwnSettings;
        tracked.Snapshot = SmapiInternals.GetConfigSnapshot(mod);
        this.TrackedHostId = hostId;
    }

    /// <summary>Put the player's settings file for a mod back as it was before the host's settings were used, if it was changed.</summary>
    /// <param name="mod">The mod metadata.</param>
    /// <param name="tracked">The settings being kept for the mod.</param>
    private void RestoreOwnFile(IModInfo mod, TrackedMod tracked)
    {
        if (tracked.OwnPath == null)
            return;

        try
        {
            if (ModFiles.RestoreFile(tracked.OwnPath, tracked.OwnContent))
                this.Monitor.Log($"{mod.Manifest.Name} saved its settings while using the host's, so your settings file was put back as it was.", LogLevel.Info);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            this.Monitor.Log($"Couldn't check your settings file for {mod.Manifest.Name}: {ex.Message}"); // tried again next time
        }
    }

    /// <summary>Delete all of the host's settings files.</summary>
    private void DeleteTempFiles()
    {
        this.DeleteFolder(this.TempDir);
    }

    /// <summary>Delete a temporary folder, if possible.</summary>
    /// <param name="path">The full folder path.</param>
    private void DeleteFolder(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            this.Monitor.Log($"Couldn't delete the temporary host settings:\n{ex}");
        }
    }

    /// <summary>Get whether one of the player's settings files has the same settings as the host's.</summary>
    /// <param name="modDir">The player's folder for the mod.</param>
    /// <param name="relativePath">The settings file path relative to the mod folder.</param>
    /// <param name="hostHash">The hash of the host's file.</param>
    private bool FileMatches(string modDir, string relativePath, string hostHash)
    {
        try
        {
            string path = ModFiles.ResolveInstallPath(modDir, relativePath);
            return File.Exists(path) && ModFiles.GetConfigHash(path) == hostHash;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return false;
        }
    }

    /// <summary>A running mod which must keep using the host's settings.</summary>
    private class TrackedMod
    {
        /// <summary>The full path to the player's own settings file, or <c>null</c> if it can't be protected.</summary>
        public string? OwnPath { get; }

        /// <summary>The content of the player's own settings file before the host's settings were used, or <c>null</c> if it didn't exist.</summary>
        public byte[]? OwnContent { get; }

        /// <summary>The hash of the host's settings which the mod is using.</summary>
        public string HostHash { get; set; } = "";

        /// <summary>The name of the file in the temporary folder which has the host's settings.</summary>
        public string HostFileName { get; set; } = "";

        /// <summary>Whether the player's own settings were already the same as the host's.</summary>
        public bool IsOwnSettings { get; set; }

        /// <summary>The settings the mod had in memory just after the host's were applied, or <c>null</c> if they can't be compared.</summary>
        public string? Snapshot { get; set; }

        /// <summary>The number of checks in a row which found the mod's settings changed.</summary>
        public int RevertsInARow { get; set; }

        /// <summary>Construct an instance.</summary>
        /// <param name="ownPath">The full path to the player's own settings file, or <c>null</c> if it can't be protected.</param>
        /// <param name="ownContent">The content of the player's own settings file before the host's settings were used, or <c>null</c> if it didn't exist.</param>
        public TrackedMod(string? ownPath, byte[]? ownContent)
        {
            this.OwnPath = ownPath;
            this.OwnContent = ownContent;
        }
    }
}

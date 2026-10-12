using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StardewModdingAPI;

namespace ModSyncCheck;

/// <summary>When joining a host which requires its mod settings, makes the running mods use those settings without changing the player's own settings files.</summary>
internal class HostSettings
{
    /*********
    ** Fields
    *********/
    /// <summary>The folder within this mod's folder used to hold the host's settings files while they're applied.</summary>
    private const string TempFolderName = "host-settings";

    /// <summary>The SMAPI API for this mod.</summary>
    private readonly IModHelper Helper;

    /// <summary>Writes messages to the SMAPI log.</summary>
    private readonly IMonitor Monitor;

    /// <summary>The hash of the host settings each running mod is using in place of the player's own, by unique mod ID.</summary>
    private readonly Dictionary<string, string> Applied = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The unique ID of the host whose settings are in <see cref="Applied"/>.</summary>
    private long? AppliedHostId;

    /// <summary>The hash of the host's settings for each mod returned by the last call to <see cref="GetModsToSync"/>, by unique mod ID.</summary>
    private readonly Dictionary<string, string> PendingHashes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The names of the mods which are using the player's own settings because the host's couldn't be applied.</summary>
    private readonly SortedSet<string> SkippedNames = new(StringComparer.OrdinalIgnoreCase);


    /*********
    ** Accessors
    *********/
    /// <summary>The folder to download the host's settings into.</summary>
    public string DownloadDir => Path.Combine(this.Helper.DirectoryPath, HostSettings.TempFolderName, "downloads");

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
    public List<ModInfo> GetModsToSync(long hostId, IReadOnlyDictionary<string, Dictionary<string, string>> hostHashes)
    {
        // settings from another host are no use here
        if (this.AppliedHostId != hostId)
            this.RestoreOwnSettings();

        this.PendingHashes.Clear();
        this.SkippedNames.Clear();

        List<ModInfo> mods = new();
        foreach ((string modId, Dictionary<string, string> hashes) in hostHashes)
        {
            if (this.Helper.ModRegistry.Get(modId) is not { } mod || SmapiInternals.GetDirectoryPath(mod) is not { } modDir)
                continue;

            foreach ((string relativePath, string hostHash) in hashes)
            {
                bool usingHostSettings = this.Applied.TryGetValue(modId, out string? appliedHash);
                bool isModSettings = !mod.IsContentPack && string.Equals(relativePath, ModFiles.ConfigFileName, StringComparison.OrdinalIgnoreCase);

                // already in use
                if (isModSettings && usingHostSettings && appliedHash == hostHash)
                    continue;
                if (!(isModSettings && usingHostSettings) && this.FileMatches(modDir, relativePath, hostHash))
                    continue;

                // only a code mod's own settings can be changed while the game is running
                if (isModSettings && SmapiInternals.CanReloadConfig(mod, out _))
                {
                    mods.Add(new ModInfo(mod.Manifest.UniqueID, mod.Manifest.Name, mod.Manifest.Version.ToString()));
                    this.PendingHashes[modId] = hostHash;
                }
                else
                    this.SkippedNames.Add(mod.Manifest.Name);
            }
        }
        return mods;
    }

    /// <summary>Make the running mods use the settings downloaded from the host.</summary>
    /// <param name="hostId">The host's unique player ID.</param>
    /// <param name="downloads">The downloaded settings for the mods returned by <see cref="GetModsToSync"/>.</param>
    public void Apply(long hostId, IEnumerable<DownloadedMod> downloads)
    {
        string tempDir = Path.Combine(this.Helper.DirectoryPath, HostSettings.TempFolderName);
        int index = 0;
        foreach (DownloadedMod download in downloads)
        {
            ModInfo mod = download.Mod;
            string fileName = $"{index++}.json";
            string? error;
            try
            {
                if (this.Helper.ModRegistry.Get(mod.Id) is not { } installed)
                    error = "it isn't installed";
                else if (!ModFiles.TryExtractConfig(download.ZipPath, Path.Combine(tempDir, fileName)))
                    error = "the host didn't send its settings";
                else if (SmapiInternals.TryApplyConfigFile(installed, this.Helper, $"{HostSettings.TempFolderName}/{fileName}", out error))
                {
                    this.Applied[mod.Id] = this.PendingHashes.GetValueOrDefault(mod.Id, "");
                    this.AppliedHostId = hostId;
                }
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

        this.DeleteTempFiles();
    }

    /// <summary>Record that a mod is using the player's own settings because the host's couldn't be downloaded.</summary>
    /// <param name="modName">The mod's display name.</param>
    public void MarkSkipped(string modName)
    {
        this.SkippedNames.Add(modName);
    }

    /// <summary>Make the running mods use the player's own settings again.</summary>
    public void RestoreOwnSettings()
    {
        foreach (string modId in this.Applied.Keys)
        {
            if (this.Helper.ModRegistry.Get(modId) is not { } mod)
                continue;

            if (SmapiInternals.TryReloadConfig(mod, out string? error))
                this.Monitor.Log($"{mod.Manifest.Name} is using your own settings again.", LogLevel.Info);
            else
                this.Monitor.Log($"Couldn't switch {mod.Manifest.Name} back to your own settings ({error}). It'll use the host's until the game restarts; your settings file wasn't changed.", LogLevel.Warn);
        }

        this.Applied.Clear();
        this.AppliedHostId = null;
    }

    /// <summary>Delete the host's settings files which were downloaded.</summary>
    public void DeleteTempFiles()
    {
        try
        {
            string tempDir = Path.Combine(this.Helper.DirectoryPath, HostSettings.TempFolderName);
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            this.Monitor.Log($"Couldn't delete the temporary host settings:\n{ex}");
        }
    }


    /*********
    ** Private methods
    *********/
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
}

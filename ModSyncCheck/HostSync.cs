using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StardewModdingAPI;

namespace ModSyncCheck;

/// <summary>When hosting, sends the host's mods and settings to blocked players who ask for them.</summary>
internal class HostSync
{
    /*********
    ** Fields
    *********/
    /// <summary>How long a blocked player can go without asking for anything before the host stops waiting for them.</summary>
    private static readonly TimeSpan SessionTimeout = TimeSpan.FromMinutes(3);

    /// <summary>How long a player has to join after saying their settings match the host's.</summary>
    private static readonly TimeSpan VerifiedTimeout = TimeSpan.FromMinutes(1);

    /// <summary>The maximum number of requests to answer per player each tick.</summary>
    private const int MaxResponsesPerTick = 16;

    /// <summary>The SMAPI API for this mod.</summary>
    private readonly IModHelper Helper;

    /// <summary>Writes messages to the SMAPI log.</summary>
    private readonly IMonitor Monitor;

    /// <summary>This mod's unique ID.</summary>
    private readonly string ModId;

    /// <summary>The mod settings.</summary>
    private readonly ModConfig Config;

    /// <summary>The blocked players who have this mod and haven't said they're done yet, by player ID.</summary>
    private readonly Dictionary<long, Session> Sessions = new();

    /// <summary>The players who said their settings match the host's, with the time by which they must join.</summary>
    private readonly Dictionary<long, DateTime> VerifiedPlayers = new();

    /// <summary>The rejected players to remove from SMAPI's peer list once it's done handling their request to join.</summary>
    private readonly List<long> PeersToForget = new();

    /// <summary>Finds the original archives the host downloaded its mods in.</summary>
    private readonly ModArchives Archives = new();

    /// <summary>The settings hashes which the players in the game were last told about, or <c>null</c> if no player who has this mod is in the game.</summary>
    private Dictionary<string, Dictionary<string, string>>? SentConfigHashes;


    /*********
    ** Public methods
    *********/
    /// <summary>Construct an instance.</summary>
    /// <param name="helper">The SMAPI API for this mod.</param>
    /// <param name="monitor">Writes messages to the SMAPI log.</param>
    /// <param name="modId">This mod's unique ID.</param>
    /// <param name="config">The mod settings.</param>
    public HostSync(IModHelper helper, IMonitor monitor, string modId, ModConfig config)
    {
        this.Helper = helper;
        this.Monitor = monitor;
        this.ModId = modId;
        this.Config = config;
    }

    /// <summary>Get the approximate total size of the mods a player needs, counting only those which can be sent.</summary>
    /// <param name="comparison">The host mods the player doesn't have at the same version.</param>
    public long GetDownloadSize(ModComparison comparison)
    {
        long size = 0;
        foreach (ModInfo needed in comparison.GetNeededMods())
        {
            try
            {
                if (this.TryGetModPaths(needed.Id, out string? directoryPath, out _))
                    size += ModFiles.GetSize(directoryPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return size;
    }

    /// <summary>Get the size of the original archive the host would send for each mod a player needs, for the mods it has one for.</summary>
    /// <param name="comparison">The host mods the player doesn't have at the same version.</param>
    /// <returns>Returns the archive size in bytes by unique mod ID.</returns>
    public Dictionary<string, long> GetArchiveSizes(ModComparison comparison)
    {
        string? folder = this.GetArchivesFolder();
        if (folder == null)
            return new Dictionary<string, long>();

        try
        {
            return this.Archives.GetSizes(folder, comparison.GetNeededMods());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            this.Monitor.Log($"Couldn't search '{folder}' for original archives, so joining players can't get mods without trusting you:\n{ex}", LogLevel.Warn);
            return new Dictionary<string, long>();
        }
    }

    /// <summary>Get a hash of each settings file for the mods whose settings joining players must use, by unique mod ID and then by file path relative to the mod folder.</summary>
    public Dictionary<string, Dictionary<string, string>> GetConfigHashes()
    {
        Dictionary<string, Dictionary<string, string>> hashes = new(StringComparer.OrdinalIgnoreCase);
        foreach (string modId in this.GetConfigSyncedModIds())
        {
            try
            {
                if (this.TryGetModPaths(modId, out string? directoryPath, out _) && ModFiles.GetConfigHashes(directoryPath) is { Count: > 0 } modHashes)
                    hashes[modId] = modHashes;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                this.Monitor.Log($"Couldn't read the settings for mod '{modId}', so joining players won't be asked to match them:\n{ex}", LogLevel.Warn);
            }
        }
        return hashes;
    }

    /// <summary>Start waiting for a blocked player who has this mod to say they're done, and for their download requests if allowed.</summary>
    /// <param name="playerId">The player's unique ID.</param>
    /// <param name="comparison">The host mods the player doesn't have at the same version.</param>
    /// <param name="allowMods">Whether the player can download the mods they need.</param>
    /// <param name="allowConfig">Whether the player can download the host's settings for the mods whose settings they must use.</param>
    /// <param name="hasJoined">Whether the player is already in the game, rather than blocked from joining.</param>
    public void Open(long playerId, ModComparison comparison, bool allowMods, bool allowConfig, bool hasJoined = false)
    {
        // this can run while SMAPI is handling the player's request to join, when it still needs them in its peer list
        if (this.Sessions.Remove(playerId, out Session? previous))
            previous.Server.DeletePackagesAsync();

        Dictionary<string, SendableMod> mods = new(StringComparer.OrdinalIgnoreCase);
        if (allowConfig)
        {
            foreach (string modId in this.GetConfigSyncedModIds())
            {
                if (this.TryGetModPaths(modId, out string? directoryPath, out string? relativePath))
                    mods[modId] = new SendableMod(directoryPath, relativePath, AllowMod: false, AllowConfig: true);
            }
        }
        if (allowMods)
        {
            string? archivesFolder = this.GetArchivesFolder();
            foreach (ModInfo needed in comparison.GetNeededMods())
            {
                if (this.TryGetModPaths(needed.Id, out string? directoryPath, out string? relativePath))
                    mods[needed.Id] = new SendableMod(directoryPath, relativePath, AllowMod: true, AllowConfig: mods.ContainsKey(needed.Id), ArchivePath: this.FindArchive(archivesFolder, needed));
            }
        }

        this.Sessions[playerId] = new Session(
            Server: new PackageServer(
                mods: mods,
                tempDir: Path.Combine(Path.GetTempPath(), "ModSyncCheck", Guid.NewGuid().ToString("N")),
                logError: message => this.Monitor.Log(message, LogLevel.Error)
            ),
            ModsMatch: comparison.IsMatch,
            HasJoined: hasJoined
        );
    }

    /// <summary>Tell the players in the game if the settings they must use have changed since they were last told, so they can download the new ones.</summary>
    /// <param name="playerIds">The unique IDs of the players in the game who have this mod.</param>
    public void SendChangedSettings(IReadOnlyCollection<long> playerIds)
    {
        if (playerIds.Count == 0)
        {
            this.SentConfigHashes = null;
            return;
        }

        // the players who just joined already compared their settings with these
        Dictionary<string, Dictionary<string, string>> hashes = this.GetConfigHashes();
        if (this.SentConfigHashes == null)
        {
            this.SentConfigHashes = hashes;
            return;
        }
        if (HostSync.IsSame(this.SentConfigHashes, hashes))
            return;

        // wait for a player who's still downloading the previous change, so nobody misses this one
        if (playerIds.Any(this.Sessions.ContainsKey))
            return;

        this.SentConfigHashes = hashes;
        this.Monitor.Log("Your mod settings changed, so the players in your game were told to use the new ones.", LogLevel.Info);
        foreach (long playerId in playerIds)
        {
            this.Open(playerId, new ModComparison(Array.Empty<ModInfo>(), Array.Empty<VersionMismatch>()), allowMods: false, allowConfig: true, hasJoined: true);
            this.Helper.Multiplayer.SendMessage(new SettingsChangedMessage(hashes), MessageTypes.SettingsChanged, modIDs: new[] { this.ModId }, playerIDs: new[] { playerId });
        }
    }

    /// <summary>Handle a blocked player saying their settings match the host's, so they can join on their next attempt.</summary>
    /// <param name="playerId">The player's unique ID.</param>
    public void OnVerified(long playerId)
    {
        // a player whose mods don't match can't skip that by claiming their settings do
        if (!this.Sessions.TryGetValue(playerId, out Session? session) || !session.ModsMatch || session.HasJoined)
            return;

        this.VerifiedPlayers[playerId] = DateTime.UtcNow + HostSync.VerifiedTimeout;
        this.Close(playerId);
    }

    /// <summary>Get whether a player said their settings match the host's since they were last blocked, and forget that they did.</summary>
    /// <param name="playerId">The player's unique ID.</param>
    public bool ConsumeVerified(long playerId)
    {
        return this.VerifiedPlayers.Remove(playerId, out DateTime expiry) && DateTime.UtcNow <= expiry;
    }

    /// <summary>Remove a rejected player from SMAPI's peer list once it's done handling their request to join, so their mods are checked again on their next attempt.</summary>
    /// <param name="playerId">The player's unique ID.</param>
    /// <remarks>SMAPI looks the player up in its peer list right after the game handles their request, and logs an error if they're gone.</remarks>
    public void ForgetPeerLater(long playerId)
    {
        this.PeersToForget.Add(playerId);
    }

    /// <summary>Remove a rejected player from SMAPI's peer list, so their mods are checked again on their next attempt to join.</summary>
    /// <param name="playerId">The player's unique ID.</param>
    public void ForgetPeer(long playerId)
    {
        if (!SmapiInternals.ForgetPeer(playerId))
            this.Monitor.LogOnce("Couldn't access SMAPI's peer list. A blocked player may need the host to restart the game before they can join.", LogLevel.Warn);
    }

    /// <summary>Queue a download request from a blocked player.</summary>
    /// <param name="playerId">The player's unique ID.</param>
    /// <param name="request">The requested data.</param>
    public void OnRequest(long playerId, ChunkRequest request)
    {
        if (this.Sessions.TryGetValue(playerId, out Session? session))
            session.Pending.Enqueue(request);
    }

    /// <summary>Answer queued download requests whose packages are ready.</summary>
    public void Update()
    {
        if (this.PeersToForget.Count > 0)
        {
            foreach (long playerId in this.PeersToForget)
                this.ForgetPeer(playerId);
            this.PeersToForget.Clear();
        }

        foreach ((long playerId, Session session) in this.Sessions)
        {
            if (session.Pending.Count == 0)
                continue;
            session.LastActivity = DateTime.UtcNow;

            for (int sent = 0; sent < HostSync.MaxResponsesPerTick && session.Pending.TryPeek(out ChunkRequest? request); sent++)
            {
                ChunkResponse? response = session.Server.TryGetResponse(request);
                if (response == null)
                    break; // still packaging

                session.Pending.Dequeue();
                this.Helper.Multiplayer.SendMessage(response, MessageTypes.Chunk, modIDs: new[] { this.ModId }, playerIDs: new[] { playerId });
            }
        }
    }

    /// <summary>Stop waiting for players who haven't asked for anything in a while.</summary>
    public void CloseExpired()
    {
        if (this.Sessions.Count == 0)
            return;

        DateTime cutoff = DateTime.UtcNow - HostSync.SessionTimeout;
        foreach (long playerId in this.Sessions.Where(p => p.Value.LastActivity < cutoff).Select(p => p.Key).ToArray())
            this.Close(playerId);
    }

    /// <summary>Stop waiting for download requests from a player.</summary>
    /// <param name="playerId">The player's unique ID.</param>
    public void Close(long playerId)
    {
        if (!this.Sessions.Remove(playerId, out Session? session))
            return;

        if (!session.HasJoined)
            this.ForgetPeer(playerId);
        session.Server.DeletePackagesAsync();
    }

    /// <summary>Stop waiting for download requests from all players.</summary>
    public void CloseAll()
    {
        foreach (long playerId in this.Sessions.Keys.ToArray())
            this.Close(playerId);
        this.VerifiedPlayers.Clear();
        this.PeersToForget.Clear();
        this.SentConfigHashes = null;
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Get whether two sets of settings hashes are the same.</summary>
    private static bool IsSame(Dictionary<string, Dictionary<string, string>> left, Dictionary<string, Dictionary<string, string>> right)
    {
        return
            left.Count == right.Count
            && left.All(mod =>
                right.TryGetValue(mod.Key, out Dictionary<string, string>? files)
                && files.Count == mod.Value.Count
                && mod.Value.All(file => files.TryGetValue(file.Key, out string? hash) && hash == file.Value)
            );
    }

    /// <summary>Get the unique IDs of the installed mods whose settings joining players must use if they have the mod.</summary>
    private IEnumerable<string> GetConfigSyncedModIds()
    {
        return this.Helper.ModRegistry.GetAll()
            .Select(mod => mod.Manifest.UniqueID)
            .Where(id => this.Config.IsConfigSynced(id) && !string.Equals(id, this.ModId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Get the full path to the folder with the original archives the host downloaded its mods in, if it can be found.</summary>
    private string? GetArchivesFolder()
    {
        try
        {
            string? modsRoot = SmapiInternals.GetModsRoot(this.Helper.ModRegistry.Get(this.ModId)!);
            return modsRoot != null ? ModArchives.GetFolderPath(modsRoot, this.Config.ArchivesFolder) : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            this.Monitor.LogOnce($"The archives folder '{this.Config.ArchivesFolder}' in the settings isn't a valid path, so mod folders are sent instead: {ex.Message}", LogLevel.Warn);
            return null;
        }
    }

    /// <summary>Get the original archive the host downloaded a mod in, if it's in the archives folder.</summary>
    /// <param name="folder">The full path to the archives folder, if known.</param>
    /// <param name="mod">The host's mod.</param>
    private string? FindArchive(string? folder, ModInfo mod)
    {
        if (folder == null)
            return null;

        try
        {
            string? path = this.Archives.Find(folder, mod.Id, mod.Version);
            this.Monitor.Log(path != null
                ? $"Found the original archive for {mod.Name} {mod.Version}: {path}"
                : $"No original archive for {mod.Name} {mod.Version} in '{folder}', so its mod folder is sent instead."
            );
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            this.Monitor.Log($"Couldn't search '{folder}' for the original archive of {mod.Name}, so its mod folder is sent instead:\n{ex}", LogLevel.Warn);
            return null;
        }
    }

    /// <summary>Get the folder paths for an installed mod, if it can be sent.</summary>
    /// <param name="modId">The unique mod ID.</param>
    /// <param name="directoryPath">The full path to the mod folder.</param>
    /// <param name="relativePath">The mod folder path relative to the mods folder.</param>
    private bool TryGetModPaths(string modId, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? directoryPath, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? relativePath)
    {
        IModInfo? mod = this.Helper.ModRegistry.Get(modId);
        directoryPath = mod != null ? SmapiInternals.GetDirectoryPath(mod) : null;
        relativePath = mod != null ? SmapiInternals.GetRelativeDirectoryPath(mod) : null;
        return directoryPath != null && relativePath != null && Directory.Exists(directoryPath);
    }

    /// <summary>A player who has this mod and hasn't said they're done downloading yet.</summary>
    /// <param name="Server">Packages and serves the mods and settings the player is allowed to download.</param>
    /// <param name="ModsMatch">Whether the player has all the required mods at the right versions, so they were only blocked to compare settings.</param>
    /// <param name="HasJoined">Whether the player is in the game and getting changed settings, rather than blocked from joining.</param>
    private record Session(PackageServer Server, bool ModsMatch, bool HasJoined)
    {
        /// <summary>The download requests waiting to be answered.</summary>
        public Queue<ChunkRequest> Pending { get; } = new();

        /// <summary>When the player last asked for something.</summary>
        public DateTime LastActivity { get; set; } = DateTime.UtcNow;
    }
}

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

    /// <summary>Get a hash of each settings file for the required mods, by unique mod ID and then by file path relative to the mod folder.</summary>
    public Dictionary<string, Dictionary<string, string>> GetConfigHashes()
    {
        Dictionary<string, Dictionary<string, string>> hashes = new(StringComparer.OrdinalIgnoreCase);
        foreach (string modId in this.GetRequiredModIds())
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
    /// <param name="allowConfig">Whether the player can download the host's settings for the required mods.</param>
    public void Open(long playerId, ModComparison comparison, bool allowMods, bool allowConfig)
    {
        this.Close(playerId);

        Dictionary<string, SendableMod> mods = new(StringComparer.OrdinalIgnoreCase);
        if (allowMods)
        {
            foreach (ModInfo needed in comparison.GetNeededMods())
            {
                if (this.TryGetModPaths(needed.Id, out string? directoryPath, out string? relativePath))
                    mods[needed.Id] = new SendableMod(directoryPath, relativePath, AllowMod: true, AllowConfig: allowConfig);
            }
        }
        if (allowConfig)
        {
            foreach (string modId in this.GetRequiredModIds())
            {
                if (!mods.ContainsKey(modId) && this.TryGetModPaths(modId, out string? directoryPath, out string? relativePath))
                    mods[modId] = new SendableMod(directoryPath, relativePath, AllowMod: false, AllowConfig: true);
            }
        }

        this.Sessions[playerId] = new Session(
            Server: new PackageServer(
                mods: mods,
                tempDir: Path.Combine(Path.GetTempPath(), "ModSyncCheck", Guid.NewGuid().ToString("N")),
                logError: message => this.Monitor.Log(message, LogLevel.Error)
            ),
            ModsMatch: comparison.IsMatch
        );
    }

    /// <summary>Handle a blocked player saying their settings match the host's, so they can join on their next attempt.</summary>
    /// <param name="playerId">The player's unique ID.</param>
    public void OnVerified(long playerId)
    {
        // a player whose mods don't match can't skip that by claiming their settings do
        if (!this.Sessions.TryGetValue(playerId, out Session? session) || !session.ModsMatch)
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

        this.ForgetPeer(playerId);
        session.Server.DeletePackagesAsync();
    }

    /// <summary>Stop waiting for download requests from all players.</summary>
    public void CloseAll()
    {
        foreach (long playerId in this.Sessions.Keys.ToArray())
            this.Close(playerId);
        this.VerifiedPlayers.Clear();
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Get the unique IDs of the installed mods which joining players must have.</summary>
    private IEnumerable<string> GetRequiredModIds()
    {
        return this.Helper.ModRegistry.GetAll()
            .Select(mod => mod.Manifest.UniqueID)
            .Where(id => !this.Config.IsIgnored(id) && !string.Equals(id, this.ModId, StringComparison.OrdinalIgnoreCase));
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

    /// <summary>A blocked player who has this mod and hasn't said they're done yet.</summary>
    /// <param name="Server">Packages and serves the mods and settings the player is allowed to download.</param>
    /// <param name="ModsMatch">Whether the player has all the required mods at the right versions, so they were only blocked to compare settings.</param>
    private record Session(PackageServer Server, bool ModsMatch)
    {
        /// <summary>The download requests waiting to be answered.</summary>
        public Queue<ChunkRequest> Pending { get; } = new();

        /// <summary>When the player last asked for something.</summary>
        public DateTime LastActivity { get; set; } = DateTime.UtcNow;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StardewModdingAPI;

namespace ModSyncCheck;

/// <summary>When hosting, sends the host's mods to blocked players who ask for them.</summary>
internal class HostSync
{
    /*********
    ** Fields
    *********/
    /// <summary>How long a blocked player can go without asking for anything before the host stops waiting for them.</summary>
    private static readonly TimeSpan SessionTimeout = TimeSpan.FromMinutes(3);

    /// <summary>The maximum number of requests to answer per player each tick.</summary>
    private const int MaxResponsesPerTick = 16;

    /// <summary>The SMAPI API for this mod.</summary>
    private readonly IModHelper Helper;

    /// <summary>Writes messages to the SMAPI log.</summary>
    private readonly IMonitor Monitor;

    /// <summary>This mod's unique ID.</summary>
    private readonly string ModId;

    /// <summary>The blocked players who have this mod and haven't said they're done yet, by player ID.</summary>
    private readonly Dictionary<long, Session> Sessions = new();


    /*********
    ** Public methods
    *********/
    /// <summary>Construct an instance.</summary>
    /// <param name="helper">The SMAPI API for this mod.</param>
    /// <param name="monitor">Writes messages to the SMAPI log.</param>
    /// <param name="modId">This mod's unique ID.</param>
    public HostSync(IModHelper helper, IMonitor monitor, string modId)
    {
        this.Helper = helper;
        this.Monitor = monitor;
        this.ModId = modId;
    }

    /// <summary>Get the approximate total size of the mods a player needs, counting only those which can be sent.</summary>
    /// <param name="comparison">The differences between the player's mods and the host's.</param>
    public long GetDownloadSize(ModComparison comparison)
    {
        long size = 0;
        foreach (SendableMod mod in this.GetSendableMods(comparison).Values)
        {
            try
            {
                size += ModFiles.GetSize(mod.DirectoryPath);
            }
            catch (IOException) { }
        }
        return size;
    }

    /// <summary>Start waiting for a blocked player who has this mod to say they're done, and for their download requests if allowed.</summary>
    /// <param name="playerId">The player's unique ID.</param>
    /// <param name="comparison">The differences between the player's mods and the host's.</param>
    /// <param name="allowDownloads">Whether the player can download the mods they need.</param>
    public void Open(long playerId, ModComparison comparison, bool allowDownloads)
    {
        this.Close(playerId);
        this.Sessions[playerId] = new Session(new PackageServer(
            mods: allowDownloads ? this.GetSendableMods(comparison) : new Dictionary<string, SendableMod>(),
            tempDir: Path.Combine(Path.GetTempPath(), "ModSyncCheck", Guid.NewGuid().ToString("N")),
            logError: message => this.Monitor.Log(message, LogLevel.Error)
        ));
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
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Get the mods a player needs which can be sent, by unique ID.</summary>
    /// <param name="comparison">The differences between the player's mods and the host's.</param>
    private Dictionary<string, SendableMod> GetSendableMods(ModComparison comparison)
    {
        Dictionary<string, SendableMod> mods = new(StringComparer.OrdinalIgnoreCase);
        foreach (ModInfo needed in comparison.GetNeededMods())
        {
            IModInfo? mod = this.Helper.ModRegistry.Get(needed.Id);
            string? directoryPath = mod != null ? SmapiInternals.GetDirectoryPath(mod) : null;
            string? relativePath = mod != null ? SmapiInternals.GetRelativeDirectoryPath(mod) : null;

            if (directoryPath != null && relativePath != null && Directory.Exists(directoryPath))
                mods[needed.Id] = new SendableMod(directoryPath, relativePath);
        }
        return mods;
    }

    /// <summary>A blocked player who has this mod and hasn't said they're done yet.</summary>
    /// <param name="Server">Packages and serves the mods the player is allowed to download.</param>
    private record Session(PackageServer Server)
    {
        /// <summary>The download requests waiting to be answered.</summary>
        public Queue<ChunkRequest> Pending { get; } = new();

        /// <summary>When the player last asked for something.</summary>
        public DateTime LastActivity { get; set; } = DateTime.UtcNow;
    }
}

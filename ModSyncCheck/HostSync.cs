using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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

    /// <summary>The blocked players who can download mods, by player ID.</summary>
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
        foreach (HostMod mod in this.GetSendableMods(comparison).Values)
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
        this.Sessions[playerId] = new Session(
            Mods: allowDownloads ? this.GetSendableMods(comparison) : new Dictionary<string, HostMod>(),
            TempDir: Path.Combine(Path.GetTempPath(), "ModSyncCheck", Guid.NewGuid().ToString("N"))
        );
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
                ChunkResponse? response = this.TryGetResponse(session, request);
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

        // delete packages once they're no longer being written
        Task.WhenAll(session.Packages.Values).ContinueWith(_ =>
        {
            try
            {
                if (Directory.Exists(session.TempDir))
                    Directory.Delete(session.TempDir, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        });
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
    private Dictionary<string, HostMod> GetSendableMods(ModComparison comparison)
    {
        Dictionary<string, HostMod> mods = new(StringComparer.OrdinalIgnoreCase);
        foreach (ModInfo needed in comparison.GetNeededMods())
        {
            IModInfo? mod = this.Helper.ModRegistry.Get(needed.Id);
            string? directoryPath = mod != null ? SmapiInternals.GetDirectoryPath(mod) : null;
            string? relativePath = mod != null ? SmapiInternals.GetRelativeDirectoryPath(mod) : null;

            if (directoryPath != null && relativePath != null && Directory.Exists(directoryPath))
                mods[needed.Id] = new HostMod(directoryPath, relativePath);
        }
        return mods;
    }

    /// <summary>Get the response to a download request, or <c>null</c> if the mod is still being packaged.</summary>
    /// <param name="session">The player's download session.</param>
    /// <param name="request">The requested data.</param>
    private ChunkResponse? TryGetResponse(Session session, ChunkRequest request)
    {
        if (!session.Mods.TryGetValue(request.ModId, out HostMod? mod))
            return HostSync.Error(request, "The host can't send this mod.");

        if (!session.Packages.TryGetValue(request.ModId, out Task<string>? package))
        {
            string zipPath = Path.Combine(session.TempDir, $"{session.Packages.Count}.zip");
            session.Packages[request.ModId] = package = Task.Run(() =>
            {
                ModFiles.CreateZip(mod.DirectoryPath, zipPath);
                return zipPath;
            });
        }

        if (!package.IsCompleted)
            return null;

        try
        {
            using FileStream stream = File.OpenRead(package.GetAwaiter().GetResult());
            if (request.Offset < 0 || request.Offset >= stream.Length)
                return HostSync.Error(request, "Invalid download request.");

            byte[] data = new byte[Math.Min(ChunkResponse.MaxDataSize, stream.Length - request.Offset)];
            stream.Seek(request.Offset, SeekOrigin.Begin);
            for (int read = 0; read < data.Length;)
            {
                int count = stream.Read(data, read, data.Length - read);
                if (count == 0)
                    throw new EndOfStreamException();
                read += count;
            }

            return new ChunkResponse(request.ModId, request.Offset, stream.Length, mod.RelativePath, data, Error: null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            this.Monitor.Log($"Couldn't send mod '{request.ModId}' to a joining player:\n{ex}", LogLevel.Error);
            return HostSync.Error(request, "The host couldn't read the mod files.");
        }
    }

    /// <summary>Get a response which tells the player a mod can't be downloaded.</summary>
    /// <param name="request">The requested data.</param>
    /// <param name="error">The reason the mod can't be downloaded.</param>
    private static ChunkResponse Error(ChunkRequest request, string error)
    {
        return new ChunkResponse(request.ModId, request.Offset, 0, "", Array.Empty<byte>(), error);
    }

    /// <summary>A host mod which can be sent to a player.</summary>
    /// <param name="DirectoryPath">The full path to the mod folder.</param>
    /// <param name="RelativePath">The mod folder path relative to the mods folder.</param>
    private record HostMod(string DirectoryPath, string RelativePath);

    /// <summary>A blocked player who can download mods.</summary>
    /// <param name="Mods">The mods the player is allowed to download, by unique ID.</param>
    /// <param name="TempDir">The folder containing the packaged mods for this player.</param>
    private record Session(Dictionary<string, HostMod> Mods, string TempDir)
    {
        /// <summary>The packaged mods which were created or are being created, by unique ID. Each task returns the package path.</summary>
        public Dictionary<string, Task<string>> Packages { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The download requests waiting to be answered.</summary>
        public Queue<ChunkRequest> Pending { get; } = new();

        /// <summary>When the player last asked for something.</summary>
        public DateTime LastActivity { get; set; } = DateTime.UtcNow;
    }
}

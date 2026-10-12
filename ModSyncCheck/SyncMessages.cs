namespace ModSyncCheck;

/// <summary>The mod message types sent between the host and a joining player.</summary>
internal static class MessageTypes
{
    /// <summary>Sent by the host with a <see cref="BlockedMessage"/> to tell a joining player why they can't join.</summary>
    public const string Blocked = "Blocked";

    /// <summary>Sent by a blocked player with a <see cref="ChunkRequest"/> to download part of a packaged mod.</summary>
    public const string ChunkRequest = "ChunkRequest";

    /// <summary>Sent by the host with a <see cref="ChunkResponse"/> in reply to a <see cref="ChunkRequest"/>.</summary>
    public const string Chunk = "Chunk";

    /// <summary>Sent by a blocked player with a <see cref="SyncEndedMessage"/> when they're done talking to the host.</summary>
    public const string SyncEnded = "SyncEnded";
}

/// <summary>Tells a joining player why they can't join.</summary>
/// <param name="Comparison">The differences between the player's mods and the host's.</param>
/// <param name="CanDownload">Whether the host lets the player download the mods they need.</param>
/// <param name="DownloadBytes">The approximate total size of the mods the player needs.</param>
internal record BlockedMessage(ModComparison Comparison, bool CanDownload, long DownloadBytes);

/// <summary>Asks the host for part of a packaged mod.</summary>
/// <param name="ModId">The unique mod ID.</param>
/// <param name="Offset">The position in the package to read from.</param>
internal record ChunkRequest(string ModId, long Offset);

/// <summary>Part of a packaged mod.</summary>
/// <param name="ModId">The unique mod ID.</param>
/// <param name="Offset">The position in the package the data was read from.</param>
/// <param name="TotalBytes">The total size of the package.</param>
/// <param name="RelativePath">The mod's folder path relative to the host's mods folder.</param>
/// <param name="Data">The package data.</param>
/// <param name="Error">The reason the mod can't be downloaded, if applicable.</param>
internal record ChunkResponse(string ModId, long Offset, long TotalBytes, string RelativePath, byte[] Data, string? Error)
{
    /// <summary>The maximum number of bytes in one chunk. This is kept small enough to fit in a single network message on every connection type.</summary>
    public const int MaxDataSize = 64 * 1024;
}

/// <summary>Tells the host that a blocked player is done.</summary>
internal record SyncEndedMessage;

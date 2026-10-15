using System.Collections.Generic;

namespace ModSyncCheck;

/// <summary>The mod message types sent between the host and a joining player.</summary>
internal static class MessageTypes
{
    /// <summary>Sent by the host with a <see cref="BlockedMessage"/> to tell a joining player why they can't join yet.</summary>
    public const string Blocked = "Blocked";

    /// <summary>Sent by a blocked player with a <see cref="VerifiedMessage"/> to say their mods and settings match after all, so they can join.</summary>
    public const string Verified = "Verified";

    /// <summary>Sent by a blocked player with a <see cref="ChunkRequest"/> to download part of a packaged mod.</summary>
    public const string ChunkRequest = "ChunkRequest";

    /// <summary>Sent by the host with a <see cref="ChunkResponse"/> in reply to a <see cref="ChunkRequest"/>.</summary>
    public const string Chunk = "Chunk";

    /// <summary>Sent by a blocked player with a <see cref="SyncEndedMessage"/> when they're done talking to the host.</summary>
    public const string SyncEnded = "SyncEnded";

    /// <summary>Sent by the host with a <see cref="SettingsChangedMessage"/> to players in its game when the settings they must use change.</summary>
    public const string SettingsChanged = "SettingsChanged";
}

/// <summary>Tells a joining player why they can't join yet.</summary>
/// <param name="Comparison">The host mods the player doesn't have at the same version.</param>
/// <param name="CanDownload">Whether the host lets the player download the mods and settings they need.</param>
/// <param name="DownloadBytes">The approximate total size of the mods the player needs.</param>
/// <param name="ConfigHashes">If the host requires players to use its mod settings, a hash of each settings file by unique mod ID and then by file path relative to the mod folder. The host can't see the player's settings, so the player compares these with their own. This is <c>null</c> if the host doesn't require its settings.</param>
internal record BlockedMessage(ModComparison Comparison, bool CanDownload, long DownloadBytes, Dictionary<string, Dictionary<string, string>>? ConfigHashes = null);

/// <summary>Tells the host that a blocked player's settings match the host's, so they can join.</summary>
internal record VerifiedMessage;

/// <summary>Asks the host for part of a packaged mod.</summary>
/// <param name="ModId">The unique mod ID.</param>
/// <param name="Offset">The position in the package to read from.</param>
/// <param name="ConfigOnly">Whether to get only the host's settings for the mod, rather than the mod itself.</param>
/// <param name="AcceptArchive">Whether the player can handle the original archive the host downloaded the mod in, if the host has it, rather than a package of the host's mod folder.</param>
internal record ChunkRequest(string ModId, long Offset, bool ConfigOnly = false, bool AcceptArchive = false);

/// <summary>Part of a packaged mod.</summary>
/// <param name="ModId">The unique mod ID.</param>
/// <param name="Offset">The position in the package the data was read from.</param>
/// <param name="TotalBytes">The total size of the package.</param>
/// <param name="RelativePath">The mod's folder path relative to the host's mods folder.</param>
/// <param name="Data">The package data.</param>
/// <param name="Error">The reason the mod can't be downloaded, if applicable.</param>
/// <param name="IsArchive">Whether the package is the original archive the host downloaded the mod in, which the player checks against Nexus Mods, rather than a package of the host's mod folder.</param>
internal record ChunkResponse(string ModId, long Offset, long TotalBytes, string RelativePath, byte[] Data, string? Error, bool IsArchive = false)
{
    /// <summary>The maximum number of bytes in one chunk. This is kept small enough to fit in a single network message on every connection type.</summary>
    public const int MaxDataSize = 64 * 1024;
}

/// <summary>Tells a player in the host's game that the settings they must use changed, so they can download the new ones.</summary>
/// <param name="ConfigHashes">A hash of each settings file the player must use, by unique mod ID and then by file path relative to the mod folder.</param>
internal record SettingsChangedMessage(Dictionary<string, Dictionary<string, string>> ConfigHashes);

/// <summary>Tells the host that a blocked player is done.</summary>
internal record SyncEndedMessage;

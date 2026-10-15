namespace ModSyncCheck;

/// <summary>What to do with a mod downloaded from the host, after checking it with Nexus Mods if it's an original archive.</summary>
internal enum ArchiveDecision
{
    /// <summary>Unpack and install it.</summary>
    Install,

    /// <summary>Unpack and install it, telling the player it couldn't be checked.</summary>
    InstallUnchecked,

    /// <summary>Unpack and install it, telling the player it doesn't match the mod's files on Nexus Mods.</summary>
    InstallModified,

    /// <summary>Delete it without unpacking it.</summary>
    Reject
}

/// <summary>Decides whether a mod downloaded from the host can be installed.</summary>
internal static class ArchiveCheckPolicy
{
    /// <summary>Decide what to do with a downloaded mod.</summary>
    /// <param name="result">The result of checking the original archive with Nexus Mods, or <c>null</c> if the host sent a package of its mod folder instead.</param>
    /// <param name="trustHost">Whether the player trusts the host. If not, only an archive confirmed by Nexus Mods is installed.</param>
    /// <param name="hasNexusId">Whether the mod has a Nexus Mods ID, without which Nexus Mods can't confirm the file is from its own page.</param>
    public static ArchiveDecision Decide(NexusCheckResult? result, bool trustHost, bool hasNexusId)
    {
        if (!trustHost)
            return result == NexusCheckResult.Verified && hasNexusId ? ArchiveDecision.Install : ArchiveDecision.Reject;

        // a player who trusts the host gets its files, even if they were changed
        return result switch
        {
            null or NexusCheckResult.Verified => ArchiveDecision.Install,
            NexusCheckResult.Unavailable => ArchiveDecision.InstallUnchecked,
            _ => ArchiveDecision.InstallModified
        };
    }

    /// <summary>Get whether a check result means the file isn't one of the mod's own files on Nexus Mods, so the host's copy was changed or isn't what it claims to be.</summary>
    /// <param name="result">The result of checking the original archive with Nexus Mods.</param>
    public static bool IsModified(NexusCheckResult? result)
    {
        return result is NexusCheckResult.Unknown or NexusCheckResult.WrongMod;
    }
}

/// <summary>A mod version for which a host sent a file which doesn't match the mod's files on Nexus Mods.</summary>
/// <param name="HostId">The unique ID of the host which sent the file. Another host may have the mod's real file.</param>
/// <param name="Id">The mod's unique ID.</param>
/// <param name="Version">The mod version the host has.</param>
internal record ModifiedArchive(long HostId, string Id, string Version);

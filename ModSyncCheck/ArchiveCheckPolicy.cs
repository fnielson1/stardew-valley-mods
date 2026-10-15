namespace ModSyncCheck;

/// <summary>What to do with a mod downloaded from the host, after checking it with Nexus Mods if it's an original archive.</summary>
internal enum ArchiveDecision
{
    /// <summary>Unpack and install it.</summary>
    Install,

    /// <summary>Unpack and install it, telling the player it couldn't be checked.</summary>
    InstallUnchecked,

    /// <summary>Delete it without unpacking it.</summary>
    Reject
}

/// <summary>Decides whether a mod downloaded from the host can be installed.</summary>
internal static class ArchiveCheckPolicy
{
    /// <summary>Decide what to do with a downloaded mod.</summary>
    /// <param name="result">The result of checking the original archive with Nexus Mods, or <c>null</c> if the host sent a package of its mod folder instead.</param>
    /// <param name="trustHost">Whether the player trusts the host. If not, only an archive Nexus Mods confirms is a file from the mod's own page is installed.</param>
    /// <param name="hasNexusId">Whether the mod has a Nexus Mods ID. Without one, Nexus Mods can only say the file is from some mod's page, not the mod's own, so it isn't confirmed.</param>
    public static ArchiveDecision Decide(NexusCheckResult? result, bool trustHost, bool hasNexusId)
    {
        if (!trustHost)
            return result == NexusCheckResult.Verified && hasNexusId ? ArchiveDecision.Install : ArchiveDecision.Reject;

        return result switch
        {
            null or NexusCheckResult.Verified => ArchiveDecision.Install,
            NexusCheckResult.Unavailable => ArchiveDecision.InstallUnchecked,
            _ => ArchiveDecision.Reject
        };
    }
}

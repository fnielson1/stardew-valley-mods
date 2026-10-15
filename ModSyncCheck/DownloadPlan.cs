using System;
using System.Collections.Generic;
using System.Linq;

namespace ModSyncCheck;

/// <summary>Which of the mods a blocked player needs they'll download from the host.</summary>
/// <param name="Targets">The mods to download.</param>
/// <param name="Bytes">The approximate total size of the mods to download.</param>
/// <param name="Left">The mods the player needs which won't be downloaded.</param>
/// <param name="TrustHost">Whether the player trusts the host, so mods packaged from its mod folders are downloaded and an archive which can't be checked with Nexus Mods is installed anyway.</param>
internal record DownloadPlan(IReadOnlyList<ModInfo> Targets, long Bytes, IReadOnlyList<ModInfo> Left, bool TrustHost)
{
    /// <summary>Get whether the player can download any mods without trusting the host, because the host has original archives for some of them which have a Nexus Mods ID.</summary>
    /// <param name="needed">The mods the player needs.</param>
    /// <param name="archiveSizes">The size of the original archive the host has for each mod, by unique mod ID, or <c>null</c> if the host didn't say.</param>
    public static bool CanSkipTrust(IEnumerable<ModInfo> needed, IReadOnlyDictionary<string, long>? archiveSizes)
    {
        return DownloadPlan.GetArchiveSize(needed, archiveSizes).Any();
    }

    /// <summary>Choose which mods to download.</summary>
    /// <param name="needed">The mods the player needs.</param>
    /// <param name="archiveSizes">The size of the original archive the host has for each mod, by unique mod ID, or <c>null</c> if the host didn't say.</param>
    /// <param name="allBytes">The approximate total size of all the mods the player needs, as the host reported it.</param>
    /// <param name="trustHost">Whether the player said they trust the host. Without archives, this is treated as true, since the player can't download without trusting the host.</param>
    public static DownloadPlan Select(IEnumerable<ModInfo> needed, IReadOnlyDictionary<string, long>? archiveSizes, long allBytes, bool trustHost)
    {
        List<ModInfo> all = needed.ToList();
        if (trustHost || !DownloadPlan.CanSkipTrust(all, archiveSizes))
            return new DownloadPlan(all, allBytes, Array.Empty<ModInfo>(), TrustHost: true);

        Dictionary<string, long> sizes = DownloadPlan.GetArchiveSize(all, archiveSizes).ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        List<ModInfo> targets = all.Where(mod => sizes.ContainsKey(mod.Id)).ToList();
        List<ModInfo> left = all.Where(mod => !sizes.ContainsKey(mod.Id)).ToList();
        return new DownloadPlan(targets, sizes.Values.Sum(), left, TrustHost: false);
    }

    /// <summary>Get the archive size for each needed mod which has one and a Nexus Mods ID.</summary>
    /// <param name="needed">The mods the player needs.</param>
    /// <param name="archiveSizes">The size of the original archive the host has for each mod, by unique mod ID, or <c>null</c> if the host didn't say.</param>
    private static IEnumerable<KeyValuePair<string, long>> GetArchiveSize(IEnumerable<ModInfo> needed, IReadOnlyDictionary<string, long>? archiveSizes)
    {
        if (archiveSizes == null || archiveSizes.Count == 0)
            yield break;

        // the message is read without the host's case-insensitive comparer
        Dictionary<string, long> lookup = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string id, long size) in archiveSizes)
            lookup[id.Trim()] = size;

        // without a Nexus Mods ID, Nexus Mods can't confirm the file is from the mod's own page
        foreach (ModInfo mod in needed)
        {
            if (mod.NexusId > 0 && lookup.TryGetValue(mod.Id.Trim(), out long size))
                yield return new KeyValuePair<string, long>(mod.Id, size);
        }
    }
}

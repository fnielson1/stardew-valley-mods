using System;
using System.Collections.Generic;
using System.Linq;

namespace ModSyncCheck;

/// <summary>Which of the mods a blocked player needs they'll download from the host.</summary>
/// <param name="Targets">The mods to download.</param>
/// <param name="Bytes">The approximate total size of the mods to download.</param>
/// <param name="Left">The mods the player needs which won't be downloaded.</param>
/// <param name="TrustHost">Whether the player trusts the host.</param>
internal record DownloadPlan(IReadOnlyList<ModInfo> Targets, long Bytes, IReadOnlyList<ModInfo> Left, bool TrustHost)
{
    /// <summary>Get whether the player can download any mods without trusting the host.</summary>
    /// <param name="needed">The mods the player needs.</param>
    /// <param name="archiveSizes">The original archive sizes by unique mod ID, or <c>null</c> if the host didn't send them.</param>
    public static bool CanSkipTrust(IEnumerable<ModInfo> needed, IReadOnlyDictionary<string, long>? archiveSizes)
    {
        return DownloadPlan.GetArchiveSize(needed, archiveSizes).Any();
    }

    /// <summary>Choose which mods to download.</summary>
    /// <param name="needed">The mods the player needs.</param>
    /// <param name="archiveSizes">The original archive sizes by unique mod ID, or <c>null</c> if the host didn't send them.</param>
    /// <param name="allBytes">The approximate size of all the needed mods, as the host reported it.</param>
    /// <param name="trustHost">Whether the player trusts the host. This is treated as true if there are no archives to download instead.</param>
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

    /// <summary>Choose only the mods which can't be checked with Nexus Mods, for a player who trusts the host with those but gets the others themselves.</summary>
    /// <param name="needed">The mods the player needs.</param>
    /// <param name="archiveSizes">The original archive sizes by unique mod ID, or <c>null</c> if the host didn't send them.</param>
    /// <param name="allBytes">The approximate size of all the needed mods, as the host reported it. The host doesn't report the size of these mods alone, so this is used as an upper bound.</param>
    public static DownloadPlan SelectUnverifiable(IEnumerable<ModInfo> needed, IReadOnlyDictionary<string, long>? archiveSizes, long allBytes)
    {
        DownloadPlan verifiable = DownloadPlan.Select(needed, archiveSizes, allBytes, trustHost: false);
        return new DownloadPlan(verifiable.Left, allBytes, verifiable.Targets, TrustHost: true);
    }

    /// <summary>Get the archive size for each needed mod which has one and a Nexus Mods ID.</summary>
    /// <param name="needed">The mods the player needs.</param>
    /// <param name="archiveSizes">The original archive sizes by unique mod ID, or <c>null</c> if the host didn't send them.</param>
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

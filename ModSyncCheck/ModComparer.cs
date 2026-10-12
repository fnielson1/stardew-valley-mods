using System;
using System.Collections.Generic;
using System.Linq;

namespace ModSyncCheck;

/// <summary>A mod installed by a player.</summary>
internal record ModInfo(string Id, string Name, string Version);

/// <summary>A mod installed by both the player and the host at different versions.</summary>
internal record VersionMismatch(string Id, string Name, string PlayerVersion, string HostVersion);

/// <summary>The differences between a joining player's mods and the host's mods.</summary>
/// <param name="Missing">Mods the host has which the player doesn't.</param>
/// <param name="Extra">Mods the player has which the host doesn't.</param>
/// <param name="VersionMismatches">Mods both have at different versions.</param>
internal record ModComparison(IReadOnlyList<ModInfo> Missing, IReadOnlyList<ModInfo> Extra, IReadOnlyList<VersionMismatch> VersionMismatches)
{
    /// <summary>The total number of mismatched mods.</summary>
    public int Count => this.Missing.Count + this.Extra.Count + this.VersionMismatches.Count;

    /// <summary>Whether the player's mods match the host's.</summary>
    public bool IsMatch => this.Count == 0;

    /// <summary>Get the host mods the player would need to install or update to match the host.</summary>
    public IEnumerable<ModInfo> GetNeededMods()
    {
        return this.Missing.Concat(this.VersionMismatches.Select(mod => new ModInfo(mod.Id, mod.Name, mod.HostVersion)));
    }
}

/// <summary>Compares the mods installed by a joining player and the host.</summary>
internal static class ModComparer
{
    /// <summary>Get the differences between a joining player's mods and the host's mods.</summary>
    /// <param name="playerMods">The mods installed by the joining player.</param>
    /// <param name="hostMods">The mods installed by the host.</param>
    /// <param name="ignoredIds">The unique mod IDs to leave out of the result.</param>
    public static ModComparison Compare(IEnumerable<ModInfo> playerMods, IEnumerable<ModInfo> hostMods, IEnumerable<string> ignoredIds)
    {
        HashSet<string> ignored = new(ignoredIds.Select(id => id.Trim()), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, ModInfo> player = ModComparer.Index(playerMods, ignored);
        Dictionary<string, ModInfo> host = ModComparer.Index(hostMods, ignored);

        List<ModInfo> missing = new();
        List<VersionMismatch> versionMismatches = new();
        foreach (ModInfo hostMod in host.Values)
        {
            if (!player.TryGetValue(hostMod.Id, out ModInfo? playerMod))
                missing.Add(hostMod);
            else if (!string.Equals(playerMod.Version, hostMod.Version, StringComparison.OrdinalIgnoreCase))
                versionMismatches.Add(new VersionMismatch(hostMod.Id, hostMod.Name, playerMod.Version, hostMod.Version));
        }

        List<ModInfo> extra = player.Values.Where(mod => !host.ContainsKey(mod.Id)).ToList();

        return new ModComparison(
            Missing: missing.OrderBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            Extra: extra.OrderBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            VersionMismatches: versionMismatches.OrderBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase).ToList()
        );
    }

    /// <summary>Get the non-ignored mods by their case-insensitive unique ID.</summary>
    private static Dictionary<string, ModInfo> Index(IEnumerable<ModInfo> mods, HashSet<string> ignored)
    {
        Dictionary<string, ModInfo> lookup = new(StringComparer.OrdinalIgnoreCase);
        foreach (ModInfo mod in mods)
        {
            if (!ignored.Contains(mod.Id))
                lookup[mod.Id] = mod;
        }
        return lookup;
    }
}

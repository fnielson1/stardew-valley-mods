using System;
using System.Collections.Generic;
using System.Linq;

namespace ModSyncCheck;

/// <summary>A mod installed by a player.</summary>
internal record ModInfo(string Id, string Name, string Version);

/// <summary>A mod installed by both the player and the host at different versions.</summary>
internal record VersionMismatch(string Id, string Name, string PlayerVersion, string HostVersion);

/// <summary>The host mods which a joining player doesn't have at the same version. Mods which only the player has are never a mismatch.</summary>
/// <param name="Missing">Mods the host has which the player doesn't.</param>
/// <param name="VersionMismatches">Mods both have at different versions.</param>
internal record ModComparison(IReadOnlyList<ModInfo> Missing, IReadOnlyList<VersionMismatch> VersionMismatches)
{
    /// <summary>The total number of mismatched mods.</summary>
    public int Count => this.Missing.Count + this.VersionMismatches.Count;

    /// <summary>Whether the player has all the host's mods at the same versions.</summary>
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
    /// <summary>Get the host mods which a joining player doesn't have at the same version.</summary>
    /// <param name="playerMods">The mods installed by the joining player.</param>
    /// <param name="hostMods">The mods installed by the host.</param>
    /// <param name="ignoredIds">The unique IDs of host mods the player doesn't need.</param>
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

        return new ModComparison(
            Missing: missing.OrderBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            VersionMismatches: versionMismatches.OrderBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase).ToList()
        );
    }

    /// <summary>Add a mod to a comparison as a version mismatch if the player has a different major version than the host, even if the mod is ignored.</summary>
    /// <param name="comparison">The mod differences.</param>
    /// <param name="hostMod">The host's mod.</param>
    /// <param name="hostMajorVersion">The major part of the host's version.</param>
    /// <param name="playerVersion">The player's version of the mod.</param>
    /// <param name="playerMajorVersion">The major part of <paramref name="playerVersion"/>.</param>
    public static ModComparison RequireSameMajorVersion(ModComparison comparison, ModInfo hostMod, int hostMajorVersion, string playerVersion, int playerMajorVersion)
    {
        if (hostMajorVersion == playerMajorVersion || comparison.VersionMismatches.Any(mod => string.Equals(mod.Id, hostMod.Id, StringComparison.OrdinalIgnoreCase)))
            return comparison;

        return comparison with
        {
            VersionMismatches = comparison.VersionMismatches
                .Append(new VersionMismatch(hostMod.Id, hostMod.Name, playerVersion, hostMod.Version))
                .OrderBy(mod => mod.Name, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
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

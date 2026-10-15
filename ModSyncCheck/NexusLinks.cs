using System;
using System.Collections.Generic;
using System.Globalization;

namespace ModSyncCheck;

/// <summary>A link to the page from which a player can download a mod.</summary>
/// <param name="Name">The mod name to show.</param>
/// <param name="Url">The page address.</param>
/// <param name="Entry">The line of the dialog text which lists the mod, in which its name is shown as the link.</param>
/// <param name="ExpandSection">For a link which shows the rest of a section of the list instead of opening a page, the section to show in full; otherwise -1.</param>
internal record ModLink(string Name, string Url, string Entry, int ExpandSection = -1);

/// <summary>Builds links to mod pages on Nexus Mods.</summary>
internal static class NexusLinks
{
    /// <summary>The prefix for a Nexus Mods update key in a mod's manifest, like <c>Nexus:2400</c>.</summary>
    private const string UpdateKeyPrefix = "Nexus:";

    /// <summary>Get the Nexus Mods ID from a mod's update keys, if it has one.</summary>
    /// <param name="updateKeys">The update keys in the mod's manifest.</param>
    public static int? GetModId(IEnumerable<string>? updateKeys)
    {
        if (updateKeys == null)
            return null;

        foreach (string key in updateKeys)
        {
            string trimmed = key?.Trim() ?? "";
            if (!trimmed.StartsWith(NexusLinks.UpdateKeyPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            // a key can name a specific file, like Nexus:2400@ContentPatcher
            string id = trimmed[NexusLinks.UpdateKeyPrefix.Length..];
            int subkey = id.IndexOf('@');
            if (subkey >= 0)
                id = id[..subkey];

            if (int.TryParse(id.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int modId) && modId > 0)
                return modId;
        }

        return null;
    }

    /// <summary>Get the address of a mod's page on Nexus Mods.</summary>
    /// <param name="modId">The Nexus Mods ID.</param>
    /// <remarks>The address is always built from a number, so a host can't send a player to any other site.</remarks>
    public static string GetModUrl(int modId)
    {
        return $"https://www.nexusmods.com/stardewvalley/mods/{modId.ToString(CultureInfo.InvariantCulture)}";
    }
}

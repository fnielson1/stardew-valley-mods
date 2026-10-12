using System;
using System.Collections.Generic;

namespace ModSyncCheck;

/// <summary>The mod settings, editable in <c>config.json</c> or through Generic Mod Config Menu.</summary>
internal class ModConfig
{
    /// <summary>The unique IDs of mods which don't need to match, e.g. client-only mods which are safe to mismatch.</summary>
    public List<string> IgnoredModIds { get; set; } = new()
    {
        "fnielson.ModSyncCheck",
        "SMAPI.ConsoleCommands",
        "SMAPI.SaveBackup",
        "spacechase0.GenericModConfigMenu",
        "Bouhm.NPCMapLocations",
        "Pathoschild.LookupAnything",
        "Annosz.UiInfoSuite2"
    };

    /// <summary>The maximum number of mismatched mods to list in the in-game warning. The SMAPI log always lists all of them.</summary>
    public int MaxModsInPopup { get; set; } = 10;

    /// <summary>When hosting, whether blocked players who have this mod can download the mods they need from you.</summary>
    public bool AllowModDownloads { get; set; } = true;

    /// <summary>When hosting, whether players who have this mod use your settings for the required mods while they're in your game. Their settings files aren't changed.</summary>
    public bool CopyConfigFromHost { get; set; } = false;

    /// <summary>Get whether a mod doesn't need to match.</summary>
    /// <param name="modId">The unique mod ID.</param>
    public bool IsIgnored(string modId)
    {
        return this.IgnoredModIds.Exists(id => ModConfig.IsSameId(id, modId));
    }

    /// <summary>Set whether a mod doesn't need to match.</summary>
    /// <param name="modId">The unique mod ID.</param>
    /// <param name="ignored">Whether to ignore the mod.</param>
    public void SetIgnored(string modId, bool ignored)
    {
        if (!ignored)
            this.IgnoredModIds.RemoveAll(id => ModConfig.IsSameId(id, modId));
        else if (!this.IsIgnored(modId))
            this.IgnoredModIds.Add(modId);
    }

    /// <summary>Reset all settings to their default values.</summary>
    public void Reset()
    {
        ModConfig defaults = new();
        this.IgnoredModIds = defaults.IgnoredModIds;
        this.MaxModsInPopup = defaults.MaxModsInPopup;
        this.AllowModDownloads = defaults.AllowModDownloads;
        this.CopyConfigFromHost = defaults.CopyConfigFromHost;
    }

    /// <summary>Get whether two unique mod IDs are equivalent.</summary>
    private static bool IsSameId(string left, string right)
    {
        return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}

using System.Collections.Generic;

namespace ModSyncCheck;

/// <summary>The mod settings, editable in <c>config.json</c>.</summary>
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
}

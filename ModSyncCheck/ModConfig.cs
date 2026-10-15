using System;
using System.Collections.Generic;
using System.Linq;

namespace ModSyncCheck;

/// <summary>The mod settings, editable in <c>config.json</c> or through Generic Mod Config Menu.</summary>
internal class ModConfig
{
    /// <summary>When hosting, whether to check joining players' mods and settings at all. If this is off, everyone can join as if this mod wasn't installed.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>When hosting, whether to block players who don't have this mod installed, even if their other mods match.</summary>
    public bool RequireThisMod { get; set; } = true;

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

    /// <summary>When hosting, the folder with the original <c>.zip</c> files you downloaded your mods in, like those from Nexus Mods. A mod found here is sent to players unchanged, so they can check it against Nexus Mods. This can be a full path or one relative to the mods folder; if it's empty, the <c>.ModSyncCheck</c> folder in the mods folder is used.</summary>
    public string ArchivesFolder { get; set; } = "";

    /// <summary>When hosting, the unique IDs of ignored mods for which players who have both this mod and that one use your settings while they're in your game. Their settings files aren't changed.</summary>
    public List<string> SyncedConfigModIds { get; set; } = new();

    /// <summary>When hosting, the unique IDs of required mods for which players keep their own settings. Players use your settings for every other required mod.</summary>
    public List<string> UnsyncedConfigModIds { get; set; } = new();

    /// <summary>The unique IDs of mods whose settings are never sent to players, whatever the settings say.</summary>
    /// <remarks>These are built in rather than saved in <c>config.json</c>, so a mod added here in an update also applies to players who already have a settings file.</remarks>
    public static readonly IReadOnlyList<string> BuiltInNeverSyncedConfigModIds = new[]
    {
        "DIGUS.CustomCaskMod",
        "cantorsdust.TimeSpeed",
        "Pathoschild.ContentPatcher",
        "SMAPI.ConsoleCommands",
        "bcmpinc.HarvestWithScythe",
        "spacechase0.GenericModConfigMenu",
        "bcmpinc.MovementSpeed",
        "SMAPI.SaveBackup"
    };

    /// <summary>The unique IDs of more mods whose settings are never sent to players, in addition to <see cref="BuiltInNeverSyncedConfigModIds"/>. These can only be changed by editing <c>config.json</c>.</summary>
    public List<string> NeverSyncedConfigModIds { get; set; } = new();

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

    /// <summary>Get whether joining players who have a mod use the host's settings for it. Unless the host chose otherwise, that's the case for mods which must match.</summary>
    /// <param name="modId">The unique mod ID.</param>
    public bool IsConfigSynced(string modId)
    {
        if (!this.CanSyncConfig(modId))
            return false;

        return this.IsIgnored(modId)
            ? this.SyncedConfigModIds.Exists(id => ModConfig.IsSameId(id, modId))
            : !this.UnsyncedConfigModIds.Exists(id => ModConfig.IsSameId(id, modId));
    }

    /// <summary>Get whether the host's settings for a mod are allowed to be sent to players.</summary>
    /// <param name="modId">The unique mod ID.</param>
    public bool CanSyncConfig(string modId)
    {
        return
            !ModConfig.BuiltInNeverSyncedConfigModIds.Any(id => ModConfig.IsSameId(id, modId))
            && !this.NeverSyncedConfigModIds.Exists(id => ModConfig.IsSameId(id, modId));
    }

    /// <summary>Set whether joining players who have a mod use the host's settings for it. This depends on whether the mod is ignored, so call <see cref="SetIgnored"/> first when changing both.</summary>
    /// <param name="modId">The unique mod ID.</param>
    /// <param name="synced">Whether to use the host's settings.</param>
    public void SetConfigSynced(string modId, bool synced)
    {
        if (!this.CanSyncConfig(modId))
            return;

        // only a choice which differs from the default is kept
        this.SyncedConfigModIds.RemoveAll(id => ModConfig.IsSameId(id, modId));
        this.UnsyncedConfigModIds.RemoveAll(id => ModConfig.IsSameId(id, modId));
        if (synced != this.IsConfigSynced(modId))
            (synced ? this.SyncedConfigModIds : this.UnsyncedConfigModIds).Add(modId);
    }

    /// <summary>Reset all settings to their default values.</summary>
    public void Reset()
    {
        ModConfig defaults = new();
        this.Enabled = defaults.Enabled;
        this.RequireThisMod = defaults.RequireThisMod;
        this.IgnoredModIds = defaults.IgnoredModIds;
        this.MaxModsInPopup = defaults.MaxModsInPopup;
        this.AllowModDownloads = defaults.AllowModDownloads;
        this.ArchivesFolder = defaults.ArchivesFolder;
        this.NeverSyncedConfigModIds = defaults.NeverSyncedConfigModIds;
        this.SyncedConfigModIds = defaults.SyncedConfigModIds;
        this.UnsyncedConfigModIds = defaults.UnsyncedConfigModIds;
    }

    /// <summary>Get whether two unique mod IDs are equivalent.</summary>
    private static bool IsSameId(string left, string right)
    {
        return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}

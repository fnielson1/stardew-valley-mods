using System;
using System.Collections.Generic;
using System.Linq;

namespace ModSyncCheck;

/// <summary>The mod settings, editable in <c>config.json</c> or through Generic Mod Config Menu.</summary>
internal class ModConfig
{
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

    /// <summary>When hosting, the unique IDs of ignored mods for which players who have both this mod and that one use your settings while they're in your game. Their settings files aren't changed.</summary>
    public List<string> SyncedConfigModIds { get; set; } = new();

    /// <summary>When hosting, the unique IDs of required mods for which players keep their own settings. Players use your settings for every other required mod.</summary>
    public List<string> UnsyncedConfigModIds { get; set; } = new();

    /// <summary>The unique IDs of mods whose settings are never sent to players, whatever the settings say.</summary>
    /// <remarks>These are built in rather than saved in <c>config.json</c>, so a mod added here in an update also applies to players who already have a settings file.</remarks>
    public static readonly IReadOnlyList<string> BuiltInNeverSyncedConfigModIds = new[]
    {
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

    /// <summary>The unique IDs of mods which players must always have at the same version and with the host's settings, whatever the other settings say, because they cause problems when the host's and players' settings differ.</summary>
    /// <remarks>These are built in rather than saved in <c>config.json</c>, so a mod added here in an update also applies to players who already have a settings file.</remarks>
    public static readonly IReadOnlyList<string> BuiltInMustMatchModIds = new[]
    {
        "DIGUS.CustomCaskMod"
    };

    /// <summary>The unique IDs of more mods which players must always have at the same version and with the host's settings, in addition to <see cref="BuiltInMustMatchModIds"/>. These can only be changed by editing <c>config.json</c>.</summary>
    public List<string> MustMatchModIds { get; set; } = new();

    /// <summary>The unique IDs of the installed mods which only read their settings when the game starts. Players must have these and use the host's settings for them, whatever the other settings say. This is found when the game starts, and isn't saved.</summary>
    private readonly HashSet<string> LaunchOnlyConfigModIds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Which installed mods each installed mod needs to load. This is found when the game starts, and isn't saved.</summary>
    private ModDependencies Dependencies = new(new Dictionary<string, IEnumerable<string>>());

    /// <summary>Set which installed mods each installed mod needs to load.</summary>
    /// <param name="dependencies">The installed mods' dependencies.</param>
    public void SetModDependencies(ModDependencies dependencies)
    {
        this.Dependencies = dependencies;
    }

    /// <summary>Set the installed mods which only read their settings when the game starts.</summary>
    /// <param name="modIds">The unique mod IDs.</param>
    public void SetLaunchOnlyConfigMods(IEnumerable<string> modIds)
    {
        this.LaunchOnlyConfigModIds.Clear();
        foreach (string id in modIds)
            this.LaunchOnlyConfigModIds.Add(id.Trim());
    }

    /// <summary>Get whether an installed mod only reads its settings when the game starts.</summary>
    /// <param name="modId">The unique mod ID.</param>
    public bool IsLaunchOnlyConfigMod(string modId)
    {
        return this.LaunchOnlyConfigModIds.Contains(modId.Trim());
    }

    /// <summary>Get whether a mod is listed as one which players must always have at the same version and with the host's settings.</summary>
    /// <param name="modId">The unique mod ID.</param>
    public bool IsListedMustMatch(string modId)
    {
        return
            ModConfig.BuiltInMustMatchModIds.Any(id => ModConfig.IsSameId(id, modId))
            || this.MustMatchModIds.Exists(id => ModConfig.IsSameId(id, modId));
    }

    /// <summary>Get whether players must have a mod at the same version and use the host's settings for it, whatever the host chose, because the mod is listed in <see cref="BuiltInMustMatchModIds"/> or <see cref="MustMatchModIds"/>, or only reads its settings when the game starts.</summary>
    /// <param name="modId">The unique mod ID.</param>
    public bool MustMatch(string modId)
    {
        return (this.IsListedMustMatch(modId) || this.IsLaunchOnlyConfigMod(modId)) && this.CanSyncConfig(modId);
    }

    /// <summary>Get whether a mod doesn't need to match.</summary>
    /// <param name="modId">The unique mod ID.</param>
    public bool IsIgnored(string modId)
    {
        return !this.MustMatch(modId) && this.IsListedIgnored(modId) && this.GetNeededBy(modId) == null;
    }

    /// <summary>Get whether a mod is listed as one which doesn't need to match, even if it must match anyway.</summary>
    /// <param name="modId">The unique mod ID.</param>
    public bool IsListedIgnored(string modId)
    {
        return this.IgnoredModIds.Exists(id => ModConfig.IsSameId(id, modId));
    }

    /// <summary>Get the required mod which needs a mod to load, directly or through other mods, which makes that mod required too.</summary>
    /// <param name="modId">The unique mod ID.</param>
    /// <returns>The unique ID of the installed mod which needs it directly, or <c>null</c> if no required mod needs it.</returns>
    public string? GetNeededBy(string modId)
    {
        return this.GetNeededByRequired().GetValueOrDefault(modId.Trim());
    }

    /// <summary>Get the unique IDs of the mods which don't need to match, leaving out those which must and those a required mod needs.</summary>
    public IEnumerable<string> GetIgnoredModIds()
    {
        Dictionary<string, string> needed = this.GetNeededByRequired();
        return this.IgnoredModIds.Where(id => !this.MustMatch(id) && !needed.ContainsKey(id.Trim())).ToList();
    }

    /// <summary>Set whether a mod doesn't need to match.</summary>
    /// <param name="modId">The unique mod ID.</param>
    /// <param name="ignored">Whether to ignore the mod.</param>
    public void SetIgnored(string modId, bool ignored)
    {
        if (this.MustMatch(modId))
            return;

        if (!ignored)
            this.IgnoredModIds.RemoveAll(id => ModConfig.IsSameId(id, modId));
        else if (!this.IsListedIgnored(modId))
            this.IgnoredModIds.Add(modId);
    }

    /// <summary>Get whether joining players who have a mod use the host's settings for it. Unless the host chose otherwise, that's the case for mods which must match.</summary>
    /// <param name="modId">The unique mod ID.</param>
    public bool IsConfigSynced(string modId)
    {
        if (!this.CanSyncConfig(modId))
            return false;
        if (this.MustMatch(modId))
            return true;

        return this.IsIgnored(modId)
            ? this.SyncedConfigModIds.Exists(id => ModConfig.IsSameId(id, modId))
            : !this.UnsyncedConfigModIds.Exists(id => ModConfig.IsSameId(id, modId));
    }

    /// <summary>Get whether the host's settings for a mod are allowed to be sent to players.</summary>
    /// <param name="modId">The unique mod ID.</param>
    public bool CanSyncConfig(string modId)
    {
        // a built-in mod which must match outranks the editable list, which older versions filled with the built-in defaults
        return
            !ModConfig.BuiltInNeverSyncedConfigModIds.Any(id => ModConfig.IsSameId(id, modId))
            && (
                ModConfig.BuiltInMustMatchModIds.Any(id => ModConfig.IsSameId(id, modId))
                || !this.NeverSyncedConfigModIds.Exists(id => ModConfig.IsSameId(id, modId))
            );
    }

    /// <summary>Set whether joining players who have a mod use the host's settings for it. This depends on whether the mod is ignored, so call <see cref="SetIgnored"/> first when changing both.</summary>
    /// <param name="modId">The unique mod ID.</param>
    /// <param name="synced">Whether to use the host's settings.</param>
    public void SetConfigSynced(string modId, bool synced)
    {
        if (!this.CanSyncConfig(modId))
            return;

        // only a choice which differs from the default is kept, and there's no choice for a locked mod
        this.SyncedConfigModIds.RemoveAll(id => ModConfig.IsSameId(id, modId));
        this.UnsyncedConfigModIds.RemoveAll(id => ModConfig.IsSameId(id, modId));
        if (this.MustMatch(modId))
            return;
        if (synced != this.IsConfigSynced(modId))
            (synced ? this.SyncedConfigModIds : this.UnsyncedConfigModIds).Add(modId);
    }

    /// <summary>Reset all settings to their default values.</summary>
    public void Reset()
    {
        ModConfig defaults = new();
        this.RequireThisMod = defaults.RequireThisMod;
        this.IgnoredModIds = defaults.IgnoredModIds;
        this.NeverSyncedConfigModIds = defaults.NeverSyncedConfigModIds;
        this.MustMatchModIds = defaults.MustMatchModIds;
        this.SyncedConfigModIds = defaults.SyncedConfigModIds;
        this.UnsyncedConfigModIds = defaults.UnsyncedConfigModIds;
    }

    /// <summary>Get the mods which required mods need to load, directly or through other mods, with the installed mod which needs each one directly.</summary>
    private Dictionary<string, string> GetNeededByRequired()
    {
        return this.Dependencies.GetNeededBy(id => this.MustMatch(id) || !this.IsListedIgnored(id));
    }

    /// <summary>Get whether two unique mod IDs are equivalent.</summary>
    private static bool IsSameId(string left, string right)
    {
        return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}

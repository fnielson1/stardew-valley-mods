using System.Collections.Generic;
using Xunit;

namespace ModSyncCheck.Tests;

public class ModConfigTests
{
    [Fact]
    public void IsIgnored_IgnoresCaseAndSpaces()
    {
        ModConfig config = new() { IgnoredModIds = new List<string> { " Pathoschild.Automate " } };

        Assert.True(config.IsIgnored("pathoschild.automate"));
        Assert.False(config.IsIgnored("Pathoschild.ContentPatcher"));
    }

    [Fact]
    public void SetIgnored_True_AddsTheModOnce()
    {
        ModConfig config = new() { IgnoredModIds = new List<string> { "Other.Mod" } };

        config.SetIgnored("Pathoschild.Automate", true);
        config.SetIgnored("pathoschild.automate", true);

        Assert.Equal(new[] { "Other.Mod", "Pathoschild.Automate" }, config.IgnoredModIds);
    }

    [Fact]
    public void SetIgnored_False_RemovesEveryMatch_AndKeepsOtherMods()
    {
        ModConfig config = new() { IgnoredModIds = new List<string> { "Pathoschild.Automate", "Not.Installed", "PATHOSCHILD.AUTOMATE" } };

        config.SetIgnored("Pathoschild.Automate", false);

        // IDs for mods which aren't installed (so have no checkbox) must survive, since they cover mods only a joining player has
        Assert.Equal(new[] { "Not.Installed" }, config.IgnoredModIds);
    }

    [Fact]
    public void SetConfigSynced_OnlyAffectsThatMod_AndIgnoresCase()
    {
        ModConfig config = new() { IgnoredModIds = new List<string>() };

        config.SetConfigSynced("Pathoschild.Automate", false);
        config.SetConfigSynced("pathoschild.automate", false);

        Assert.Equal("Pathoschild.Automate", Assert.Single(config.UnsyncedConfigModIds), ignoreCase: true);
        Assert.False(config.IsConfigSynced("PATHOSCHILD.AUTOMATE"));
        Assert.True(config.IsConfigSynced("Pathoschild.ChestsAnywhere"));

        config.SetConfigSynced("PATHOSCHILD.AUTOMATE", true);
        Assert.Empty(config.UnsyncedConfigModIds);
        Assert.True(config.IsConfigSynced("Pathoschild.Automate"));
    }

    [Fact]
    public void IsConfigSynced_FollowsWhetherTheModIsRequired_UntilChosen()
    {
        ModConfig defaults = new();
        Assert.True(defaults.IsConfigSynced("Pathoschild.Automate"));
        Assert.False(defaults.IsConfigSynced("spacechase0.GenericModConfigMenu")); // ignored by default

        ModConfig config = new() { IgnoredModIds = new List<string>() };

        config.SetIgnored("Pathoschild.Automate", true);
        Assert.False(config.IsConfigSynced("Pathoschild.Automate"));

        config.SetIgnored("Pathoschild.Automate", false);
        Assert.True(config.IsConfigSynced("Pathoschild.Automate"));
    }

    [Fact]
    public void SetConfigSynced_IgnoredMod_CanStillUseTheHostsSettings()
    {
        ModConfig config = new() { IgnoredModIds = new List<string> { "Pathoschild.Automate" } };

        config.SetConfigSynced("Pathoschild.Automate", true);

        Assert.True(config.IsIgnored("Pathoschild.Automate"));
        Assert.True(config.IsConfigSynced("Pathoschild.Automate"));
        Assert.Equal(new[] { "Pathoschild.Automate" }, config.SyncedConfigModIds);
        Assert.Empty(config.UnsyncedConfigModIds);

        config.SetConfigSynced("pathoschild.automate", false);
        Assert.False(config.IsConfigSynced("Pathoschild.Automate"));
        Assert.Empty(config.SyncedConfigModIds);
        Assert.Empty(config.UnsyncedConfigModIds);
    }

    [Fact]
    public void SetConfigSynced_OnlyStoresChoicesWhichDifferFromTheDefault()
    {
        ModConfig config = new() { IgnoredModIds = new List<string> { "Ignored.Mod" } };

        config.SetConfigSynced("Required.Mod", true);
        config.SetConfigSynced("Ignored.Mod", false);

        Assert.Empty(config.SyncedConfigModIds);
        Assert.Empty(config.UnsyncedConfigModIds);
    }

    [Theory]
    [InlineData("cantorsdust.timespeed")]
    [InlineData("Pathoschild.ContentPatcher")]
    [InlineData("SMAPI.ConsoleCommands")]
    [InlineData("bcmpinc.HarvestWithScythe")]
    [InlineData("spacechase0.GenericModConfigMenu")]
    [InlineData("bcmpinc.MovementSpeed")]
    [InlineData("SMAPI.SaveBackup")]
    public void IsConfigSynced_NeverSyncedMod_IsFalse_AndCantBeTurnedOn(string modId)
    {
        ModConfig config = new();
        config.SetIgnored(modId, false); // required, so it would be synced otherwise
        Assert.False(config.CanSyncConfig(modId));
        Assert.False(config.IsConfigSynced(modId));

        config.SetConfigSynced(modId, true);
        Assert.False(config.IsConfigSynced(modId));
        Assert.Empty(config.SyncedConfigModIds);
        Assert.Empty(config.UnsyncedConfigModIds);

        config.SetIgnored(modId, true);
        config.SetConfigSynced(modId, true);
        Assert.False(config.IsConfigSynced(modId));
    }

    [Fact]
    public void IsConfigSynced_NeverSyncedList_CanBeEdited()
    {
        ModConfig config = new() { NeverSyncedConfigModIds = new List<string> { "Pathoschild.Automate" } };

        Assert.False(config.IsConfigSynced("Pathoschild.Automate"));
        Assert.True(config.IsConfigSynced("Pathoschild.ChestsAnywhere"));

        // the built-in mods can't be taken off by editing the list
        Assert.False(config.IsConfigSynced("cantorsdust.TimeSpeed"));
    }

    [Fact]
    public void NeverSyncedConfigModIds_DefaultsToEmpty_SoBuiltInModsAddedLaterStillApply()
    {
        Assert.Empty(new ModConfig().NeverSyncedConfigModIds);
        Assert.Contains("spacechase0.GenericModConfigMenu", ModConfig.BuiltInNeverSyncedConfigModIds);
    }

    [Fact]
    public void Reset_RestoresDefaults()
    {
        ModConfig config = new() { IgnoredModIds = new List<string>(), RequireThisMod = false, SyncedConfigModIds = new List<string> { "Other.Mod" }, UnsyncedConfigModIds = new List<string> { "Pathoschild.Automate" } };

        config.Reset();

        ModConfig defaults = new();
        Assert.Equal(defaults.IgnoredModIds, config.IgnoredModIds);
        Assert.True(config.RequireThisMod);
        Assert.Empty(config.SyncedConfigModIds);
        Assert.Empty(config.UnsyncedConfigModIds);
        Assert.True(config.IsIgnored("spacechase0.GenericModConfigMenu"));
    }

    [Fact]
    public void MustMatch_LaunchOnlyMod_IsSynced_EvenIfUnsynced()
    {
        ModConfig config = new() { IgnoredModIds = new List<string>(), UnsyncedConfigModIds = new List<string> { "Ellibehr.ElliesIdealGreenhouse" } };
        config.SetLaunchOnlyConfigMods(new[] { " ellibehr.elliesidealgreenhouse " });

        Assert.True(config.MustMatch("Ellibehr.ElliesIdealGreenhouse"));
        Assert.True(config.IsConfigSynced("Ellibehr.ElliesIdealGreenhouse"));
        Assert.False(config.MustMatch("Pathoschild.Automate"));
    }

    [Fact]
    public void MustMatch_LaunchOnlyMod_IsRequired_EvenIfIgnored()
    {
        ModConfig config = new() { IgnoredModIds = new List<string> { "Ellibehr.ElliesIdealGreenhouse", "Pathoschild.Automate" } };
        config.SetLaunchOnlyConfigMods(new[] { "Ellibehr.ElliesIdealGreenhouse" });

        Assert.False(config.IsIgnored("Ellibehr.ElliesIdealGreenhouse"));
        Assert.True(config.IsConfigSynced("Ellibehr.ElliesIdealGreenhouse"));
        Assert.Equal(new[] { "Pathoschild.Automate" }, config.GetIgnoredModIds());
    }

    [Fact]
    public void MustMatch_LaunchOnlyMod_CantBeIgnoredOrUnsynced()
    {
        ModConfig config = new() { IgnoredModIds = new List<string>(), UnsyncedConfigModIds = new List<string> { "Ellibehr.ElliesIdealGreenhouse" } };
        config.SetLaunchOnlyConfigMods(new[] { "Ellibehr.ElliesIdealGreenhouse" });

        config.SetIgnored("Ellibehr.ElliesIdealGreenhouse", true);
        config.SetConfigSynced("Ellibehr.ElliesIdealGreenhouse", false);

        Assert.False(config.IsIgnored("Ellibehr.ElliesIdealGreenhouse"));
        Assert.True(config.IsConfigSynced("Ellibehr.ElliesIdealGreenhouse"));
        Assert.Empty(config.IgnoredModIds);
        Assert.Empty(config.UnsyncedConfigModIds);
    }

    [Fact]
    public void MustMatch_NeverSyncedLaunchOnlyMod_FollowsTheOtherSettings()
    {
        ModConfig config = new() { IgnoredModIds = new List<string> { "cantorsdust.TimeSpeed" } };
        config.SetLaunchOnlyConfigMods(new[] { "cantorsdust.TimeSpeed" });

        Assert.False(config.MustMatch("cantorsdust.TimeSpeed"));
        Assert.True(config.IsIgnored("cantorsdust.TimeSpeed"));
        Assert.False(config.IsConfigSynced("cantorsdust.TimeSpeed"));
    }

    [Fact]
    public void MustMatch_CustomCaskMod_IsRequiredAndSynced_ByDefault()
    {
        ModConfig config = new() { IgnoredModIds = new List<string> { "DIGUS.CustomCaskMod" }, UnsyncedConfigModIds = new List<string> { "DIGUS.CustomCaskMod" } };

        Assert.True(config.MustMatch("digus.customcaskmod"));
        Assert.False(config.IsIgnored("DIGUS.CustomCaskMod"));
        Assert.True(config.IsConfigSynced("DIGUS.CustomCaskMod"));
        Assert.Empty(config.GetIgnoredModIds());
    }

    [Fact]
    public void MustMatch_MustMatchList_CanBeEdited()
    {
        ModConfig config = new() { IgnoredModIds = new List<string> { "Pathoschild.Automate" }, MustMatchModIds = new List<string> { " pathoschild.automate " } };

        Assert.True(config.MustMatch("Pathoschild.Automate"));
        Assert.False(config.IsIgnored("Pathoschild.Automate"));
        Assert.True(config.IsConfigSynced("Pathoschild.Automate"));
        Assert.False(config.MustMatch("Pathoschild.ChestsAnywhere"));
    }

    [Fact]
    public void MustMatch_NeverSyncedListWins()
    {
        ModConfig config = new() { MustMatchModIds = new List<string> { "Pathoschild.Automate" }, NeverSyncedConfigModIds = new List<string> { "Pathoschild.Automate" } };

        Assert.False(config.MustMatch("Pathoschild.Automate"));
        Assert.False(config.IsConfigSynced("Pathoschild.Automate"));
    }

    [Fact]
    public void MustMatch_BuiltInMod_OutranksTheNeverSyncedList()
    {
        // older versions wrote Custom Cask Mod into the editable list when it was a built-in never-synced mod
        ModConfig config = new() { NeverSyncedConfigModIds = new List<string> { "DIGUS.CustomCaskMod", "cantorsdust.TimeSpeed" } };

        Assert.True(config.CanSyncConfig("DIGUS.CustomCaskMod"));
        Assert.True(config.MustMatch("DIGUS.CustomCaskMod"));
        Assert.True(config.IsConfigSynced("DIGUS.CustomCaskMod"));
    }

    [Fact]
    public void MustMatchModIds_DefaultsToEmpty_SoBuiltInModsAddedLaterStillApply()
    {
        Assert.Empty(new ModConfig().MustMatchModIds);
        Assert.Contains("DIGUS.CustomCaskMod", ModConfig.BuiltInMustMatchModIds);
        Assert.DoesNotContain("DIGUS.CustomCaskMod", ModConfig.BuiltInNeverSyncedConfigModIds);
    }
}

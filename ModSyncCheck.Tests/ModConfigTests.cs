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
    [InlineData("DIGUS.CustomCaskMod")]
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

    [Theory]
    [InlineData("flashshifter.immersivefarm2remastered")]
    [InlineData("FlashShifter.ImmersiveFarm2Remastered")]
    [InlineData("Ellibehr.ElliesIdealGreenhouse")]
    public void CanSyncConfig_ContentPacksWhoseSettingsCantChangeInGame_AreNeverSynced(string modId)
    {
        ModConfig config = new();

        Assert.False(config.CanSyncConfig(modId));
        Assert.False(config.IsConfigSynced(modId));
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
}

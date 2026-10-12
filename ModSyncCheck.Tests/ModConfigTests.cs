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
    public void SetIgnored_ChangesTheComparison()
    {
        ModConfig config = new() { IgnoredModIds = new List<string>() };
        ModInfo[] hostMods = { new("Pathoschild.Automate", "Automate", "2.3.0") };
        ModInfo[] playerMods = System.Array.Empty<ModInfo>();

        Assert.False(ModComparer.Compare(playerMods, hostMods, config.IgnoredModIds).IsMatch);

        config.SetIgnored("Pathoschild.Automate", true);
        Assert.True(ModComparer.Compare(playerMods, hostMods, config.IgnoredModIds).IsMatch);
    }

    [Fact]
    public void Reset_RestoresDefaults()
    {
        ModConfig config = new() { IgnoredModIds = new List<string>(), MaxModsInPopup = 3, AllowModDownloads = false, CopyConfigFromHost = true };

        config.Reset();

        ModConfig defaults = new();
        Assert.Equal(defaults.IgnoredModIds, config.IgnoredModIds);
        Assert.Equal(defaults.MaxModsInPopup, config.MaxModsInPopup);
        Assert.True(config.AllowModDownloads);
        Assert.False(config.CopyConfigFromHost);
        Assert.True(config.IsIgnored("spacechase0.GenericModConfigMenu"));
    }
}

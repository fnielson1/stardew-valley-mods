using Xunit;

namespace ModSyncCheck.Tests;

public class NexusLinksTests
{
    [Theory]
    [InlineData(1915, "Nexus:1915")]
    [InlineData(1915, "nexus:1915")]
    [InlineData(1915, " Nexus: 1915 ")]
    [InlineData(1915, "Nexus:1915@ContentPatcher")]
    [InlineData(1915, "GitHub:Pathoschild/StardewMods", "Nexus:1915")]
    [InlineData(1915, "Nexus:not-a-number", "Nexus:1915")]
    public void GetModId_FindsTheNexusKey(int expected, params string[] updateKeys)
    {
        Assert.Equal(expected, NexusLinks.GetModId(updateKeys));
    }

    [Theory]
    [InlineData]
    [InlineData("GitHub:Pathoschild/StardewMods")]
    [InlineData("ModDrop:123")]
    [InlineData("Nexus:")]
    [InlineData("Nexus:-5")]
    [InlineData("Nexus:0")]
    [InlineData("Nexus:12abc")]
    [InlineData("Nexus:99999999999")]
    public void GetModId_WithoutAValidNexusKey_IsNull(params string[] updateKeys)
    {
        Assert.Null(NexusLinks.GetModId(updateKeys));
    }

    [Fact]
    public void GetModId_NullKeys_IsNull()
    {
        Assert.Null(NexusLinks.GetModId(null));
    }

    [Fact]
    public void GetModUrl_PointsToTheStardewValleyModPage()
    {
        Assert.Equal("https://www.nexusmods.com/stardewvalley/mods/53850", NexusLinks.GetModUrl(53850));
    }
}

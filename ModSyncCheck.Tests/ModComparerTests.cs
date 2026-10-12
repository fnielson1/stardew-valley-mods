using System;
using System.Linq;
using Xunit;

namespace ModSyncCheck.Tests;

public class ModComparerTests
{
    private static readonly ModInfo Automate = new("Pathoschild.Automate", "Automate", "2.3.0");
    private static readonly ModInfo ContentPatcher = new("Pathoschild.ContentPatcher", "Content Patcher", "2.5.0");
    private static readonly ModInfo TimeSpeed = new("cantorsdust.TimeSpeed", "TimeSpeed", "2.8.0");

    [Fact]
    public void IdenticalLists_Match()
    {
        ModComparison result = ModComparer.Compare(new[] { Automate, ContentPatcher }, new[] { ContentPatcher, Automate }, Array.Empty<string>());

        Assert.True(result.IsMatch);
        Assert.Equal(0, result.Count);
    }

    [Fact]
    public void ModOnlyOnHost_IsMissing()
    {
        ModComparison result = ModComparer.Compare(new[] { Automate }, new[] { Automate, ContentPatcher }, Array.Empty<string>());

        Assert.Equal(ContentPatcher, Assert.Single(result.Missing));
        Assert.Empty(result.Extra);
        Assert.Empty(result.VersionMismatches);
    }

    [Fact]
    public void ModOnlyOnPlayer_IsExtra()
    {
        ModComparison result = ModComparer.Compare(new[] { Automate, TimeSpeed }, new[] { Automate }, Array.Empty<string>());

        Assert.Equal(TimeSpeed, Assert.Single(result.Extra));
        Assert.Empty(result.Missing);
        Assert.Empty(result.VersionMismatches);
    }

    [Fact]
    public void DifferentVersion_IsVersionMismatch()
    {
        ModComparison result = ModComparer.Compare(new[] { Automate with { Version = "2.2.0" } }, new[] { Automate }, Array.Empty<string>());

        VersionMismatch mismatch = Assert.Single(result.VersionMismatches);
        Assert.Equal("2.2.0", mismatch.PlayerVersion);
        Assert.Equal("2.3.0", mismatch.HostVersion);
        Assert.Empty(result.Missing);
        Assert.Empty(result.Extra);
    }

    [Fact]
    public void IgnoredIds_AreLeftOut()
    {
        ModComparison result = ModComparer.Compare(
            new[] { Automate with { Version = "2.2.0" }, TimeSpeed },
            new[] { Automate, ContentPatcher },
            new[] { "pathoschild.automate", " cantorsdust.TimeSpeed ", "Pathoschild.ContentPatcher" }
        );

        Assert.True(result.IsMatch);
    }

    [Fact]
    public void ModIds_AreCaseInsensitive()
    {
        ModComparison result = ModComparer.Compare(new[] { Automate with { Id = "PATHOSCHILD.AUTOMATE" } }, new[] { Automate }, Array.Empty<string>());

        Assert.True(result.IsMatch);
    }

    [Fact]
    public void EmptyPlayerList_ReportsAllHostModsAsMissing()
    {
        ModComparison result = ModComparer.Compare(Array.Empty<ModInfo>(), new[] { TimeSpeed, Automate }, Array.Empty<string>());

        Assert.Equal(new[] { "Automate", "TimeSpeed" }, result.Missing.Select(mod => mod.Name));
    }

    [Fact]
    public void GetNeededMods_ReturnsMissingAndOutdatedModsAtTheHostVersion_ButNotExtraMods()
    {
        ModComparison result = ModComparer.Compare(
            new[] { Automate with { Version = "2.2.0" }, TimeSpeed },
            new[] { Automate, ContentPatcher },
            Array.Empty<string>()
        );

        Assert.Equal(new[] { ContentPatcher, Automate }, result.GetNeededMods());
    }

    [Fact]
    public void GetNeededMods_MatchingMods_IsEmpty()
    {
        ModComparison result = ModComparer.Compare(new[] { Automate }, new[] { Automate }, Array.Empty<string>());

        Assert.Empty(result.GetNeededMods());
    }
}

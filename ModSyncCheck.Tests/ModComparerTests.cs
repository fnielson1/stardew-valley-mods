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
        Assert.Empty(result.VersionMismatches);
    }

    [Fact]
    public void ModOnlyOnPlayer_IsAllowed()
    {
        ModComparison result = ModComparer.Compare(new[] { Automate, TimeSpeed }, new[] { Automate }, Array.Empty<string>());

        Assert.True(result.IsMatch);
    }

    [Fact]
    public void HostWithoutMods_AllowsAnyPlayerMods()
    {
        ModComparison result = ModComparer.Compare(new[] { Automate, TimeSpeed }, Array.Empty<ModInfo>(), Array.Empty<string>());

        Assert.True(result.IsMatch);
    }

    [Fact]
    public void DifferentVersion_IsVersionMismatch()
    {
        ModComparison result = ModComparer.Compare(new[] { Automate with { Version = "2.2.0" } }, new[] { Automate }, Array.Empty<string>());

        VersionMismatch mismatch = Assert.Single(result.VersionMismatches);
        Assert.Equal("2.2.0", mismatch.PlayerVersion);
        Assert.Equal("2.3.0", mismatch.HostVersion);
        Assert.Empty(result.Missing);
    }

    [Fact]
    public void IgnoredIds_AreLeftOut()
    {
        ModComparison result = ModComparer.Compare(
            new[] { Automate with { Version = "2.2.0" } },
            new[] { Automate, ContentPatcher },
            new[] { "pathoschild.automate", " Pathoschild.ContentPatcher " }
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
    public void GetNeededMods_ReturnsMissingAndOutdatedModsAtTheHostVersion()
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

    [Theory]
    [InlineData("1.0.4")]
    [InlineData("1.9.0")]
    public void RequireSameMajorVersion_SameMajorVersion_ChangesNothing(string playerVersion)
    {
        // this mod is ignored by default, so a different minor or patch version isn't a mismatch
        ModInfo thisMod = new("fnielson.ModSyncCheck", "Mod Sync Check", "1.2.0");
        ModComparison comparison = ModComparer.Compare(new[] { thisMod with { Version = playerVersion } }, new[] { thisMod }, new[] { thisMod.Id });

        ModComparison result = ModComparer.RequireSameMajorVersion(comparison, thisMod, hostMajorVersion: 1, playerVersion, playerMajorVersion: 1);

        Assert.True(result.IsMatch);
    }

    [Theory]
    [InlineData("2.0.0", 2, "1.9.9", 1)]
    [InlineData("1.9.9", 1, "2.0.0", 2)]
    public void RequireSameMajorVersion_DifferentMajorVersion_IsVersionMismatchEvenIfIgnored(string hostVersion, int hostMajorVersion, string playerVersion, int playerMajorVersion)
    {
        ModInfo thisMod = new("fnielson.ModSyncCheck", "Mod Sync Check", hostVersion);
        ModComparison comparison = ModComparer.Compare(new[] { thisMod with { Version = playerVersion }, Automate }, new[] { thisMod, Automate }, new[] { thisMod.Id });

        ModComparison result = ModComparer.RequireSameMajorVersion(comparison, thisMod, hostMajorVersion, playerVersion, playerMajorVersion);

        Assert.Equal(new VersionMismatch(thisMod.Id, "Mod Sync Check", playerVersion, hostVersion), Assert.Single(result.VersionMismatches));
        Assert.Empty(result.Missing);

        // so the player can download the host's version
        Assert.Equal(new[] { thisMod }, result.GetNeededMods());
    }

    [Fact]
    public void RequireSameMajorVersion_AlreadyMismatched_IsListedOnce()
    {
        // the host chose to require this mod at the same version
        ModInfo thisMod = new("fnielson.ModSyncCheck", "Mod Sync Check", "2.0.0");
        ModComparison comparison = ModComparer.Compare(new[] { thisMod with { Version = "1.0.4" } }, new[] { thisMod }, Array.Empty<string>());

        ModComparison result = ModComparer.RequireSameMajorVersion(comparison, thisMod, hostMajorVersion: 2, "1.0.4", playerMajorVersion: 1);

        Assert.Single(result.VersionMismatches);
    }
}

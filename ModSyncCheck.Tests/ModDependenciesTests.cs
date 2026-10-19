using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ModSyncCheck.Tests;

public class ModDependenciesTests
{
    /// <summary>A content pack for Content Patcher, which needs SpaceCore, and an unrelated mod.</summary>
    private static ModDependencies CreateChain()
    {
        return new ModDependencies(new Dictionary<string, IEnumerable<string>>
        {
            ["Example.Pack"] = new[] { "Pathoschild.ContentPatcher" },
            ["Pathoschild.ContentPatcher"] = new[] { "spacechase0.SpaceCore" },
            ["spacechase0.SpaceCore"] = new string[0],
            ["Other.Mod"] = new string[0]
        });
    }

    [Fact]
    public void GetNeededBy_FollowsDependenciesOfDependencies()
    {
        Dictionary<string, string> needed = CreateChain().GetNeededBy(id => id == "Example.Pack");

        Assert.Equal("Example.Pack", needed["pathoschild.contentpatcher"]);
        Assert.Equal("Pathoschild.ContentPatcher", needed["spacechase0.SpaceCore"]);
        Assert.Equal(2, needed.Count);
    }

    [Fact]
    public void GetNeededBy_NothingRequired_IsEmpty()
    {
        Assert.Empty(CreateChain().GetNeededBy(_ => false));
    }

    [Fact]
    public void GetNeededBy_LeavesOutModsWhichArentInstalled_AndSelfDependencies()
    {
        ModDependencies dependencies = new(new Dictionary<string, IEnumerable<string>>
        {
            ["Example.Mod"] = new[] { "Not.Installed", "Example.Mod", " Other.Mod " },
            ["Other.Mod"] = new string[0]
        });

        Dictionary<string, string> needed = dependencies.GetNeededBy(_ => true);

        Assert.Equal(new[] { "Other.Mod" }, needed.Keys);
    }

    [Fact]
    public void GetNeededBy_HandlesCycles()
    {
        ModDependencies dependencies = new(new Dictionary<string, IEnumerable<string>>
        {
            ["A"] = new[] { "B" },
            ["B"] = new[] { "A" }
        });

        Dictionary<string, string> needed = dependencies.GetNeededBy(id => id == "A");

        Assert.Equal(new[] { "A", "B" }, needed.Keys.OrderBy(id => id));
    }

    [Fact]
    public void GetDependents_FollowsModsWhichNeedThem()
    {
        Dictionary<string, string> dependents = CreateChain().GetDependents(new[] { "SPACECHASE0.SPACECORE" });

        Assert.Equal("spacechase0.SpaceCore", dependents["Pathoschild.ContentPatcher"], ignoreCase: true);
        Assert.Equal("Pathoschild.ContentPatcher", dependents["Example.Pack"]);
        Assert.Equal(2, dependents.Count);
    }

    [Fact]
    public void GetDependents_ModNobodyNeeds_IsEmpty()
    {
        Assert.Empty(CreateChain().GetDependents(new[] { "Example.Pack" }));
        Assert.Empty(CreateChain().GetDependents(new[] { "Other.Mod" }));
    }
}

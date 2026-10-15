using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ModSyncCheck.Tests;

public class DownloadPlanTests
{
    private static readonly ModInfo[] Needed =
    {
        new("Pathoschild.Automate", "Automate", "2.6.1", NexusId: 1063),
        new("Example.Private", "Private Mod", "1.0.0"),
        new("Pathoschild.ContentPatcher", "Content Patcher", "2.9.1", NexusId: 1915),
        new("FlashShifter.SVECode", "Stardew Valley Expanded", "1.15.11")
    };

    private static readonly Dictionary<string, long> Sizes = new()
    {
        ["pathoschild.automate"] = 200,
        ["Pathoschild.ContentPatcher"] = 300,
        ["FlashShifter.SVECode"] = 100_000,
        ["Someone.NotNeeded"] = 5000
    };

    [Fact]
    public void Select_NoArchiveList_GetsEverything_AndNeedsTrust()
    {
        DownloadPlan plan = DownloadPlan.Select(Needed, archiveSizes: null, allBytes: 900, trustHost: false);

        Assert.Equal(Needed, plan.Targets);
        Assert.Equal(900, plan.Bytes);
        Assert.Empty(plan.Left);
        Assert.True(plan.TrustHost);
        Assert.False(DownloadPlan.CanSkipTrust(Needed, null));
    }

    [Fact]
    public void Select_NoNeededModHasAnArchive_NeedsTrust()
    {
        Dictionary<string, long> sizes = new() { ["Someone.NotNeeded"] = 5000 };

        Assert.False(DownloadPlan.CanSkipTrust(Needed, sizes));
        Assert.True(DownloadPlan.Select(Needed, sizes, allBytes: 900, trustHost: false).TrustHost);
    }

    [Fact]
    public void Select_Trusted_GetsEverything()
    {
        DownloadPlan plan = DownloadPlan.Select(Needed, Sizes, allBytes: 900, trustHost: true);

        Assert.Equal(Needed, plan.Targets);
        Assert.Equal(900, plan.Bytes);
        Assert.Empty(plan.Left);
        Assert.True(plan.TrustHost);
    }

    [Fact]
    public void Select_Untrusted_GetsOnlyArchivedMods_AndListsTheRest()
    {
        Assert.True(DownloadPlan.CanSkipTrust(Needed, Sizes));

        DownloadPlan plan = DownloadPlan.Select(Needed, Sizes, allBytes: 900, trustHost: false);

        Assert.Equal(new[] { "Pathoschild.Automate", "Pathoschild.ContentPatcher" }, plan.Targets.Select(mod => mod.Id));
        Assert.Equal(500, plan.Bytes);
        Assert.Equal(new[] { "Example.Private", "FlashShifter.SVECode" }, plan.Left.Select(mod => mod.Id));
        Assert.False(plan.TrustHost);
    }

    [Fact]
    public void SelectUnverifiable_GetsOnlyTheModsWhichCantBeChecked_AndTrustsTheHost()
    {
        DownloadPlan plan = DownloadPlan.SelectUnverifiable(Needed, Sizes, allBytes: 900);

        Assert.Equal(new[] { "Example.Private", "FlashShifter.SVECode" }, plan.Targets.Select(mod => mod.Id));
        Assert.Equal(new[] { "Pathoschild.Automate", "Pathoschild.ContentPatcher" }, plan.Left.Select(mod => mod.Id));
        Assert.True(plan.TrustHost);
    }

    [Fact]
    public void Select_ArchivedModsWithoutNexusId_NeedTrust()
    {
        ModInfo[] needed = { new("FlashShifter.SVECode", "Stardew Valley Expanded", "1.15.11"), new("Someone.Pack", "Pack", "1.0.0", NexusId: -1) };
        Dictionary<string, long> sizes = new() { ["FlashShifter.SVECode"] = 100_000, ["Someone.Pack"] = 50 };

        Assert.False(DownloadPlan.CanSkipTrust(needed, sizes));
        Assert.True(DownloadPlan.Select(needed, sizes, allBytes: 100_050, trustHost: false).TrustHost);
    }
}

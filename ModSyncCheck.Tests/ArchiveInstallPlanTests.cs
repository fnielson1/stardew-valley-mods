using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace ModSyncCheck.Tests;

public class ArchiveInstallPlanTests
{
    [Fact]
    public void Plan_ComparesWithTheInstalledVersion()
    {
        ArchivedMod[] mods =
        {
            new("Someone.New", "1.0.0", "New/"),
            new("Someone.Older", "1.1.0", "Older/"),
            new("Someone.Same", "1.0", "Same/"),
            new("Someone.Newer", "1.0.0", "Newer/")
        };
        Dictionary<string, string> installed = new(StringComparer.OrdinalIgnoreCase)
        {
            ["someone.older"] = "1.0.0",
            ["Someone.Same"] = "1.0.0",
            ["Someone.Newer"] = "2.0.0"
        };

        List<ArchiveInstallStep> steps = ArchiveInstallPlan.Plan("Mods.zip", mods, id => installed.GetValueOrDefault(id));

        Assert.Equal(
            new[] { ArchiveInstallAction.Install, ArchiveInstallAction.Update, ArchiveInstallAction.AlreadyInstalled, ArchiveInstallAction.NewerInstalled },
            steps.Select(step => step.Action)
        );
        Assert.Equal("1.0.0", steps[1].InstalledVersion);
        Assert.True(ArchiveInstallPlan.Installs(steps[0]));
        Assert.True(ArchiveInstallPlan.Installs(steps[1]));
        Assert.False(ArchiveInstallPlan.Installs(steps[2]));
        Assert.False(ArchiveInstallPlan.Installs(steps[3]));
    }

    [Fact]
    public void Plan_NewFolder_IsTheModsFolderInTheArchive_KeepingParentFolders()
    {
        ArchivedMod[] mods =
        {
            new("FlashShifter.SVE", "1.15.11", "Stardew Valley Expanded/[CP] Stardew Valley Expanded/"),
            new("FlashShifter.SVE.FTM", "1.15.11", "Stardew Valley Expanded/[FTM] Stardew Valley Expanded/")
        };

        List<ArchiveInstallStep> steps = ArchiveInstallPlan.Plan("SVE.zip", mods, _ => null);

        Assert.Equal(
            new[] { "Stardew Valley Expanded/[CP] Stardew Valley Expanded", "Stardew Valley Expanded/[FTM] Stardew Valley Expanded" },
            steps.Select(step => step.NewFolder)
        );
        Assert.All(steps, step => Assert.Equal(ArchiveInstallAction.Install, step.Action));
    }

    [Fact]
    public void Plan_ManifestInTheArchiveRoot_UsesTheArchiveName()
    {
        ArchiveInstallStep step = Assert.Single(ArchiveInstallPlan.Plan("Cool Mod 1.2.zip", new[] { new ArchivedMod("Someone.Cool", "1.2.0", "") }, _ => null));

        Assert.Equal("Cool Mod 1.2", step.NewFolder);
    }

    [Fact]
    public void Plan_ModInsideAnotherModsFolder_IsInstalledWithIt()
    {
        ArchivedMod[] mods =
        {
            new("Someone.Outer", "1.0.0", "Outer/"),
            new("Someone.Inner", "1.0.0", "Outer/Inner/"),
            new("Someone.Beside", "1.0.0", "OuterBeside/")
        };

        List<ArchiveInstallStep> steps = ArchiveInstallPlan.Plan("Mods.zip", mods, _ => null);

        Assert.Equal(
            new[] { ArchiveInstallAction.Install, ArchiveInstallAction.InsideAnotherMod, ArchiveInstallAction.Install },
            steps.Select(step => step.Action)
        );
    }

    [Fact]
    public void GetFreePath_NameTaken_AddsANumber()
    {
        string folder = Path.Combine("archives");
        HashSet<string> taken = new(StringComparer.OrdinalIgnoreCase) { Path.Combine(folder, "Mod.zip"), Path.Combine(folder, "Mod (2).zip") };

        Assert.Equal(Path.Combine(folder, "Other.zip"), ArchiveInstallPlan.GetFreePath(folder, "Other.zip", taken.Contains));
        Assert.Equal(Path.Combine(folder, "Mod (3).zip"), ArchiveInstallPlan.GetFreePath(folder, "Mod.zip", taken.Contains));
    }
}

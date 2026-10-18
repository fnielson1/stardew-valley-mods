using System;
using System.IO;
using System.Linq;
using Xunit;

namespace ModSyncCheck.Tests;

public sealed class RecycleBinTests : IDisposable
{
    /// <summary>A temporary folder for the test files.</summary>
    private readonly string Root = Path.Combine(Path.GetTempPath(), "ModSyncCheck.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        ModFilesTests.DeleteLinks(this.Root);
        if (Directory.Exists(this.Root))
            Directory.Delete(this.Root, recursive: true);
    }

    [Fact]
    public void GetMods_ListsTheModsStillThere_WithTheirArchives()
    {
        string batch = Path.Combine(this.Root, "uninstalled", "2026-10-18 12.00.00");
        string modDir = this.CreateFolder(Path.Combine(batch, "Junimo"));
        string archive = this.CreateFile(Path.Combine(batch, "Junimo.zip"));
        string otherArchive = this.CreateFile(Path.Combine(batch, "Other.zip"));
        RecycleBin.WriteRecord(batch, new[]
        {
            new RecycledEntry(modDir, Path.Combine(this.Root, "Mods", "Group", "Junimo"), null, new[] { "Frank.Junimo" }, "Junimo Hut", "1.0.0"),
            new RecycledEntry(archive, Path.Combine(this.Root, "Mods", ".ModSyncCheck", "Junimo.zip"), null, new[] { "Frank.Junimo" }),
            new RecycledEntry(otherArchive, Path.Combine(this.Root, "Mods", ".ModSyncCheck", "Other.zip"), null, new[] { "Frank.Other" }),
            new RecycledEntry(Path.Combine(batch, "Restored"), Path.Combine(this.Root, "Mods", "Restored"), null, new[] { "Frank.Restored" }, "Restored", "2.0.0")
        });

        RecycledMod mod = Assert.Single(RecycleBin.GetMods(Path.Combine(this.Root, "uninstalled")));

        Assert.Equal("Junimo Hut", mod.Folder.Name);
        Assert.Equal("1.0.0", mod.Folder.Version);
        Assert.Equal("2026-10-18 12.00.00", mod.Uninstalled);
        Assert.Equal(new[] { archive }, mod.Archives.Select(entry => entry.Path));
    }

    [Fact]
    public void GetMods_ListsTheNewestFirst()
    {
        foreach (string batchName in new[] { "2026-10-12 08.00.00", "2026-10-18 08.00.00" })
        {
            string batch = Path.Combine(this.Root, "uninstalled", batchName);
            string modDir = this.CreateFolder(Path.Combine(batch, "Mod"));
            RecycleBin.WriteRecord(batch, new[] { new RecycledEntry(modDir, Path.Combine(this.Root, "Mods", "Mod"), null, new[] { "Mod" }, batchName, "1.0") });
        }

        Assert.Equal(new[] { "2026-10-18 08.00.00", "2026-10-12 08.00.00" }, RecycleBin.GetMods(Path.Combine(this.Root, "uninstalled")).Select(mod => mod.Uninstalled));
    }

    [Fact]
    public void Restore_PutsTheModAndArchiveBack_AndTheLink()
    {
        string batch = Path.Combine(this.Root, "uninstalled", "2026-10-18 12.00.00");
        string modDir = this.CreateFolder(Path.Combine(batch, "[CP] Pack"));
        string archive = this.CreateFile(Path.Combine(batch, "Pack.zip"));
        string originalDir = Path.Combine(this.Root, "Mods", "Group", "[CP] Pack");
        string originalArchive = Path.Combine(this.Root, "Mods", ".ModSyncCheck", "Pack.zip");
        string linkDir = Path.Combine(this.Root, "Selected Mods", "[CP] Pack");
        RecycleBin.WriteRecord(batch, new[]
        {
            new RecycledEntry(modDir, originalDir, linkDir, new[] { "Pack" }, "Pack", "1.0"),
            new RecycledEntry(archive, originalArchive, null, new[] { "Pack" })
        });

        RecycleBin.Restore(RecycleBin.GetMods(Path.Combine(this.Root, "uninstalled")).Single());

        Assert.Equal("mod", File.ReadAllText(Path.Combine(originalDir, "manifest.json")));
        Assert.Equal("mod", File.ReadAllText(Path.Combine(linkDir, "manifest.json")));
        Assert.NotNull(new DirectoryInfo(linkDir).LinkTarget);
        Assert.Equal("file", File.ReadAllText(originalArchive));
        Assert.False(Directory.Exists(modDir));
        Assert.False(File.Exists(archive));
        Assert.Empty(RecycleBin.GetMods(Path.Combine(this.Root, "uninstalled")));
    }

    [Fact]
    public void Restore_SomethingAlreadyWhereTheModWas_ThrowsAndMovesNothing()
    {
        string batch = Path.Combine(this.Root, "uninstalled", "2026-10-18 12.00.00");
        string modDir = this.CreateFolder(Path.Combine(batch, "Mod"));
        string originalDir = this.CreateFolder(Path.Combine(this.Root, "Mods", "Mod"));
        RecycleBin.WriteRecord(batch, new[] { new RecycledEntry(modDir, originalDir, null, new[] { "Mod" }, "Mod", "1.0") });

        Assert.Throws<IOException>(() => RecycleBin.Restore(RecycleBin.GetMods(Path.Combine(this.Root, "uninstalled")).Single()));

        Assert.True(Directory.Exists(modDir));
    }

    [Fact]
    public void DeleteEmpty_DeletesFoldersWithOnlyTheirRecord_UnlessKept()
    {
        string uninstalled = Path.Combine(this.Root, "uninstalled");
        string empty = Path.Combine(uninstalled, "2026-10-18 10.00.00");
        string kept = Path.Combine(uninstalled, "2026-10-18 11.00.00");
        string full = Path.Combine(uninstalled, "2026-10-18 12.00.00");
        RecycleBin.WriteRecord(empty, Array.Empty<RecycledEntry>());
        RecycleBin.WriteRecord(kept, Array.Empty<RecycledEntry>());
        this.CreateFolder(Path.Combine(full, "Mod"));
        RecycleBin.WriteRecord(full, Array.Empty<RecycledEntry>());

        RecycleBin.DeleteEmpty(uninstalled, keep: new[] { kept });

        Assert.False(Directory.Exists(empty));
        Assert.True(Directory.Exists(kept));
        Assert.True(Directory.Exists(full));
    }

    /// <summary>Create a mod folder with a manifest.</summary>
    /// <param name="path">The folder path.</param>
    private string CreateFolder(string path)
    {
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "manifest.json"), "mod");
        return path;
    }

    /// <summary>Create a file standing in for an archive.</summary>
    /// <param name="path">The file path.</param>
    private string CreateFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "file");
        return path;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Xunit;

namespace ModSyncCheck.Tests;

public sealed class ModArchivesTests : IDisposable
{
    /// <summary>A temporary folder for the test files.</summary>
    private readonly string Root = Path.Combine(Path.GetTempPath(), "ModSyncCheck.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(this.Root))
            Directory.Delete(this.Root, recursive: true);
    }

    [Fact]
    public void Find_MatchesTheModIdAndVersion_InAnyFolderOfTheArchive()
    {
        this.WriteZip("Automate 2.2.0.zip", ("Automate/manifest.json", Manifest("Pathoschild.Automate", "2.2.0")));
        string wanted = this.WriteZip("Automate 2.3.0.zip", ("Automate/manifest.json", Manifest("Pathoschild.Automate", "2.3.0")));
        this.WriteZip("Bundle.zip", ("Bundle/[CP] Pack/manifest.json", Manifest("Someone.Pack", "2.3.0")));

        Assert.Equal(wanted, new ModArchives().Find(this.Root, "pathoschild.automate", "2.3.0"));
    }

    [Fact]
    public void Find_ShortVersionInManifest_MatchesSmapisFormat()
    {
        string wanted = this.WriteZip("Mod.zip", ("manifest.json", Manifest("Someone.Mod", "1.2")));

        Assert.Equal(wanted, new ModArchives().Find(this.Root, "Someone.Mod", "1.2.0"));
    }

    [Fact]
    public void Find_ManifestWithCommentsAndTrailingCommas_IsRead()
    {
        const string manifest = """
            {
                // the mod name
                "Name": "Mod",
                "uniqueid": "Someone.Mod",
                "version": "1.0.0-beta.2", /* prerelease */
            }
            """;
        string wanted = this.WriteZip("Mod.zip", ("Mod/manifest.json", Encoding.UTF8.GetBytes(manifest)));

        Assert.Equal(wanted, new ModArchives().Find(this.Root, "Someone.Mod", "1.0.0-beta.2"));
    }

    [Fact]
    public void Find_SkipsInvalidFiles_AndSubfolders()
    {
        Directory.CreateDirectory(this.Root);
        File.WriteAllText(Path.Combine(this.Root, "broken.zip"), "not a zip file");
        this.WriteZip("BadManifest.zip", ("Mod/manifest.json", "{ not json"u8.ToArray()));
        this.WriteZip(Path.Combine("downloads", "Mod.zip"), ("Mod/manifest.json", Manifest("Someone.Mod", "1.0.0")));

        Assert.Null(new ModArchives().Find(this.Root, "Someone.Mod", "1.0.0"));
    }

    [Fact]
    public void Find_MissingFolder_ReturnsNull()
    {
        Assert.Null(new ModArchives().Find(Path.Combine(this.Root, "missing"), "Someone.Mod", "1.0.0"));
    }

    [Fact]
    public void Find_ArchiveReplacedWithAnotherVersion_IsReadAgain()
    {
        ModArchives archives = new();
        string path = this.WriteZip("Mod.zip", ("Mod/manifest.json", Manifest("Someone.Mod", "1.0.0")));
        Assert.Equal(path, archives.Find(this.Root, "Someone.Mod", "1.0.0"));

        File.Delete(path);
        this.WriteZip("Mod.zip", ("Mod/manifest.json", Manifest("Someone.Mod", "1.1.0-updated")));

        Assert.Equal(path, archives.Find(this.Root, "Someone.Mod", "1.1.0-updated"));
    }

    [Fact]
    public void GetSizes_ListsOnlyModsWithAnArchiveAtTheirVersion()
    {
        string automate = this.WriteZip("Automate.zip", ("Automate/manifest.json", Manifest("Pathoschild.Automate", "2.6.1")));
        this.WriteZip("Old.zip", ("Old/manifest.json", Manifest("Someone.Old", "1.0.0")));
        ModInfo[] needed =
        {
            new("pathoschild.automate", "Automate", "2.6.1"),
            new("Someone.Old", "Old Mod", "2.0.0"),
            new("Someone.Missing", "Missing Mod", "1.0.0")
        };

        var sizes = new ModArchives().GetSizes(this.Root, needed);

        Assert.Equal(new FileInfo(automate).Length, Assert.Single(sizes).Value);
        Assert.True(sizes.ContainsKey("Pathoschild.Automate"));
    }

    [Theory]
    [InlineData("1.2", "1.2.0")]
    [InlineData("1.2.0", "1.2.0")]
    [InlineData("1", "1.0.0")]
    [InlineData("1.2-Beta", "1.2.0-beta")]
    [InlineData(" 1.2.3.4 ", "1.2.3.4")]
    public void NormalizeVersion_PadsToThreeParts(string version, string expected)
    {
        Assert.Equal(expected, ModArchives.NormalizeVersion(version));
    }

    [Theory]
    [InlineData("1.6.0", "1.7.0", -1)]
    [InlineData("1.10.0", "1.9.0", 1)]
    [InlineData("1.2", "1.2.0", 0)]
    [InlineData("2.0.0-beta", "2.0.0", -1)]
    [InlineData("2.0.0-beta.2", "2.0.0-beta.10", -1)]
    [InlineData("2.0.0-alpha", "2.0.0-beta", -1)]
    [InlineData("1.2.0+build5", "1.2.0", 0)]
    [InlineData("1.2.3.4", "1.2.3", 1)]
    [InlineData("not a version", "1.0.0", 0)]
    public void CompareVersions_OrdersLikeSmapi(string a, string b, int expected)
    {
        Assert.Equal(expected, Math.Sign(ModArchives.CompareVersions(a, b)));
        Assert.Equal(-expected, Math.Sign(ModArchives.CompareVersions(b, a)));
    }

    [Fact]
    public void DeleteSuperseded_DeletesOlderArchives_AndKeepsTheNewest()
    {
        string old1 = this.WriteZip("Mod Sync Check 1.5.0.zip", ("ModSyncCheck/manifest.json", Manifest("fnielson.ModSyncCheck", "1.5.0")));
        string old2 = this.WriteZip("ModSyncCheck-1.6.0.zip", ("ModSyncCheck/manifest.json", Manifest("fnielson.ModSyncCheck", "1.6.0")));
        string newest = this.WriteZip("ModSyncCheck-1.7.0.zip", ("ModSyncCheck/manifest.json", Manifest("FNIELSON.ModSyncCheck", "1.7.0")));
        string other = this.WriteZip("Automate.zip", ("Automate/manifest.json", Manifest("Pathoschild.Automate", "2.0.0")));

        var (deleted, failed) = new ModArchives().DeleteSuperseded(this.Root, new Dictionary<string, string>());

        Assert.Equal(new[] { old1, old2 }, deleted);
        Assert.Empty(failed);
        Assert.False(File.Exists(old1));
        Assert.False(File.Exists(old2));
        Assert.True(File.Exists(newest));
        Assert.True(File.Exists(other));
    }

    [Fact]
    public void DeleteSuperseded_KeepsTheArchiveOfTheInstalledVersion()
    {
        string installed = this.WriteZip("Mod 1.0.zip", ("Mod/manifest.json", Manifest("Someone.Mod", "1.0")));
        string older = this.WriteZip("Mod 0.9.zip", ("Mod/manifest.json", Manifest("Someone.Mod", "0.9.0")));
        string newer = this.WriteZip("Mod 1.1.zip", ("Mod/manifest.json", Manifest("Someone.Mod", "1.1.0")));

        var (deleted, _) = new ModArchives().DeleteSuperseded(this.Root, new Dictionary<string, string> { ["someone.mod"] = "1.0.0" });

        Assert.Equal(new[] { older }, deleted);
        Assert.True(File.Exists(installed));
        Assert.True(File.Exists(newer));
    }

    [Fact]
    public void GetSuperseded_ArchiveOfSeveralMods_IsKeptWhileAnyIsCurrent()
    {
        Dictionary<string, IReadOnlyList<ArchivedMod>> archives = new()
        {
            ["Bundle 1.0.zip"] = new[] { new ArchivedMod("Someone.A", "1.0.0", "A/"), new ArchivedMod("Someone.B", "1.0.0", "B/") },
            ["A 1.1.zip"] = new[] { new ArchivedMod("Someone.A", "1.1.0", "A/") },
            ["Bundle 0.9.zip"] = new[] { new ArchivedMod("Someone.A", "0.9.0", "A/"), new ArchivedMod("Someone.B", "0.9.0", "B/") },
            ["Not a mod.zip"] = Array.Empty<ArchivedMod>(),
            ["Odd 1.zip"] = new[] { new ArchivedMod("Someone.Odd", "first", "") },
            ["Odd 2.zip"] = new[] { new ArchivedMod("Someone.Odd", "second", "") }
        };

        Assert.Equal(new[] { "Bundle 0.9.zip" }, ModArchives.GetSuperseded(archives, new Dictionary<string, string>()));
    }

    [Fact]
    public void DeleteSuperseded_MissingFolder_DeletesNothing()
    {
        var (deleted, failed) = new ModArchives().DeleteSuperseded(Path.Combine(this.Root, "missing"), new Dictionary<string, string>());

        Assert.Empty(deleted);
        Assert.Empty(failed);
    }

    [Fact]
    public void GetFolderPath_Empty_IsTheWorkFolder()
    {
        string modsRoot = Path.Combine(this.Root, "Mods");

        Assert.Equal(Path.Combine(modsRoot, ".ModSyncCheck"), ModArchives.GetFolderPath(modsRoot, "  "));
    }

    [Fact]
    public void GetFolderPath_Relative_IsWithinTheModsFolder_AndAbsoluteIsKept()
    {
        string modsRoot = Path.Combine(this.Root, "Mods");
        string absolute = Path.Combine(this.Root, "Downloads");

        Assert.Equal(Path.Combine(modsRoot, "archives"), ModArchives.GetFolderPath(modsRoot, "archives"));
        Assert.Equal(absolute, ModArchives.GetFolderPath(modsRoot, absolute));
    }

    [Theory]
    [InlineData("~", "")]
    [InlineData("~/Downloads", "Downloads")]
    [InlineData("~\\Downloads", "Downloads")]
    public void GetFolderPath_StartingWithTilde_IsInTheHomeFolder(string setting, string expected)
    {
        string modsRoot = Path.Combine(this.Root, "Mods");
        string home = Path.Combine(this.Root, "home");

        Assert.Equal(Path.GetFullPath(Path.Combine(home, expected)), ModArchives.GetFolderPath(modsRoot, setting, home));
    }

    [Fact]
    public void GetStardropDownloadsFolder_IsTheNexusFolderInStardropsData()
    {
        string appData = Path.Combine(this.Root, "AppData");

        Assert.Equal(Path.Combine(appData, "Stardrop", "Data", "Nexus"), ModArchives.GetStardropDownloadsFolder(appData));
    }

    [Fact]
    public void MoveIn_FileWithoutZipExtension_IsMovedWithOne()
    {
        string downloaded = this.WriteZip("Stardrop/Automate", ("Automate/manifest.json", Manifest("Pathoschild.Automate", "2.3.0")));
        string archives = Path.Combine(this.Root, "archives");

        string? newPath = new ModArchives().MoveIn(downloaded, archives);

        Assert.Equal(Path.Combine(archives, "Automate.zip"), newPath);
        Assert.True(File.Exists(newPath));
        Assert.False(File.Exists(downloaded));
    }

    [Fact]
    public void MoveIn_NameAlreadyTaken_GetsAFreeName()
    {
        this.WriteZip("archives/Automate.zip", ("Automate/manifest.json", Manifest("Pathoschild.Automate", "2.2.0")));
        string downloaded = this.WriteZip("Stardrop/Automate.zip", ("Automate/manifest.json", Manifest("Pathoschild.Automate", "2.3.0")));
        string archives = Path.Combine(this.Root, "archives");

        Assert.Equal(Path.Combine(archives, "Automate (2).zip"), new ModArchives().MoveIn(downloaded, archives));
    }

    [Fact]
    public void MoveIn_SameVersionsAlreadyArchived_DeletesTheDownload()
    {
        this.WriteZip("archives/Automate 2.3.0.zip", ("Automate/manifest.json", Manifest("Pathoschild.Automate", "2.3.0")));
        string downloaded = this.WriteZip("Stardrop/Automate", ("Automate/manifest.json", Manifest("Pathoschild.Automate", "2.3.0")));

        Assert.Null(new ModArchives().MoveIn(downloaded, Path.Combine(this.Root, "archives")));
        Assert.False(File.Exists(downloaded));
    }

    [Fact]
    public void MoveIn_NotAZipOrNoMods_ThrowsAndLeavesTheFile()
    {
        Directory.CreateDirectory(this.Root);
        string notZip = Path.Combine(this.Root, "Mod.7z");
        File.WriteAllText(notZip, "not a zip file");
        string noMods = this.WriteZip("Readme.zip", ("readme.txt", "hi"u8.ToArray()));
        string archives = Path.Combine(this.Root, "archives");

        Assert.Throws<InvalidDataException>(() => new ModArchives().MoveIn(notZip, archives));
        Assert.Throws<InvalidDataException>(() => new ModArchives().MoveIn(noMods, archives));
        Assert.True(File.Exists(notZip));
        Assert.True(File.Exists(noMods));
    }

    [Theory]
    [InlineData("Mods.7z")]
    [InlineData("Mods-rar4.rar")]
    [InlineData("Mods-rar5.rar")]
    public void ReadMods_7zAndRar_FindsEachModAndItsFolder(string fixture)
    {
        List<ArchivedMod> mods = ModArchives.ReadMods(ArchiveFileTests.Fixture(fixture));

        Assert.Equal(new[] { new ArchivedMod("Someone.Mod", "1.2.0", "Mod/"), new ArchivedMod("Someone.Other", "2.0.0", "Other/") }, mods.OrderBy(mod => mod.Id));
    }

    [Theory]
    [InlineData("Mods.7z")]
    [InlineData("Mods-rar5.rar")]
    public void Find_7zAndRar_AreSearched(string fixture)
    {
        Directory.CreateDirectory(this.Root);
        string path = Path.Combine(this.Root, fixture);
        File.Copy(ArchiveFileTests.Fixture(fixture), path);

        Assert.Equal(path, new ModArchives().Find(this.Root, "someone.other", "2.0.0"));
    }

    [Theory]
    [InlineData("Mods.7z", "Mod.7z")]
    [InlineData("Mods-rar4.rar", "Mod.rar")]
    public void MoveIn_7zOrRarWithoutExtension_IsMovedUnchangedWithIt(string fixture, string expectedName)
    {
        Directory.CreateDirectory(Path.Combine(this.Root, "Stardrop"));
        string downloaded = Path.Combine(this.Root, "Stardrop", "Mod");
        File.Copy(ArchiveFileTests.Fixture(fixture), downloaded);
        string archives = Path.Combine(this.Root, "archives");

        string? newPath = new ModArchives().MoveIn(downloaded, archives);

        Assert.Equal(Path.Combine(archives, expectedName), newPath);
        Assert.Equal(File.ReadAllBytes(ArchiveFileTests.Fixture(fixture)), File.ReadAllBytes(newPath!));
    }

    [Fact]
    public void MoveIn_ExtensionDoesntMatchTheFormat_AddsTheRightOne()
    {
        Directory.CreateDirectory(Path.Combine(this.Root, "Stardrop"));
        string downloaded = Path.Combine(this.Root, "Stardrop", "Mod.zip");
        File.Copy(ArchiveFileTests.Fixture("Mods.7z"), downloaded);
        string archives = Path.Combine(this.Root, "archives");

        Assert.Equal(Path.Combine(archives, "Mod.zip.7z"), new ModArchives().MoveIn(downloaded, archives));
    }

    /// <summary>Get the content of a minimal manifest.</summary>
    private static byte[] Manifest(string id, string version)
    {
        return Encoding.UTF8.GetBytes($$"""{ "UniqueID": "{{id}}", "Version": "{{version}}" }""");
    }

    /// <summary>Create a zip file in the test folder.</summary>
    private string WriteZip(string fileName, params (string Path, byte[] Content)[] entries)
    {
        string zipPath = Path.Combine(this.Root, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        using ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach ((string path, byte[] content) in entries)
        {
            using Stream stream = zip.CreateEntry(path).Open();
            stream.Write(content);
        }
        return zipPath;
    }
}

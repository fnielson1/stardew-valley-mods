using System;
using System.IO;
using System.IO.Compression;
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

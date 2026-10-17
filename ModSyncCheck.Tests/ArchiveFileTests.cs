using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Xunit;

namespace ModSyncCheck.Tests;

public sealed class ArchiveFileTests : IDisposable
{
    /// <summary>The files in each test archive made by <c>Fixtures/make-fixtures.py</c>.</summary>
    private static readonly string[] FixtureFiles = { "Mod/assets/data.txt", "Mod/config.json", "Mod/content.json", "Mod/manifest.json", "Other/manifest.json", "Other/readme.txt" };

    /// <summary>A temporary folder for the test files.</summary>
    private readonly string Root = Path.Combine(Path.GetTempPath(), "ModSyncCheck.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(this.Root))
            Directory.Delete(this.Root, recursive: true);
    }

    /// <summary>Get the full path to a test archive made by <c>Fixtures/make-fixtures.py</c>.</summary>
    /// <param name="name">The file name.</param>
    public static string Fixture(string name)
    {
        return Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
    }

    [Theory]
    [InlineData("Mods.7z", ".7z")]
    [InlineData("Mods-rar4.rar", ".rar")]
    [InlineData("Mods-rar5.rar", ".rar")]
    public void DetectExtension_ReadsTheFormatFromTheContent(string fixture, string expected)
    {
        Directory.CreateDirectory(this.Root);
        string path = Path.Combine(this.Root, "no extension");
        File.Copy(ArchiveFileTests.Fixture(fixture), path);

        Assert.Equal(expected, ArchiveFile.DetectExtension(path));
    }

    [Fact]
    public void DetectExtension_Zip_And_OtherFiles()
    {
        Directory.CreateDirectory(this.Root);
        string zip = Path.Combine(this.Root, "Mod");
        using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            archive.CreateEntry("manifest.json");
        string text = Path.Combine(this.Root, "Mod.7z");
        File.WriteAllText(text, "not an archive");
        string empty = Path.Combine(this.Root, "empty.rar");
        File.WriteAllBytes(empty, Array.Empty<byte>());

        Assert.Equal(".zip", ArchiveFile.DetectExtension(zip));
        Assert.Null(ArchiveFile.DetectExtension(text));
        Assert.Null(ArchiveFile.DetectExtension(empty));
    }

    [Theory]
    [InlineData("Mods.7z")]
    [InlineData("Mods-rar4.rar")]
    [InlineData("Mods-rar5.rar")]
    public void ReadFiles_ReadsEveryFile_WithForwardSlashes(string fixture)
    {
        Dictionary<string, string> files = new();

        ArchiveFile.ReadFiles(ArchiveFileTests.Fixture(fixture), (path, open) =>
        {
            using StreamReader reader = new(open());
            files[path] = reader.ReadToEnd();
        });

        Assert.Equal(ArchiveFileTests.FixtureFiles, new SortedSet<string>(files.Keys, StringComparer.Ordinal));
        Assert.Equal("some mod data", files["Mod/assets/data.txt"]);
        Assert.Equal("""{ "Name": "Mod", "UniqueID": "Someone.Mod", "Version": "1.2.0" }""", files["Mod/manifest.json"]);
    }

    [Fact]
    public void ReadFiles_FilesWhichArentRead_AreSkipped()
    {
        List<string> paths = new();

        ArchiveFile.ReadFiles(ArchiveFileTests.Fixture("Mods.7z"), (path, _) => paths.Add(path));

        Assert.Equal(ArchiveFileTests.FixtureFiles.Length, paths.Count);
    }

    [Fact]
    public void ReadFiles_NotAnArchive_Throws()
    {
        Directory.CreateDirectory(this.Root);
        string path = Path.Combine(this.Root, "Mod.rar");
        File.WriteAllText(path, "not an archive");

        Assert.Throws<InvalidDataException>(() => ArchiveFile.ReadFiles(path, (_, _) => { }));
    }

    [Theory]
    [InlineData("Mods.7z")]
    [InlineData("Mods-rar4.rar")]
    [InlineData("Mods-rar5.rar")]
    public void ReadFiles_CutShort_Throws(string fixture)
    {
        Directory.CreateDirectory(this.Root);
        string path = Path.Combine(this.Root, fixture);
        byte[] data = File.ReadAllBytes(ArchiveFileTests.Fixture(fixture));
        File.WriteAllBytes(path, data[..(data.Length / 2)]);

        Assert.Throws<InvalidDataException>(() => ArchiveFile.ReadFiles(path, (_, open) =>
        {
            using StreamReader reader = new(open());
            reader.ReadToEnd();
        }));
    }

    [Fact]
    public void EnumerateIn_FindsEachArchiveFormat()
    {
        Directory.CreateDirectory(this.Root);
        foreach (string name in new[] { "a.zip", "b.7z", "c.RAR", "d.txt", "e" })
            File.WriteAllText(Path.Combine(this.Root, name), "");

        Assert.Equal(new[] { "a.zip", "b.7z", "c.RAR" }, new SortedSet<string>(System.Linq.Enumerable.Select(ArchiveFile.EnumerateIn(this.Root), Path.GetFileName)!, StringComparer.Ordinal));
    }
}

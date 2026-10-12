using System;
using System.IO;
using System.IO.Compression;
using Xunit;

namespace ModSyncCheck.Tests;

public sealed class ModFilesTests : IDisposable
{
    /// <summary>A temporary folder which stands in for the mods folder.</summary>
    private readonly string Root = Path.Combine(Path.GetTempPath(), "ModSyncCheck.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(this.Root))
            Directory.Delete(this.Root, recursive: true);
    }

    [Fact]
    public void CreateZip_IncludesNestedFiles_AndLeavesOutConfig()
    {
        string modDir = this.CreateMod("Host/Automate", ("manifest.json", "host"), ("assets/icon.png", "png"), ("config.json", "host settings"));
        string zipPath = Path.Combine(this.Root, "packages", "automate.zip");

        ModFiles.CreateZip(modDir, zipPath);

        using ZipArchive zip = ZipFile.OpenRead(zipPath);
        Assert.NotNull(zip.GetEntry("manifest.json"));
        Assert.NotNull(zip.GetEntry("assets/icon.png"));
        Assert.Null(zip.GetEntry("config.json"));
        Assert.Equal("host".Length + "png".Length, ModFiles.GetSize(modDir));
    }

    [Fact]
    public void Install_NewMod_CreatesFolderWithoutBackup()
    {
        string zipPath = this.CreatePackage(("manifest.json", "host"), ("assets/icon.png", "png"));
        string targetDir = Path.Combine(this.Root, "Mods", "Group", "Automate");
        string backupDir = Path.Combine(this.Root, "backup", "Automate");

        ModFiles.Install(zipPath, targetDir, Path.Combine(this.Root, "staging"), backupDir);

        Assert.Equal("host", File.ReadAllText(Path.Combine(targetDir, "manifest.json")));
        Assert.Equal("png", File.ReadAllText(Path.Combine(targetDir, "assets", "icon.png")));
        Assert.False(Directory.Exists(backupDir));
        Assert.False(Directory.Exists(Path.Combine(this.Root, "staging")));
    }

    [Fact]
    public void Install_ExistingMod_BacksUpOldVersion_AndKeepsConfig()
    {
        string targetDir = this.CreateMod("Mods/Automate", ("manifest.json", "old"), ("old-file.txt", "old"), ("config.json", "my settings"));
        string zipPath = this.CreatePackage(("manifest.json", "new"));
        string backupDir = Path.Combine(this.Root, "backup", "Automate");

        ModFiles.Install(zipPath, targetDir, Path.Combine(this.Root, "staging"), backupDir);

        Assert.Equal("new", File.ReadAllText(Path.Combine(targetDir, "manifest.json")));
        Assert.Equal("my settings", File.ReadAllText(Path.Combine(targetDir, "config.json")));
        Assert.False(File.Exists(Path.Combine(targetDir, "old-file.txt")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(backupDir, "manifest.json")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(backupDir, "old-file.txt")));
    }

    [Fact]
    public void Install_PackageWithPathOutsideFolder_ThrowsAndLeavesExistingMod()
    {
        string targetDir = this.CreateMod("Mods/Automate", ("manifest.json", "old"));
        string zipPath = Path.Combine(this.Root, "evil.zip");
        using (ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            using StreamWriter writer = new(zip.CreateEntry("../../evil.txt").Open());
            writer.Write("evil");
        }

        Assert.Throws<InvalidDataException>(() => ModFiles.Install(zipPath, targetDir, Path.Combine(this.Root, "work", "staging"), Path.Combine(this.Root, "backup", "Automate")));

        Assert.Equal("old", File.ReadAllText(Path.Combine(targetDir, "manifest.json")));
        Assert.False(File.Exists(Path.Combine(this.Root, "evil.txt")));
    }

    [Theory]
    [InlineData("Automate", "Automate")]
    [InlineData("Stardew Valley Expanded\\[CP] Stardew Valley Expanded", "Stardew Valley Expanded/[CP] Stardew Valley Expanded")]
    [InlineData("Group/Automate", "Group/Automate")]
    public void ResolveInstallPath_ValidPath_IsWithinModsFolder(string relativePath, string expected)
    {
        string modsRoot = Path.Combine(this.Root, "Mods");

        string path = ModFiles.ResolveInstallPath(modsRoot, relativePath);

        Assert.Equal(Path.GetFullPath(Path.Combine(modsRoot, expected)), Path.GetFullPath(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("../Automate")]
    [InlineData("Group/../../Automate")]
    [InlineData("C:\\Windows")]
    [InlineData("/etc")]
    [InlineData(".ModSyncCheck/backup")]
    [InlineData("Automate/")]
    public void ResolveInstallPath_UnsafePath_Throws(string relativePath)
    {
        Assert.Throws<InvalidDataException>(() => ModFiles.ResolveInstallPath(Path.Combine(this.Root, "Mods"), relativePath));
    }

    /// <summary>Create a mod folder containing the given files.</summary>
    private string CreateMod(string relativePath, params (string Path, string Content)[] files)
    {
        string modDir = Path.Combine(this.Root, relativePath);
        foreach ((string path, string content) in files)
        {
            string fullPath = Path.Combine(modDir, path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
        }
        return modDir;
    }

    /// <summary>Create a packaged mod containing the given files.</summary>
    private string CreatePackage(params (string Path, string Content)[] files)
    {
        string zipPath = Path.Combine(this.Root, "packages", $"{Guid.NewGuid():N}.zip");
        ModFiles.CreateZip(this.CreateMod($"source/{Guid.NewGuid():N}", files), zipPath);
        return zipPath;
    }
}

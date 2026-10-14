using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
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
    public void CreateZip_ConfigOnly_IncludesOnlyConfigFiles()
    {
        string modDir = this.CreateMod("Host/Pack", ("manifest.json", "host"), ("config.json", "root"), ("[CP] Pack/config.json", "nested"), ("[CP] Pack/content.json", "content"));
        string zipPath = Path.Combine(this.Root, "packages", "pack.zip");

        ModFiles.CreateZip(modDir, zipPath, PackageContent.ConfigOnly);

        using ZipArchive zip = ZipFile.OpenRead(zipPath);
        Assert.Equal(new[] { "[CP] Pack/config.json", "config.json" }, zip.Entries.Select(entry => entry.FullName).OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void GetConfigHashes_ListsEverySettingsFileByRelativePath()
    {
        string modDir = this.CreateMod("Host/Pack", ("manifest.json", "host"), ("config.json", "{ \"A\": 1 }"), ("[CP] Pack/config.json", "{ \"A\": 2 }"));

        Dictionary<string, string> hashes = ModFiles.GetConfigHashes(modDir);

        Assert.Equal(new[] { "[CP] Pack/config.json", "config.json" }, hashes.Keys.OrderBy(name => name, StringComparer.Ordinal));
        Assert.NotEqual(hashes["config.json"], hashes["[CP] Pack/config.json"]);
        Assert.Equal(hashes["config.json"], ModFiles.GetConfigHash(Path.Combine(modDir, "config.json")));
    }

    [Fact]
    public void GetConfigHashes_ModWithoutSettings_IsEmpty()
    {
        Assert.Empty(ModFiles.GetConfigHashes(this.CreateMod("Host/Automate", ("manifest.json", "host"))));
    }

    [Fact]
    public void GetConfigHash_SameSettings_FormattedDifferently_Match()
    {
        string hostDir = this.CreateMod("Host/Automate", ("config.json", "{\r\n    \"Enabled\": true,\r\n    \"Speed\": 2\r\n}"));
        string playerDir = this.CreateMod("Mods/Automate", ("config.json", "﻿{\"Enabled\":true,\"Speed\":2}\n"));

        Assert.Equal(ModFiles.GetConfigHash(Path.Combine(hostDir, "config.json")), ModFiles.GetConfigHash(Path.Combine(playerDir, "config.json")));
    }

    [Fact]
    public void GetConfigHash_DifferentValue_DoesNotMatch()
    {
        string hostDir = this.CreateMod("Host/Automate", ("config.json", "{ \"Speed\": 2 }"));
        string playerDir = this.CreateMod("Mods/Automate", ("config.json", "{ \"Speed\": 3 }"));

        Assert.NotEqual(ModFiles.GetConfigHash(Path.Combine(hostDir, "config.json")), ModFiles.GetConfigHash(Path.Combine(playerDir, "config.json")));
    }

    [Fact]
    public void TryExtractConfig_ExtractsTheModsOwnSettings_NotABundledPacks()
    {
        string hostDir = this.CreateMod("Host/Pack", ("config.json", "root"), ("[CP] Pack/config.json", "nested"));
        string zipPath = Path.Combine(this.Root, "packages", "pack.zip");
        string path = Path.Combine(this.Root, "extracted", "host.json");
        ModFiles.CreateZip(hostDir, zipPath, PackageContent.ConfigOnly);

        Assert.True(ModFiles.TryExtractConfig(zipPath, path));
        Assert.Equal("root", File.ReadAllText(path));
    }

    [Fact]
    public void TryExtractConfig_NoSettingsInTheModsOwnFolder_ReturnsFalse()
    {
        string hostDir = this.CreateMod("Host/Pack", ("manifest.json", "host"), ("[CP] Pack/config.json", "nested"));
        string zipPath = Path.Combine(this.Root, "packages", "pack.zip");
        string path = Path.Combine(this.Root, "extracted", "host.json");
        ModFiles.CreateZip(hostDir, zipPath, PackageContent.ConfigOnly);

        Assert.False(ModFiles.TryExtractConfig(zipPath, path));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Install_PackageContainingSettings_NeverReplacesThePlayersSettings()
    {
        string targetDir = this.CreateMod("Mods/Automate", ("manifest.json", "old"), ("config.json", "my settings"));
        string zipPath = Path.Combine(this.Root, "packages", "automate.zip");
        string hostDir = this.CreateMod("Host/Automate", ("manifest.json", "new"), ("config.json", "host settings"));
        Directory.CreateDirectory(Path.Combine(this.Root, "packages"));
        using (ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(Path.Combine(hostDir, "manifest.json"), "manifest.json");
            zip.CreateEntryFromFile(Path.Combine(hostDir, "config.json"), "config.json");
        }

        ModFiles.Install(zipPath, targetDir, Path.Combine(this.Root, "staging"), Path.Combine(this.Root, "backup", "Automate"));

        Assert.Equal("new", File.ReadAllText(Path.Combine(targetDir, "manifest.json")));
        Assert.Equal("my settings", File.ReadAllText(Path.Combine(targetDir, "config.json")));
    }

    [Fact]
    public void RestoreFile_Unchanged_LeavesItAlone()
    {
        string path = Path.Combine(this.CreateMod("Mods/Automate", ("config.json", "mine")), "config.json");
        DateTime written = File.GetLastWriteTimeUtc(path);

        Assert.False(ModFiles.RestoreFile(path, File.ReadAllBytes(path)));
        Assert.Equal(written, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void RestoreFile_Changed_PutsTheOriginalBack()
    {
        string path = Path.Combine(this.CreateMod("Mods/Automate", ("config.json", "mine")), "config.json");
        byte[] original = File.ReadAllBytes(path);
        File.WriteAllText(path, "host settings");

        Assert.True(ModFiles.RestoreFile(path, original));
        Assert.Equal("mine", File.ReadAllText(path));
    }

    [Fact]
    public void RestoreFile_Deleted_PutsTheOriginalBack()
    {
        string path = Path.Combine(this.CreateMod("Mods/Automate", ("config.json", "mine")), "config.json");
        byte[] original = File.ReadAllBytes(path);
        File.Delete(path);

        Assert.True(ModFiles.RestoreFile(path, original));
        Assert.Equal("mine", File.ReadAllText(path));
    }

    [Fact]
    public void RestoreFile_DidntExistBefore_DeletesIt()
    {
        string path = Path.Combine(this.CreateMod("Mods/Automate", ("manifest.json", "mod")), "config.json");

        Assert.False(ModFiles.RestoreFile(path, null));
        File.WriteAllText(path, "host settings");
        Assert.True(ModFiles.RestoreFile(path, null));
        Assert.False(File.Exists(path));
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
    public void Install_ExistingMod_KeepsNestedConfig_OnlyWhereTheFolderStillExists()
    {
        string targetDir = this.CreateMod("Mods/Pack", ("manifest.json", "old"), ("[CP] Pack/config.json", "kept"), ("removed/config.json", "dropped"));
        string zipPath = this.CreatePackage(("manifest.json", "new"), ("[CP] Pack/content.json", "new"));

        ModFiles.Install(zipPath, targetDir, Path.Combine(this.Root, "staging"), Path.Combine(this.Root, "backup", "Pack"));

        Assert.Equal("kept", File.ReadAllText(Path.Combine(targetDir, "[CP] Pack", "config.json")));
        Assert.False(Directory.Exists(Path.Combine(targetDir, "removed")));
        Assert.Equal("dropped", File.ReadAllText(Path.Combine(this.Root, "backup", "Pack", "removed", "config.json")));
    }

    [Fact]
    public void Install_LeftoverStagingFolder_IsReplaced()
    {
        string stagingDir = this.CreateMod("staging", ("leftover.txt", "leftover"));
        string zipPath = this.CreatePackage(("manifest.json", "new"));
        string targetDir = Path.Combine(this.Root, "Mods", "Automate");

        ModFiles.Install(zipPath, targetDir, stagingDir, Path.Combine(this.Root, "backup", "Automate"));

        Assert.Equal("new", File.ReadAllText(Path.Combine(targetDir, "manifest.json")));
        Assert.False(File.Exists(Path.Combine(targetDir, "leftover.txt")));
    }

    [Fact]
    public void CreateZipThenInstall_KeepsBinaryFilesIntact()
    {
        byte[] data = new byte[300_000];
        new Random(1).NextBytes(data);
        string modDir = this.CreateMod("Host/Automate", ("manifest.json", "host"));
        File.WriteAllBytes(Path.Combine(modDir, "Automate.dll"), data);
        string zipPath = Path.Combine(this.Root, "packages", "automate.zip");
        string targetDir = Path.Combine(this.Root, "Mods", "Automate");

        ModFiles.CreateZip(modDir, zipPath);
        ModFiles.Install(zipPath, targetDir, Path.Combine(this.Root, "staging"), Path.Combine(this.Root, "backup", "Automate"));

        Assert.Equal(data, File.ReadAllBytes(Path.Combine(targetDir, "Automate.dll")));
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
    [InlineData("Auto:mate")]
    [InlineData("Auto?mate")]
    [InlineData("/etc")]
    [InlineData(".ModSyncCheck/backup")]
    [InlineData("Automate/")]
    public void ResolveInstallPath_UnsafePath_Throws(string relativePath)
    {
        Assert.Throws<InvalidDataException>(() => ModFiles.ResolveInstallPath(Path.Combine(this.Root, "Mods"), relativePath));
    }

    [Fact]
    public void Stage_ExistingMod_LeavesItUntouched_AndCopiesItsConfig()
    {
        string targetDir = this.CreateMod("Mods/Automate", ("manifest.json", "old"), ("config.json", "my settings"));
        string zipPath = this.CreatePackage(("manifest.json", "new"));
        string stagingDir = Path.Combine(this.Root, "pending", "0");

        ModFiles.Stage(zipPath, targetDir, stagingDir);

        Assert.Equal("old", File.ReadAllText(Path.Combine(targetDir, "manifest.json")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(stagingDir, "manifest.json")));
        Assert.Equal("my settings", File.ReadAllText(Path.Combine(stagingDir, "config.json")));
    }

    [Fact]
    public void StageThenSwap_ReplacesTheMod_AndBacksUpTheOldVersion()
    {
        string targetDir = this.CreateMod("Mods/Automate", ("manifest.json", "old"));
        string stagingDir = Path.Combine(this.Root, "pending", "0");
        string backupDir = Path.Combine(this.Root, "backup", "Automate");
        ModFiles.Stage(this.CreatePackage(("manifest.json", "new")), targetDir, stagingDir);

        ModFiles.Swap(targetDir, stagingDir, backupDir);

        Assert.Equal("new", File.ReadAllText(Path.Combine(targetDir, "manifest.json")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(backupDir, "manifest.json")));
        Assert.False(Directory.Exists(stagingDir));
    }

    [Fact]
    public void AppendPendingSwaps_AddsOneTabSeparatedLinePerMod()
    {
        string listPath = Path.Combine(this.Root, "work", "pending.txt");

        ModFiles.AppendPendingSwaps(listPath, new[] { new PendingSwap("Mods/A", "pending/0", "backup/0 - A") });
        ModFiles.AppendPendingSwaps(listPath, new[] { new PendingSwap("Mods/[CP] B", "pending/1", "backup/1 - [CP] B") });

        Assert.Equal(new[] { "Mods/A\tpending/0\tbackup/0 - A", "Mods/[CP] B\tpending/1\tbackup/1 - [CP] B" }, File.ReadAllLines(listPath));
    }

    [Theory]
    [InlineData("Mods/Auto\tmate")]
    [InlineData("Mods/Auto\nmate")]
    [InlineData("")]
    public void AppendPendingSwaps_PathWhichCantBeListed_ThrowsAndWritesNothing(string targetDir)
    {
        string listPath = Path.Combine(this.Root, "work", "pending.txt");

        Assert.Throws<InvalidDataException>(() => ModFiles.AppendPendingSwaps(listPath, new[] { new PendingSwap("Mods/A", "pending/0", "backup/A"), new PendingSwap(targetDir, "pending/1", "backup/B") }));

        Assert.False(File.Exists(listPath));
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

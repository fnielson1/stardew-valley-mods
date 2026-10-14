using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Xunit;

namespace ModSyncCheck.Tests;

public sealed class GameRestarterTests : IDisposable
{
    /// <summary>A temporary folder for the test files.</summary>
    private readonly string Root = Path.Combine(Path.GetTempPath(), "ModSyncCheck.Tests", Guid.NewGuid().ToString("N"));

    public GameRestarterTests()
    {
        Directory.CreateDirectory(this.Root);
    }

    public void Dispose()
    {
        // the launched program may still be using the folder for a moment
        for (int attempt = 0; attempt < 20 && Directory.Exists(this.Root); attempt++)
        {
            try
            {
                Directory.Delete(this.Root, recursive: true);
            }
            catch (IOException)
            {
                Thread.Sleep(100);
            }
        }
    }

    [Fact]
    public void BuildWaiter_Windows_RunsHiddenPowerShellWithTheLaunchDetails()
    {
        ProcessStartInfo startInfo = GameRestarter.BuildWaiter("C:\\Game Folder\\StardewModdingAPI.exe", "C:\\Game Folder", new[] { "--mods-path", "My Mods" }, 1234, isWindows: true);

        Assert.Equal("powershell", startInfo.FileName);
        Assert.True(startInfo.CreateNoWindow);
        Assert.False(startInfo.UseShellExecute);
        Assert.Equal("1234", startInfo.Environment["MODSYNC_PID"]);
        Assert.Equal("C:\\Game Folder\\StardewModdingAPI.exe", startInfo.Environment["MODSYNC_EXE"]);
        Assert.Equal("C:\\Game Folder", startInfo.Environment["MODSYNC_DIR"]);
        Assert.Equal("--mods-path \"My Mods\"", startInfo.Environment["MODSYNC_ARGS"]);
    }

    [Fact]
    public void BuildWaiter_Unix_RunsShellWithQuotedArguments()
    {
        ProcessStartInfo startInfo = GameRestarter.BuildWaiter("/games/Stardew Valley/StardewModdingAPI", "/games/Stardew Valley", new[] { "--mods-path", "Frank's Mods" }, 1234, isWindows: false);

        Assert.Equal("/bin/sh", startInfo.FileName);
        Assert.Equal("--mods-path 'Frank'\\''s Mods'", startInfo.Environment["MODSYNC_ARGS"]);
    }

    [Fact]
    public void BuildWaiter_NoArguments_LeavesThemEmpty()
    {
        ProcessStartInfo startInfo = GameRestarter.BuildWaiter("C:\\Game\\StardewModdingAPI.exe", "C:\\Game", Array.Empty<string>(), 1234, isWindows: true);

        Assert.Equal("", startInfo.Environment["MODSYNC_ARGS"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Waiter_MovesStagedModsIntoPlace_AndOnlyLaunchesIfAsked(bool restart)
    {
        bool isWindows = OperatingSystem.IsWindows();

        // an updated mod whose folder name has wildcard characters, a new mod, and a mod whose staged folder is gone
        string updatedDir = this.CreateFolder("Mods/[CP] Pack", "old");
        string newDir = Path.Combine(this.Root, "Mods", "Group", "New Mod");
        string skippedDir = this.CreateFolder("Mods/Skipped", "old");
        string listPath = Path.Combine(this.Root, "work", "pending.txt");
        string flagPath = Path.Combine(this.Root, "work", "restart");
        string backupDir = Path.Combine(this.Root, "work", "backup", "0 - [CP] Pack");
        ModFiles.AppendPendingSwaps(listPath, new[]
        {
            new PendingSwap(updatedDir, this.CreateFolder("work/pending/0", "new"), backupDir),
            new PendingSwap(newDir, this.CreateFolder("work/pending/1", "new"), Path.Combine(this.Root, "work", "backup", "1 - New Mod")),
            new PendingSwap(skippedDir, Path.Combine(this.Root, "work", "pending", "2"), Path.Combine(this.Root, "work", "backup", "2 - Skipped"))
        });
        if (restart)
            File.WriteAllText(flagPath, "");
        string markerPath = Path.Combine(this.Root, "marker.txt");

        // a shell stands in for the game: it writes a marker file when launched, in the working folder it was given
        ProcessStartInfo watchedInfo = isWindows
            ? new ProcessStartInfo("cmd", "/c ping -n 2 127.0.0.1 >nul")
            : new ProcessStartInfo("/bin/sh", "-c \"sleep 1\"");
        watchedInfo.CreateNoWindow = true;
        watchedInfo.UseShellExecute = false;
        using Process watched = Process.Start(watchedInfo)!;
        ProcessStartInfo startInfo = isWindows
            ? GameRestarter.BuildWaiter(Path.Combine(Environment.SystemDirectory, "cmd.exe"), this.Root, new[] { "/c", "echo.>marker.txt" }, watched.Id, isWindows, listPath, flagPath)
            : GameRestarter.BuildWaiter("/bin/sh", this.Root, new[] { "-c", "echo launched > marker.txt" }, watched.Id, isWindows, listPath, flagPath);
        using Process waiter = Process.Start(startInfo)!;
        Assert.True(waiter.WaitForExit(30_000), "The waiter didn't finish.");
        for (int i = 0; restart && i < 50 && !File.Exists(markerPath); i++)
            Thread.Sleep(100);

        Assert.Equal("new", File.ReadAllText(Path.Combine(updatedDir, "manifest.json")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(backupDir, "manifest.json")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(newDir, "manifest.json")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(skippedDir, "manifest.json")));
        Assert.False(File.Exists(listPath));
        Assert.False(File.Exists(flagPath));
        Assert.Equal(restart, File.Exists(markerPath));
    }

    /// <summary>Create a folder containing a manifest with the given content.</summary>
    private string CreateFolder(string relativePath, string content)
    {
        string path = Path.Combine(this.Root, relativePath);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "manifest.json"), content);
        return path;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Waiter_LaunchesTheProgram_AfterTheWatchedProcessExits(bool withArgumentContainingSpace)
    {
        if (!OperatingSystem.IsWindows())
            return;

        // 'cmd' stands in for the game: it writes a marker file when launched, in the working folder it was given
        string[] args = withArgumentContainingSpace
            ? new[] { "/c", "echo launched>marker.txt" }
            : new[] { "/c", "echo.>marker.txt" };
        string markerPath = Path.Combine(this.Root, "marker.txt");

        using Process watched = Process.Start(new ProcessStartInfo("cmd", "/c ping -n 3 127.0.0.1 >nul") { CreateNoWindow = true, UseShellExecute = false })!;
        ProcessStartInfo startInfo = GameRestarter.BuildWaiter(Path.Combine(Environment.SystemDirectory, "cmd.exe"), this.Root, args, watched.Id, isWindows: true);

        using Process waiter = Process.Start(startInfo)!;
        Thread.Sleep(500);
        Assert.False(File.Exists(markerPath), "The program was launched before the watched process exited.");

        Assert.True(waiter.WaitForExit(30_000), "The waiter didn't finish.");
        Assert.True(watched.HasExited);
        for (int i = 0; i < 50 && !File.Exists(markerPath); i++)
            Thread.Sleep(100);
        Assert.True(File.Exists(markerPath), "The program wasn't launched in the working folder.");
    }
}

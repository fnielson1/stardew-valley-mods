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

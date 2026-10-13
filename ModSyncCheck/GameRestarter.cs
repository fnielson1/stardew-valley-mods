using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace ModSyncCheck;

/// <summary>Relaunches the game after it exits.</summary>
internal static class GameRestarter
{
    /// <summary>The script which waits for the game to exit and launches it again on Windows.</summary>
    private const string WindowsScript = "Wait-Process -Id $env:MODSYNC_PID -ErrorAction SilentlyContinue; $launch = @{ FilePath = $env:MODSYNC_EXE; WorkingDirectory = $env:MODSYNC_DIR }; if ($env:MODSYNC_ARGS) { $launch.ArgumentList = $env:MODSYNC_ARGS }; Start-Process @launch";

    /// <summary>The script which waits for the game to exit and launches it again on Linux and macOS.</summary>
    private const string UnixScript = "while kill -0 \"$MODSYNC_PID\" 2>/dev/null; do sleep 0.5; done; cd \"$MODSYNC_DIR\" && eval \"exec \\\"\\$MODSYNC_EXE\\\" $MODSYNC_ARGS\"";

    /// <summary>Start a background process which launches the game again with the same options once the current process exits. The caller should then exit the game.</summary>
    public static void RestartAfterExit()
    {
        string exePath = Environment.ProcessPath ?? throw new InvalidOperationException("Can't find the path of the running game.");
        ProcessStartInfo startInfo = GameRestarter.BuildWaiter(
            exePath: exePath,
            workingDir: Path.GetDirectoryName(exePath)!,
            args: Environment.GetCommandLineArgs().Skip(1),
            processId: Environment.ProcessId,
            isWindows: OperatingSystem.IsWindows()
        );
        Process.Start(startInfo)?.Dispose();
    }

    /// <summary>Get the background process to start, which waits for a process to exit and then launches it again.</summary>
    /// <param name="exePath">The path of the executable to launch.</param>
    /// <param name="workingDir">The folder to launch it from.</param>
    /// <param name="args">The command-line arguments to launch it with.</param>
    /// <param name="processId">The ID of the process to wait for.</param>
    /// <param name="isWindows">Whether to use the Windows shell.</param>
    public static ProcessStartInfo BuildWaiter(string exePath, string workingDir, IEnumerable<string> args, int processId, bool isWindows)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = isWindows ? "powershell" : "/bin/sh",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // the values are passed through the environment so they don't need to be escaped for the script
        startInfo.Environment["MODSYNC_PID"] = processId.ToString();
        startInfo.Environment["MODSYNC_EXE"] = exePath;
        startInfo.Environment["MODSYNC_DIR"] = workingDir;
        startInfo.Environment["MODSYNC_ARGS"] = string.Join(" ", args.Select(arg => GameRestarter.Quote(arg, isWindows)));

        if (isWindows)
        {
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add(GameRestarter.WindowsScript);
        }
        else
        {
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(GameRestarter.UnixScript);
        }
        return startInfo;
    }

    /// <summary>Quote a command-line argument so it's passed on as one argument.</summary>
    /// <param name="arg">The argument value.</param>
    /// <param name="isWindows">Whether to quote it for the Windows shell.</param>
    private static string Quote(string arg, bool isWindows)
    {
        if (arg.Length > 0 && arg.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' or '/' or '\\' or ':' or '='))
            return arg;

        return isWindows
            ? "\"" + arg.Replace("\"", "\\\"") + "\""
            : "'" + arg.Replace("'", "'\\''") + "'";
    }
}

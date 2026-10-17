using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace ModSyncCheck;

/// <summary>Installs downloaded mods and relaunches the game after it exits.</summary>
internal static class GameRestarter
{
    /// <summary>The script which waits for the game to exit, moves the staged mods into place, and launches the game again on Windows.</summary>
    /// <remarks>A staged mod which can't be moved into place, like when a file in its folder is in use, is left in the list so it's tried again the next time the game closes. This uses .NET methods for the folders since PowerShell's own commands treat the square brackets in a mod folder name as wildcards. Links are directory junctions, like those mod managers make, since unlike symbolic links they don't need administrator rights.</remarks>
    private const string WindowsScript =
        "Wait-Process -Id $env:MODSYNC_PID -ErrorAction SilentlyContinue; "
        + "if ($env:MODSYNC_SWAPS -and [IO.File]::Exists($env:MODSYNC_SWAPS)) { "
        + "$retry = [Collections.Generic.List[string]]::new(); "
        + "foreach ($line in [IO.File]::ReadAllLines($env:MODSYNC_SWAPS)) { "
        + "$target, $staged, $backup, $link = $line.Split([char]9); "
        + "if (-not $backup -or -not [IO.Directory]::Exists($staged)) { continue }; "
        + "try { "
        + "if ([IO.Directory]::Exists($target)) { [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($backup)); [IO.Directory]::Move($target, $backup) }; "
        + "[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)); "
        + "[IO.Directory]::Move($staged, $target); "
        + "if ($link -and -not [IO.Directory]::Exists($link)) { [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($link)); cmd /c mklink /J \"$link\" \"$target\" | Out-Null } "
        + "} catch { if (-not [IO.Directory]::Exists($target) -and [IO.Directory]::Exists($backup)) { [IO.Directory]::Move($backup, $target) }; $retry.Add($line) } "
        + "}; "
        + "if ($retry.Count) { [IO.File]::WriteAllLines($env:MODSYNC_SWAPS, $retry) } else { [IO.File]::Delete($env:MODSYNC_SWAPS) } }; "
        + "$restart = -not $env:MODSYNC_RESTART -or [IO.File]::Exists($env:MODSYNC_RESTART); "
        + "if ($env:MODSYNC_RESTART) { [IO.File]::Delete($env:MODSYNC_RESTART) }; "
        + "if ($restart) { $launch = @{ FilePath = $env:MODSYNC_EXE; WorkingDirectory = $env:MODSYNC_DIR }; if ($env:MODSYNC_ARGS) { $launch.ArgumentList = $env:MODSYNC_ARGS }; Start-Process @launch }";

    /// <summary>The script which waits for the game to exit, moves the staged mods into place, and launches the game again on Linux and macOS.</summary>
    /// <remarks>A staged mod which can't be moved into place is left in the list, as on Windows. On Linux, SMAPI runs in a terminal window which closes when the game exits, hanging up on the processes it started; so the script ignores that until it launches the game, and doesn't use the closed terminal for its input or output.</remarks>
    private const string UnixScript =
        "trap '' HUP; exec </dev/null >/dev/null 2>&1; "
        + "while kill -0 \"$MODSYNC_PID\" 2>/dev/null; do sleep 0.5; done; "
        + "if [ -n \"$MODSYNC_SWAPS\" ] && [ -f \"$MODSYNC_SWAPS\" ]; then "
        + "retry=\"$MODSYNC_SWAPS.retry\"; rm -f \"$retry\"; "
        + "while IFS=\"$(printf '\\t')\" read -r target staged backup link; do "
        + "backup=\"${backup%\"$(printf '\\r')\"}\"; link=\"${link%\"$(printf '\\r')\"}\"; "
        + "if [ -z \"$backup\" ] || [ ! -d \"$staged\" ]; then continue; fi; "
        + "if [ -d \"$target\" ]; then mkdir -p \"$(dirname \"$backup\")\" && mv \"$target\" \"$backup\" || { printf '%s\\t%s\\t%s\\t%s\\n' \"$target\" \"$staged\" \"$backup\" \"$link\" >> \"$retry\"; continue; }; fi; "
        + "mkdir -p \"$(dirname \"$target\")\"; "
        + "if mv \"$staged\" \"$target\"; then "
        + "if [ -n \"$link\" ] && [ ! -e \"$link\" ]; then mkdir -p \"$(dirname \"$link\")\" && ln -s \"$target\" \"$link\"; fi; "
        + "else [ -d \"$backup\" ] && mv \"$backup\" \"$target\"; printf '%s\\t%s\\t%s\\t%s\\n' \"$target\" \"$staged\" \"$backup\" \"$link\" >> \"$retry\"; fi; "
        + "done < \"$MODSYNC_SWAPS\"; "
        + "if [ -s \"$retry\" ]; then mv -f \"$retry\" \"$MODSYNC_SWAPS\"; else rm -f \"$MODSYNC_SWAPS\" \"$retry\"; fi; fi; "
        + "if [ -n \"$MODSYNC_RESTART\" ]; then if [ -f \"$MODSYNC_RESTART\" ]; then rm -f \"$MODSYNC_RESTART\"; else exit 0; fi; fi; "
        + "trap - HUP; "
        + "cd \"$MODSYNC_DIR\" && eval \"exec \\\"\\$MODSYNC_EXE\\\" $MODSYNC_ARGS\"";

    /// <summary>The name of the script SMAPI installs in the game folder on Linux and macOS, which launches it in a terminal window.</summary>
    private const string UnixLauncherName = "StardewValley";

    /// <summary>Whether a background process is waiting for the game to exit to move the staged mods into place.</summary>
    public static bool IsWaitingToInstall { get; private set; }

    /// <summary>Start a background process which launches the game again with the same options once the current process exits. The caller should then exit the game.</summary>
    public static void RestartAfterExit()
    {
        GameRestarter.StartWaiter(swapListPath: null, restartFlagPath: null);
    }

    /// <summary>Start a background process which moves the staged mods into place once the current process exits, since the game is still using the installed ones. It then launches the game again with the same options if the player asked it to. This does nothing if that process was already started.</summary>
    /// <param name="swapListPath">The file listing the staged mods, written by <see cref="ModFiles.AppendPendingSwaps"/>. It's read when the game exits, so mods can still be added to it.</param>
    /// <param name="restartFlagPath">The file which is created to launch the game again.</param>
    public static void InstallAfterExit(string swapListPath, string restartFlagPath)
    {
        if (GameRestarter.IsWaitingToInstall)
            return;

        File.Delete(restartFlagPath);
        GameRestarter.StartWaiter(swapListPath, restartFlagPath);
        GameRestarter.IsWaitingToInstall = true;
    }

    /// <summary>Start the background process which waits for the current process to exit.</summary>
    /// <param name="swapListPath">The file listing the staged mods to move into place, if any.</param>
    /// <param name="restartFlagPath">The file which must exist for the game to be launched again, or <c>null</c> to always launch it.</param>
    private static void StartWaiter(string? swapListPath, string? restartFlagPath)
    {
        string exePath = Environment.ProcessPath ?? throw new InvalidOperationException("Can't find the path of the running game.");
        ProcessStartInfo startInfo = GameRestarter.BuildWaiter(
            exePath: OperatingSystem.IsWindows() ? exePath : GameRestarter.GetUnixLauncher(exePath) ?? exePath,
            workingDir: Path.GetDirectoryName(exePath)!,
            args: Environment.GetCommandLineArgs().Skip(1),
            processId: Environment.ProcessId,
            isWindows: OperatingSystem.IsWindows(),
            swapListPath: swapListPath,
            restartFlagPath: restartFlagPath
        );
        Process.Start(startInfo)?.Dispose();
    }

    /// <summary>Get the background process to start, which waits for a process to exit and then launches it again.</summary>
    /// <param name="exePath">The path of the executable to launch.</param>
    /// <param name="workingDir">The folder to launch it from.</param>
    /// <param name="args">The command-line arguments to launch it with.</param>
    /// <param name="processId">The ID of the process to wait for.</param>
    /// <param name="isWindows">Whether to use the Windows shell.</param>
    /// <param name="swapListPath">The file listing the staged mods to move into place before launching, if any.</param>
    /// <param name="restartFlagPath">The file which must exist once the process exits for it to be launched again, or <c>null</c> to always launch it.</param>
    public static ProcessStartInfo BuildWaiter(string exePath, string workingDir, IEnumerable<string> args, int processId, bool isWindows, string? swapListPath = null, string? restartFlagPath = null)
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
        startInfo.Environment["MODSYNC_SWAPS"] = swapListPath ?? "";
        startInfo.Environment["MODSYNC_RESTART"] = restartFlagPath ?? "";

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

    /// <summary>Get SMAPI's launcher script next to the running game on Linux or macOS, if it's there. Launching that instead of the game opens SMAPI in a terminal window again, as when the game was first started.</summary>
    /// <param name="exePath">The path of the running game.</param>
    /// <remarks>Without SMAPI installed, a script with the same name launches the game without mods, so the script is only used if it launches SMAPI.</remarks>
    public static string? GetUnixLauncher(string exePath)
    {
        string path = Path.Combine(Path.GetDirectoryName(exePath)!, GameRestarter.UnixLauncherName);
        if (path == exePath || !File.Exists(path) || new FileInfo(path).Length > 64 * 1024)
            return null;

        try
        {
            return File.ReadAllText(path).Contains("StardewModdingAPI") ? path : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
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

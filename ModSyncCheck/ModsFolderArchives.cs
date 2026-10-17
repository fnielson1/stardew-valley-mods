using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace ModSyncCheck;

/// <summary>Installs the mods in archives (<c>.zip</c>, <c>.7z</c>, or <c>.rar</c> files) put directly in the mods folder, so players don't need to unpack them, and moves each file into the archives folder so it can be sent to joining players later.</summary>
internal class ModsFolderArchives
{
    /*********
    ** Fields
    *********/
    /// <summary>The SMAPI API for this mod.</summary>
    private readonly IModHelper Helper;

    /// <summary>Writes messages to the SMAPI log.</summary>
    private readonly IMonitor Monitor;

    /// <summary>This mod's unique ID.</summary>
    private readonly string ModId;

    /// <summary>The mod settings.</summary>
    private readonly ModConfig Config;

    /// <summary>Restarts the game once the mods are installed.</summary>
    private readonly ClientSync ClientSync;

    /// <summary>Finds the archives already in the archives folder.</summary>
    private readonly ModArchives Archives = new();

    /// <summary>The task unpacking the archives, which returns the text to show the player, or <c>null</c> if there's nothing to show.</summary>
    private Task<(string Text, bool Installed)?>? InstallTask;


    /*********
    ** Public methods
    *********/
    /// <summary>Construct an instance.</summary>
    /// <param name="helper">The SMAPI API for this mod.</param>
    /// <param name="monitor">Writes messages to the SMAPI log.</param>
    /// <param name="modId">This mod's unique ID.</param>
    /// <param name="config">The mod settings.</param>
    /// <param name="clientSync">Restarts the game once the mods are installed.</param>
    public ModsFolderArchives(IModHelper helper, IMonitor monitor, string modId, ModConfig config, ClientSync clientSync)
    {
        this.Helper = helper;
        this.Monitor = monitor;
        this.ModId = modId;
        this.Config = config;
        this.ClientSync = clientSync;
    }

    /// <summary>Start unpacking the archives in the mods folder in the background. The mods are moved into place once the game closes, since it may be using the installed versions.</summary>
    public void Start()
    {
        IModInfo self = this.Helper.ModRegistry.Get(this.ModId)!;
        string? loadRoot = SmapiInternals.GetModsRoot(self);
        string? modsRoot = SmapiInternals.GetInstallRoot(self);
        if (loadRoot == null || modsRoot == null)
            return;

        string[] zips;
        try
        {
            zips = ArchiveFile.EnumerateIn(modsRoot).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            this.Monitor.Log($"Couldn't check the mods folder for archives to install:\n{ex}", LogLevel.Warn);
            return;
        }
        if (zips.Length == 0)
            return;

        string? archivesFolder = this.GetArchivesFolder(modsRoot);

        // SMAPI's mod registry isn't safe to read from another thread
        Dictionary<string, (string Version, string? Path)> installed = new(StringComparer.OrdinalIgnoreCase);
        foreach (IModInfo mod in this.Helper.ModRegistry.GetAll())
            installed.TryAdd(mod.Manifest.UniqueID, (mod.Manifest.Version.ToString(), SmapiInternals.GetDirectoryPath(mod) is { } dir ? ModFiles.GetRealPath(dir) : null));

        this.InstallTask = Task.Run(() => this.Install(loadRoot, modsRoot, archivesFolder, zips, installed));
    }

    /// <summary>Show the result on the title screen once the archives are unpacked.</summary>
    public void Update()
    {
        if (this.InstallTask is not { IsCompleted: true } task)
            return;
        if (!(Game1.activeClickableMenu is TitleMenu { titleInPosition: true, buttonsToShow: >= 4 } && TitleMenu.subMenu == null))
            return;

        this.InstallTask = null;
        if (task.IsFaulted)
        {
            this.Monitor.Log($"Couldn't install the archives in the mods folder:\n{task.Exception}", LogLevel.Error);
            return;
        }

        if (task.Result is { } result)
        {
            TitleMenu.subMenu = result.Installed
                ? new SyncDialog(result.Text, onConfirm: this.ClientSync.Restart)
                : new SyncDialog(result.Text);
        }
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Unpack the mods in each archive, and move the archives into the archives folder.</summary>
    /// <param name="loadRoot">The mods folder SMAPI loads mods from.</param>
    /// <param name="modsRoot">The mods folder to install mods into. See <see cref="ModFiles.GetInstallRoot"/>.</param>
    /// <param name="archivesFolder">The archives folder, if it's valid and isn't the mods folder itself.</param>
    /// <param name="zips">The archives in the mods folder.</param>
    /// <param name="installed">The installed version and folder of each mod, by unique ID.</param>
    /// <returns>Returns the text to show the player, and whether any mods will be installed when the game closes.</returns>
    private (string Text, bool Installed)? Install(string loadRoot, string modsRoot, string? archivesFolder, string[] zips, Dictionary<string, (string Version, string? Path)> installed)
    {
        string workRoot = Path.Combine(modsRoot, ModFiles.WorkFolderName);
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss");
        string stagingRoot = Path.Combine(workRoot, "pending", $"{timestamp} mods folder");
        string backupRoot = Path.Combine(workRoot, "backup", timestamp);

        List<PendingSwap> swaps = new();
        List<string> installedNames = new();
        List<string> failures = new();
        HashSet<string> targets = new(StringComparer.OrdinalIgnoreCase);

        foreach (string zipPath in zips.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            string zipName = Path.GetFileName(zipPath);
            try
            {
                List<ArchivedMod> mods = ModArchives.ReadMods(zipPath);
                if (mods.Count == 0)
                {
                    this.Monitor.Log($"'{zipName}' in the mods folder has no mods in it, so it was left alone.", LogLevel.Warn);
                    continue;
                }

                bool failed = false;
                foreach (ArchiveInstallStep step in ArchiveInstallPlan.Plan(zipName, mods, id => installed.TryGetValue(id, out var mod) ? mod.Version : null))
                {
                    ArchivedMod mod = step.Mod;
                    switch (step.Action)
                    {
                        case ArchiveInstallAction.AlreadyInstalled:
                            this.Monitor.Log($"{mod.Id} {mod.Version} from '{zipName}' is already installed.");
                            continue;
                        case ArchiveInstallAction.NewerInstalled:
                            this.Monitor.Log($"{mod.Id} {mod.Version} from '{zipName}' wasn't installed, since you have the newer version {step.InstalledVersion}.", LogLevel.Info);
                            continue;
                        case ArchiveInstallAction.InsideAnotherMod:
                            continue;
                    }

                    try
                    {
                        string targetDir = (installed.TryGetValue(mod.Id, out var existing) ? existing.Path : null)
                            ?? ModFiles.ResolveInstallPath(modsRoot, step.NewFolder);
                        if (!targets.Add(targetDir))
                            throw new InvalidDataException($"another archive in the mods folder also installs into '{targetDir}'");

                        int index = swaps.Count;
                        PendingSwap swap = new(targetDir, StagedDir: Path.Combine(stagingRoot, index.ToString()), BackupDir: Path.Combine(backupRoot, $"{index} - {Path.GetFileName(targetDir)}"), LinkDir: ModFiles.GetLoadLinkPath(loadRoot, modsRoot, targetDir));
                        ModFiles.Stage(zipPath, swap.TargetDir, swap.StagedDir, mod.Folder);
                        swaps.Add(swap);
                        string name = Path.GetFileName(targetDir);
                        installedNames.Add(step.Action == ArchiveInstallAction.Update ? $"{name} {step.InstalledVersion} -> {mod.Version}" : $"{name} {mod.Version}");
                        this.Monitor.Log($"Unpacked {mod.Id} {mod.Version} from '{zipName}'. It'll be installed into '{targetDir}' when the game closes.", LogLevel.Info);
                    }
                    catch (Exception ex)
                    {
                        failed = true;
                        failures.Add($"{mod.Id} ({zipName}): {ex.Message}");
                        this.Monitor.Log($"Couldn't install {mod.Id} from '{zipName}':\n{ex}", LogLevel.Error);
                    }
                }

                // a file which couldn't be fully installed stays in the mods folder, so it's tried again next time
                if (!failed)
                    this.MoveToArchives(zipPath, mods, archivesFolder);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                failures.Add($"{zipName}: {ex.Message}");
                this.Monitor.Log($"Couldn't read '{zipName}' in the mods folder:\n{ex}", LogLevel.Error);
            }
        }

        bool willInstall = false;
        if (swaps.Count > 0)
        {
            try
            {
                ModFiles.AppendPendingSwaps(Path.Combine(workRoot, "pending.txt"), swaps);
                GameRestarter.InstallAfterExit(Path.Combine(workRoot, "pending.txt"), Path.Combine(workRoot, "restart"));
                willInstall = true;
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Couldn't arrange for the mods from archives in the mods folder to be installed when the game closes:\n{ex}", LogLevel.Error);
                failures.AddRange(installedNames.Select(name => $"{name}: {ex.Message}"));
                installedNames.Clear();
            }
        }

        if (installedNames.Count == 0 && failures.Count == 0)
            return null;

        List<string> lines = new();
        if (installedNames.Count > 0)
        {
            lines.Add(this.Helper.Translation.Get("archives.installed"));
            lines.AddRange(installedNames.Select(name => $"- {name}"));
        }
        if (failures.Count > 0)
        {
            if (lines.Count > 0)
                lines.Add("");
            lines.Add(this.Helper.Translation.Get("archives.failed"));
            lines.AddRange(failures.Select(failure => $"- {failure}"));
        }
        if (willInstall)
        {
            lines.Add("");
            lines.Add(this.Helper.Translation.Get("archives.restart-prompt"));
        }
        return (string.Join("\n", lines), willInstall);
    }

    /// <summary>Move an archive from the mods folder into the archives folder, or delete it if the archives folder already has its mods at the same versions.</summary>
    /// <param name="zipPath">The archive in the mods folder.</param>
    /// <param name="mods">The mods in the archive.</param>
    /// <param name="archivesFolder">The archives folder, if it's valid and isn't the mods folder itself.</param>
    private void MoveToArchives(string zipPath, List<ArchivedMod> mods, string? archivesFolder)
    {
        string zipName = Path.GetFileName(zipPath);
        if (archivesFolder == null)
            return;

        try
        {
            if (mods.All(mod => this.Archives.Find(archivesFolder, mod.Id, mod.Version) != null))
            {
                File.Delete(zipPath);
                this.Monitor.Log($"Deleted '{zipName}' from the mods folder, since '{archivesFolder}' already has an archive of the same versions.", LogLevel.Info);
                return;
            }

            Directory.CreateDirectory(archivesFolder);
            string newPath = ArchiveInstallPlan.GetFreePath(archivesFolder, zipName, File.Exists);
            File.Move(zipPath, newPath);
            this.Monitor.Log($"Moved '{zipName}' from the mods folder to '{newPath}', so it can be sent to joining players.", LogLevel.Info);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            this.Monitor.Log($"Couldn't move '{zipName}' from the mods folder to '{archivesFolder}':\n{ex}", LogLevel.Warn);
        }
    }

    /// <summary>Get the archives folder, if it's a valid path and isn't the mods folder itself.</summary>
    /// <param name="modsRoot">The mods folder.</param>
    private string? GetArchivesFolder(string modsRoot)
    {
        try
        {
            string folder = ModArchives.GetFolderPath(modsRoot, this.Config.ArchivesFolder);
            if (string.Equals(Path.GetFullPath(folder).TrimEnd('\\', '/'), Path.GetFullPath(modsRoot).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                return null;
            return folder;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            this.Monitor.Log($"The archives folder '{this.Config.ArchivesFolder}' in the settings isn't a valid path, so archives in the mods folder are installed but left there: {ex.Message}", LogLevel.Warn);
            return null;
        }
    }
}

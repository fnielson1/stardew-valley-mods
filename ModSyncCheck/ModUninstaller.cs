using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using StardewModdingAPI;

namespace ModSyncCheck;

/// <summary>Uninstalls mods the player chose in the settings once the game closes, by moving their folders and archives into the uninstalled folder, restores them from there, and deletes them after a while.</summary>
internal class ModUninstaller
{
    /*********
    ** Fields
    *********/
    /// <summary>The name of the folder within the work folder which uninstalled mods are moved into, in a subfolder named after when they were uninstalled.</summary>
    public const string UninstalledFolderName = "uninstalled";

    /// <summary>How long uninstalled mods are kept before they're deleted.</summary>
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(7);

    /// <summary>The SMAPI API for this mod.</summary>
    private readonly IModHelper Helper;

    /// <summary>Writes messages to the SMAPI log.</summary>
    private readonly IMonitor Monitor;

    /// <summary>This mod's unique ID.</summary>
    private readonly string ModId;


    /*********
    ** Public methods
    *********/
    /// <summary>Construct an instance.</summary>
    /// <param name="helper">The SMAPI API for this mod.</param>
    /// <param name="monitor">Writes messages to the SMAPI log.</param>
    /// <param name="modId">This mod's unique ID.</param>
    public ModUninstaller(IModHelper helper, IMonitor monitor, string modId)
    {
        this.Helper = helper;
        this.Monitor = monitor;
        this.ModId = modId;
    }

    /// <summary>Get whether a mod can be uninstalled, which needs its folder to be known.</summary>
    /// <param name="mod">The mod to check.</param>
    public bool CanUninstall(IModInfo mod)
    {
        return this.GetRoots() != null && SmapiInternals.GetDirectoryPath(mod) != null;
    }

    /// <summary>Get the unique IDs of the mods which will be uninstalled once the game closes.</summary>
    public HashSet<string> GetPending()
    {
        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        if (this.GetRoots() is not { } roots)
            return ids;

        try
        {
            HashSet<string> paths = ModFiles.ReadPendingUninstalls(ModUninstaller.GetSwapListPath(roots.ModsRoot))
                .Select(uninstall => ModUninstaller.NormalizePath(uninstall.Path))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (IModInfo mod in this.Helper.ModRegistry.GetAll())
            {
                if (SmapiInternals.GetDirectoryPath(mod) is { } dir && paths.Contains(ModUninstaller.NormalizePath(ModFiles.GetRealPath(dir))))
                    ids.Add(mod.Manifest.UniqueID);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            this.Monitor.Log($"Couldn't read which mods will be uninstalled when the game closes:\n{ex}", LogLevel.Warn);
        }
        return ids;
    }

    /// <summary>Set which mods to uninstall once the game closes, replacing those chosen before.</summary>
    /// <param name="modIds">The unique IDs of the mods to uninstall.</param>
    public void SetPending(IReadOnlyCollection<string> modIds)
    {
        if (this.GetRoots() is not { } roots)
            return;

        string workRoot = Path.Combine(roots.ModsRoot, ModFiles.WorkFolderName);
        string uninstalledRoot = ModUninstaller.GetUninstalledRoot(roots.ModsRoot);
        string batchFolder = Path.Combine(uninstalledRoot, DateTime.Now.ToString(ModFiles.TimestampFormat, CultureInfo.InvariantCulture));
        string swapListPath = ModUninstaller.GetSwapListPath(roots.ModsRoot);

        try
        {
            // the mods chosen before are replaced, so their records are no longer needed
            RecycleBin.DeleteEmpty(uninstalledRoot, keep: Array.Empty<string>());

            List<PendingUninstall> uninstalls = new();
            List<RecycledEntry> record = new();
            HashSet<string> usedNames = new(StringComparer.OrdinalIgnoreCase);
            string GetUninstalledPath(string path)
            {
                string uninstalledPath = ArchiveInstallPlan.GetFreePath(batchFolder, Path.GetFileName(path.TrimEnd('\\', '/')), candidate => usedNames.Contains(Path.GetFileName(candidate)));
                usedNames.Add(Path.GetFileName(uninstalledPath));
                return uninstalledPath;
            }

            // the mod folders
            List<IModInfo> mods = modIds
                .Select(id => this.Helper.ModRegistry.Get(id))
                .OfType<IModInfo>()
                .Where(mod => !string.Equals(mod.Manifest.UniqueID, this.ModId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (IModInfo mod in mods)
            {
                if (SmapiInternals.GetDirectoryPath(mod) is not { } dir)
                    continue;

                string realDir = ModFiles.GetRealPath(dir);
                string? link = ModUninstaller.NormalizePath(realDir) != ModUninstaller.NormalizePath(dir) ? dir : null;
                PendingUninstall uninstall = new(realDir, GetUninstalledPath(realDir), link);
                uninstalls.Add(uninstall);
                record.Add(new RecycledEntry(uninstall.UninstalledPath, realDir, link, new[] { mod.Manifest.UniqueID }, mod.Manifest.Name, mod.Manifest.Version.ToString()));
            }

            // the archives which only have mods being uninstalled
            HashSet<string> ids = mods.Select(mod => mod.Manifest.UniqueID).ToHashSet(StringComparer.OrdinalIgnoreCase);
            string archivesFolder = ModArchives.GetFolderPath(roots.ModsRoot);
            if (ids.Count > 0 && Directory.Exists(archivesFolder))
            {
                foreach (string archive in ArchiveFile.EnumerateIn(archivesFolder).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    List<ArchivedMod> archived = this.TryReadMods(archive);
                    if (archived.Count == 0 || !archived.Any(mod => ids.Contains(mod.Id.Trim())))
                        continue;

                    if (archived.All(mod => ids.Contains(mod.Id.Trim())))
                    {
                        PendingUninstall uninstall = new(archive, GetUninstalledPath(archive));
                        uninstalls.Add(uninstall);
                        record.Add(new RecycledEntry(uninstall.UninstalledPath, archive, null, archived.Select(mod => mod.Id.Trim()).ToList()));
                    }
                    else
                        this.Monitor.Log($"Kept '{Path.GetFileName(archive)}', since it also has mods which aren't being uninstalled.", LogLevel.Info);
                }
            }

            if (record.Count > 0)
                RecycleBin.WriteRecord(batchFolder, record);
            ModFiles.SetPendingUninstalls(swapListPath, uninstalls);
            if (uninstalls.Count > 0)
            {
                GameRestarter.InstallAfterExit(swapListPath, Path.Combine(workRoot, "restart"));
                this.Monitor.Log($"These will be uninstalled into '{batchFolder}' when the game closes:\n   {string.Join("\n   ", uninstalls.Select(uninstall => uninstall.Path))}", LogLevel.Info);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            this.Monitor.Log($"Couldn't set which mods to uninstall when the game closes:\n{ex}", LogLevel.Error);
        }
    }

    /// <summary>Get the uninstalled mods which can be restored, newest first.</summary>
    public List<RecycledMod> GetRecycled()
    {
        if (this.GetRoots() is not { } roots)
            return new List<RecycledMod>();

        try
        {
            return RecycleBin.GetMods(ModUninstaller.GetUninstalledRoot(roots.ModsRoot));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            this.Monitor.Log($"Couldn't read the uninstalled mods:\n{ex}", LogLevel.Warn);
            return new List<RecycledMod>();
        }
    }

    /// <summary>Get a path relative to the mods folder to show the player, if it's in there.</summary>
    /// <param name="path">The full path.</param>
    public string GetDisplayPath(string path)
    {
        if (this.GetRoots() is not { } roots)
            return path;

        string relative = Path.GetRelativePath(roots.ModsRoot, path);
        return relative.StartsWith("..") || Path.IsPathRooted(relative) ? path : relative;
    }

    /// <summary>Put an uninstalled mod and its archives back where they were.</summary>
    /// <param name="mod">The mod to restore.</param>
    /// <returns>Returns whether it was restored.</returns>
    public bool Restore(RecycledMod mod)
    {
        try
        {
            RecycleBin.Restore(mod);
            this.Monitor.Log($"Restored {mod.Folder.Name} {mod.Folder.Version} to '{mod.Folder.OriginalPath}'{(mod.Archives.Count > 0 ? $", with {string.Join(", ", mod.Archives.Select(archive => $"'{Path.GetFileName(archive.OriginalPath)}'"))}" : "")}. It'll be loaded the next time the game starts.", LogLevel.Info);
            if (this.GetRoots() is { } roots)
                RecycleBin.DeleteEmpty(ModUninstaller.GetUninstalledRoot(roots.ModsRoot), this.GetPendingFolders(roots.ModsRoot));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            this.Monitor.Log($"Couldn't restore {mod.Folder.Name} {mod.Folder.Version} from '{mod.Folder.Path}':\n{ex}", LogLevel.Error);
            return false;
        }
    }

    /// <summary>Delete the mods uninstalled longer ago than <see cref="KeepFor"/>, and the folders in the uninstalled folder with nothing left in them.</summary>
    public void DeleteOld()
    {
        if (this.GetRoots() is not { } roots)
            return;

        string uninstalledFolder = ModUninstaller.GetUninstalledRoot(roots.ModsRoot);
        if (!Directory.Exists(uninstalledFolder))
            return;

        try
        {
            RecycleBin.DeleteEmpty(uninstalledFolder, this.GetPendingFolders(roots.ModsRoot));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            this.Monitor.Log($"Couldn't delete the empty folders in '{uninstalledFolder}':\n{ex}", LogLevel.Warn);
        }

        foreach (string folder in ModFiles.GetExpiredFolders(Directory.EnumerateDirectories(uninstalledFolder).Select(Path.GetFileName).OfType<string>(), DateTime.Now, ModUninstaller.KeepFor))
        {
            string path = Path.Combine(uninstalledFolder, folder);
            try
            {
                Directory.Delete(path, recursive: true);
                this.Monitor.Log($"Deleted the mods uninstalled more than {ModUninstaller.KeepFor.TotalDays} days ago in '{path}'.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                this.Monitor.Log($"Couldn't delete the old uninstalled mods in '{path}':\n{ex}", LogLevel.Warn);
            }
        }
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Get the mods folder SMAPI loads mods from and the one to install them into, if SMAPI says where they are.</summary>
    private (string LoadRoot, string ModsRoot)? GetRoots()
    {
        IModInfo self = this.Helper.ModRegistry.Get(this.ModId)!;
        return SmapiInternals.GetModsRoot(self) is { } loadRoot && SmapiInternals.GetInstallRoot(self) is { } modsRoot
            ? (loadRoot, modsRoot)
            : null;
    }

    /// <summary>Get the folder uninstalled mods are moved into.</summary>
    /// <param name="modsRoot">The mods folder to install mods into.</param>
    private static string GetUninstalledRoot(string modsRoot)
    {
        return Path.Combine(modsRoot, ModFiles.WorkFolderName, ModUninstaller.UninstalledFolderName);
    }

    /// <summary>Get the folders in the uninstalled folder which mods are still waiting to be moved into, like when a file in a mod's folder was in use when the game last closed.</summary>
    /// <param name="modsRoot">The mods folder to install mods into.</param>
    private List<string> GetPendingFolders(string modsRoot)
    {
        return ModFiles.ReadPendingUninstalls(ModUninstaller.GetSwapListPath(modsRoot))
            .Select(uninstall => Path.GetDirectoryName(uninstall.UninstalledPath))
            .OfType<string>()
            .ToList();
    }

    /// <summary>Get the file listing the changes to make to the mods folder once the game closes.</summary>
    /// <param name="modsRoot">The mods folder to install mods into.</param>
    private static string GetSwapListPath(string modsRoot)
    {
        return Path.Combine(modsRoot, ModFiles.WorkFolderName, "pending.txt");
    }

    /// <summary>Get the mods in an archive, or none if it can't be read.</summary>
    /// <param name="path">The full path to the archive.</param>
    private List<ArchivedMod> TryReadMods(string path)
    {
        try
        {
            return ModArchives.ReadMods(path);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            this.Monitor.Log($"Couldn't read '{path}' to check whether it has a mod being uninstalled:\n{ex}", LogLevel.Warn);
            return new List<ArchivedMod>();
        }
    }

    /// <summary>Get a full path without a trailing slash, for comparing paths.</summary>
    /// <param name="path">The path.</param>
    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).TrimEnd('\\', '/');
    }
}

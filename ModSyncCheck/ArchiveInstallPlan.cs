using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ModSyncCheck;

/// <summary>What to do with a mod found in a <c>.zip</c> file in the mods folder.</summary>
internal enum ArchiveInstallAction
{
    /// <summary>The mod isn't installed, so install it.</summary>
    Install,

    /// <summary>An older version is installed, so replace it.</summary>
    Update,

    /// <summary>The same version is already installed.</summary>
    AlreadyInstalled,

    /// <summary>A newer version is installed, so leave it.</summary>
    NewerInstalled,

    /// <summary>The mod's folder is inside another mod's folder in the archive, so it's installed with that mod.</summary>
    InsideAnotherMod
}

/// <summary>A mod found in a <c>.zip</c> file in the mods folder, and what to do with it.</summary>
/// <param name="Mod">The mod in the archive.</param>
/// <param name="Action">What to do with it.</param>
/// <param name="InstalledVersion">The installed version of the mod, if any.</param>
/// <param name="NewFolder">For a mod which isn't installed, the folder to install it into, relative to the mods folder: its folder in the archive, or the archive's name if its manifest is in the archive's root.</param>
internal record ArchiveInstallStep(ArchivedMod Mod, ArchiveInstallAction Action, string? InstalledVersion, string NewFolder);

/// <summary>Decides how to install the mods in a <c>.zip</c> file put in the mods folder, so players don't need to unzip it themselves.</summary>
internal static class ArchiveInstallPlan
{
    /*********
    ** Public methods
    *********/
    /// <summary>Decide what to do with each mod in an archive.</summary>
    /// <param name="zipFileName">The archive's file name, used as the folder name for a mod whose manifest is in the archive's root.</param>
    /// <param name="mods">The mods in the archive.</param>
    /// <param name="getInstalledVersion">Get the installed version of a mod by its unique ID, or <c>null</c> if it isn't installed.</param>
    public static List<ArchiveInstallStep> Plan(string zipFileName, IReadOnlyList<ArchivedMod> mods, Func<string, string?> getInstalledVersion)
    {
        List<ArchiveInstallStep> steps = new();
        foreach (ArchivedMod mod in mods)
        {
            string newFolder = mod.Folder.TrimEnd('/').Length > 0
                ? mod.Folder.TrimEnd('/')
                : Path.GetFileNameWithoutExtension(zipFileName).Trim();
            string? installed = getInstalledVersion(mod.Id);

            ArchiveInstallAction action;
            if (mods.Any(other => other != mod && other.Folder.Length < mod.Folder.Length && mod.Folder.StartsWith(other.Folder, StringComparison.OrdinalIgnoreCase)))
                action = ArchiveInstallAction.InsideAnotherMod;
            else if (installed == null)
                action = ArchiveInstallAction.Install;
            else
            {
                int compared = ModArchives.CompareVersions(mod.Version, installed);
                action = compared > 0
                    ? ArchiveInstallAction.Update
                    : compared < 0
                        ? ArchiveInstallAction.NewerInstalled
                        : ArchiveInstallAction.AlreadyInstalled;
            }

            steps.Add(new ArchiveInstallStep(mod, action, installed, newFolder));
        }
        return steps;
    }

    /// <summary>Get whether a step installs or updates the mod.</summary>
    /// <param name="step">The step to check.</param>
    public static bool Installs(ArchiveInstallStep step)
    {
        return step.Action is ArchiveInstallAction.Install or ArchiveInstallAction.Update;
    }

    /// <summary>Get a free file path in a folder for a file, adding a number like <c>Mod (2).zip</c> if the name is taken.</summary>
    /// <param name="folder">The folder to put the file in.</param>
    /// <param name="fileName">The file's name.</param>
    /// <param name="exists">Get whether a path is taken.</param>
    public static string GetFreePath(string folder, string fileName, Func<string, bool> exists)
    {
        string path = Path.Combine(folder, fileName);
        string name = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);
        for (int i = 2; exists(path); i++)
            path = Path.Combine(folder, $"{name} ({i}){extension}");
        return path;
    }
}

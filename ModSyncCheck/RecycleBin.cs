using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ModSyncCheck;

/// <summary>A mod folder or archive moved into the uninstalled folder, as written to its record.</summary>
/// <param name="Path">Where it's moved to in the uninstalled folder.</param>
/// <param name="OriginalPath">Where it was before it was uninstalled.</param>
/// <param name="LinkDir">The link to the mod folder in the folder SMAPI loads mods from, if there was one.</param>
/// <param name="ModIds">The unique IDs of the mods it has.</param>
/// <param name="Name">The mod's name, or an empty string for an archive.</param>
/// <param name="Version">The mod's version, or an empty string for an archive.</param>
internal record RecycledEntry(string Path, string OriginalPath, string? LinkDir, IReadOnlyList<string> ModIds, string Name = "", string Version = "")
{
    /// <summary>Whether this is a mod folder, rather than an archive.</summary>
    public bool IsMod => this.Name.Length > 0;
}

/// <summary>An uninstalled mod which can be restored.</summary>
/// <param name="Folder">The mod folder.</param>
/// <param name="Archives">The archives which had the mod.</param>
/// <param name="Uninstalled">The name of the folder in the uninstalled folder it's in, which is when it was uninstalled.</param>
internal record RecycledMod(RecycledEntry Folder, IReadOnlyList<RecycledEntry> Archives, string Uninstalled);

/// <summary>Keeps track of the mods in the uninstalled folder, so they can be put back where they were.</summary>
internal static class RecycleBin
{
    /*********
    ** Fields
    *********/
    /// <summary>The name of the file in each folder in the uninstalled folder which records where its mods and archives came from.</summary>
    public const string RecordFileName = "restore.txt";


    /*********
    ** Public methods
    *********/
    /// <summary>Record where the mods and archives moved into a folder in the uninstalled folder came from, replacing any record already there.</summary>
    /// <param name="folder">The folder in the uninstalled folder.</param>
    /// <param name="entries">The mod folders and archives to be moved into it.</param>
    public static void WriteRecord(string folder, IEnumerable<RecycledEntry> entries)
    {
        static string Clean(string value) => new(value.Select(ch => char.IsControl(ch) ? ' ' : ch).ToArray());

        List<string> lines = entries
            .Select(entry => string.Join('\t', entry.Path, entry.OriginalPath, entry.LinkDir ?? "", string.Join(',', entry.ModIds), Clean(entry.Name), Clean(entry.Version)))
            .ToList();
        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, RecycleBin.RecordFileName), lines);
    }

    /// <summary>Get the uninstalled mods which are still in the uninstalled folder, newest first.</summary>
    /// <param name="uninstalledRoot">The uninstalled folder.</param>
    public static List<RecycledMod> GetMods(string uninstalledRoot)
    {
        List<RecycledMod> mods = new();
        if (!Directory.Exists(uninstalledRoot))
            return mods;

        foreach (string folder in Directory.EnumerateDirectories(uninstalledRoot).OrderByDescending(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
        {
            List<RecycledEntry> entries = RecycleBin.ReadRecord(folder);
            List<RecycledEntry> archives = entries.Where(entry => !entry.IsMod && File.Exists(entry.Path)).ToList();
            foreach (RecycledEntry entry in entries.Where(entry => entry.IsMod && Directory.Exists(entry.Path)))
            {
                List<RecycledEntry> modArchives = archives.Where(archive => archive.ModIds.Intersect(entry.ModIds, StringComparer.OrdinalIgnoreCase).Any()).ToList();
                mods.Add(new RecycledMod(entry, modArchives, Path.GetFileName(folder)));
            }
        }
        return mods;
    }

    /// <summary>Put an uninstalled mod and its archives back where they were. The mod is loaded the next time the game starts.</summary>
    /// <param name="mod">The mod to restore.</param>
    /// <exception cref="IOException">Something is already where the mod folder was, or it couldn't be moved.</exception>
    public static void Restore(RecycledMod mod)
    {
        RecycledEntry folder = mod.Folder;
        if (Directory.Exists(folder.OriginalPath) || File.Exists(folder.OriginalPath))
            throw new IOException($"'{folder.OriginalPath}' already exists.");

        Directory.CreateDirectory(Path.GetDirectoryName(folder.OriginalPath)!);
        Directory.Move(folder.Path, folder.OriginalPath);
        if (folder.LinkDir != null && !Directory.Exists(folder.LinkDir) && !File.Exists(folder.LinkDir))
            ModFiles.CreateLink(folder.LinkDir, folder.OriginalPath);

        // an archive which is already back, like one restored with another of its mods, is left where it is
        foreach (RecycledEntry archive in mod.Archives)
        {
            if (File.Exists(archive.Path) && !File.Exists(archive.OriginalPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(archive.OriginalPath)!);
                File.Move(archive.Path, archive.OriginalPath);
            }
        }
    }

    /// <summary>Delete the folders in the uninstalled folder which have nothing left in them but their record, like after their mods are restored or when the mods to uninstall are chosen again.</summary>
    /// <param name="uninstalledRoot">The uninstalled folder.</param>
    /// <param name="keep">The folders to keep even if they're empty, like those whose mods are still waiting to be moved in.</param>
    /// <returns>Returns the folders which were deleted.</returns>
    public static List<string> DeleteEmpty(string uninstalledRoot, IEnumerable<string> keep)
    {
        List<string> deleted = new();
        if (!Directory.Exists(uninstalledRoot))
            return deleted;

        HashSet<string> kept = keep.Select(path => Path.GetFullPath(path).TrimEnd('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string folder in Directory.EnumerateDirectories(uninstalledRoot).ToList())
        {
            if (kept.Contains(Path.GetFullPath(folder).TrimEnd('\\', '/')))
                continue;

            bool empty = Directory.EnumerateFileSystemEntries(folder).All(path => string.Equals(Path.GetFileName(path), RecycleBin.RecordFileName, StringComparison.OrdinalIgnoreCase));
            if (empty)
            {
                Directory.Delete(folder, recursive: true);
                deleted.Add(folder);
            }
        }
        return deleted;
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Read the record in a folder in the uninstalled folder, if it has one.</summary>
    /// <param name="folder">The folder in the uninstalled folder.</param>
    private static List<RecycledEntry> ReadRecord(string folder)
    {
        string path = Path.Combine(folder, RecycleBin.RecordFileName);
        if (!File.Exists(path))
            return new List<RecycledEntry>();

        return File.ReadAllLines(path)
            .Select(line => line.Split('\t'))
            .Where(fields => fields.Length >= 6 && fields[0].Length > 0 && fields[1].Length > 0)
            .Select(fields => new RecycledEntry(
                Path: fields[0],
                OriginalPath: fields[1],
                LinkDir: fields[2].Length > 0 ? fields[2] : null,
                ModIds: fields[3].Split(',', StringSplitOptions.RemoveEmptyEntries),
                Name: fields[4],
                Version: fields[5]
            ))
            .ToList();
    }
}

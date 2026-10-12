using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ModSyncCheck;

/// <summary>The files from a mod folder to include in a package.</summary>
internal enum PackageContent
{
    /// <summary>The mod without the player's settings for it.</summary>
    ModOnly,

    /// <summary>Only the player's settings for the mod.</summary>
    ConfigOnly
}

/// <summary>Packages and installs mod folders.</summary>
internal static class ModFiles
{
    /*********
    ** Fields
    *********/
    /// <summary>The name of the folder within the mods folder used for downloads, staging, and backups. SMAPI ignores folders whose name starts with a dot.</summary>
    public const string WorkFolderName = ".ModSyncCheck";

    /// <summary>The name of the per-player settings file in a mod folder.</summary>
    public const string ConfigFileName = "config.json";


    /*********
    ** Public methods
    *********/
    /// <summary>Get the total size of the files in a mod folder, not counting the player's settings.</summary>
    /// <param name="modDir">The mod folder path.</param>
    public static long GetSize(string modDir)
    {
        return new DirectoryInfo(modDir)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Where(file => !ModFiles.IsConfigFile(file.Name))
            .Sum(file => file.Length);
    }

    /// <summary>Package a mod folder into a zip file.</summary>
    /// <param name="modDir">The mod folder path.</param>
    /// <param name="zipPath">The zip file to create.</param>
    /// <param name="content">The files to include.</param>
    public static void CreateZip(string modDir, string zipPath, PackageContent content = PackageContent.ModOnly)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);

        using ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (string file in Directory.EnumerateFiles(modDir, "*", SearchOption.AllDirectories))
        {
            if (ModFiles.IsConfigFile(Path.GetFileName(file)) != (content == PackageContent.ConfigOnly))
                continue;

            string entryName = Path.GetRelativePath(modDir, file).Replace('\\', '/');
            zip.CreateEntryFromFile(file, entryName, CompressionLevel.Fastest);
        }
    }

    /// <summary>Get a hash of each settings file in a mod folder, by its path relative to the folder with forward slashes.</summary>
    /// <param name="modDir">The mod folder path.</param>
    public static Dictionary<string, string> GetConfigHashes(string modDir)
    {
        Dictionary<string, string> hashes = new(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.EnumerateFiles(modDir, ModFiles.ConfigFileName, SearchOption.AllDirectories))
            hashes[Path.GetRelativePath(modDir, file).Replace('\\', '/')] = ModFiles.GetConfigHash(file);
        return hashes;
    }

    /// <summary>Get a hash of a settings file.</summary>
    /// <param name="path">The file path.</param>
    /// <remarks>Whitespace is ignored, so two files with the same settings match even if they're formatted differently.</remarks>
    public static string GetConfigHash(string path)
    {
        StringBuilder text = new();
        foreach (char ch in File.ReadAllText(path))
        {
            if (!char.IsWhiteSpace(ch) && ch != '﻿')
                text.Append(ch);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    /// <summary>Extract a mod's own settings file from a package of its settings, if it has one.</summary>
    /// <param name="zipPath">The package containing the settings for a mod.</param>
    /// <param name="path">The file path to extract it to.</param>
    /// <returns>Returns whether the package has a settings file in the mod's root folder.</returns>
    public static bool TryExtractConfig(string zipPath, string path)
    {
        using ZipArchive zip = ZipFile.OpenRead(zipPath);

        ZipArchiveEntry? entry = zip.Entries.FirstOrDefault(entry => ModFiles.IsConfigFile(entry.FullName));
        if (entry == null)
            return false;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        entry.ExtractToFile(path, overwrite: true);
        return true;
    }

    /// <summary>Get the folder to install a new mod into, given its folder path relative to the host's mods folder.</summary>
    /// <param name="modsRoot">The local mods folder path.</param>
    /// <param name="relativePath">The mod's folder path relative to the host's mods folder.</param>
    /// <exception cref="InvalidDataException">The relative path isn't a safe path within the mods folder.</exception>
    public static string ResolveInstallPath(string modsRoot, string relativePath)
    {
        string[] segments = relativePath.Split('/', '\\');
        char[] invalidChars = Path.GetInvalidFileNameChars();

        bool valid = segments.All(segment =>
            segment.Trim().Length > 0
            && !segment.StartsWith('.') // not '.', '..', or a folder SMAPI ignores
            && !segment.EndsWith(' ')
            && !segment.EndsWith('.')
            && segment.IndexOfAny(invalidChars) == -1
        );
        if (!valid)
            throw new InvalidDataException($"The host sent an invalid mod folder path '{relativePath}'.");

        return Path.Combine(modsRoot, Path.Combine(segments));
    }

    /// <summary>Install a packaged mod into a folder. If the folder already exists, it's moved to the backup folder first and its settings are kept.</summary>
    /// <param name="zipPath">The packaged mod.</param>
    /// <param name="targetDir">The folder to install the mod into.</param>
    /// <param name="stagingDir">A temporary folder on the same drive as <paramref name="targetDir"/> to unpack the mod into.</param>
    /// <param name="backupDir">The folder to move an existing <paramref name="targetDir"/> to.</param>
    /// <exception cref="InvalidDataException">The package contains a file path outside the mod folder.</exception>
    public static void Install(string zipPath, string targetDir, string stagingDir, string backupDir)
    {
        // unpack
        if (Directory.Exists(stagingDir))
            Directory.Delete(stagingDir, recursive: true);
        Directory.CreateDirectory(stagingDir);

        string stagingRoot = Path.GetFullPath(stagingDir) + Path.DirectorySeparatorChar;
        using (ZipArchive zip = ZipFile.OpenRead(zipPath))
        {
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                if (entry.Name == "")
                    continue; // folder

                string path = Path.GetFullPath(Path.Combine(stagingDir, entry.FullName));
                if (!path.StartsWith(stagingRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"The package contains an invalid file path '{entry.FullName}'.");

                // the player's settings are never replaced by a download
                if (ModFiles.IsConfigFile(entry.Name))
                    continue;

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                entry.ExtractToFile(path, overwrite: true);
            }
        }

        // back up the installed version, and keep its settings
        bool hadExisting = Directory.Exists(targetDir);
        if (hadExisting)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(backupDir)!);
            Directory.Move(targetDir, backupDir);

            foreach (string configPath in Directory.EnumerateFiles(backupDir, ModFiles.ConfigFileName, SearchOption.AllDirectories))
            {
                string newPath = Path.Combine(stagingDir, Path.GetRelativePath(backupDir, configPath));
                if (Directory.Exists(Path.GetDirectoryName(newPath)))
                    File.Copy(configPath, newPath, overwrite: true);
            }
        }

        // install
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetDir)!);
            Directory.Move(stagingDir, targetDir);
        }
        catch
        {
            if (hadExisting && !Directory.Exists(targetDir))
                Directory.Move(backupDir, targetDir);
            throw;
        }
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Get whether a file is a per-player settings file.</summary>
    /// <param name="fileName">The file name without the path.</param>
    private static bool IsConfigFile(string fileName)
    {
        return string.Equals(fileName, ModFiles.ConfigFileName, StringComparison.OrdinalIgnoreCase);
    }
}

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

/// <summary>A staged mod to move into place once the game closes.</summary>
/// <param name="TargetDir">The folder to install the mod into.</param>
/// <param name="StagedDir">The folder containing the unpacked mod.</param>
/// <param name="BackupDir">The folder to move an existing <paramref name="TargetDir"/> to.</param>
/// <param name="LinkDir">A link to create to <paramref name="TargetDir"/> once it's in place, if it doesn't exist, so SMAPI loads the mod from another folder. See <see cref="ModFiles.GetLoadLinkPath"/>.</param>
internal record PendingSwap(string TargetDir, string StagedDir, string BackupDir, string? LinkDir = null);

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

    /// <summary>The characters which aren't allowed in a mod folder name sent by the host. These are the ones Windows doesn't allow, applied on every platform so the same path is valid or invalid for every player.</summary>
    private static readonly char[] InvalidFolderNameChars = Enumerable.Range(0, 32).Select(code => (char)code).Concat("\"<>|:*?\\/").ToArray();


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

    /// <summary>Put a file back as it was, if it changed.</summary>
    /// <param name="path">The full file path.</param>
    /// <param name="original">The content the file should have, or <c>null</c> if it shouldn't exist.</param>
    /// <returns>Returns whether the file had changed.</returns>
    public static bool RestoreFile(string path, byte[]? original)
    {
        if (!File.Exists(path))
        {
            if (original == null)
                return false;
        }
        else if (original == null)
        {
            File.Delete(path);
            return true;
        }
        else if (File.ReadAllBytes(path).AsSpan().SequenceEqual(original))
            return false;

        File.WriteAllBytes(path, original);
        return true;
    }

    /// <summary>Get the folder a mod folder's files are really in, following it if it's a link to another folder.</summary>
    /// <param name="folder">The mod folder.</param>
    public static string GetRealPath(string folder)
    {
        try
        {
            return new DirectoryInfo(folder).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? folder;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return folder;
        }
    }

    /// <summary>Get the mods folder to install mods into and keep the work folder in. That's the one SMAPI loads mods from, unless this mod's folder there is a link to another one, like those a mod manager makes for the mods it chose to load; then it's the folder the link leads into, where the mods really are.</summary>
    /// <param name="loadRoot">The mods folder SMAPI loaded the mods from.</param>
    /// <param name="ownDir">This mod's folder, as SMAPI loaded it.</param>
    /// <param name="ownRelativePath">This mod's folder path relative to <paramref name="loadRoot"/>.</param>
    public static string GetInstallRoot(string loadRoot, string ownDir, string ownRelativePath)
    {
        string realDir = ModFiles.GetRealPath(ownDir);
        if (realDir == ownDir)
            return loadRoot;

        string root = realDir.TrimEnd('\\', '/');
        foreach (string _ in ownRelativePath.Split('/', '\\', StringSplitOptions.RemoveEmptyEntries))
            root = Path.GetDirectoryName(root) ?? root;
        return root;
    }

    /// <summary>Get the link to create so SMAPI loads a mod installed into the install root, or <c>null</c> if that's the folder SMAPI loads mods from. See <see cref="GetInstallRoot"/>.</summary>
    /// <param name="loadRoot">The mods folder SMAPI loads mods from.</param>
    /// <param name="installRoot">The mods folder mods are installed into.</param>
    /// <param name="targetDir">The folder the mod is installed into.</param>
    public static string? GetLoadLinkPath(string loadRoot, string installRoot, string targetDir)
    {
        return string.Equals(Path.GetFullPath(loadRoot).TrimEnd('\\', '/'), Path.GetFullPath(installRoot).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)
            ? null
            : Path.Combine(loadRoot, Path.GetFileName(targetDir));
    }

    /// <summary>Get the folder to install a new mod into, given its folder path relative to the host's mods folder.</summary>
    /// <param name="modsRoot">The local mods folder path.</param>
    /// <param name="relativePath">The mod's folder path relative to the host's mods folder.</param>
    /// <exception cref="InvalidDataException">The relative path isn't a safe path within the mods folder.</exception>
    public static string ResolveInstallPath(string modsRoot, string relativePath)
    {
        string[] segments = relativePath.Split('/', '\\');

        bool valid = segments.All(segment =>
            segment.Trim().Length > 0
            && !segment.StartsWith('.') // not '.', '..', or a folder SMAPI ignores
            && !segment.EndsWith(' ')
            && !segment.EndsWith('.')
            && segment.IndexOfAny(ModFiles.InvalidFolderNameChars) == -1
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
        ModFiles.Stage(zipPath, targetDir, stagingDir);
        ModFiles.Swap(targetDir, stagingDir, backupDir);
    }

    /// <summary>Unpack a packaged mod into a staging folder, ready to be swapped into place. If the mod is already installed, its settings are copied into the staging folder.</summary>
    /// <param name="zipPath">The packaged mod.</param>
    /// <param name="targetDir">The folder the mod will be installed into.</param>
    /// <param name="stagingDir">A folder on the same drive as <paramref name="targetDir"/> to unpack the mod into.</param>
    /// <param name="folder">The path within the package of the mod folder to unpack, with a trailing slash, or an empty string to unpack the whole package. Files outside it are skipped.</param>
    /// <exception cref="InvalidDataException">The package contains a file path outside the mod folder.</exception>
    public static void Stage(string zipPath, string targetDir, string stagingDir, string folder = "")
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

                string entryName = entry.FullName.Replace('\\', '/');
                if (!entryName.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                    continue; // another mod in the same archive
                entryName = entryName[folder.Length..];

                string path = Path.GetFullPath(Path.Combine(stagingDir, entryName));
                if (!path.StartsWith(stagingRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"The package contains an invalid file path '{entry.FullName}'.");

                // the player's settings are never replaced by a download
                if (ModFiles.IsConfigFile(entry.Name))
                    continue;

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                entry.ExtractToFile(path, overwrite: true);
            }
        }

        // keep the settings of the installed version
        if (Directory.Exists(targetDir))
        {
            foreach (string configPath in Directory.EnumerateFiles(targetDir, ModFiles.ConfigFileName, SearchOption.AllDirectories))
            {
                string newPath = Path.Combine(stagingDir, Path.GetRelativePath(targetDir, configPath));
                if (Directory.Exists(Path.GetDirectoryName(newPath)))
                    File.Copy(configPath, newPath, overwrite: true);
            }
        }
    }

    /// <summary>Get the folder within a mod's original archive which contains the mod, for <see cref="Stage"/>.</summary>
    /// <param name="zipPath">The archive.</param>
    /// <param name="mod">The mod to find.</param>
    /// <exception cref="InvalidDataException">The archive doesn't contain the mod.</exception>
    public static string GetArchivedModFolder(string zipPath, ModInfo mod)
    {
        using ZipArchive zip = ZipFile.OpenRead(zipPath);

        ArchivedMod[] matches = ModArchives.ReadMods(zip)
            .Where(archived => string.Equals(archived.Id, mod.Id, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        ArchivedMod? match =
            matches.FirstOrDefault(archived => ModArchives.NormalizeVersion(archived.Version) == ModArchives.NormalizeVersion(mod.Version))
            ?? matches.FirstOrDefault();

        return match?.Folder ?? throw new InvalidDataException($"The archive sent by the host doesn't contain {mod.Name}.");
    }

    /// <summary>Move a staged mod into place. If the target folder already exists, it's moved to the backup folder first.</summary>
    /// <param name="targetDir">The folder to install the mod into.</param>
    /// <param name="stagingDir">The folder containing the unpacked mod, on the same drive as <paramref name="targetDir"/>.</param>
    /// <param name="backupDir">The folder to move an existing <paramref name="targetDir"/> to.</param>
    public static void Swap(string targetDir, string stagingDir, string backupDir)
    {
        bool hadExisting = Directory.Exists(targetDir);
        if (hadExisting)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(backupDir)!);
            Directory.Move(targetDir, backupDir);
        }

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

    /// <summary>Add staged mods to the list of those to move into place once the game closes, which is done by <see cref="GameRestarter"/>.</summary>
    /// <param name="listPath">The file listing the staged mods.</param>
    /// <param name="swaps">The staged mods to add.</param>
    /// <exception cref="InvalidDataException">A folder path can't be written to the list.</exception>
    public static void AppendPendingSwaps(string listPath, IEnumerable<PendingSwap> swaps)
    {
        List<string> lines = new();
        foreach (PendingSwap swap in swaps)
        {
            string[] paths = swap.LinkDir != null
                ? new[] { swap.TargetDir, swap.StagedDir, swap.BackupDir, swap.LinkDir }
                : new[] { swap.TargetDir, swap.StagedDir, swap.BackupDir };
            if (paths.Any(path => path.Length == 0 || path.Any(char.IsControl)))
                throw new InvalidDataException($"Can't install into the folder '{swap.TargetDir}' when the game closes, since its path has unsupported characters.");
            lines.Add(string.Join('\t', paths));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(listPath)!);
        File.AppendAllLines(listPath, lines);
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

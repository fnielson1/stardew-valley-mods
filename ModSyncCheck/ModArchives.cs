using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace ModSyncCheck;

/// <summary>A mod found in a downloaded archive.</summary>
/// <param name="Id">The unique mod ID.</param>
/// <param name="Version">The mod version.</param>
/// <param name="Folder">The path within the archive of the folder containing the mod's manifest, with a trailing slash, or an empty string if it's in the root.</param>
internal record ArchivedMod(string Id, string Version, string Folder);

/// <summary>Finds the original archives the host downloaded its mods in, like those from Nexus Mods, so they can be sent unchanged.</summary>
internal class ModArchives
{
    /*********
    ** Fields
    *********/
    /// <summary>The name of a mod's manifest file.</summary>
    private const string ManifestFileName = "manifest.json";

    /// <summary>The options for reading a manifest, which SMAPI allows to have comments and trailing commas.</summary>
    private static readonly JsonDocumentOptions JsonOptions = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    /// <summary>The mods found in each archive, by full path, with the archive's size and last write time when it was read.</summary>
    private readonly Dictionary<string, (long Size, DateTime LastWrite, IReadOnlyList<ArchivedMod> Mods)> Cache = new(StringComparer.OrdinalIgnoreCase);


    /*********
    ** Public methods
    *********/
    /// <summary>Get the full path to the folder containing the archives.</summary>
    /// <param name="modsRoot">The local mods folder path.</param>
    /// <param name="setting">The folder set in the mod settings: a full path, a path relative to the mods folder, or empty for the mod's work folder in the mods folder.</param>
    public static string GetFolderPath(string modsRoot, string? setting)
    {
        return string.IsNullOrWhiteSpace(setting)
            ? Path.Combine(modsRoot, ModFiles.WorkFolderName)
            : Path.GetFullPath(Path.Combine(modsRoot, Environment.ExpandEnvironmentVariables(setting.Trim())));
    }

    /// <summary>Get the full path to the folder Stardrop downloads mods from Nexus Mods into.</summary>
    /// <param name="appData">The application data folder, which Stardrop keeps its data in.</param>
    public static string GetStardropDownloadsFolder(string appData)
    {
        return Path.Combine(appData, "Stardrop", "Data", "Nexus");
    }

    /// <summary>Move a downloaded archive into a folder, or delete it if the folder already has its mods at the same versions.</summary>
    /// <param name="path">The archive to move. It doesn't need a <c>.zip</c> extension, which is added if missing.</param>
    /// <param name="folder">The folder to move it into.</param>
    /// <returns>Returns the new full path of the archive, or <c>null</c> if it was deleted.</returns>
    /// <exception cref="InvalidDataException">The file isn't a <c>.zip</c> file, or has no mods in it.</exception>
    public string? MoveIn(string path, string folder)
    {
        List<ArchivedMod> mods;
        using (ZipArchive zip = ZipFile.OpenRead(path))
            mods = ModArchives.ReadMods(zip);
        if (mods.Count == 0)
            throw new InvalidDataException("it has no mods in it");

        if (mods.All(mod => this.Find(folder, mod.Id, mod.Version) != null))
        {
            File.Delete(path);
            return null;
        }

        string fileName = Path.GetFileName(path);
        if (!string.Equals(Path.GetExtension(fileName), ".zip", StringComparison.OrdinalIgnoreCase))
            fileName += ".zip";

        Directory.CreateDirectory(folder);
        string newPath = ArchiveInstallPlan.GetFreePath(folder, fileName, File.Exists);
        File.Move(path, newPath);
        return newPath;
    }

    /// <summary>Find an archive in a folder which contains a mod at the given version.</summary>
    /// <param name="folder">The folder to search. Subfolders aren't searched.</param>
    /// <param name="modId">The unique mod ID.</param>
    /// <param name="version">The mod version.</param>
    /// <returns>Returns the full path to the archive, or <c>null</c> if none was found.</returns>
    public string? Find(string folder, string modId, string version)
    {
        if (!Directory.Exists(folder))
            return null;

        string wantedVersion = ModArchives.NormalizeVersion(version);
        foreach (string path in Directory.EnumerateFiles(folder, "*.zip").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (this.GetMods(path).Any(mod => string.Equals(mod.Id, modId.Trim(), StringComparison.OrdinalIgnoreCase) && ModArchives.NormalizeVersion(mod.Version) == wantedVersion))
                return path;
        }
        return null;
    }

    /// <summary>Get the size of the archive in a folder for each mod which has one at the given version.</summary>
    /// <param name="folder">The folder to search. Subfolders aren't searched.</param>
    /// <param name="mods">The mods to find.</param>
    /// <returns>Returns the archive size in bytes by unique mod ID, for the mods which have an archive.</returns>
    public Dictionary<string, long> GetSizes(string folder, IEnumerable<ModInfo> mods)
    {
        Dictionary<string, long> sizes = new(StringComparer.OrdinalIgnoreCase);
        foreach (ModInfo mod in mods)
        {
            if (this.Find(folder, mod.Id, mod.Version) is { } path)
                sizes[mod.Id] = new FileInfo(path).Length;
        }
        return sizes;
    }

    /// <summary>Delete the archives in a folder which only contain older versions of mods than other archives there.</summary>
    /// <param name="folder">The folder to clean up. Subfolders aren't searched.</param>
    /// <param name="installedVersions">The version of each installed mod, by unique mod ID. An archive with a mod at its installed version is kept, so it can still be sent to players.</param>
    /// <returns>Returns the full paths of the archives which were deleted, and of those which couldn't be deleted with the error.</returns>
    /// <remarks>An archive is only deleted if every mod in it has a newer version in another archive, so an archive of several mods is kept while any of them is current. Archives without mods, and versions which can't be compared, are never deleted.</remarks>
    public (List<string> Deleted, List<(string Path, Exception Error)> Failed) DeleteSuperseded(string folder, IReadOnlyDictionary<string, string> installedVersions)
    {
        List<string> deleted = new();
        List<(string, Exception)> failed = new();
        if (!Directory.Exists(folder))
            return (deleted, failed);

        Dictionary<string, IReadOnlyList<ArchivedMod>> archives = Directory.EnumerateFiles(folder, "*.zip").ToDictionary(path => path, this.GetMods, StringComparer.OrdinalIgnoreCase);
        foreach (string path in ModArchives.GetSuperseded(archives, installedVersions))
        {
            try
            {
                File.Delete(path);
                this.Cache.Remove(path);
                deleted.Add(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed.Add((path, ex));
            }
        }
        return (deleted, failed);
    }

    /// <summary>Get the archives which only contain older versions of mods than other archives, for <see cref="DeleteSuperseded"/>.</summary>
    /// <param name="archives">The mods in each archive, by archive path.</param>
    /// <param name="installedVersions">The version of each installed mod, by unique mod ID. An archive with a mod at its installed version is never superseded.</param>
    public static List<string> GetSuperseded(IReadOnlyDictionary<string, IReadOnlyList<ArchivedMod>> archives, IReadOnlyDictionary<string, string> installedVersions)
    {
        // the newest version of each mod in any archive
        Dictionary<string, string> newest = new(StringComparer.OrdinalIgnoreCase);
        foreach (ArchivedMod mod in archives.Values.SelectMany(mods => mods))
        {
            if (!newest.TryGetValue(mod.Id, out string? best) || ModArchives.CompareVersions(mod.Version, best) > 0)
                newest[mod.Id] = mod.Version;
        }

        Dictionary<string, string> installed = new(installedVersions, StringComparer.OrdinalIgnoreCase);
        return archives
            .Where(archive => archive.Value.Count > 0 && archive.Value.All(mod =>
                ModArchives.CompareVersions(mod.Version, newest[mod.Id]) < 0
                && !(installed.TryGetValue(mod.Id, out string? version) && ModArchives.NormalizeVersion(version) == ModArchives.NormalizeVersion(mod.Version))
            ))
            .Select(archive => archive.Key)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Compare two versions like SMAPI does: by number, then a version with a pre-release tag (like <c>1.2.0-beta</c>) before the same version without one.</summary>
    /// <param name="a">The first version.</param>
    /// <param name="b">The second version.</param>
    /// <returns>Returns a negative number if <paramref name="a"/> is older, a positive one if it's newer, or zero if they're the same or either can't be read.</returns>
    public static int CompareVersions(string a, string b)
    {
        if (!ModArchives.TryParseVersion(a, out int[] numbersA, out string tagA) || !ModArchives.TryParseVersion(b, out int[] numbersB, out string tagB))
            return 0;

        for (int i = 0; i < Math.Max(numbersA.Length, numbersB.Length); i++)
        {
            int partA = i < numbersA.Length ? numbersA[i] : 0;
            int partB = i < numbersB.Length ? numbersB[i] : 0;
            if (partA != partB)
                return partA.CompareTo(partB);
        }

        if (tagA == tagB)
            return 0;
        if (tagA == "")
            return 1;
        if (tagB == "")
            return -1;

        string[] fieldsA = tagA.Split('.');
        string[] fieldsB = tagB.Split('.');
        for (int i = 0; i < Math.Min(fieldsA.Length, fieldsB.Length); i++)
        {
            int result = int.TryParse(fieldsA[i], out int numberA) && int.TryParse(fieldsB[i], out int numberB)
                ? numberA.CompareTo(numberB)
                : string.Compare(fieldsA[i], fieldsB[i], StringComparison.OrdinalIgnoreCase);
            if (result != 0)
                return result;
        }
        return fieldsA.Length.CompareTo(fieldsB.Length);
    }

    /// <summary>Get the mods in an archive, by reading the manifest of each.</summary>
    /// <param name="zip">The archive to read.</param>
    /// <remarks>An archive whose files can't be read, or a manifest which isn't valid, is skipped.</remarks>
    public static List<ArchivedMod> ReadMods(ZipArchive zip)
    {
        List<ArchivedMod> mods = new();
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            if (!string.Equals(entry.Name, ModArchives.ManifestFileName, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                using StreamReader reader = new(entry.Open());
                using JsonDocument manifest = JsonDocument.Parse(reader.ReadToEnd(), ModArchives.JsonOptions);
                string? id = ModArchives.GetString(manifest.RootElement, "UniqueID");
                string? version = ModArchives.GetString(manifest.RootElement, "Version");
                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(version))
                    mods.Add(new ArchivedMod(id.Trim(), version.Trim(), entry.FullName[..^entry.Name.Length].Replace('\\', '/')));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException) { }
        }
        return mods;
    }

    /// <summary>Get a version in a form which can be compared with another, so <c>1.2</c> matches <c>1.2.0</c> like SMAPI shows it.</summary>
    /// <param name="version">The version to normalize.</param>
    public static string NormalizeVersion(string version)
    {
        version = version.Trim();
        int suffixStart = version.IndexOfAny(new[] { '-', '+' });
        string number = suffixStart >= 0 ? version[..suffixStart] : version;
        string suffix = suffixStart >= 0 ? version[suffixStart..] : "";

        List<string> parts = number.Split('.').ToList();
        while (parts.Count < 3)
            parts.Add("0");
        return (string.Join('.', parts) + suffix).ToLowerInvariant();
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Get the mods in an archive, reading it only if it changed since last time.</summary>
    /// <param name="path">The full path to the archive.</param>
    private IReadOnlyList<ArchivedMod> GetMods(string path)
    {
        try
        {
            FileInfo file = new(path);
            if (this.Cache.TryGetValue(path, out var cached) && cached.Size == file.Length && cached.LastWrite == file.LastWriteTimeUtc)
                return cached.Mods;

            List<ArchivedMod> mods;
            try
            {
                using ZipArchive zip = ZipFile.OpenRead(path);
                mods = ModArchives.ReadMods(zip);
            }
            catch (InvalidDataException)
            {
                mods = new List<ArchivedMod>(); // not a zip file
            }

            this.Cache[path] = (file.Length, file.LastWriteTimeUtc, mods);
            return mods;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<ArchivedMod>();
        }
    }

    /// <summary>Read a version's numbers and pre-release tag, ignoring any build metadata after <c>+</c>.</summary>
    /// <param name="version">The version to read.</param>
    /// <param name="numbers">The version numbers, like 1, 2 and 0 for <c>1.2.0</c>.</param>
    /// <param name="tag">The pre-release tag in lowercase, like <c>beta.2</c>, or an empty string if there's none.</param>
    /// <returns>Returns whether the version could be read.</returns>
    private static bool TryParseVersion(string version, out int[] numbers, out string tag)
    {
        version = version.Trim();
        int buildStart = version.IndexOf('+');
        if (buildStart >= 0)
            version = version[..buildStart];

        int tagStart = version.IndexOf('-');
        tag = tagStart >= 0 ? version[(tagStart + 1)..].ToLowerInvariant() : "";
        string[] parts = (tagStart >= 0 ? version[..tagStart] : version).Split('.');

        numbers = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out numbers[i]) || numbers[i] < 0)
                return false;
        }
        return parts.Length > 0;
    }

    /// <summary>Get a text field from a manifest, matching its name case-insensitively like SMAPI does.</summary>
    /// <param name="manifest">The manifest.</param>
    /// <param name="name">The field name.</param>
    private static string? GetString(JsonElement manifest, string name)
    {
        if (manifest.ValueKind != JsonValueKind.Object)
            return null;

        foreach (JsonProperty property in manifest.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
        }
        return null;
    }
}

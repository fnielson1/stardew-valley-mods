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

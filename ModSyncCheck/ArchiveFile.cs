using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace ModSyncCheck;

/// <summary>Reads the files in the archive formats mods are downloaded in: <c>.zip</c>, <c>.7z</c>, and <c>.rar</c>.</summary>
/// <remarks>Archives are only ever read, never changed, so an original download keeps the MD5 hash Nexus Mods has for it.</remarks>
internal static class ArchiveFile
{
    /*********
    ** Fields
    *********/
    /// <summary>The file extensions of the archive formats which can be read, with the dot.</summary>
    private static readonly string[] Extensions = { ".zip", ".7z", ".rar" };


    /*********
    ** Public methods
    *********/
    /// <summary>Get whether a file has the extension of an archive format which can be read.</summary>
    /// <param name="path">The file path.</param>
    public static bool HasArchiveExtension(string path)
    {
        return ArchiveFile.Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Get the archives directly in a folder.</summary>
    /// <param name="folder">The folder to search. Subfolders aren't searched.</param>
    public static IEnumerable<string> EnumerateIn(string folder)
    {
        return Directory.EnumerateFiles(folder).Where(ArchiveFile.HasArchiveExtension);
    }

    /// <summary>Get the file extension for a file's archive format, from its first bytes rather than its name.</summary>
    /// <param name="path">The file path.</param>
    /// <returns>Returns <c>.zip</c>, <c>.7z</c>, or <c>.rar</c>, or <c>null</c> if it isn't one of those formats.</returns>
    public static string? DetectExtension(string path)
    {
        byte[] header = new byte[6];
        int length = 0;
        using (FileStream stream = File.OpenRead(path))
        {
            for (int read; length < header.Length && (read = stream.Read(header, length, header.Length - length)) > 0;)
                length += read;
        }

        if (length >= 4 && header[0] == 'P' && header[1] == 'K' && header[2] is 3 or 5 or 7 && header[3] == header[2] + 1)
            return ".zip";
        if (length >= 6 && header.AsSpan().SequenceEqual(new byte[] { (byte)'7', (byte)'z', 0xBC, 0xAF, 0x27, 0x1C }))
            return ".7z";
        if (length >= 6 && header.AsSpan().SequenceEqual("Rar!\x1a\x07"u8))
            return ".rar";
        return null;
    }

    /// <summary>Read each file in an archive, in the order they're stored. Folders are skipped.</summary>
    /// <param name="path">The archive path. Its format is found from its content, not its name.</param>
    /// <param name="read">Called with each file's path within the archive, using forward slashes, and a function which opens its content. The stream is only valid during the call.</param>
    /// <exception cref="InvalidDataException">The file isn't a <c>.zip</c>, <c>.7z</c>, or <c>.rar</c> file, or it can't be read.</exception>
    public static void ReadFiles(string path, Action<string, Func<Stream>> read)
    {
        switch (ArchiveFile.DetectExtension(path))
        {
            // .zip files are read with .NET's own reader
            case ".zip":
                using (ZipArchive zip = ZipFile.OpenRead(path))
                {
                    foreach (ZipArchiveEntry entry in zip.Entries)
                    {
                        if (entry.Name != "")
                            read(entry.FullName.Replace('\\', '/'), entry.Open);
                    }
                }
                break;

            case ".7z":
            case ".rar":
                try
                {
                    using IArchive archive = ArchiveFactory.OpenArchive(path);

                    // a solid archive is compressed as one stream, so it's read in order rather than unpacking from the start for each file
                    if (archive.IsSolid || archive.Type == ArchiveType.SevenZip)
                    {
                        using IReader reader = archive.ExtractAllEntries();
                        while (reader.MoveToNextEntry())
                        {
                            if (!reader.Entry.IsDirectory && reader.Entry.Key is { } key)
                                read(key.Replace('\\', '/'), reader.OpenEntryStream);
                        }
                    }
                    else
                    {
                        foreach (IArchiveEntry entry in archive.Entries)
                        {
                            if (!entry.IsDirectory && entry.Key is { } key)
                                read(key.Replace('\\', '/'), entry.OpenEntryStream);
                        }
                    }
                }
                catch (Exception ex) when (ex is SharpCompressException or ArchiveException or ExtractionException or SharpCompress.Common.CryptographicException or System.Security.Cryptography.CryptographicException or InvalidOperationException or NotSupportedException)
                {
                    throw new InvalidDataException(ex.Message, ex);
                }
                break;

            default:
                throw new InvalidDataException("it isn't a .zip, .7z, or .rar file");
        }
    }
}

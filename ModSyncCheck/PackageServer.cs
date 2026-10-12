using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace ModSyncCheck;

/// <summary>A host mod which can be sent to a player.</summary>
/// <param name="DirectoryPath">The full path to the mod folder.</param>
/// <param name="RelativePath">The mod folder path relative to the mods folder.</param>
internal record SendableMod(string DirectoryPath, string RelativePath);

/// <summary>Packages a set of mods on request and serves the packages in chunks.</summary>
internal class PackageServer
{
    /*********
    ** Fields
    *********/
    /// <summary>The mods which can be requested, by unique ID.</summary>
    private readonly IReadOnlyDictionary<string, SendableMod> Mods;

    /// <summary>The folder in which to create the packaged mods.</summary>
    private readonly string TempDir;

    /// <summary>Log an error which prevented a mod from being sent.</summary>
    private readonly Action<string> LogError;

    /// <summary>The packaged mods which were created or are being created, by unique ID. Each task returns the package path.</summary>
    private readonly Dictionary<string, Task<string>> Packages = new(StringComparer.OrdinalIgnoreCase);


    /*********
    ** Public methods
    *********/
    /// <summary>Construct an instance.</summary>
    /// <param name="mods">The mods which can be requested, by unique ID.</param>
    /// <param name="tempDir">The folder in which to create the packaged mods.</param>
    /// <param name="logError">Log an error which prevented a mod from being sent.</param>
    public PackageServer(IReadOnlyDictionary<string, SendableMod> mods, string tempDir, Action<string> logError)
    {
        this.Mods = mods;
        this.TempDir = tempDir;
        this.LogError = logError;
    }

    /// <summary>Get the response to a download request, or <c>null</c> if the mod is still being packaged in the background.</summary>
    /// <param name="request">The requested data.</param>
    public ChunkResponse? TryGetResponse(ChunkRequest request)
    {
        if (!this.Mods.TryGetValue(request.ModId, out SendableMod? mod))
            return PackageServer.Error(request, "The host can't send this mod.");

        if (!this.Packages.TryGetValue(request.ModId, out Task<string>? package))
        {
            string zipPath = Path.Combine(this.TempDir, $"{this.Packages.Count}.zip");
            this.Packages[request.ModId] = package = Task.Run(() =>
            {
                ModFiles.CreateZip(mod.DirectoryPath, zipPath);
                return zipPath;
            });
        }

        if (!package.IsCompleted)
            return null;

        try
        {
            using FileStream stream = File.OpenRead(package.GetAwaiter().GetResult());
            if (request.Offset < 0 || request.Offset >= stream.Length)
                return PackageServer.Error(request, "Invalid download request.");

            byte[] data = new byte[Math.Min(ChunkResponse.MaxDataSize, stream.Length - request.Offset)];
            stream.Seek(request.Offset, SeekOrigin.Begin);
            for (int read = 0; read < data.Length;)
            {
                int count = stream.Read(data, read, data.Length - read);
                if (count == 0)
                    throw new EndOfStreamException();
                read += count;
            }

            return new ChunkResponse(request.ModId, request.Offset, stream.Length, mod.RelativePath, data, Error: null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            this.LogError($"Couldn't send mod '{request.ModId}' to a joining player:\n{ex}");
            return PackageServer.Error(request, "The host couldn't read the mod files.");
        }
    }

    /// <summary>Delete the packaged mods once they're no longer being written.</summary>
    public Task DeletePackagesAsync()
    {
        return Task.WhenAll(this.Packages.Values).ContinueWith(_ =>
        {
            try
            {
                if (Directory.Exists(this.TempDir))
                    Directory.Delete(this.TempDir, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        });
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Get a response which tells the player a mod can't be downloaded.</summary>
    /// <param name="request">The requested data.</param>
    /// <param name="error">The reason the mod can't be downloaded.</param>
    private static ChunkResponse Error(ChunkRequest request, string error)
    {
        return new ChunkResponse(request.ModId, request.Offset, 0, "", Array.Empty<byte>(), error);
    }
}

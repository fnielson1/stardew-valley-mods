using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Xunit;

namespace ModSyncCheck.Tests;

/// <summary>Runs the host's <see cref="PackageServer"/> and the joining player's <see cref="PackageDownloader"/> against each other in memory.</summary>
public sealed class DownloadProtocolTests : IDisposable
{
    private static readonly ModInfo Automate = new("Pathoschild.Automate", "Automate", "2.3.0");
    private static readonly ModInfo ContentPatcher = new("Pathoschild.ContentPatcher", "Content Patcher", "2.9.1");
    private static readonly ModInfo TimeSpeed = new("cantorsdust.TimeSpeed", "TimeSpeed", "2.8.0");

    /// <summary>A temporary folder for the test files.</summary>
    private readonly string Root = Path.Combine(Path.GetTempPath(), "ModSyncCheck.Tests", Guid.NewGuid().ToString("N"));

    /// <summary>The mods the host can send, by unique ID.</summary>
    private readonly Dictionary<string, SendableMod> HostMods = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether the host sends its settings for the mods added after this is set.</summary>
    private bool IncludeConfig;

    /// <summary>Whether the host sends the mods added after this is set, rather than only their settings.</summary>
    private bool AllowMods = true;

    /// <summary>Whether the player asks only for the host's settings for the mods.</summary>
    private bool RequestConfigOnly;

    /// <summary>The host side, once created.</summary>
    private PackageServer? Server;

    /// <summary>The requests sent by the player which the host hasn't answered yet.</summary>
    private readonly Queue<ChunkRequest> Requests = new();

    /// <summary>The mods which couldn't be downloaded, with the reason.</summary>
    private readonly List<(ModInfo Mod, string Error)> Failures = new();

    /// <summary>The errors logged by the host.</summary>
    private readonly List<string> HostErrors = new();

    /// <summary>The highest number of unanswered requests seen at once.</summary>
    private int MaxRequestsSeen;

    public void Dispose()
    {
        if (Directory.Exists(this.Root))
            Directory.Delete(this.Root, recursive: true);
    }

    [Fact]
    public void Download_TransfersEveryMod_AndInstalledFilesMatchTheHosts()
    {
        // large enough to need many chunks, and random so compression can't shrink it
        byte[] dll = this.RandomBytes(ChunkResponse.MaxDataSize * 20 + 123);
        byte[] image = this.RandomBytes(5000);
        this.AddHostMod(Automate, "Automate", ("manifest.json", "automate"u8.ToArray()), ("Automate.dll", dll), ("config.json", "host settings"u8.ToArray()));
        this.AddHostMod(ContentPatcher, "Group/ContentPatcher", ("manifest.json", "cp"u8.ToArray()), ("assets/icon.png", image));
        PackageDownloader downloader = this.CreateDownloader(Automate, ContentPatcher);

        downloader.Start();
        this.Pump(downloader);

        Assert.True(downloader.IsComplete);
        Assert.Empty(this.Failures);
        Assert.Empty(this.HostErrors);
        Assert.Equal(new[] { Automate, ContentPatcher }, downloader.Downloads.Select(download => download.Mod));
        Assert.Equal(new[] { "Automate", "Group/ContentPatcher" }, downloader.Downloads.Select(download => download.RelativePath));

        string modsRoot = Path.Combine(this.Root, "client", "Mods");
        foreach (DownloadedMod download in downloader.Downloads)
        {
            string targetDir = ModFiles.ResolveInstallPath(modsRoot, download.RelativePath);
            ModFiles.Install(download.ZipPath, targetDir, Path.Combine(this.Root, "client", "staging"), Path.Combine(this.Root, "client", "backup", download.Mod.Id));
        }
        Assert.Equal(dll, File.ReadAllBytes(Path.Combine(modsRoot, "Automate", "Automate.dll")));
        Assert.Equal(image, File.ReadAllBytes(Path.Combine(modsRoot, "Group", "ContentPatcher", "assets", "icon.png")));
        Assert.Equal("cp", File.ReadAllText(Path.Combine(modsRoot, "Group", "ContentPatcher", "manifest.json")));
        Assert.False(File.Exists(Path.Combine(modsRoot, "Automate", "config.json")));
    }

    [Fact]
    public void Download_WholeMod_NeverIncludesTheHostsSettings_EvenWhenTheHostRequiresThem()
    {
        this.IncludeConfig = true;
        this.AddHostMod(Automate, "Automate", ("manifest.json", "automate"u8.ToArray()), ("config.json", "host settings"u8.ToArray()));
        PackageDownloader downloader = this.CreateDownloader(Automate);

        downloader.Start();
        this.Pump(downloader);

        using System.IO.Compression.ZipArchive zip = System.IO.Compression.ZipFile.OpenRead(Assert.Single(downloader.Downloads).ZipPath);
        Assert.Equal(new[] { "manifest.json" }, zip.Entries.Select(entry => entry.FullName));
    }

    [Fact]
    public void Download_ConfigOnly_GetsOnlyTheHostsSettings()
    {
        this.IncludeConfig = true;
        this.AllowMods = false;
        this.RequestConfigOnly = true;
        this.AddHostMod(Automate, "Automate", ("manifest.json", "host manifest"u8.ToArray()), ("Automate.dll", this.RandomBytes(5000)), ("config.json", "host settings"u8.ToArray()));
        PackageDownloader downloader = this.CreateDownloader(Automate);

        downloader.Start();
        this.Pump(downloader);

        DownloadedMod download = Assert.Single(downloader.Downloads);
        Assert.True(download.ConfigOnly);
        Assert.Empty(this.Failures);
        string path = Path.Combine(this.Root, "client", "host-settings", "0.json");
        Assert.True(ModFiles.TryExtractConfig(download.ZipPath, path));
        Assert.Equal("host settings", File.ReadAllText(path));
        using System.IO.Compression.ZipArchive zip = System.IO.Compression.ZipFile.OpenRead(download.ZipPath);
        Assert.Single(zip.Entries);
    }

    [Fact]
    public void Download_WholeMod_WhenOnlySettingsAreAllowed_Fails()
    {
        this.IncludeConfig = true;
        this.AllowMods = false;
        this.AddHostMod(Automate, "Automate", ("manifest.json", "automate"u8.ToArray()), ("config.json", "host settings"u8.ToArray()));
        PackageDownloader downloader = this.CreateDownloader(Automate);

        downloader.Start();
        this.Pump(downloader);

        Assert.Equal(Automate, Assert.Single(this.Failures).Mod);
        Assert.Empty(downloader.Downloads);
    }

    [Fact]
    public void Download_Settings_WhenTheHostDoesNotRequireThem_Fails()
    {
        this.RequestConfigOnly = true;
        this.AddHostMod(Automate, "Automate", ("manifest.json", "automate"u8.ToArray()), ("config.json", "host settings"u8.ToArray()));
        PackageDownloader downloader = this.CreateDownloader(Automate);

        downloader.Start();
        this.Pump(downloader);

        Assert.Equal(Automate, Assert.Single(this.Failures).Mod);
        Assert.Empty(downloader.Downloads);
    }

    [Fact]
    public void Download_KeepsRequestsInFlightWithinTheLimit()
    {
        this.AddHostMod(Automate, "Automate", ("Automate.dll", this.RandomBytes(ChunkResponse.MaxDataSize * 30)));
        PackageDownloader downloader = this.CreateDownloader(Automate);

        downloader.Start();
        Assert.Single(this.Requests); // the package size isn't known until the first reply
        this.Pump(downloader);

        Assert.True(downloader.IsComplete);
        Assert.Equal(PackageDownloader.MaxRequestsInFlight, this.MaxRequestsSeen);
    }

    [Fact]
    public void Download_ReportsProgress()
    {
        this.AddHostMod(Automate, "Automate", ("Automate.dll", this.RandomBytes(ChunkResponse.MaxDataSize * 4)));
        PackageDownloader downloader = this.CreateDownloader(Automate);

        downloader.Start();
        Assert.Equal(Automate, downloader.CurrentMod);
        Assert.Equal(0, downloader.Received);
        Assert.Equal(-1, downloader.Total);

        this.AnswerNext(downloader);
        Assert.Equal(ChunkResponse.MaxDataSize, downloader.Received);
        Assert.True(downloader.Total > ChunkResponse.MaxDataSize * 4);
        Assert.False(downloader.IsComplete);

        this.Pump(downloader);
        Assert.Null(downloader.CurrentMod);
        Assert.Equal(1, downloader.Index);
    }

    [Fact]
    public void Download_ModTheHostWontSend_FailsThatModAndContinues()
    {
        this.AddHostMod(ContentPatcher, "ContentPatcher", ("manifest.json", "cp"u8.ToArray()));
        PackageDownloader downloader = this.CreateDownloader(Automate, ContentPatcher);

        downloader.Start();
        this.Pump(downloader);

        Assert.True(downloader.IsComplete);
        Assert.Equal(Automate, Assert.Single(this.Failures).Mod);
        Assert.Equal(ContentPatcher, Assert.Single(downloader.Downloads).Mod);
    }

    [Fact]
    public void Download_ChunkOutOfOrder_FailsThatModAndContinues()
    {
        this.AddHostMod(Automate, "Automate", ("Automate.dll", this.RandomBytes(ChunkResponse.MaxDataSize * 4)));
        this.AddHostMod(ContentPatcher, "ContentPatcher", ("manifest.json", "cp"u8.ToArray()));
        PackageServer server = this.CreateServer();
        PackageDownloader downloader = this.CreateDownloader(Automate, ContentPatcher);

        downloader.Start();
        this.AnswerNext(downloader, server);
        ChunkRequest skipped = this.Requests.Dequeue();
        this.AnswerNext(downloader, server); // arrives before the skipped chunk
        ChunkResponse late = this.WaitForResponse(server, skipped);
        downloader.OnChunk(late); // late reply for the failed mod
        this.Pump(downloader, server);

        Assert.True(downloader.IsComplete);
        Assert.Equal(Automate, Assert.Single(this.Failures).Mod);
        Assert.Equal(ContentPatcher, Assert.Single(downloader.Downloads).Mod);
    }

    [Fact]
    public void Download_EmptyChunk_FailsThatMod()
    {
        PackageDownloader downloader = this.CreateDownloader(Automate);

        downloader.Start();
        downloader.OnChunk(new ChunkResponse(Automate.Id, 0, 100, "Automate", Array.Empty<byte>(), Error: null));

        Assert.True(downloader.IsComplete);
        Assert.Single(this.Failures);
        Assert.Empty(downloader.Downloads);
    }

    [Fact]
    public void Download_ChunkForAnotherMod_IsIgnored()
    {
        this.AddHostMod(Automate, "Automate", ("manifest.json", "automate"u8.ToArray()));
        PackageDownloader downloader = this.CreateDownloader(Automate);

        downloader.Start();
        downloader.OnChunk(new ChunkResponse(TimeSpeed.Id, 0, 3, "TimeSpeed", new byte[] { 1, 2, 3 }, Error: null));
        Assert.False(downloader.IsComplete);
        Assert.Empty(this.Failures);

        this.Pump(downloader);
        Assert.Equal(Automate, Assert.Single(downloader.Downloads).Mod);
    }

    [Fact]
    public void Download_NoMods_IsCompleteImmediately()
    {
        PackageDownloader downloader = this.CreateDownloader();

        downloader.Start();

        Assert.True(downloader.IsComplete);
        Assert.Empty(this.Requests);
    }

    [Fact]
    public void Cancel_StopsTheDownload_AndReleasesTheFile()
    {
        this.AddHostMod(Automate, "Automate", ("Automate.dll", this.RandomBytes(ChunkResponse.MaxDataSize * 4)));
        PackageServer server = this.CreateServer();
        PackageDownloader downloader = this.CreateDownloader(Automate);

        downloader.Start();
        this.AnswerNext(downloader, server);
        downloader.Cancel();
        ChunkResponse late = this.WaitForResponse(server, this.Requests.Dequeue());
        downloader.OnChunk(late);

        Assert.True(downloader.IsComplete);
        Assert.Empty(downloader.Downloads);
        Directory.Delete(Path.Combine(this.Root, "client", "downloads"), recursive: true); // fails if the file is still open
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(long.MaxValue)]
    public void Server_InvalidOffset_ReturnsError(long offset)
    {
        this.AddHostMod(Automate, "Automate", ("manifest.json", "automate"u8.ToArray()));
        PackageServer server = this.CreateServer();

        ChunkResponse response = this.WaitForResponse(server, new ChunkRequest(Automate.Id, offset));

        Assert.NotNull(response.Error);
        Assert.Empty(response.Data);
    }

    [Fact]
    public void Server_ModIdCase_IsIgnored()
    {
        this.AddHostMod(Automate, "Automate", ("manifest.json", "automate"u8.ToArray()));
        PackageServer server = this.CreateServer();

        ChunkResponse response = this.WaitForResponse(server, new ChunkRequest(Automate.Id.ToUpperInvariant(), 0));

        Assert.Null(response.Error);
        Assert.Equal(response.TotalBytes, response.Data.Length);
    }

    [Fact]
    public async System.Threading.Tasks.Task Server_DeletePackages_RemovesTheTempFolder()
    {
        this.AddHostMod(Automate, "Automate", ("manifest.json", "automate"u8.ToArray()));
        PackageServer server = this.CreateServer();
        this.WaitForResponse(server, new ChunkRequest(Automate.Id, 0));
        Assert.True(Directory.Exists(Path.Combine(this.Root, "host", "temp")));

        await server.DeletePackagesAsync();

        Assert.False(Directory.Exists(Path.Combine(this.Root, "host", "temp")));
    }

    /// <summary>Create a mod folder on the host which can be sent.</summary>
    private void AddHostMod(ModInfo mod, string relativePath, params (string Path, byte[] Content)[] files)
    {
        string modDir = Path.Combine(this.Root, "host", "Mods", relativePath);
        foreach ((string path, byte[] content) in files)
        {
            string fullPath = Path.Combine(modDir, path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllBytes(fullPath, content);
        }
        this.HostMods[mod.Id] = new SendableMod(modDir, relativePath, AllowMod: this.AllowMods, AllowConfig: this.IncludeConfig);
    }

    /// <summary>Get the host side, which is created on first use for the mods added so far.</summary>
    private PackageServer CreateServer()
    {
        return this.Server ??= new PackageServer(this.HostMods, Path.Combine(this.Root, "host", "temp"), this.HostErrors.Add);
    }

    /// <summary>Create the joining player's side, which queues its requests for the host.</summary>
    private PackageDownloader CreateDownloader(params ModInfo[] mods)
    {
        string downloadDir = Path.Combine(this.Root, "client", "downloads");
        Directory.CreateDirectory(downloadDir);
        return new PackageDownloader(
            mods.Select(mod => new DownloadTarget(mod, this.RequestConfigOnly)).ToArray(),
            downloadDir,
            sendRequest: request =>
            {
                this.Requests.Enqueue(SyncMessagesTests.RoundTrip(request));
                this.MaxRequestsSeen = Math.Max(this.MaxRequestsSeen, this.Requests.Count);
            },
            onFailed: (mod, error) => this.Failures.Add((mod, error))
        );
    }

    /// <summary>Answer the player's requests until none are left.</summary>
    private void Pump(PackageDownloader downloader, PackageServer? server = null)
    {
        server ??= this.CreateServer();
        while (this.Requests.Count > 0)
            this.AnswerNext(downloader, server);
    }

    /// <summary>Answer the player's oldest request.</summary>
    private void AnswerNext(PackageDownloader downloader, PackageServer? server = null)
    {
        server ??= this.CreateServer();
        ChunkResponse response = this.WaitForResponse(server, this.Requests.Dequeue());
        downloader.OnChunk(SyncMessagesTests.RoundTrip(response));
    }

    /// <summary>Get the host's response to a request, waiting for the mod to be packaged if needed.</summary>
    private ChunkResponse WaitForResponse(PackageServer server, ChunkRequest request)
    {
        DateTime timeout = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            if (server.TryGetResponse(request) is { } response)
                return response;

            Assert.True(DateTime.UtcNow < timeout, "The host took too long to package the mod.");
            Thread.Sleep(5);
        }
    }

    /// <summary>Get reproducible random data.</summary>
    private byte[] RandomBytes(int length)
    {
        byte[] data = new byte[length];
        new Random(length).NextBytes(data);
        return data;
    }
}

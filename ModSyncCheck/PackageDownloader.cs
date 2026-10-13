using System;
using System.Collections.Generic;
using System.IO;

namespace ModSyncCheck;

/// <summary>A mod to download from the host.</summary>
/// <param name="Mod">The host's mod.</param>
/// <param name="ConfigOnly">Whether to download only the host's settings for the mod, rather than the mod itself.</param>
internal record DownloadTarget(ModInfo Mod, bool ConfigOnly);

/// <summary>A downloaded mod waiting to be installed.</summary>
/// <param name="Mod">The host's mod.</param>
/// <param name="ConfigOnly">Whether the package only has the host's settings for the mod, rather than the mod itself.</param>
/// <param name="ZipPath">The downloaded package.</param>
/// <param name="RelativePath">The mod's folder path relative to the host's mods folder.</param>
internal record DownloadedMod(ModInfo Mod, bool ConfigOnly, string ZipPath, string RelativePath);

/// <summary>Downloads a list of packaged mods from the host, one at a time, by requesting them in chunks.</summary>
internal class PackageDownloader
{
    /*********
    ** Fields
    *********/
    /// <summary>The number of download requests to keep in flight at once.</summary>
    public const int MaxRequestsInFlight = 8;

    /// <summary>The mods to download.</summary>
    private readonly IReadOnlyList<DownloadTarget> Targets;

    /// <summary>The folder to download the packages into.</summary>
    private readonly string DownloadDir;

    /// <summary>Send a download request to the host.</summary>
    private readonly Action<ChunkRequest> SendRequest;

    /// <summary>Handle a mod which couldn't be downloaded, given the mod and the reason.</summary>
    private readonly Action<ModInfo, string> OnFailed;

    /// <summary>The file being downloaded.</summary>
    private FileStream? Output;

    /// <summary>The position in the current mod's package to request next.</summary>
    private long NextOffset;

    /// <summary>The number of requests sent for the current mod which weren't answered yet.</summary>
    private int RequestsInFlight;

    /// <summary>The downloaded mods.</summary>
    private readonly List<DownloadedMod> DownloadedMods = new();


    /*********
    ** Accessors
    *********/
    /// <summary>The index of the mod being downloaded, which is equal to <see cref="Count"/> once they're all done.</summary>
    public int Index { get; private set; }

    /// <summary>The number of mods to download.</summary>
    public int Count => this.Targets.Count;

    /// <summary>The mod being downloaded, if any.</summary>
    public ModInfo? CurrentMod => this.CurrentTarget?.Mod;

    /// <summary>The number of bytes received for the current mod.</summary>
    public long Received { get; private set; }

    /// <summary>The package size for the current mod, or -1 if not known yet.</summary>
    public long Total { get; private set; }

    /// <summary>When the host last sent data, or when the current mod was started.</summary>
    public DateTime LastProgress { get; private set; }

    /// <summary>Whether every mod was either downloaded or failed.</summary>
    public bool IsComplete => this.Index >= this.Targets.Count;

    /// <summary>The downloaded mods.</summary>
    public IReadOnlyList<DownloadedMod> Downloads => this.DownloadedMods;


    /*********
    ** Public methods
    *********/
    /// <summary>Construct an instance.</summary>
    /// <param name="targets">The mods to download.</param>
    /// <param name="downloadDir">The folder to download the packages into, which must exist.</param>
    /// <param name="sendRequest">Send a download request to the host.</param>
    /// <param name="onFailed">Handle a mod which couldn't be downloaded, given the mod and the reason.</param>
    public PackageDownloader(IReadOnlyList<DownloadTarget> targets, string downloadDir, Action<ChunkRequest> sendRequest, Action<ModInfo, string> onFailed)
    {
        this.Targets = targets;
        this.DownloadDir = downloadDir;
        this.SendRequest = sendRequest;
        this.OnFailed = onFailed;
    }

    /// <summary>Start downloading the first mod.</summary>
    public void Start()
    {
        this.StartMod(0);
    }

    /// <summary>Handle part of a packaged mod sent by the host.</summary>
    /// <param name="chunk">The received data.</param>
    public void OnChunk(ChunkResponse chunk)
    {
        // ignore late replies for a mod which already failed
        if (this.Output == null || this.CurrentMod == null || !string.Equals(chunk.ModId, this.CurrentMod.Id, StringComparison.OrdinalIgnoreCase))
            return;

        this.RequestsInFlight--;
        this.LastProgress = DateTime.UtcNow;

        if (chunk.Error != null)
        {
            this.FailCurrentMod(chunk.Error);
            return;
        }
        if (chunk.Offset != this.Received || chunk.Data.Length == 0)
        {
            this.FailCurrentMod("The download was corrupted.");
            return;
        }

        try
        {
            this.Output.Write(chunk.Data);
        }
        catch (IOException ex)
        {
            this.FailCurrentMod(ex.Message);
            return;
        }
        this.Received += chunk.Data.Length;
        this.Total = chunk.TotalBytes;

        if (this.Received >= this.Total)
        {
            this.DownloadedMods.Add(new DownloadedMod(this.CurrentMod, this.CurrentTarget!.ConfigOnly, this.Output.Name, chunk.RelativePath));
            this.CloseOutput();
            this.StartMod(this.Index + 1);
        }
        else
        {
            while (this.RequestsInFlight < PackageDownloader.MaxRequestsInFlight && this.NextOffset < this.Total)
                this.RequestNextChunk();
        }
    }

    /// <summary>Stop downloading and release the file being downloaded.</summary>
    public void Cancel()
    {
        this.CloseOutput();
        this.Index = this.Targets.Count;
    }


    /*********
    ** Private methods
    *********/
    /// <summary>The download in progress, if any.</summary>
    private DownloadTarget? CurrentTarget => this.Index < this.Targets.Count ? this.Targets[this.Index] : null;

    /// <summary>Start downloading a mod, if there are any left.</summary>
    /// <param name="index">The index of the mod to download.</param>
    private void StartMod(int index)
    {
        this.Index = index;
        this.Received = 0;
        this.Total = -1;
        this.NextOffset = 0;
        this.RequestsInFlight = 0;
        this.LastProgress = DateTime.UtcNow;
        if (this.IsComplete)
            return;

        try
        {
            this.Output = File.Create(Path.Combine(this.DownloadDir, $"{index}.zip"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            this.FailCurrentMod(ex.Message);
            return;
        }

        // the package size isn't known until the first reply
        this.RequestNextChunk();
    }

    /// <summary>Ask the host for the next part of the current mod.</summary>
    private void RequestNextChunk()
    {
        ChunkRequest request = new(this.CurrentMod!.Id, this.NextOffset, this.CurrentTarget!.ConfigOnly);
        this.NextOffset += ChunkResponse.MaxDataSize;
        this.RequestsInFlight++;
        this.SendRequest(request);
    }

    /// <summary>Skip the current mod after a failure, and move on to the next one.</summary>
    /// <param name="error">The reason it failed.</param>
    private void FailCurrentMod(string error)
    {
        ModInfo mod = this.CurrentMod!;
        this.CloseOutput();
        this.OnFailed(mod, error);
        this.StartMod(this.Index + 1);
    }

    /// <summary>Close the file being downloaded, if any.</summary>
    private void CloseOutput()
    {
        this.Output?.Dispose();
        this.Output = null;
    }
}

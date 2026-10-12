using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Network;

namespace ModSyncCheck;

/// <summary>When blocked from joining a host, shows the reason and optionally downloads the host's mods.</summary>
internal class ClientSync
{
    /*********
    ** Fields
    *********/
    /// <summary>How long to wait for the host to send more data before giving up.</summary>
    private static readonly TimeSpan StallTimeout = TimeSpan.FromMinutes(2);

    /// <summary>The SMAPI API for this mod.</summary>
    private readonly IModHelper Helper;

    /// <summary>Writes messages to the SMAPI log.</summary>
    private readonly IMonitor Monitor;

    /// <summary>This mod's unique ID.</summary>
    private readonly string ModId;

    /// <summary>Builds the player-facing text for a mod comparison.</summary>
    private readonly ComparisonText Text;

    /// <summary>What the player is currently doing.</summary>
    private Stage CurrentStage = Stage.Idle;

    /// <summary>The message from the host which hasn't been shown yet, if any.</summary>
    private BlockedMessage? Pending;

    /// <summary>The differences between the local mods and the host's.</summary>
    private ModComparison Comparison = null!; // set when blocked

    /// <summary>The connection to the host, kept open while the player decides and downloads.</summary>
    private Client? Connection;

    /// <summary>The local mods folder.</summary>
    private string ModsRoot = null!; // set when the download prompt is shown

    /// <summary>The mods to download.</summary>
    private List<ModInfo> Mods = new();

    /// <summary>Downloads the host's mods, if a download is in progress or waiting to be installed.</summary>
    private PackageDownloader? Downloader;

    /// <summary>The mods which couldn't be downloaded or installed, with the reason.</summary>
    private readonly List<string> Failures = new();

    /// <summary>The task installing the downloaded mods, which returns the number installed.</summary>
    private Task<int>? InstallTask;


    /*********
    ** Accessors
    *********/
    /// <summary>The unique ID of the host being joined, if any.</summary>
    public long? HostId { get; set; }


    /*********
    ** Public methods
    *********/
    /// <summary>Construct an instance.</summary>
    /// <param name="helper">The SMAPI API for this mod.</param>
    /// <param name="monitor">Writes messages to the SMAPI log.</param>
    /// <param name="modId">This mod's unique ID.</param>
    /// <param name="text">Builds the player-facing text for a mod comparison.</param>
    public ClientSync(IModHelper helper, IMonitor monitor, string modId, ComparisonText text)
    {
        this.Helper = helper;
        this.Monitor = monitor;
        this.ModId = modId;
        this.Text = text;
    }

    /// <summary>Handle the host saying we can't join.</summary>
    /// <param name="message">The reason we can't join.</param>
    public void OnBlocked(BlockedMessage message)
    {
        if (this.CurrentStage == Stage.Idle)
            this.Pending = message;
    }

    /// <summary>Handle part of a packaged mod sent by the host.</summary>
    /// <param name="chunk">The received data.</param>
    public void OnChunk(ChunkResponse chunk)
    {
        if (this.CurrentStage != Stage.Downloading || this.Downloader == null)
            return;

        this.Downloader.OnChunk(chunk);
        if (this.Downloader.IsComplete)
            this.StartInstall();
    }

    /// <summary>Update the dialogs and download.</summary>
    public void Update()
    {
        // keep the connection to the host alive, since the farmhand menu which normally does this was closed
        if (this.Connection != null)
        {
            try
            {
                this.Connection.receiveMessages();
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Lost the connection to the host:\n{ex}", LogLevel.Error);
                this.Abort();
                return;
            }
        }

        switch (this.CurrentStage)
        {
            case Stage.Idle:
                if (this.Pending != null)
                    this.ShowBlocked();
                break;

            // the player or game closed the dialog some other way
            case Stage.Prompt or Stage.Downloading when TitleMenu.subMenu is not SyncDialog:
                this.Abort();
                break;

            case Stage.Downloading when DateTime.UtcNow - this.Downloader!.LastProgress > ClientSync.StallTimeout:
                this.Abort();
                ClientSync.ShowWarning(this.Helper.Translation.Get("client.download-lost"));
                break;

            case Stage.Installing when this.InstallTask!.IsCompleted:
                this.Finish();
                break;
        }
    }

    /// <summary>Show a message on the title screen.</summary>
    /// <param name="text">The text to show.</param>
    public static void ShowWarning(string text)
    {
        ClientSync.ShowDialog(new SyncDialog(text));
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Show a dialog on the title screen in place of the current submenu.</summary>
    /// <param name="dialog">The dialog to show.</param>
    private static void ShowDialog(SyncDialog dialog)
    {
        // picking a farmhand switches the game to its loading screen, which the farmhand menu normally undoes when the host replies
        if (Game1.activeClickableMenu is TitleMenu)
            Game1.gameMode = Game1.titleScreenGameMode;

        TitleMenu.subMenu = dialog;
    }

    /// <summary>Show why we can't join, and offer to download the host's mods if possible.</summary>
    private void ShowBlocked()
    {
        BlockedMessage message = this.Pending!;
        this.Pending = null;
        this.Comparison = message.Comparison;

        this.Monitor.Log($"The host blocked you from joining because your mods don't match: {this.Text.GetCounts(message.Comparison)}.", LogLevel.Warn);
        foreach (string line in this.Text.GetEntries(message.Comparison))
            this.Monitor.Log($"   {line}", LogLevel.Warn);

        string warning = this.Text.BuildWarning(message.Comparison);
        this.Mods = message.Comparison.GetNeededMods().ToList();
        string? modsRoot = SmapiInternals.GetModsRoot(this.Helper.ModRegistry.Get(this.ModId)!);

        // the host rejected us, so we should still be on the farmhand selection screen with an open connection
        if (!message.CanDownload || this.Mods.Count == 0 || modsRoot == null || Game1.activeClickableMenu is not TitleMenu || TitleMenu.subMenu is not FarmhandMenu { client: not null } farmhandMenu)
        {
            this.SendEnded();
            if (Game1.activeClickableMenu is TitleMenu)
                ClientSync.ShowWarning(warning); // replacing the farmhand menu closes the connection
            SmapiInternals.ResetPeers();
            return;
        }

        // take over the connection so it stays open when the farmhand menu is closed
        this.ModsRoot = modsRoot;
        this.Connection = farmhandMenu.client;
        farmhandMenu.client = null;

        List<string> lines = new() { warning, "" };
        if (message.Comparison.Extra.Count > 0)
            lines.Add(this.Helper.Translation.Get("client.extra-note"));
        lines.Add(this.Helper.Translation.Get("client.download-prompt", new { count = this.Mods.Count, size = ClientSync.FormatSize(message.DownloadBytes) }));

        this.CurrentStage = Stage.Prompt;
        ClientSync.ShowDialog(new SyncDialog(string.Join("\n", lines), onConfirm: this.StartDownload, onCancel: this.Cancel));
    }

    /// <summary>Start downloading the host's mods.</summary>
    private void StartDownload()
    {
        this.Failures.Clear();
        this.CurrentStage = Stage.Downloading;

        string downloadDir = this.GetWorkPath("downloads");
        try
        {
            if (Directory.Exists(downloadDir))
                Directory.Delete(downloadDir, recursive: true);
            Directory.CreateDirectory(downloadDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            this.Monitor.Log($"Couldn't prepare the download folder:\n{ex}", LogLevel.Error);
            this.Abort();
            ClientSync.ShowWarning(this.Helper.Translation.Get("client.download-failed", new { error = ex.Message }));
            return;
        }

        // the dialog is sized by its initial text
        string sizingText = this.Helper.Translation.Get("client.downloading", new { index = this.Mods.Count, count = this.Mods.Count }) + "\n" + new string('M', 36);
        ClientSync.ShowDialog(new SyncDialog(sizingText, onCancel: this.Cancel, getText: this.GetProgressText));

        this.Downloader = new PackageDownloader(
            mods: this.Mods,
            downloadDir: downloadDir,
            sendRequest: request => this.SendToHost(request, MessageTypes.ChunkRequest),
            onFailed: (mod, error) =>
            {
                this.Monitor.Log($"Couldn't download {mod.Name}: {error}", LogLevel.Error);
                this.Failures.Add($"{mod.Name}: {error}");
            }
        );
        this.Downloader.Start();
        if (this.Downloader.IsComplete)
            this.StartInstall();
    }

    /// <summary>Install the downloaded mods in the background.</summary>
    private void StartInstall()
    {
        this.CurrentStage = Stage.Installing;

        // SMAPI's mod registry isn't safe to read from another thread
        IReadOnlyList<DownloadedMod> downloads = this.Downloader!.Downloads;
        Dictionary<DownloadedMod, string?> installedPaths = downloads.ToDictionary(
            download => download,
            download => this.Helper.ModRegistry.Get(download.Mod.Id) is { } installed ? SmapiInternals.GetDirectoryPath(installed) : null
        );
        string backupRoot = Path.Combine(this.GetWorkPath("backup"), DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss"));

        this.InstallTask = Task.Run(() =>
        {
            int installed = 0;
            for (int i = 0; i < downloads.Count; i++)
            {
                DownloadedMod download = downloads[i];
                try
                {
                    string targetDir = installedPaths[download] ?? ModFiles.ResolveInstallPath(this.ModsRoot, download.RelativePath);
                    ModFiles.Install(
                        zipPath: download.ZipPath,
                        targetDir: targetDir,
                        stagingDir: Path.Combine(this.GetWorkPath("staging"), i.ToString()),
                        backupDir: Path.Combine(backupRoot, $"{i} - {Path.GetFileName(targetDir)}")
                    );
                    this.Monitor.Log($"Installed {download.Mod.Name} {download.Mod.Version} from the host into '{targetDir}'.", LogLevel.Info);
                    installed++;
                }
                catch (Exception ex)
                {
                    this.Monitor.Log($"Couldn't install {download.Mod.Name}:\n{ex}", LogLevel.Error);
                    this.Failures.Add($"{download.Mod.Name}: {ex.Message}");
                }
            }

            this.DeleteWorkFolder("downloads");
            this.DeleteWorkFolder("staging");
            return installed;
        });
    }

    /// <summary>Show the result after installing the downloaded mods.</summary>
    private void Finish()
    {
        int installed = this.InstallTask!.Result;
        this.InstallTask = null;
        this.Close();

        List<string> lines = new();
        if (installed > 0)
            lines.Add(this.Helper.Translation.Get("client.installed", new { count = installed }));
        if (this.Failures.Count > 0)
        {
            lines.Add(this.Helper.Translation.Get("client.install-failures"));
            lines.AddRange(this.Failures);
        }
        if (this.Comparison.Extra.Count > 0)
            lines.Add(this.Helper.Translation.Get("client.extra-note"));

        ClientSync.ShowWarning(string.Join("\n", lines));
    }

    /// <summary>Handle the player declining the download or cancelling it.</summary>
    private void Cancel()
    {
        if (this.CurrentStage == Stage.Installing)
            return; // can't be interrupted safely

        this.Abort();
        (Game1.activeClickableMenu as TitleMenu)?.backButtonPressed();
    }

    /// <summary>Stop the download without installing anything, and disconnect from the host.</summary>
    private void Abort()
    {
        if (this.CurrentStage == Stage.Installing)
            return;

        this.Downloader?.Cancel();
        this.DeleteWorkFolder("downloads");
        this.Close();
    }

    /// <summary>Disconnect from the host and reset for the next attempt to join.</summary>
    private void Close()
    {
        this.SendEnded();

        try
        {
            this.Connection?.disconnect();
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Couldn't disconnect from the host cleanly:\n{ex}");
        }
        this.Connection = null;
        this.Downloader = null;
        this.CurrentStage = Stage.Idle;
        this.Pending = null;

        SmapiInternals.ResetPeers();
    }

    /// <summary>Tell the host we're done, so it can let us try joining again.</summary>
    private void SendEnded()
    {
        this.SendToHost(new SyncEndedMessage(), MessageTypes.SyncEnded);
    }

    /// <summary>Send a mod message to the host.</summary>
    /// <param name="message">The message to send.</param>
    /// <param name="type">The message type.</param>
    private void SendToHost<TMessage>(TMessage message, string type)
        where TMessage : notnull
    {
        if (this.HostId == null)
            return;

        try
        {
            this.Helper.Multiplayer.SendMessage(message, type, modIDs: new[] { this.ModId }, playerIDs: new[] { this.HostId.Value });
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Couldn't send a '{type}' message to the host:\n{ex}");
        }
    }

    /// <summary>Get the text for the progress dialog.</summary>
    private string GetProgressText()
    {
        if (this.CurrentStage != Stage.Downloading || this.Downloader?.CurrentMod is not { } mod)
            return this.Helper.Translation.Get("client.installing");

        int percent = this.Downloader.Total > 0
            ? (int)(this.Downloader.Received * 100 / this.Downloader.Total)
            : 0;
        return
            this.Helper.Translation.Get("client.downloading", new { index = this.Downloader.Index + 1, count = this.Downloader.Count })
            + "\n"
            + this.Helper.Translation.Get("client.downloading-mod", new { name = mod.Name, percent });
    }

    /// <summary>Get the path to a folder within this mod's work folder.</summary>
    /// <param name="name">The folder name.</param>
    private string GetWorkPath(string name)
    {
        return Path.Combine(this.ModsRoot, ModFiles.WorkFolderName, name);
    }

    /// <summary>Delete a folder within this mod's work folder, if possible.</summary>
    /// <param name="name">The folder name.</param>
    private void DeleteWorkFolder(string name)
    {
        try
        {
            string path = this.GetWorkPath(name);
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            this.Monitor.Log($"Couldn't delete the temporary '{name}' folder:\n{ex}");
        }
    }

    /// <summary>Get a human-readable file size.</summary>
    /// <param name="bytes">The size in bytes.</param>
    private static string FormatSize(long bytes)
    {
        return bytes >= 1024 * 1024
            ? $"{bytes / (1024.0 * 1024.0):0.#} MB"
            : $"{Math.Max(1, bytes / 1024)} KB";
    }

    /// <summary>What the player is currently doing.</summary>
    private enum Stage
    {
        /// <summary>Nothing is in progress.</summary>
        Idle,

        /// <summary>The player is being asked whether to download the host's mods.</summary>
        Prompt,

        /// <summary>The host's mods are being downloaded.</summary>
        Downloading,

        /// <summary>The downloaded mods are being installed.</summary>
        Installing
    }
}

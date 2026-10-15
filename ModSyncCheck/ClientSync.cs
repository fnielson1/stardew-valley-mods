using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Network;

namespace ModSyncCheck;

/// <summary>When the host says we can't join yet, uses the host's settings if that's all it needs, else shows the reason and optionally downloads the host's mods.</summary>
internal class ClientSync
{
    /*********
    ** Fields
    *********/
    /// <summary>The name of the folder within the work folder which has the unpacked mods waiting to be moved into place.</summary>
    private const string StagingFolderName = "pending";

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

    /// <summary>Makes the running mods use the host's settings without changing our settings files.</summary>
    private readonly HostSettings Settings;

    /// <summary>What the player is currently doing.</summary>
    private Stage CurrentStage = Stage.Idle;

    /// <summary>The message from the host which hasn't been handled yet, if any.</summary>
    private BlockedMessage? Pending;

    /// <summary>The connection to the host, kept open while the player decides and downloads mods.</summary>
    private Client? Connection;

    /// <summary>The local mods folder.</summary>
    private string ModsRoot = null!; // set when the download prompt is shown

    /// <summary>The mods to download and install.</summary>
    private List<DownloadTarget> Targets = new();

    /// <summary>Whether the player trusts the host for the current download, so mods packaged from its mod folders are installed and an original archive which can't be checked with Nexus Mods is installed anyway.</summary>
    private bool TrustHost = true;

    /// <summary>The mods the player needs which weren't included in the current download because they didn't trust the host.</summary>
    private IReadOnlyList<ModInfo> LeftOut = Array.Empty<ModInfo>();

    /// <summary>The mod comparison which led to the current download.</summary>
    private ModComparison? Comparison;

    /// <summary>When we last told the host that we're using its settings.</summary>
    private DateTime LastVerified;

    /// <summary>The number of times in a row we told the host that we're using its settings without being let in.</summary>
    private int VerifyAttempts;

    /// <summary>Downloads the host's mods or settings, if a download is in progress or waiting to be installed.</summary>
    private PackageDownloader? Downloader;

    /// <summary>The mods which couldn't be downloaded or installed, with the reason.</summary>
    private readonly List<string> Failures = new();

    /// <summary>The unique IDs of the mods which couldn't be downloaded or installed.</summary>
    private readonly HashSet<string> FailedIds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The names of the mods sent as original archives which couldn't be checked with Nexus Mods, and were installed anyway.</summary>
    private readonly List<string> Unverified = new();

    /// <summary>The task installing the downloaded mods, which returns the number installed.</summary>
    private Task<int>? InstallTask;

    /// <summary>The changed host settings to get once the current download is done, if any.</summary>
    private SettingsChangedMessage? PendingSettings;

    /// <summary>The names of the mods for which the player was told their settings change was undone.</summary>
    private readonly HashSet<string> RevertedNames = new();


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
        this.Settings = new HostSettings(helper, monitor);
    }

    /// <summary>Handle the host saying we can't join yet.</summary>
    /// <param name="message">The reason we can't join.</param>
    public void OnBlocked(BlockedMessage message)
    {
        if (this.CurrentStage == Stage.Idle)
            this.Pending = message;
    }

    /// <summary>Handle part of a package sent by the host.</summary>
    /// <param name="chunk">The received data.</param>
    public void OnChunk(ChunkResponse chunk)
    {
        if (this.Downloader == null || this.CurrentStage is not (Stage.Downloading or Stage.SyncingSettings or Stage.RefreshingSettings))
            return;

        this.Downloader.OnChunk(chunk);
        if (!this.Downloader.IsComplete)
            return;

        switch (this.CurrentStage)
        {
            case Stage.SyncingSettings:
                this.FinishSettingsSync();
                break;

            case Stage.RefreshingSettings:
                this.FinishSettingsRefresh();
                break;

            default:
                this.StartInstall();
                break;
        }
    }

    /// <summary>Handle the game having started: finish or clean up after the mods downloaded the last time the game ran.</summary>
    public void OnGameLaunched()
    {
        string? modsRoot = SmapiInternals.GetModsRoot(this.Helper.ModRegistry.Get(this.ModId)!);
        if (modsRoot == null)
            return;
        this.ModsRoot = modsRoot;

        try
        {
            // the game closed in a way which didn't let the mods be installed, like the computer shutting down
            if (File.Exists(this.SwapListPath))
            {
                GameRestarter.InstallAfterExit(this.SwapListPath, this.RestartFlagPath);
                this.Monitor.Log("The mods you downloaded from a host weren't installed when the game last closed. They'll be installed when you close the game.", LogLevel.Warn);
            }
            else
            {
                // deleting a file throws if its folder doesn't exist, which it won't until something is downloaded
                if (File.Exists(this.RestartFlagPath))
                    File.Delete(this.RestartFlagPath);
                this.DeleteWorkFolder(ClientSync.StagingFolderName);
            }
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Couldn't finish installing the mods downloaded from a host:\n{ex}", LogLevel.Warn);
        }
    }

    /// <summary>Handle having joined the host's game.</summary>
    public void OnJoinedGame()
    {
        this.ShowSkippedSettings();
    }

    /// <summary>Handle the host changing the settings we must use while we're in its game.</summary>
    /// <param name="message">The host's new settings.</param>
    public void OnSettingsChanged(SettingsChangedMessage message)
    {
        if (!Context.IsWorldReady || Context.IsMainPlayer || this.HostId == null)
            return;

        if (this.CurrentStage != Stage.Idle)
        {
            this.PendingSettings = message;
            return;
        }

        List<ModInfo> mods = this.Settings.GetModsToSync(this.HostId.Value, message.ConfigHashes);
        if (mods.Count == 0)
        {
            this.SendEnded();
            this.ShowSkippedSettings();
            return;
        }

        this.CurrentStage = Stage.RefreshingSettings;
        if (!this.StartSettingsDownload(mods))
            this.EndSettingsRefresh();
    }

    /// <summary>While in the host's game, undo any change to the settings which must match the host's.</summary>
    public void UndoSettingsChanges()
    {
        List<string> reverted = this.Settings.UndoChanges();

        // a mod which changes its own settings as it runs would be undone over and over, so only say it once
        reverted.RemoveAll(name => !this.RevertedNames.Add(name));
        if (reverted.Count > 0)
            Game1.chatBox?.addInfoMessage(this.Helper.Translation.Get("client.settings-reverted", new { names = string.Join(", ", reverted) }));
    }

    /// <summary>Handle leaving the host's game.</summary>
    public void OnReturnedToTitle()
    {
        if (this.CurrentStage == Stage.RefreshingSettings)
        {
            this.Downloader?.Cancel();
            this.Downloader = null;
            this.CurrentStage = Stage.Idle;
        }
        this.PendingSettings = null;
        this.RevertedNames.Clear();

        this.Settings.RestoreOwnSettings();
    }

    /// <summary>Update the dialogs and downloads.</summary>
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
                    this.HandleBlocked();
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

            // the player backed out of the farmhand screen
            case Stage.SyncingSettings when ClientSync.GetFarmhandMenu() == null:
                this.CancelSettingsSync();
                break;

            case Stage.SyncingSettings when DateTime.UtcNow - this.Downloader!.LastProgress > ClientSync.StallTimeout:
                this.CancelSettingsSync();
                ClientSync.ShowWarning(this.Helper.Translation.Get("client.download-lost"));
                break;

            case Stage.RefreshingSettings when DateTime.UtcNow - this.Downloader!.LastProgress > ClientSync.StallTimeout:
                this.Monitor.Log("The host stopped sending its changed settings, so the previous ones are still in use.", LogLevel.Warn);
                this.Downloader.Cancel();
                this.EndSettingsRefresh();
                break;
        }
    }

    /// <summary>Show a message on the title screen.</summary>
    /// <param name="text">The text to show.</param>
    public static void ShowWarning(string text)
    {
        ClientSync.ShowDialog(new SyncDialog(text));
    }

    /// <summary>Show which of the host's mods we don't have at the same version on the title screen, with links to download them from Nexus Mods.</summary>
    /// <param name="comparison">The mod differences.</param>
    public void ShowMismatchWarning(ModComparison comparison)
    {
        ClientSync.ShowDialog(this.CreateMismatchDialog(comparison));
    }


    /*********
    ** Private methods
    *********/
    /****
    ** Shared
    ****/
    /// <summary>Show a dialog on the title screen in place of the current submenu.</summary>
    /// <param name="dialog">The dialog to show.</param>
    private static void ShowDialog(SyncDialog dialog)
    {
        // picking a farmhand switches the game to its loading screen, which the farmhand menu normally undoes when the host replies
        if (Game1.activeClickableMenu is TitleMenu)
            Game1.gameMode = Game1.titleScreenGameMode;

        TitleMenu.subMenu = dialog;
    }

    /// <summary>Get the farmhand selection screen if it's open with a connection to the host, which it should be when the host rejects us.</summary>
    private static FarmhandMenu? GetFarmhandMenu()
    {
        return Game1.activeClickableMenu is TitleMenu && TitleMenu.subMenu is FarmhandMenu { client: not null } menu
            ? menu
            : null;
    }

    /// <summary>Handle the host saying we can't join yet.</summary>
    private void HandleBlocked()
    {
        BlockedMessage message = this.Pending!;
        this.Pending = null;

        if (message.Comparison.IsMatch)
            this.HandleSettingsRequired(message);
        else
            this.ShowModMismatch(message);
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

    /****
    ** Host settings
    ****/
    /// <summary>Handle being blocked only because the host requires its mod settings: use them and ask to join again, without changing our settings files.</summary>
    /// <param name="message">The message from the host.</param>
    private void HandleSettingsRequired(BlockedMessage message)
    {
        FarmhandMenu? menu = ClientSync.GetFarmhandMenu();
        if (menu == null || this.HostId == null)
        {
            this.SendEnded();
            SmapiInternals.ResetPeers();
            return;
        }

        // the host can't see our settings, so we compare them with hashes of its own
        List<ModInfo> mods = message.ConfigHashes != null && message.CanDownload
            ? this.Settings.GetModsToSync(this.HostId.Value, message.ConfigHashes)
            : new List<ModInfo>();
        if (mods.Count == 0)
        {
            this.VerifyAndRejoin(menu);
            return;
        }

        // the farmhand screen stays open and keeps the connection alive while the settings download
        this.CurrentStage = Stage.SyncingSettings;
        if (!this.StartSettingsDownload(mods))
        {
            this.CurrentStage = Stage.Idle;
            this.VerifyAndRejoin(menu);
        }
    }

    /// <summary>Start downloading the host's settings for some mods.</summary>
    /// <param name="mods">The mods whose settings to get.</param>
    /// <returns>Returns whether the download started.</returns>
    private bool StartSettingsDownload(List<ModInfo> mods)
    {
        try
        {
            this.Settings.PrepareDownloadDir();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            this.Monitor.Log($"Couldn't prepare the folder for the host's settings, so your own are used:\n{ex}", LogLevel.Warn);
            return false;
        }

        Stage stage = this.CurrentStage;
        this.Downloader = new PackageDownloader(
            targets: mods.Select(mod => new DownloadTarget(mod, ConfigOnly: true)).ToList(),
            downloadDir: this.Settings.DownloadDir,
            sendRequest: request => this.SendToHost(request, MessageTypes.ChunkRequest),
            onFailed: (mod, error) =>
            {
                this.Monitor.Log($"Couldn't get the host's settings for {mod.Name}: {error}", LogLevel.Warn);
                this.Settings.MarkSkipped(mod.Name);
            }
        );
        this.Downloader.Start();
        if (this.Downloader.IsComplete)
        {
            if (stage == Stage.RefreshingSettings)
                this.FinishSettingsRefresh();
            else
                this.FinishSettingsSync();
        }
        return true;
    }

    /// <summary>Make the running mods use the changed settings downloaded from the host while in its game.</summary>
    private void FinishSettingsRefresh()
    {
        IReadOnlyList<DownloadedMod> downloads = this.Downloader!.Downloads;
        this.Settings.Apply(this.HostId!.Value, downloads);
        if (downloads.Count > 0)
            Game1.chatBox?.addInfoMessage(this.Helper.Translation.Get("client.settings-updated", new { names = string.Join(", ", downloads.Select(download => download.Mod.Name)) }));

        this.EndSettingsRefresh();
    }

    /// <summary>Stop getting the host's changed settings, and get any newer ones the host told us about in the meantime.</summary>
    private void EndSettingsRefresh()
    {
        this.Downloader = null;
        this.Settings.DeleteDownloads();
        this.CurrentStage = Stage.Idle;
        this.SendEnded();
        this.ShowSkippedSettings();

        if (this.PendingSettings is { } pending)
        {
            this.PendingSettings = null;
            this.OnSettingsChanged(pending);
        }
    }

    /// <summary>Tell the player which mods are using their own settings because the host's couldn't be applied, if any.</summary>
    private void ShowSkippedSettings()
    {
        if (this.Settings.Skipped.Count == 0)
            return;

        string text = this.Helper.Translation.Get("client.config-skipped", new { names = string.Join(", ", this.Settings.Skipped) });
        this.Monitor.Log(text, LogLevel.Warn);
        Game1.chatBox?.addInfoMessage(text);
    }

    /// <summary>Make the running mods use the downloaded settings, then ask to join again.</summary>
    private void FinishSettingsSync()
    {
        this.Settings.Apply(this.HostId!.Value, this.Downloader!.Downloads);
        this.Downloader = null;
        this.CurrentStage = Stage.Idle;

        if (ClientSync.GetFarmhandMenu() is { } menu)
            this.VerifyAndRejoin(menu);
        else
        {
            this.SendEnded();
            SmapiInternals.ResetPeers();
        }
    }

    /// <summary>Stop downloading the host's settings.</summary>
    private void CancelSettingsSync()
    {
        this.Downloader?.Cancel();
        this.Downloader = null;
        this.Settings.DeleteDownloads();
        this.CurrentStage = Stage.Idle;

        this.SendEnded();
        SmapiInternals.ResetPeers();
    }

    /// <summary>Tell the host we're using its settings as far as possible, and ask to join again.</summary>
    /// <param name="menu">The farmhand selection screen.</param>
    private void VerifyAndRejoin(FarmhandMenu menu)
    {
        // the host should let us in after this, so stop if it keeps refusing
        if (DateTime.UtcNow - this.LastVerified > TimeSpan.FromSeconds(30))
            this.VerifyAttempts = 0;
        this.LastVerified = DateTime.UtcNow;
        if (++this.VerifyAttempts > 2)
        {
            this.SendEnded();
            ClientSync.ShowWarning(this.Helper.Translation.Get("client.verify-failed")); // replacing the farmhand menu closes the connection
            SmapiInternals.ResetPeers();
            return;
        }

        this.SendToHost(new VerifiedMessage(), MessageTypes.Verified);
        SmapiInternals.ResetPeers(); // the host sends its mod list again
        menu.client.connectionMessage = null;
        menu.client.sendPlayerIntroduction();
    }

    /****
    ** Mod downloads
    ****/
    /// <summary>Show which of the host's mods we don't have at the same version, and offer to download them if possible.</summary>
    /// <param name="message">The message from the host.</param>
    private void ShowModMismatch(BlockedMessage message)
    {
        this.Monitor.Log($"The host blocked you from joining because your mods don't match: {this.Text.GetCounts(message.Comparison)}.", LogLevel.Warn);
        foreach (string line in this.Text.GetEntries(message.Comparison))
            this.Monitor.Log($"   {line}", LogLevel.Warn);

        List<ModInfo> needed = message.Comparison.GetNeededMods().ToList();
        string? modsRoot = SmapiInternals.GetModsRoot(this.Helper.ModRegistry.Get(this.ModId)!);
        FarmhandMenu? menu = ClientSync.GetFarmhandMenu();

        if (!message.CanDownload || modsRoot == null || menu == null)
        {
            this.SendEnded();
            if (Game1.activeClickableMenu is TitleMenu)
                this.ShowMismatchWarning(message.Comparison); // replacing the farmhand menu closes the connection
            SmapiInternals.ResetPeers();
            return;
        }

        // take over the connection so it stays open when the farmhand menu is closed
        this.ModsRoot = modsRoot;
        this.Connection = menu.client;
        menu.client = null;

        this.Comparison = message.Comparison;
        this.CurrentStage = Stage.Prompt;
        DownloadPlan all = DownloadPlan.Select(needed, message.ArchiveSizes, message.DownloadBytes, trustHost: true);
        string allPrompt = this.Helper.Translation.Get("client.download-prompt", new { count = all.Targets.Count, size = ClientSync.FormatSize(all.Bytes) });

        // without original archives, the player can only get the mods by trusting the host
        if (!DownloadPlan.CanSkipTrust(needed, message.ArchiveSizes))
        {
            ClientSync.ShowDialog(this.CreateMismatchDialog(message.Comparison, allPrompt, onConfirm: () => this.StartDownload(all), onCancel: this.Cancel, checkboxLabel: this.Helper.Translation.Get("client.download-trust")));
            return;
        }

        // otherwise the checkbox chooses between the original archives and everything
        DownloadPlan nexus = DownloadPlan.Select(needed, message.ArchiveSizes, message.DownloadBytes, trustHost: false);
        string nexusPrompt = this.Helper.Translation.Get("client.download-prompt-nexus", new { count = nexus.Targets.Count, size = ClientSync.FormatSize(nexus.Bytes) });
        if (nexus.Left.Count > 0)
            nexusPrompt += "\n\n" + this.Helper.Translation.Get("client.download-left-out", new { count = nexus.Left.Count, names = string.Join(", ", nexus.Left.Select(mod => mod.Name)) });

        SyncDialog dialog = null!;
        dialog = this.CreateMismatchDialog(
            message.Comparison,
            getPrompt: isChecked => isChecked ? allPrompt : nexusPrompt,
            onConfirm: () => this.StartDownload(dialog.IsChecked ? all : nexus),
            onCancel: this.Cancel,
            checkboxLabel: this.Helper.Translation.Get("client.download-trust-all", new { count = all.Targets.Count })
        );
        ClientSync.ShowDialog(dialog);
    }

    /// <summary>Create a dialog listing the host's mods we don't have at the same version, with each name linking to the mod's Nexus Mods page if it has one.</summary>
    /// <param name="comparison">The mod differences.</param>
    /// <param name="prompt">The question to show below the list, if any.</param>
    /// <param name="onConfirm">The action to perform when the player clicks OK, or <c>null</c> to hide that button.</param>
    /// <param name="onCancel">The action to perform when the player clicks cancel, or <c>null</c> to close the dialog.</param>
    /// <param name="checkboxLabel">The label for a checkbox the player must tick before they can click OK (or which chooses what OK does, if <paramref name="getPrompt"/> is set), or <c>null</c> for no checkbox.</param>
    /// <param name="getPrompt">Get the question to show below the list for whether the checkbox is ticked, if it depends on that. If set, the checkbox chooses what OK does instead of having to be ticked.</param>
    private SyncDialog CreateMismatchDialog(ModComparison comparison, string? prompt = null, Action? onConfirm = null, Action? onCancel = null, string? checkboxLabel = null, Func<bool, string>? getPrompt = null)
    {
        List<ModLink> links = this.Text.GetNexusLinks(comparison);

        List<string> parts = new() { this.Text.BuildWarning(comparison) };
        if (links.Count > 0)
            parts.Add(this.Helper.Translation.Get(onConfirm != null ? "client.nexus-hint-instead" : "client.nexus-hint"));

        string BuildText(string? question) => string.Join("\n\n", question != null ? parts.Append(question) : parts);

        return new SyncDialog(
            BuildText(prompt),
            onConfirm: onConfirm,
            onCancel: onCancel,
            checkboxLabel: checkboxLabel,
            links: links,
            openLink: this.OpenLink,
            requireCheckbox: getPrompt == null,
            getCheckboxText: getPrompt != null ? isChecked => BuildText(getPrompt(isChecked)) : null
        );
    }

    /// <summary>Open a mod's download page in the player's browser.</summary>
    /// <param name="link">The link to open.</param>
    private void OpenLink(ModLink link)
    {
        try
        {
            Process.Start(new ProcessStartInfo(link.Url) { UseShellExecute = true })?.Dispose();
            this.Monitor.Log($"Opened the download page for {link.Name}: {link.Url}");
        }
        catch (Exception ex)
        {
            bool copied = DesktopClipboard.SetText(link.Url);
            this.Monitor.Log($"Couldn't open your browser for {link.Name}, so {(copied ? "the address was copied to the clipboard" : "go to this address yourself")}: {link.Url}\n{ex}", LogLevel.Warn);
        }
    }

    /// <summary>Start downloading the host's mods.</summary>
    /// <param name="plan">The mods to download, and whether the player trusts the host.</param>
    private void StartDownload(DownloadPlan plan)
    {
        this.Targets = plan.Targets.Select(mod => new DownloadTarget(mod, ConfigOnly: false)).ToList();
        this.TrustHost = plan.TrustHost;
        this.LeftOut = plan.Left;
        this.Monitor.Log(plan.TrustHost
            ? $"Downloading {plan.Targets.Count} mods from the host, trusting it."
            : $"Downloading the {plan.Targets.Count} mods the host has original Nexus Mods files for, which are checked before they're used; leaving out {plan.Left.Count} others."
        );

        this.Failures.Clear();
        this.FailedIds.Clear();
        this.Unverified.Clear();
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
        string sizingText = this.Helper.Translation.Get("client.downloading", new { index = this.Targets.Count, count = this.Targets.Count }) + "\n" + new string('M', 36);
        ClientSync.ShowDialog(new SyncDialog(sizingText, onCancel: this.Cancel, getText: this.GetProgressText));

        this.Downloader = new PackageDownloader(
            targets: this.Targets,
            downloadDir: downloadDir,
            sendRequest: request => this.SendToHost(request, MessageTypes.ChunkRequest),
            onFailed: (mod, error) =>
            {
                this.Monitor.Log($"Couldn't download {mod.Name}: {error}", LogLevel.Error);
                this.Failures.Add($"{mod.Name}: {error}");
                this.FailedIds.Add(mod.Id);
            }
        );
        this.Downloader.Start();
        if (this.Downloader.IsComplete)
            this.StartInstall();
    }

    /// <summary>Unpack the downloaded mods in the background, to be moved into place once the game closes since it's still using the installed ones.</summary>
    private void StartInstall()
    {
        this.CurrentStage = Stage.Installing;

        // SMAPI's mod registry isn't safe to read from another thread
        IReadOnlyList<DownloadedMod> downloads = this.Downloader!.Downloads;
        Dictionary<DownloadedMod, string?> installedPaths = downloads.ToDictionary(
            download => download,
            download => this.Helper.ModRegistry.Get(download.Mod.Id) is { } installed ? SmapiInternals.GetDirectoryPath(installed) : null
        );
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss");
        string backupRoot = Path.Combine(this.GetWorkPath("backup"), timestamp);
        string stagingRoot = Path.Combine(this.GetWorkPath(ClientSync.StagingFolderName), timestamp);

        this.InstallTask = Task.Run(() =>
        {
            List<(DownloadedMod Download, PendingSwap Swap)> staged = new();
            for (int i = 0; i < downloads.Count; i++)
            {
                DownloadedMod download = downloads[i];
                try
                {
                    // nothing reads the download until it's been checked
                    string? folder = this.CheckDownload(download);
                    if (folder == null)
                        continue;

                    string targetDir = installedPaths[download] ?? ModFiles.ResolveInstallPath(this.ModsRoot, download.RelativePath);
                    PendingSwap swap = new(targetDir, StagedDir: Path.Combine(stagingRoot, i.ToString()), BackupDir: Path.Combine(backupRoot, $"{i} - {Path.GetFileName(targetDir)}"));
                    ModFiles.Stage(download.ZipPath, swap.TargetDir, swap.StagedDir, folder);
                    staged.Add((download, swap));
                }
                catch (Exception ex)
                {
                    this.Monitor.Log($"Couldn't install {download.Mod.Name}:\n{ex}", LogLevel.Error);
                    this.Failures.Add($"{download.Mod.Name}: {ex.Message}");
                    this.FailedIds.Add(download.Mod.Id);
                }
            }
            this.DeleteWorkFolder("downloads");

            return this.InstallWhenGameCloses(staged);
        });
    }

    /// <summary>Decide whether a mod sent by the host can be installed, checking an original archive against the files on Nexus Mods first. A mod which can't be installed is deleted without being unpacked. This blocks until Nexus Mods answers, so it should be called in the background.</summary>
    /// <param name="download">The downloaded mod.</param>
    /// <returns>Returns the folder within the download which contains the mod, or <c>null</c> if it can't be installed.</returns>
    private string? CheckDownload(DownloadedMod download)
    {
        ModInfo mod = download.Mod;
        NexusCheckResult? result = null;
        string? error = null;
        if (download.IsArchive)
            result = NexusFileCheck.Check(download.ZipPath, mod.NexusId, out error);

        switch (ArchiveCheckPolicy.Decide(result, this.TrustHost, hasNexusId: mod.NexusId > 0))
        {
            case ArchiveDecision.Install:
                if (result == NexusCheckResult.Verified)
                    this.Monitor.Log($"The file the host sent for {mod.Name} matches a download from Nexus Mods.", LogLevel.Info);
                break;

            case ArchiveDecision.InstallUnchecked:
                this.Monitor.Log($"Couldn't check the file the host sent for {mod.Name} with Nexus Mods, so it's installed without being checked: {error}", LogLevel.Warn);
                this.Unverified.Add(mod.Name);
                break;

            default:
                string reason = result switch
                {
                    NexusCheckResult.Unknown => "the file the host sent isn't a download from Nexus Mods, so it may have been changed",
                    NexusCheckResult.WrongMod => "the file the host sent is a download for a different mod on Nexus Mods",
                    NexusCheckResult.Unavailable => $"couldn't check the file the host sent with Nexus Mods ({error}), so it wasn't installed",
                    NexusCheckResult.Verified => "it has no Nexus Mods ID, so the file the host sent can't be confirmed as one from its own page, and it wasn't installed",
                    _ => "the host sent its own copy instead of the original Nexus Mods file, so it wasn't installed"
                };
                this.Monitor.Log($"Couldn't install {mod.Name}: {reason}.", LogLevel.Error);
                this.Failures.Add($"{mod.Name}: {reason}");
                this.FailedIds.Add(mod.Id);
                try
                {
                    File.Delete(download.ZipPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    this.Monitor.Log($"Couldn't delete the file the host sent for {mod.Name}; it's deleted with the download folder:\n{ex}");
                }
                return null;
        }

        return download.IsArchive
            ? ModFiles.GetArchivedModFolder(download.ZipPath, mod)
            : "";
    }

    /// <summary>Arrange for the unpacked mods to be moved into place once the game closes. If that isn't possible, they're moved into place now.</summary>
    /// <param name="staged">The unpacked mods.</param>
    /// <returns>Returns the number of mods which were or will be installed.</returns>
    private int InstallWhenGameCloses(List<(DownloadedMod Download, PendingSwap Swap)> staged)
    {
        if (staged.Count == 0)
            return 0;

        try
        {
            ModFiles.AppendPendingSwaps(this.SwapListPath, staged.Select(entry => entry.Swap));
            GameRestarter.InstallAfterExit(this.SwapListPath, this.RestartFlagPath);

            foreach ((DownloadedMod download, PendingSwap swap) in staged)
                this.Monitor.Log($"Downloaded {download.Mod.Name} {download.Mod.Version} from the host. It'll be installed into '{swap.TargetDir}' when the game closes.", LogLevel.Info);
            return staged.Count;
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Couldn't arrange for the downloaded mods to be installed when the game closes, so they're being installed now:\n{ex}", LogLevel.Warn);
        }

        // a process which was already started may still read the list when the game closes, which is fine since the staged folders will be gone
        int installed = 0;
        foreach ((DownloadedMod download, PendingSwap swap) in staged)
        {
            try
            {
                ModFiles.Swap(swap.TargetDir, swap.StagedDir, swap.BackupDir);
                this.Monitor.Log($"Installed {download.Mod.Name} {download.Mod.Version} from the host into '{swap.TargetDir}'.", LogLevel.Info);
                installed++;
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Couldn't install {download.Mod.Name}:\n{ex}", LogLevel.Error);
                this.Failures.Add($"{download.Mod.Name}: {ex.Message}");
                this.FailedIds.Add(download.Mod.Id);
            }
        }
        return installed;
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
        if (this.Unverified.Count > 0)
            lines.Add(this.Helper.Translation.Get("client.unverified", new { names = string.Join(", ", this.Unverified) }));

        // without trusting the host, the player gets the rest themselves
        List<ModLink> links = new();
        if (!this.TrustHost && this.Comparison != null)
        {
            HashSet<string> stillNeededIds = new(this.LeftOut.Select(mod => mod.Id).Concat(this.FailedIds), StringComparer.OrdinalIgnoreCase);
            ModComparison stillNeeded = new(
                this.Comparison.Missing.Where(mod => stillNeededIds.Contains(mod.Id)).ToList(),
                this.Comparison.VersionMismatches.Where(mod => stillNeededIds.Contains(mod.Id)).ToList()
            );
            if (stillNeeded.Count > 0)
            {
                lines.Add("");
                lines.Add(this.Helper.Translation.Get("client.still-needed"));
                lines.AddRange(this.Text.GetEntries(stillNeeded));
                links = this.Text.GetNexusLinks(stillNeeded);
            }
        }

        // the installed mods aren't loaded until the game restarts
        if (installed > 0)
        {
            lines.Add("");
            lines.Add(this.Helper.Translation.Get("client.restart-prompt"));
            ClientSync.ShowDialog(new SyncDialog(string.Join("\n", lines), onConfirm: this.Restart, links: links, openLink: this.OpenLink));
        }
        else
            ClientSync.ShowDialog(new SyncDialog(string.Join("\n", lines), links: links, openLink: this.OpenLink));
    }

    /// <summary>Close the game and launch it again.</summary>
    private void Restart()
    {
        try
        {
            // the process which installs the mods when the game closes also launches it again if asked
            if (GameRestarter.IsWaitingToInstall)
                File.WriteAllText(this.RestartFlagPath, "");
            else
                GameRestarter.RestartAfterExit();
            Game1.quit = true;
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Couldn't restart the game automatically:\n{ex}", LogLevel.Error);
            ClientSync.ShowWarning(this.Helper.Translation.Get("client.restart-failed"));
        }
    }

    /// <summary>Handle the player declining the download or cancelling it.</summary>
    private void Cancel()
    {
        if (this.CurrentStage == Stage.Installing)
            return; // can't be interrupted safely

        this.Abort();
        (Game1.activeClickableMenu as TitleMenu)?.backButtonPressed();
    }

    /// <summary>Stop the mod download without installing anything, and disconnect from the host.</summary>
    private void Abort()
    {
        if (this.CurrentStage == Stage.Installing)
            return;

        this.Downloader?.Cancel();
        if (this.CurrentStage == Stage.SyncingSettings)
            this.Settings.DeleteDownloads();
        else
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

    /// <summary>The file listing the unpacked mods to move into place once the game closes.</summary>
    private string SwapListPath => this.GetWorkPath("pending.txt");

    /// <summary>The file which is created to launch the game again once it closes.</summary>
    private string RestartFlagPath => this.GetWorkPath("restart");

    /// <summary>Get the path to a folder within this mod's work folder in the mods folder.</summary>
    /// <param name="name">The folder name.</param>
    private string GetWorkPath(string name)
    {
        return Path.Combine(this.ModsRoot, ModFiles.WorkFolderName, name);
    }

    /// <summary>Delete a folder within this mod's work folder, if possible.</summary>
    /// <param name="name">The folder name.</param>
    private void DeleteWorkFolder(string name)
    {
        if (this.ModsRoot == null)
            return;

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
        Installing,

        /// <summary>The host's settings are being downloaded while the player waits on the farmhand selection screen.</summary>
        SyncingSettings,

        /// <summary>The host's changed settings are being downloaded while the player is in the host's game.</summary>
        RefreshingSettings
    }
}

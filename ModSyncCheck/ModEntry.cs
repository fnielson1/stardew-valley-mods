using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Network;

namespace ModSyncCheck;

/// <summary>The mod entry point.</summary>
internal sealed class ModEntry : Mod
{
    /*********
    ** Fields
    *********/
    /// <summary>The mod instance, for the Harmony patch.</summary>
    private static ModEntry Instance = null!; // set in Entry

    /// <summary>The mod settings.</summary>
    private ModConfig Config = null!; // set in Entry

    /// <summary>Builds the player-facing text for a mod comparison.</summary>
    private ComparisonText Text = null!; // set in Entry

    /// <summary>When hosting, sends the host's mods to blocked players who ask for them.</summary>
    private HostSync HostSync = null!; // set in Entry

    /// <summary>When blocked from joining a host, shows the reason and optionally downloads the host's mods.</summary>
    private ClientSync ClientSync = null!; // set in Entry

    /// <summary>Installs the mods in .zip files put directly in the mods folder.</summary>
    private ModsFolderArchives ModsFolderArchives = null!; // set in Entry

    /// <summary>When joining a host which doesn't have this mod with mismatched mods, the differences to show after disconnecting.</summary>
    private ModComparison? PendingMismatch;

    /// <summary>When joining a host which has a different version of this mod, the message to show once in the game.</summary>
    private string? VersionNotice;

    /// <summary>When hosting, the players whose request to join should be rejected.</summary>
    private readonly Dictionary<long, BlockedPlayer> BlockedPlayers = new();


    /*********
    ** Public methods
    *********/
    /// <inheritdoc />
    public override void Entry(IModHelper helper)
    {
        ModEntry.Instance = this;
        this.Config = helper.ReadConfig<ModConfig>();
        this.Text = new ComparisonText((key, tokens) => helper.Translation.Get(key, tokens));
        this.HostSync = new HostSync(helper, this.Monitor, this.ModManifest.UniqueID, this.Config);
        this.ClientSync = new ClientSync(helper, this.Monitor, this.ModManifest.UniqueID, this.Text);
        this.ModsFolderArchives = new ModsFolderArchives(helper, this.Monitor, this.ModManifest.UniqueID, this.Config, this.ClientSync);

        helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
        helper.Events.Multiplayer.PeerContextReceived += this.OnPeerContextReceived;
        helper.Events.Multiplayer.ModMessageReceived += this.OnModMessageReceived;
        helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        helper.Events.GameLoop.OneSecondUpdateTicked += this.OnOneSecondUpdateTicked;
        helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
        helper.Events.GameLoop.ReturnedToTitle += this.OnReturnedToTitle;

        new Harmony(this.ModManifest.UniqueID).Patch(
            original: AccessTools.Method(typeof(GameServer), nameof(GameServer.checkFarmhandRequest)),
            prefix: new HarmonyMethod(typeof(ModEntry), nameof(ModEntry.Before_CheckFarmhandRequest))
        );
    }


    /*********
    ** Private methods
    *********/
    /// <inheritdoc cref="IGameLoopEvents.GameLaunched" />
    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        new ConfigMenu(this.Helper, this.ModManifest, this.Config).Register();
        this.ClientSync.OnGameLaunched();
        this.HostSync.DeleteOldArchives();
        this.ModsFolderArchives.Start();
    }

    /// <inheritdoc cref="IMultiplayerEvents.PeerContextReceived" />
    private void OnPeerContextReceived(object? sender, PeerContextReceivedEventArgs e)
    {
        IMultiplayerPeer peer = e.Peer;
        if (peer.IsSplitScreen)
            return;

        IEnumerable<ModInfo> localMods = this.Helper.ModRegistry.GetAll().Select(mod => new ModInfo(mod.Manifest.UniqueID, mod.Manifest.Name, mod.Manifest.Version.ToString(), NexusLinks.GetModId(mod.Manifest.UpdateKeys)));
        IEnumerable<ModInfo> peerMods = peer.Mods.Select(mod => new ModInfo(mod.ID, mod.Name, mod.Version.ToString()));
        bool peerHasThisMod = peer.GetMod(this.ModManifest.UniqueID) != null;

        // joining a host
        if (peer.IsHost)
        {
            this.ClientSync.HostId = peer.PlayerID;
            this.PendingMismatch = null;
            this.VersionNotice = null;

            // a host with this mod decides using its own settings, and tells us if we're blocked
            if (peer.GetMod(this.ModManifest.UniqueID) is { } hostMod)
            {
                // the two versions may not understand each other fully, which the host doesn't check
                ISemanticVersion version = this.ModManifest.Version;
                this.VersionNotice = this.Text.BuildVersionNotice(hostMod.Version.ToString(), hostMod.Version.MajorVersion, version.ToString(), version.MajorVersion);
                if (this.VersionNotice != null)
                    this.Monitor.Log(this.VersionNotice, LogLevel.Warn);
                return;
            }

            ModComparison comparison = ModComparer.Compare(playerMods: localMods, hostMods: peerMods, this.Config.IgnoredModIds);
            this.LogComparison("Your mods", comparison, peer);
            if (!comparison.IsMatch)
                this.PendingMismatch = comparison;
        }

        // hosting a joining player
        else if (Game1.IsServer)
        {
            if (!this.Config.Enabled)
            {
                this.BlockedPlayers.Remove(peer.PlayerID);
                this.Monitor.Log($"Didn't check the mods for joining player {peer.PlayerID}, since checking is turned off in the settings.");
                return;
            }

            ModComparison comparison = ModComparer.Compare(playerMods: peerMods, hostMods: localMods, this.Config.IgnoredModIds);

            // a different major version of this mod may not understand ours, and an older one should be updated to ours, so
            // the player must have one which is compatible even though this mod is ignored by default
            if (peer.GetMod(this.ModManifest.UniqueID) is { } peerMod)
            {
                ISemanticVersion version = this.ModManifest.Version;
                comparison = ModComparer.RequireCompatibleVersion(
                    comparison,
                    hostMod: new ModInfo(this.ModManifest.UniqueID, this.ModManifest.Name, version.ToString(), NexusLinks.GetModId(this.ModManifest.UpdateKeys)),
                    hostMajorVersion: version.MajorVersion,
                    playerVersion: peerMod.Version.ToString(),
                    playerMajorVersion: peerMod.Version.MajorVersion,
                    playerIsOlder: peerMod.Version.IsOlderThan(version)
                );
            }
            this.LogComparison($"The mods for joining player {peer.PlayerID}", comparison, peer);

            // we can't see a player's settings, so if they must use ours for some mods we block the player and send hashes
            // of those settings; the player switches to our settings where needed, says so, and is let in on their next attempt
            Dictionary<string, Dictionary<string, string>>? configHashes = peerHasThisMod
                ? this.HostSync.GetConfigHashes()
                : null;
            bool requireConfig = configHashes?.Count > 0;

            bool missingThisMod = this.Config.RequireThisMod && !peerHasThisMod;
            if (missingThisMod)
                this.Monitor.Log($"Joining player {peer.PlayerID} doesn't have {this.ModManifest.Name}, which the settings require.", LogLevel.Warn);

            if (comparison.IsMatch && !missingThisMod && (!requireConfig || this.HostSync.ConsumeVerified(peer.PlayerID)))
            {
                this.BlockedPlayers.Remove(peer.PlayerID);
                return;
            }

            long downloadBytes = peerHasThisMod && this.Config.AllowModDownloads
                ? this.HostSync.GetDownloadSize(comparison)
                : 0;
            BlockedPlayer blocked = new(comparison, HasThisMod: peerHasThisMod, AllowMods: downloadBytes > 0, AllowConfig: requireConfig);
            this.BlockedPlayers[peer.PlayerID] = blocked;
            if (peerHasThisMod)
            {
                // a player who has to update their mods by hand can't sync settings until that's done
                bool canDownload = comparison.IsMatch ? blocked.AllowConfig : blocked.AllowMods;
                Dictionary<string, long> archiveSizes = !comparison.IsMatch && blocked.AllowMods
                    ? this.HostSync.GetArchiveSizes(comparison)
                    : new Dictionary<string, long>();
                this.Helper.Multiplayer.SendMessage(new BlockedMessage(comparison, canDownload, downloadBytes, requireConfig ? configHashes : null, archiveSizes), MessageTypes.Blocked, modIDs: new[] { this.ModManifest.UniqueID }, playerIDs: new[] { peer.PlayerID });
            }
        }
    }

    /// <inheritdoc cref="IMultiplayerEvents.ModMessageReceived" />
    private void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
    {
        if (e.FromModID != this.ModManifest.UniqueID)
            return;

        // from the host we're joining
        if (e.FromPlayerID == this.ClientSync.HostId)
        {
            switch (e.Type)
            {
                case MessageTypes.Blocked:
                    this.ClientSync.OnBlocked(e.ReadAs<BlockedMessage>());
                    break;

                case MessageTypes.Chunk:
                    this.ClientSync.OnChunk(e.ReadAs<ChunkResponse>());
                    break;

                case MessageTypes.SettingsChanged:
                    this.ClientSync.OnSettingsChanged(e.ReadAs<SettingsChangedMessage>());
                    break;
            }
        }

        // from a player we blocked, or one in our game who's getting changed settings
        else if (Game1.IsServer)
        {
            switch (e.Type)
            {
                case MessageTypes.ChunkRequest:
                    this.HostSync.OnRequest(e.FromPlayerID, e.ReadAs<ChunkRequest>());
                    break;

                case MessageTypes.Verified:
                    this.HostSync.OnVerified(e.FromPlayerID);
                    break;

                case MessageTypes.SyncEnded:
                    this.HostSync.Close(e.FromPlayerID);
                    break;
            }
        }
    }

    /// <inheritdoc cref="IGameLoopEvents.UpdateTicked" />
    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        this.HostSync.Update();
        this.ClientSync.Update();
        this.ModsFolderArchives.Update();

        if (this.PendingMismatch != null)
        {
            ModComparison comparison = this.PendingMismatch;
            this.PendingMismatch = null;

            // still on the farmhand selection screen: replacing the menu closes the connection
            if (Game1.activeClickableMenu is TitleMenu && TitleMenu.subMenu is FarmhandMenu)
            {
                this.ClientSync.ShowMismatchWarning(comparison);
                SmapiInternals.ResetPeers();
            }

            // the host let us in since it doesn't have this mod, so leave
            else
            {
                Game1.Multiplayer.Disconnect(Multiplayer.DisconnectType.ExitedToMainMenu);
                Game1.ExitToTitle(() =>
                {
                    (Game1.activeClickableMenu as TitleMenu)?.skipToTitleButtons();
                    this.ClientSync.ShowMismatchWarning(comparison);
                });
            }
        }
    }

    /// <inheritdoc cref="IGameLoopEvents.OneSecondUpdateTicked" />
    private void OnOneSecondUpdateTicked(object? sender, OneSecondUpdateTickedEventArgs e)
    {
        this.HostSync.CloseExpired();

        if (Context.IsWorldReady && e.IsMultipleOf(180))
        {
            // settings can be changed during the game, on either side
            if (Context.IsMainPlayer)
            {
                this.HostSync.SendChangedSettings(
                    this.Helper.Multiplayer.GetConnectedPlayers()
                        .Where(peer => this.Config.Enabled && !peer.IsSplitScreen && peer.GetMod(this.ModManifest.UniqueID) != null && Game1.otherFarmers.ContainsKey(peer.PlayerID))
                        .Select(peer => peer.PlayerID)
                        .ToArray()
                );
            }
            else
                this.ClientSync.UndoSettingsChanges();
        }
    }

    /// <inheritdoc cref="IGameLoopEvents.SaveLoaded" />
    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        if (!Context.IsMainPlayer)
        {
            this.ClientSync.OnJoinedGame();
            if (this.VersionNotice != null)
                Game1.chatBox?.addInfoMessage(this.VersionNotice);
        }
        this.VersionNotice = null;
    }

    /// <inheritdoc cref="IGameLoopEvents.ReturnedToTitle" />
    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        this.BlockedPlayers.Clear();
        this.VersionNotice = null;
        this.HostSync.CloseAll();
        this.ClientSync.OnReturnedToTitle();
    }

    /// <summary>When hosting, reject a request to join from a player whose mods don't match, or whose settings need to be compared, before the game approves it.</summary>
    /// <param name="farmer">The farmhand the player is asking to join as.</param>
    /// <param name="sendMessage">Send a message to the joining player.</param>
    /// <returns>Returns whether to run the game's own checks.</returns>
    private static bool Before_CheckFarmhandRequest(NetFarmerRoot farmer, Action<OutgoingMessage> sendMessage)
    {
        try
        {
            ModEntry mod = ModEntry.Instance;
            long? playerId = farmer.Value?.UniqueMultiplayerID;
            if (playerId == null || !mod.BlockedPlayers.Remove(playerId.Value, out BlockedPlayer? blocked))
                return true;

            // checking was turned off after their mods were compared
            if (!mod.Config.Enabled)
                return true;

            string rejection;
            if (!blocked.Comparison.IsMatch)
                rejection = mod.Text.BuildRejectionMessage(blocked.Comparison, suggestInstall: !blocked.HasThisMod);
            else if (blocked.HasThisMod)
                rejection = mod.Text.BuildCheckingMessage();
            else
                rejection = mod.Text.BuildModRequiredMessage();
            sendMessage(new OutgoingMessage(Multiplayer.connectionMessage, Game1.player, rejection));

            // a player with this mod stays connected to compare settings, see the reason, and download what they need, and tells us when they're done
            if (blocked.HasThisMod)
                mod.HostSync.Open(playerId.Value, blocked.Comparison, blocked.AllowMods, blocked.AllowConfig);
            else
                mod.HostSync.ForgetPeerLater(playerId.Value);

            if (blocked.Comparison.IsMatch && blocked.HasThisMod)
                mod.Monitor.Log($"Asked player {playerId} to compare their mod settings with ours before joining.");
            else if (blocked.Comparison.IsMatch)
            {
                mod.Monitor.Log($"Blocked player {playerId} from joining: they don't have {mod.ModManifest.Name}.", LogLevel.Warn);
                Game1.addHUDMessage(new HUDMessage(mod.Helper.Translation.Get("host.blocked-no-mod"), HUDMessage.error_type));
            }
            else
            {
                mod.Monitor.Log($"Blocked player {playerId} from joining: {blocked.Comparison.Count} mod mismatches.", LogLevel.Warn);
                Game1.addHUDMessage(new HUDMessage(mod.Helper.Translation.Get("host.blocked", new { count = blocked.Comparison.Count }), HUDMessage.error_type));
            }
            return false;
        }
        catch (Exception ex)
        {
            ModEntry.Instance.Monitor.Log($"Failed checking a joining player's mods, so they weren't blocked:\n{ex}", LogLevel.Error);
            return true;
        }
    }

    /// <summary>Log the full result of a mod comparison.</summary>
    /// <param name="subject">A description of whose mods were compared with the host's, like "Your mods".</param>
    /// <param name="comparison">The mod differences.</param>
    /// <param name="peer">The other player in the comparison.</param>
    private void LogComparison(string subject, ModComparison comparison, IMultiplayerPeer peer)
    {
        string smapiNote = peer.HasSmapi
            ? ""
            : $" ({(peer.IsHost ? "the host" : "that player")} doesn't have SMAPI installed)";

        if (comparison.IsMatch)
        {
            this.Monitor.Log($"{subject} match the host's{smapiNote}.", LogLevel.Info);
            return;
        }

        this.Monitor.Log($"{subject} don't match the host's{smapiNote}: {this.Text.GetCounts(comparison)}.", LogLevel.Warn);
        foreach (string line in this.Text.GetEntries(comparison))
            this.Monitor.Log($"   {line}", LogLevel.Warn);
    }

    /// <summary>When hosting, a player whose request to join should be rejected.</summary>
    /// <param name="Comparison">The differences between the player's mods and the host's.</param>
    /// <param name="HasThisMod">Whether the player has this mod installed.</param>
    /// <param name="AllowMods">Whether the player can download the mods they need from the host.</param>
    /// <param name="AllowConfig">Whether the player must use the host's settings for the required mods, and can download them from the host.</param>
    private record BlockedPlayer(ModComparison Comparison, bool HasThisMod, bool AllowMods, bool AllowConfig);
}

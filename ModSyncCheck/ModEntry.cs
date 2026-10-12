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

    /// <summary>When joining a host which doesn't have this mod with mismatched mods, the warning to show after disconnecting.</summary>
    private string? PendingWarning;

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
        this.Text = new ComparisonText((key, tokens) => helper.Translation.Get(key, tokens), this.Config);
        this.HostSync = new HostSync(helper, this.Monitor, this.ModManifest.UniqueID);
        this.ClientSync = new ClientSync(helper, this.Monitor, this.ModManifest.UniqueID, this.Text);

        helper.Events.Multiplayer.PeerContextReceived += this.OnPeerContextReceived;
        helper.Events.Multiplayer.ModMessageReceived += this.OnModMessageReceived;
        helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        helper.Events.GameLoop.OneSecondUpdateTicked += this.OnOneSecondUpdateTicked;
        helper.Events.GameLoop.ReturnedToTitle += this.OnReturnedToTitle;

        new Harmony(this.ModManifest.UniqueID).Patch(
            original: AccessTools.Method(typeof(GameServer), nameof(GameServer.checkFarmhandRequest)),
            prefix: new HarmonyMethod(typeof(ModEntry), nameof(ModEntry.Before_CheckFarmhandRequest))
        );
    }


    /*********
    ** Private methods
    *********/
    /// <inheritdoc cref="IMultiplayerEvents.PeerContextReceived" />
    private void OnPeerContextReceived(object? sender, PeerContextReceivedEventArgs e)
    {
        IMultiplayerPeer peer = e.Peer;
        if (peer.IsSplitScreen)
            return;

        IEnumerable<ModInfo> localMods = this.Helper.ModRegistry.GetAll().Select(mod => new ModInfo(mod.Manifest.UniqueID, mod.Manifest.Name, mod.Manifest.Version.ToString()));
        IEnumerable<ModInfo> peerMods = peer.Mods.Select(mod => new ModInfo(mod.ID, mod.Name, mod.Version.ToString()));
        bool peerHasThisMod = peer.GetMod(this.ModManifest.UniqueID) != null;

        // joining a host
        if (peer.IsHost)
        {
            this.ClientSync.HostId = peer.PlayerID;
            this.PendingWarning = null;

            // a host with this mod decides using its own settings, and tells us if we're blocked
            if (peerHasThisMod)
                return;

            ModComparison comparison = ModComparer.Compare(playerMods: localMods, hostMods: peerMods, this.Config.IgnoredModIds);
            this.LogComparison("Your mods", comparison, peer);
            if (!comparison.IsMatch)
                this.PendingWarning = this.Text.BuildWarning(comparison);
        }

        // hosting a joining player
        else if (Game1.IsServer)
        {
            ModComparison comparison = ModComparer.Compare(playerMods: peerMods, hostMods: localMods, this.Config.IgnoredModIds);
            this.LogComparison($"The mods for joining player {peer.PlayerID}", comparison, peer);
            if (comparison.IsMatch)
            {
                this.BlockedPlayers.Remove(peer.PlayerID);
                return;
            }

            long downloadBytes = peerHasThisMod && this.Config.AllowModDownloads
                ? this.HostSync.GetDownloadSize(comparison)
                : 0;
            this.BlockedPlayers[peer.PlayerID] = new BlockedPlayer(comparison, HasThisMod: peerHasThisMod, CanDownload: downloadBytes > 0);
            if (peerHasThisMod)
                this.Helper.Multiplayer.SendMessage(new BlockedMessage(comparison, CanDownload: downloadBytes > 0, downloadBytes), MessageTypes.Blocked, modIDs: new[] { this.ModManifest.UniqueID }, playerIDs: new[] { peer.PlayerID });
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
            }
        }

        // from a player we blocked
        else if (Game1.IsServer)
        {
            switch (e.Type)
            {
                case MessageTypes.ChunkRequest:
                    this.HostSync.OnRequest(e.FromPlayerID, e.ReadAs<ChunkRequest>());
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

        if (this.PendingWarning != null)
        {
            string warning = this.PendingWarning;
            this.PendingWarning = null;

            // still on the farmhand selection screen: replacing the menu closes the connection
            if (Game1.activeClickableMenu is TitleMenu && TitleMenu.subMenu is FarmhandMenu)
            {
                ClientSync.ShowWarning(warning);
                SmapiInternals.ResetPeers();
            }

            // the host let us in since it doesn't have this mod, so leave
            else
            {
                Game1.Multiplayer.Disconnect(Multiplayer.DisconnectType.ExitedToMainMenu);
                Game1.ExitToTitle(() =>
                {
                    (Game1.activeClickableMenu as TitleMenu)?.skipToTitleButtons();
                    ClientSync.ShowWarning(warning);
                });
            }
        }
    }

    /// <inheritdoc cref="IGameLoopEvents.OneSecondUpdateTicked" />
    private void OnOneSecondUpdateTicked(object? sender, OneSecondUpdateTickedEventArgs e)
    {
        this.HostSync.CloseExpired();
    }

    /// <inheritdoc cref="IGameLoopEvents.ReturnedToTitle" />
    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        this.BlockedPlayers.Clear();
        this.HostSync.CloseAll();
    }

    /// <summary>When hosting, reject a request to join from a player whose mods don't match before the game approves it.</summary>
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

            sendMessage(new OutgoingMessage(Multiplayer.connectionMessage, Game1.player, mod.Text.BuildRejectionMessage(blocked.Comparison, suggestInstall: !blocked.HasThisMod)));

            // a player with this mod stays connected to see the reason and download mods, and tells us when they're done
            if (blocked.HasThisMod)
                mod.HostSync.Open(playerId.Value, blocked.Comparison, blocked.CanDownload);
            else
                mod.HostSync.ForgetPeer(playerId.Value);

            mod.Monitor.Log($"Blocked player {playerId} from joining: {blocked.Comparison.Count} mod mismatches.", LogLevel.Warn);
            Game1.addHUDMessage(new HUDMessage(mod.Helper.Translation.Get("host.blocked", new { count = blocked.Comparison.Count }), HUDMessage.error_type));
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
    /// <param name="CanDownload">Whether the player can download the mods they need from the host.</param>
    private record BlockedPlayer(ModComparison Comparison, bool HasThisMod, bool CanDownload);
}

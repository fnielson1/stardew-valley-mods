using System.Collections;
using StardewModdingAPI;
using StardewValley;

namespace ModSyncCheck;

/// <summary>Accesses SMAPI internals which aren't available through its public API.</summary>
internal static class SmapiInternals
{
    /// <summary>Get the full path to a mod's folder, if available.</summary>
    /// <param name="mod">The mod metadata.</param>
    public static string? GetDirectoryPath(IModInfo mod)
    {
        return SmapiInternals.GetString(mod, "DirectoryPath");
    }

    /// <summary>Get the path to a mod's folder relative to the mods folder, if available.</summary>
    /// <param name="mod">The mod metadata.</param>
    public static string? GetRelativeDirectoryPath(IModInfo mod)
    {
        return SmapiInternals.GetString(mod, "RelativeDirectoryPath");
    }

    /// <summary>Get the full path to the mods folder a mod was loaded from, if available.</summary>
    /// <param name="mod">The mod metadata.</param>
    public static string? GetModsRoot(IModInfo mod)
    {
        return SmapiInternals.GetString(mod, "RootPath");
    }

    /// <summary>When hosting, remove a rejected player from SMAPI's peer list.</summary>
    /// <param name="playerId">The player's unique ID.</param>
    /// <returns>Returns whether the peer list was accessed successfully.</returns>
    /// <remarks>SMAPI only removes a peer when the game sees them disconnect, which doesn't happen for a player who was never approved. If they're left in the list, SMAPI ignores their mod list on their next attempt and they're treated as having no mods.</remarks>
    public static bool ForgetPeer(long playerId)
    {
        object multiplayer = Game1.Multiplayer;
        if (multiplayer.GetType().GetProperty("Peers")?.GetValue(multiplayer) is not IDictionary peers)
            return false;

        peers.Remove(playerId);
        return true;
    }

    /// <summary>When rejected as a joining player, clear SMAPI's connection info for the host.</summary>
    /// <remarks>SMAPI only does this when exiting a loaded game to the title screen. If the host is left in the list, SMAPI ignores the host's mod list on the next attempt and sends mod messages through the closed connection.</remarks>
    public static void ResetPeers()
    {
        object multiplayer = Game1.Multiplayer;
        multiplayer.GetType().GetMethod("CleanupOnMultiplayerExit")?.Invoke(multiplayer, null);
    }

    /// <summary>Get a string property from an internal SMAPI object, if it exists.</summary>
    /// <param name="instance">The object whose property to read.</param>
    /// <param name="property">The property name.</param>
    private static string? GetString(object instance, string property)
    {
        return instance.GetType().GetProperty(property)?.GetValue(instance) as string;
    }
}

using System;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
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

    /// <summary>Get the full path to the mods folder to install mods into, if available. See <see cref="ModFiles.GetInstallRoot"/>.</summary>
    /// <param name="mod">This mod's metadata.</param>
    public static string? GetInstallRoot(IModInfo mod)
    {
        string? loadRoot = SmapiInternals.GetModsRoot(mod);
        string? dir = SmapiInternals.GetDirectoryPath(mod);
        string? relativePath = SmapiInternals.GetRelativeDirectoryPath(mod);
        return loadRoot != null && dir != null && relativePath != null
            ? ModFiles.GetInstallRoot(loadRoot, dir, relativePath)
            : loadRoot;
    }

    /// <summary>Get whether a running mod's settings can be changed without restarting the game.</summary>
    /// <param name="mod">The mod metadata.</param>
    /// <param name="error">The reason the settings can't be changed, if applicable.</param>
    /// <remarks>See the remarks on <see cref="LiveConfigApplier"/> for the limitations.</remarks>
    public static bool CanReloadConfig(IModInfo mod, out string? error)
    {
        return SmapiInternals.TryGetModInstance(mod, out IMod? instance, out error) && LiveConfigApplier.CanApply(instance, out error);
    }

    /// <summary>Get text which changes when the settings a running mod keeps in memory change, or <c>null</c> if they can't be read.</summary>
    /// <param name="mod">The mod metadata.</param>
    public static string? GetConfigSnapshot(IModInfo mod)
    {
        return SmapiInternals.TryGetModInstance(mod, out IMod? instance, out _)
            ? LiveConfigApplier.GetSnapshot(instance)
            : null;
    }

    /// <summary>Make a running mod use its own settings file as it is on disk now, without restarting the game.</summary>
    /// <param name="mod">The mod metadata.</param>
    /// <param name="error">The reason the settings couldn't be applied, if applicable.</param>
    /// <returns>Returns whether the settings were applied.</returns>
    public static bool TryReloadConfig(IModInfo mod, out string? error)
    {
        MethodInfo readConfig = typeof(IModHelper).GetMethod(nameof(IModHelper.ReadConfig))!;
        return SmapiInternals.TryApplyConfig(mod, (instance, type) => readConfig.MakeGenericMethod(type).Invoke(instance.Helper, null), out error);
    }

    /// <summary>Make a running mod use the settings in another file, without restarting the game or changing its own settings file.</summary>
    /// <param name="mod">The mod metadata.</param>
    /// <param name="helper">The SMAPI API for this mod.</param>
    /// <param name="relativePath">The path of the settings file to use, relative to this mod's folder.</param>
    /// <param name="error">The reason the settings couldn't be applied, if applicable.</param>
    /// <returns>Returns whether the settings were applied.</returns>
    public static bool TryApplyConfigFile(IModInfo mod, IModHelper helper, string relativePath, out string? error)
    {
        // read it the same way SMAPI reads a mod's settings
        MethodInfo readJsonFile = typeof(IDataHelper).GetMethod(nameof(IDataHelper.ReadJsonFile))!;
        return SmapiInternals.TryApplyConfig(mod, (_, type) => readJsonFile.MakeGenericMethod(type).Invoke(helper.Data, new object[] { relativePath }), out error);
    }

    /// <summary>Copy settings into the settings object a running mod keeps in memory.</summary>
    /// <param name="mod">The mod metadata.</param>
    /// <param name="readConfig">Read the settings to use as a new object of the given type, for the given mod instance.</param>
    /// <param name="error">The reason the settings couldn't be applied, if applicable.</param>
    private static bool TryApplyConfig(IModInfo mod, Func<IMod, Type, object?> readConfig, out string? error)
    {
        if (!SmapiInternals.TryGetModInstance(mod, out IMod? instance, out error))
            return false;

        try
        {
            return LiveConfigApplier.TryApply(instance, type => readConfig(instance, type), out error);
        }
        catch (Exception ex)
        {
            error = (ex as TargetInvocationException)?.InnerException?.Message ?? ex.Message;
            return false;
        }
    }

    /// <summary>Get the entry class instance for a loaded code mod.</summary>
    /// <param name="mod">The mod metadata.</param>
    /// <param name="instance">The mod's entry class instance.</param>
    /// <param name="error">The reason there's no instance, if applicable.</param>
    private static bool TryGetModInstance(IModInfo mod, [NotNullWhen(true)] out IMod? instance, out string? error)
    {
        instance = null;
        if (mod.IsContentPack)
        {
            error = "it's a content pack, whose settings are only read when the game starts";
            return false;
        }

        instance = mod.GetType().GetProperty("Mod")?.GetValue(mod) as IMod;
        error = instance == null ? "it isn't loaded" : null;
        return instance != null;
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

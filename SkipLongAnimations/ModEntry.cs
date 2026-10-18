using System;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Locations;
using StardewValley.Menus;

namespace SkipLongAnimations;

/// <summary>The mod entry point.</summary>
internal sealed class ModEntry : Mod
{
    /*********
    ** Fields
    *********/
    /// <summary>The mod instance, for the Harmony patches.</summary>
    private static ModEntry Instance = null!; // set in Entry

    /// <summary>The mod settings.</summary>
    private ModConfig Config = null!; // set in Entry

    /// <summary>Whether a geode menu is being updated again by <see cref="After_GeodeMenuUpdate"/>, so it doesn't speed up its own extra updates.</summary>
    private static bool InExtraGeodeUpdate;

    /// <summary>Whether skipping part of the bus ride has failed, so the error is only logged once.</summary>
    private static bool LoggedBusError;


    /*********
    ** Public methods
    *********/
    /// <inheritdoc />
    public override void Entry(IModHelper helper)
    {
        ModEntry.Instance = this;
        this.Config = helper.ReadConfig<ModConfig>();

        helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;

        Harmony harmony = new(this.ModManifest.UniqueID);
        this.Patch(harmony, typeof(GeodeMenu), nameof(GeodeMenu.update), postfix: nameof(ModEntry.After_GeodeMenuUpdate));
        this.Patch(harmony, typeof(BusStop), nameof(BusStop.answerDialogue), postfix: nameof(ModEntry.After_BusStopAnswerDialogue));
        foreach (Type location in new[] { typeof(BusStop), typeof(Desert) })
        {
            this.Patch(harmony, location, "busStartMovingOff", prefix: nameof(ModEntry.Before_BusStartMovingOff));
            this.Patch(harmony, location, "busDriveBack", postfix: nameof(ModEntry.After_BusDriveBack));
        }
    }


    /*********
    ** Private methods
    *********/
    /// <inheritdoc cref="IGameLoopEvents.GameLaunched" />
    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        new ConfigMenu(this.Helper, this.ModManifest, this.Config).Register();
    }

    /// <summary>Patch a game method, or log a warning if it can't be found.</summary>
    /// <param name="harmony">The Harmony instance.</param>
    /// <param name="type">The game type which has the method.</param>
    /// <param name="methodName">The method's name.</param>
    /// <param name="prefix">The name of the <see cref="ModEntry"/> method to run before it, if any.</param>
    /// <param name="postfix">The name of the <see cref="ModEntry"/> method to run after it, if any.</param>
    private void Patch(Harmony harmony, Type type, string methodName, string? prefix = null, string? postfix = null)
    {
        MethodInfo? method = AccessTools.Method(type, methodName);
        if (method == null)
        {
            this.Monitor.Log($"Couldn't find {type.Name}.{methodName} in the game code, so that part of its animation plays as usual.", LogLevel.Warn);
            return;
        }

        harmony.Patch(
            original: method,
            prefix: prefix != null ? new HarmonyMethod(typeof(ModEntry), prefix) : null,
            postfix: postfix != null ? new HarmonyMethod(typeof(ModEntry), postfix) : null
        );
    }

    /// <summary>After Clint's geode menu updates while a geode is being cracked, update it again until it's run <see cref="ModConfig.GeodeSpeed"/> times this tick.</summary>
    /// <remarks>Updating again, rather than passing a longer time, keeps the parts which move a set distance each update (like the treasure rising out of the geode) looking the same.</remarks>
    /// <param name="__instance">The geode menu.</param>
    /// <param name="time">The game time for this tick.</param>
    private static void After_GeodeMenuUpdate(GeodeMenu __instance, GameTime time)
    {
        if (ModEntry.InExtraGeodeUpdate)
            return;

        ModEntry.InExtraGeodeUpdate = true;
        try
        {
            int speed = ModEntry.Instance.Config.GetGeodeSpeed();
            for (int i = 1; i < speed && __instance.geodeAnimationTimer > 0; i++)
                __instance.update(time);
        }
        finally
        {
            ModEntry.InExtraGeodeUpdate = false;
        }
    }

    /// <summary>After the player buys a bus ticket to the desert, get on the bus now instead of walking to its door.</summary>
    /// <param name="__instance">The bus stop.</param>
    private static void After_BusStopAnswerDialogue(BusStop __instance)
    {
        if (!ModEntry.Instance.Config.SkipBusRide)
            return;

        try
        {
            // the base game starts this timer when the ticket is bought, in case the walk gets stuck
            IReflectionHelper reflection = ModEntry.Instance.Helper.Reflection;
            if (reflection.GetField<int>(__instance, "forceWarpTimer").GetValue() <= 0)
                return;

            Game1.player.controller = null;
            reflection.GetMethod(__instance, "playerReachedBusDoor").Invoke(Game1.player, __instance);
        }
        catch (Exception ex)
        {
            ModEntry.LogBusError(ex);
        }
    }

    /// <summary>Once the bus door has closed, leave for the other end straight away instead of driving off.</summary>
    /// <param name="__instance">The bus stop or desert.</param>
    /// <returns>Whether to run the base game's code.</returns>
    private static bool Before_BusStartMovingOff(GameLocation __instance)
    {
        if (!ModEntry.Instance.Config.SkipBusRide)
            return true;

        try
        {
            IReflectionHelper reflection = ModEntry.Instance.Helper.Reflection;
            Game1.changeMusicTrack("silence");
            if (__instance is Desert)
            {
                // it moves the screen away before warping, so fade first like the base game
                IReflectedMethod leave = reflection.GetMethod(__instance, "busLeftToValley");
                Game1.globalFadeToBlack(() => leave.Invoke());
            }
            else
                reflection.GetMethod(__instance, "busLeftToDesert").Invoke();
            return false;
        }
        catch (Exception ex)
        {
            ModEntry.LogBusError(ex);
            return true;
        }
    }

    /// <summary>After the bus starts driving in, park it at its stop and open the door, like the base game does once it's arrived.</summary>
    /// <param name="__instance">The bus stop or desert.</param>
    private static void After_BusDriveBack(GameLocation __instance)
    {
        if (!ModEntry.Instance.Config.SkipBusRide)
            return;

        try
        {
            // find everything first, so a missing field can't leave the bus half parked
            IReflectionHelper reflection = ModEntry.Instance.Helper.Reflection;
            TemporaryAnimatedSprite door = reflection.GetField<TemporaryAnimatedSprite>(__instance, "busDoor").GetValue();
            IReflectedMethod doorOpened = reflection.GetMethod(__instance, "doorOpenAfterReturn");
            IReflectedField<Vector2> busPosition = reflection.GetField<Vector2>(__instance, "busPosition");
            IReflectedField<Vector2> busMotion = reflection.GetField<Vector2>(__instance, "busMotion");
            IReflectedField<bool> drivingBack = reflection.GetField<bool>(__instance, "drivingBack");

            Vector2 position = busPosition.GetValue();
            position.X = (__instance is Desert ? Desert.busDefaultXTile : BusStop.busDefaultXTile) * 64f;
            busPosition.SetValue(position);
            busMotion.SetValue(Vector2.Zero);
            drivingBack.SetValue(false);

            door.Position = position + new Vector2(16f, 26f) * 4f;
            door.pingPong = true;
            door.interval = 70f;
            door.currentParentTileIndex = 5;
            door.endFunction = extraInfo => doorOpened.Invoke(extraInfo);
            __instance.localSound("trashcanlid");

            if (__instance is BusStop)
                ModEntry.ShowReturnToHorseMessage(__instance);
        }
        catch (Exception ex)
        {
            ModEntry.LogBusError(ex);
        }
    }

    /// <summary>Remind the player that their horse is waiting at the bus stop, like the base game does when the bus arrives.</summary>
    /// <param name="busStop">The bus stop.</param>
    private static void ShowReturnToHorseMessage(GameLocation busStop)
    {
        if (string.IsNullOrEmpty(Game1.player.horseName.Value))
            return;

        foreach (NPC character in busStop.characters)
        {
            if (character is Horse horse && horse.getOwner() == Game1.player)
            {
                string key = string.IsNullOrEmpty(horse.Name)
                    ? "BusStop_ReturnToHorse2"
                    : "BusStop_ReturnToHorse" + (Game1.random.Next(2) + 1);
                Game1.showGlobalMessage(Game1.content.LoadString("Strings\\Locations:" + key, horse.displayName));
                break;
            }
        }
    }

    /// <summary>Log an error skipping part of the bus ride, the first time one happens.</summary>
    /// <param name="ex">The error.</param>
    private static void LogBusError(Exception ex)
    {
        if (ModEntry.LoggedBusError)
            return;

        ModEntry.LoggedBusError = true;
        ModEntry.Instance.Monitor.Log($"Couldn't skip part of the bus ride, so it plays as usual.\n{ex}", LogLevel.Error);
    }
}

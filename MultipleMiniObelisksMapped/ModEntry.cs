using System;
using System.Linq;
using HarmonyLib;
using LiveFarmMap;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using SObject = StardewValley.Object;

namespace MultipleMiniObelisksMapped;

/// <summary>The mod entry point.</summary>
internal sealed class ModEntry : Mod
{
    /*********
    ** Fields
    *********/
    /// <summary>The unique ID of Live Farm Map.</summary>
    private const string LiveFarmMapId = "fnielson.LiveFarmMap";

    /// <summary>The most mini-obelisks on the farm for which the base game's pairing still works; with more, using one opens the farm map instead.</summary>
    private const int MaxPaired = 2;

    /// <summary>The mod instance, for the Harmony patches.</summary>
    private static ModEntry Instance = null!; // set in Entry

    /// <summary>Live Farm Map's API, if it loaded.</summary>
    private ILiveFarmMapApi? Map;

    /// <summary>The markers drawn on the farm map, if Live Farm Map's API loaded.</summary>
    private ObeliskMarkers? Markers;


    /*********
    ** Accessors
    *********/
    /// <summary>The qualified item ID of the mini-obelisk.</summary>
    public const string MiniObeliskId = "(BC)238";


    /*********
    ** Public methods
    *********/
    /// <inheritdoc />
    public override void Entry(IModHelper helper)
    {
        ModEntry.Instance = this;

        Harmony harmony = new(this.ModManifest.UniqueID);
        harmony.Patch(
            original: AccessTools.Method(typeof(SObject), nameof(SObject.placementAction)),
            prefix: new HarmonyMethod(typeof(ModEntry), nameof(ModEntry.Before_PlacementAction))
        );
        harmony.Patch(
            original: AccessTools.Method(typeof(SObject), "CheckForActionOnMiniObelisk"),
            prefix: new HarmonyMethod(typeof(ModEntry), nameof(ModEntry.Before_CheckForActionOnMiniObelisk))
        );

        helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
        helper.Events.Display.RenderedActiveMenu += this.OnRenderedActiveMenu;
        helper.Events.Input.ButtonPressed += this.OnButtonPressed;
    }

    /// <summary>Get whether the player can teleport to a mini-obelisk from where they are. Like in the base game, they need to be on the farm with it.</summary>
    public static bool CanTeleportFromHere()
    {
        return Context.IsWorldReady && Game1.player.currentLocation is Farm;
    }


    /*********
    ** Private methods
    *********/
    /// <inheritdoc cref="IGameLoopEvents.GameLaunched" />
    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        this.Map = this.Helper.ModRegistry.GetApi<ILiveFarmMapApi>(ModEntry.LiveFarmMapId);
        if (this.Map == null)
        {
            this.Monitor.Log("Couldn't get Live Farm Map's API, so mini-obelisks won't show on the farm map. You can still place more than two.", LogLevel.Error);
            return;
        }

        this.Markers = new ObeliskMarkers(this.Map, this.Helper.Translation);
    }

    /// <inheritdoc cref="IDisplayEvents.RenderedActiveMenu" />
    private void OnRenderedActiveMenu(object? sender, RenderedActiveMenuEventArgs e)
    {
        this.Markers?.Draw(e.SpriteBatch);
    }

    /// <inheritdoc cref="IInputEvents.ButtonPressed" />
    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (this.Markers == null || e.Button is not (SButton.MouseLeft or SButton.ControllerA) || this.Markers.GetHovered() is not { } obelisk)
            return;

        this.Helper.Input.Suppress(e.Button);

        if (!ModEntry.CanTeleportFromHere())
        {
            Game1.playSound("cancel");
            return;
        }

        Game1.activeClickableMenu?.exitThisMenu(playSound: false);
        Teleporter.TeleportTo(obelisk.TileX, obelisk.TileY);
    }

    /// <summary>Count the mini-obelisks in a location.</summary>
    /// <param name="location">The location to check.</param>
    private static int CountObelisks(GameLocation location)
    {
        return location.objects.Values.Count(obj => obj.QualifiedItemId == ModEntry.MiniObeliskId);
    }

    /// <summary>Before an item is placed, place a mini-obelisk on the farm even if there are already two, which the base game doesn't allow.</summary>
    /// <param name="__instance">The item being placed.</param>
    /// <param name="location">The location it's placed in.</param>
    /// <param name="x">The X pixel position it's placed at.</param>
    /// <param name="y">The Y pixel position it's placed at.</param>
    /// <param name="who">The player placing it.</param>
    /// <param name="__result">Whether it was placed.</param>
    /// <returns>Whether to run the base game's code.</returns>
    /// <remarks>The rest is what the base game does when placing a plain big craftable.</remarks>
    private static bool Before_PlacementAction(SObject __instance, GameLocation location, int x, int y, Farmer? who, ref bool __result)
    {
        try
        {
            if (__instance.QualifiedItemId != ModEntry.MiniObeliskId || location is not Farm || ModEntry.CountObelisks(location) < ModEntry.MaxPaired)
                return true;

            Vector2 tile = new(x / 64, y / 64);
            if (location.objects.ContainsKey(tile))
            {
                __result = false;
                return false;
            }

            __instance.Location = location;
            __instance.TileLocation = tile;
            __instance.owner.Value = who?.UniqueMultiplayerID ?? Game1.player.UniqueMultiplayerID;

            if (!__instance.performDropDownAction(who))
            {
                SObject placed = (SObject)__instance.getOne();
                placed.shakeTimer = 50;
                placed.Location = location;
                placed.TileLocation = tile;
                placed.performDropDownAction(who);
                location.objects.Add(tile, placed);
                placed.initializeLightSource(tile);
            }

            location.playSound("woodyStep");
            __result = true;
            return false;
        }
        catch (Exception ex)
        {
            ModEntry.Instance.Monitor.Log($"Couldn't place the mini-obelisk, so the base game's limit of two applies.\n{ex}", LogLevel.Error);
            return true;
        }
    }

    /// <summary>Before a player uses a mini-obelisk, open the farm map to choose where to go if there are more than two on the farm.</summary>
    /// <param name="__instance">The mini-obelisk.</param>
    /// <param name="who">The player using it.</param>
    /// <param name="justCheckingForActivity">Whether the game is only checking if it can be used, like for the cursor.</param>
    /// <param name="__result">Whether it was used.</param>
    /// <returns>Whether to run the base game's code.</returns>
    private static bool Before_CheckForActionOnMiniObelisk(SObject __instance, Farmer who, bool justCheckingForActivity, ref bool __result)
    {
        if (justCheckingForActivity || !who.IsLocalPlayer || __instance.Location is not Farm farm || ModEntry.CountObelisks(farm) <= ModEntry.MaxPaired)
            return true;

        if (ModEntry.Instance.Map?.OpenMap() != true)
            return true;

        __result = true;
        return false;
    }
}

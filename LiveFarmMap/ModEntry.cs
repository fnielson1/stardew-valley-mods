using System;
using System.Collections.Generic;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace LiveFarmMap;

/// <summary>The mod entry point.</summary>
internal sealed class ModEntry : Mod
{
    /*********
    ** Fields
    *********/
    /// <summary>The start of the IDs of the farm's tooltip areas on the world map, from the farm's area in <c>Data/WorldMap</c>.</summary>
    private const string FarmTooltipPrefix = "Farm/";

    /// <summary>The mod settings.</summary>
    private ModConfig Config = null!; // set in Entry


    /*********
    ** Public methods
    *********/
    /// <inheritdoc />
    public override void Entry(IModHelper helper)
    {
        this.Config = helper.ReadConfig<ModConfig>();

        helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
        helper.Events.Input.ButtonPressed += this.OnButtonPressed;
        helper.Events.Display.RenderedActiveMenu += this.OnRenderedActiveMenu;
    }

    /// <inheritdoc />
    public override object GetApi()
    {
        return new LiveFarmMapApi(openMap: this.OpenFromApi);
    }


    /*********
    ** Private methods
    *********/
    /// <inheritdoc cref="IGameLoopEvents.GameLaunched" />
    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        new ConfigMenu(this.Helper, this.ModManifest, this.Config).Register();
    }

    /// <inheritdoc cref="IInputEvents.ButtonPressed" />
    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (!this.Config.ModEnabled || !Context.IsWorldReady)
            return;

        IClickableMenu? openMenu = Game1.activeClickableMenu;
        MapPage? mapPage = ModEntry.GetMapPage(openMenu);

        // hotkey, from the world or the world map
        if (this.Config.OpenKey.JustPressed() && (openMenu == null || mapPage != null))
        {
            this.Helper.Input.SuppressActiveKeybinds(this.Config.OpenKey);
            this.Open(returnTo: openMenu);
            return;
        }

        // click on the farm map button or the farm in the world map
        int mouseX = Game1.getMouseX(ui_scale: true);
        int mouseY = Game1.getMouseY(ui_scale: true);
        if (mapPage != null && e.Button is SButton.MouseLeft or SButton.ControllerA && (this.GetFarmMapButton().Contains(mouseX, mouseY) || ModEntry.IsOverFarm(mapPage, mouseX, mouseY)))
        {
            this.Helper.Input.Suppress(e.Button);
            this.Open(returnTo: openMenu);
        }
    }

    /// <inheritdoc cref="IDisplayEvents.RenderedActiveMenu" />
    private void OnRenderedActiveMenu(object? sender, RenderedActiveMenuEventArgs e)
    {
        if (!this.Config.ModEnabled || !Context.IsWorldReady)
            return;

        // add the farm map button to the world map, with the cursor back on top
        IClickableMenu? openMenu = Game1.activeClickableMenu;
        if (ModEntry.GetMapPage(openMenu) != null)
        {
            this.GetFarmMapButton().Draw(e.SpriteBatch);
            openMenu!.drawMouse(e.SpriteBatch);
        }
    }

    /// <summary>Get the button shown on the world map which opens the farm map.</summary>
    private MapSwitchButton GetFarmMapButton()
    {
        return MapSwitchButton.ToFarmMap(this.Helper.Translation.Get("map.to-farm-map"));
    }

    /// <summary>Open the farm map for another mod, if it can be opened now.</summary>
    /// <returns>Whether it opened.</returns>
    private bool OpenFromApi()
    {
        if (!this.Config.ModEnabled || !Context.IsWorldReady)
            return false;

        this.Open(returnTo: null);
        return true;
    }

    /// <summary>Open the farm map.</summary>
    /// <param name="returnTo">The menu to go back to when the farm map closes, if any.</param>
    private void Open(IClickableMenu? returnTo)
    {
        Game1.playSound("bigSelect");
        Game1.activeClickableMenu = new FarmMapMenu(this.Config, this.Helper.Translation, returnTo);
    }

    /// <summary>Get the world map shown by a menu, if it's showing one.</summary>
    /// <param name="menu">The open menu.</param>
    private static MapPage? GetMapPage(IClickableMenu? menu)
    {
        return menu switch
        {
            MapPage page => page,
            GameMenu gameMenu => gameMenu.GetCurrentPage() as MapPage,
            _ => null
        };
    }

    /// <summary>Get whether a screen position is over the farm on the world map.</summary>
    /// <param name="mapPage">The world map.</param>
    /// <param name="x">The screen X position, in UI pixels.</param>
    /// <param name="y">The screen Y position, in UI pixels.</param>
    private static bool IsOverFarm(MapPage mapPage, int x, int y)
    {
        foreach (KeyValuePair<string, ClickableComponent> point in mapPage.points)
        {
            if (point.Key.StartsWith(ModEntry.FarmTooltipPrefix, StringComparison.OrdinalIgnoreCase) && point.Value.containsPoint(x, y))
                return true;
        }
        return false;
    }
}

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.TerrainFeatures;

namespace HighlightCrops;

/// <summary>The mod entry point.</summary>
internal sealed class ModEntry : Mod
{
    /*********
    ** Fields
    *********/
    /// <summary>The colour of a highlighted tile's border.</summary>
    private static readonly Color BorderColor = Color.Blue * 0.8f;

    /// <summary>The colour inside a highlighted tile's border.</summary>
    private static readonly Color FillColor = Color.Blue * 0.3f;

    /// <summary>How many pixels thick a highlighted tile's border is.</summary>
    private const int BorderSize = 2;

    /// <summary>The mod settings.</summary>
    private ModConfig Config = null!; // set in Entry

    /// <summary>Whether the overlay is showing, for each split-screen player.</summary>
    private readonly PerScreen<bool> Showing = new();


    /*********
    ** Public methods
    *********/
    /// <inheritdoc />
    public override void Entry(IModHelper helper)
    {
        this.Config = helper.ReadConfig<ModConfig>();

        helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
        helper.Events.GameLoop.ReturnedToTitle += this.OnReturnedToTitle;
        helper.Events.Input.ButtonsChanged += this.OnButtonsChanged;
        helper.Events.Display.RenderedWorld += this.OnRenderedWorld;
    }


    /*********
    ** Private methods
    *********/
    /// <inheritdoc cref="IGameLoopEvents.GameLaunched" />
    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        new ConfigMenu(this.Helper, this.ModManifest, this.Config).Register();
    }

    /// <inheritdoc cref="IGameLoopEvents.ReturnedToTitle" />
    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        this.Showing.ResetAllScreens();
    }

    /// <inheritdoc cref="IInputEvents.ButtonsChanged" />
    private void OnButtonsChanged(object? sender, ButtonsChangedEventArgs e)
    {
        if (!Context.IsPlayerFree || !this.Config.ToggleKey.JustPressed())
            return;

        this.Helper.Input.SuppressActiveKeybinds(this.Config.ToggleKey);
        this.Showing.Value = !this.Showing.Value;
    }

    /// <inheritdoc cref="IDisplayEvents.RenderedWorld" />
    private void OnRenderedWorld(object? sender, RenderedWorldEventArgs e)
    {
        GameLocation? location = Game1.currentLocation;
        if (!this.Showing.Value || !Context.IsWorldReady || Game1.eventUp || location == null)
            return;

        // only the tiles on screen, with a tile's margin so ones partly on screen are included
        Rectangle visible = new(
            Game1.viewport.X / Game1.tileSize - 1,
            Game1.viewport.Y / Game1.tileSize - 1,
            Game1.viewport.Width / Game1.tileSize + 3,
            Game1.viewport.Height / Game1.tileSize + 3
        );

        foreach ((Vector2 tile, TerrainFeature feature) in location.terrainFeatures.Pairs)
        {
            if (feature is not HoeDirt dirt || !visible.Contains((int)tile.X, (int)tile.Y))
                continue;

            Crop? crop = dirt.crop;
            bool highlight = HighlightRules.ShouldHighlight(
                hasCrop: crop != null,
                isDead: crop?.dead.Value == true,
                readyForHarvest: crop != null && dirt.readyForHarvest(),
                highlightEmpty: this.Config.HighlightEmpty,
                highlightReady: this.Config.HighlightReady
            );
            if (highlight)
                this.DrawHighlight(e.SpriteBatch, tile);
        }
    }

    /// <summary>Draw a blue square over a tile.</summary>
    /// <param name="b">The sprite batch to draw to.</param>
    /// <param name="tile">The tile's position.</param>
    private void DrawHighlight(SpriteBatch b, Vector2 tile)
    {
        Vector2 position = Game1.GlobalToLocal(Game1.viewport, tile * Game1.tileSize);
        Rectangle area = new((int)position.X, (int)position.Y, Game1.tileSize, Game1.tileSize);

        b.Draw(Game1.staminaRect, area, ModEntry.FillColor);
        b.Draw(Game1.staminaRect, new Rectangle(area.X, area.Y, area.Width, ModEntry.BorderSize), ModEntry.BorderColor);
        b.Draw(Game1.staminaRect, new Rectangle(area.X, area.Bottom - ModEntry.BorderSize, area.Width, ModEntry.BorderSize), ModEntry.BorderColor);
        b.Draw(Game1.staminaRect, new Rectangle(area.X, area.Y + ModEntry.BorderSize, ModEntry.BorderSize, area.Height - ModEntry.BorderSize * 2), ModEntry.BorderColor);
        b.Draw(Game1.staminaRect, new Rectangle(area.Right - ModEntry.BorderSize, area.Y + ModEntry.BorderSize, ModEntry.BorderSize, area.Height - ModEntry.BorderSize * 2), ModEntry.BorderColor);
    }
}

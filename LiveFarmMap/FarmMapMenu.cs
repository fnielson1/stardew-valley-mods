using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;

namespace LiveFarmMap;

/// <summary>A full-screen map of the farm as it is right now.</summary>
internal sealed class FarmMapMenu : IClickableMenu
{
    /*********
    ** Fields
    *********/
    /// <summary>The space between the screen edge and the map frame.</summary>
    private const int ScreenMargin = 32;

    /// <summary>The space above the map frame for the title scroll.</summary>
    private const int TitleHeight = 80;

    /// <summary>The space between the map frame and the farm.</summary>
    private const int FramePadding = 28;

    /// <summary>The mod settings.</summary>
    private readonly ModConfig Config;

    /// <summary>The menu to go back to when this one closes, if any.</summary>
    private readonly IClickableMenu? ReturnTo;

    /// <summary>The title shown above the map.</summary>
    private readonly string Title;

    /// <summary>Draws the farm.</summary>
    private readonly FarmRenderer Renderer = new();

    /// <summary>Draws the icons for everyone on the farm.</summary>
    private readonly MapMarkers Markers;

    /// <summary>The seconds left until the farm is redrawn.</summary>
    private double SecondsUntilRefresh;


    /*********
    ** Accessors
    *********/
    /// <summary>Where the farm is drawn on screen.</summary>
    public MapLayout Layout { get; private set; } = null!; // set in UpdateLayout


    /*********
    ** Public methods
    *********/
    /// <summary>Construct an instance.</summary>
    /// <param name="config">The mod settings.</param>
    /// <param name="translation">The mod's translations.</param>
    /// <param name="returnTo">The menu to go back to when this one closes, if any.</param>
    public FarmMapMenu(ModConfig config, ITranslationHelper translation, IClickableMenu? returnTo)
        : base(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height, showUpperRightCloseButton: true)
    {
        this.Config = config;
        this.ReturnTo = returnTo;
        this.Markers = new MapMarkers(config);
        this.Title = translation.Get("map.title", new { farmName = Game1.player.farmName.Value });
        this.UpdateLayout();
    }

    /// <inheritdoc />
    public override void update(GameTime time)
    {
        base.update(time);

        this.SecondsUntilRefresh -= time.ElapsedGameTime.TotalSeconds;
        if (this.SecondsUntilRefresh > 0)
            return;
        this.SecondsUntilRefresh = this.Config.RefreshSeconds;

        Farm farm = Game1.getFarm();
        float uiScale = Game1.options.uiScale;
        this.Renderer.Render(farm, (int)(this.Layout.Width * uiScale), (int)(this.Layout.Height * uiScale));
    }

    /// <inheritdoc />
    public override void draw(SpriteBatch b)
    {
        if (!Game1.options.showMenuBackground)
            b.Draw(Game1.fadeToBlackRect, new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height), Color.Black * 0.75f);

        IClickableMenu.drawTextureBox(b, this.xPositionOnScreen, this.yPositionOnScreen, this.width, this.height, Color.White);

        if (this.Renderer.Snapshot is { } snapshot)
            b.Draw(snapshot, new Rectangle(this.Layout.X, this.Layout.Y, this.Layout.Width, this.Layout.Height), Color.White);
        this.Markers.Draw(b, Game1.getFarm(), this.Layout);

        SpriteText.drawStringWithScrollCenteredAt(b, this.Title, this.xPositionOnScreen + this.width / 2, this.yPositionOnScreen - FarmMapMenu.TitleHeight);

        base.draw(b);
        this.Markers.DrawHoveredName(b);
        this.drawMouse(b);
    }

    /// <inheritdoc />
    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (this.upperRightCloseButton?.containsPoint(x, y) == true)
        {
            if (playSound)
                Game1.playSound(this.closeSound);
            this.Close();
        }
    }

    /// <inheritdoc />
    public override void receiveRightClick(int x, int y, bool playSound = true)
    {
        this.Close();
    }

    /// <inheritdoc />
    public override void receiveKeyPress(Keys key)
    {
        if (key == Keys.Escape || Game1.options.doesInputListContain(Game1.options.menuButton, key) || Game1.options.doesInputListContain(Game1.options.mapButton, key))
            this.Close();
    }

    /// <inheritdoc />
    public override void receiveGamePadButton(Buttons b)
    {
        if (b is Buttons.B or Buttons.Back)
            this.Close();
    }

    /// <inheritdoc />
    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
    {
        base.gameWindowSizeChanged(oldBounds, newBounds);
        this.UpdateLayout();
    }

    /// <inheritdoc />
    protected override void cleanupBeforeExit()
    {
        this.Renderer.Dispose();
        base.cleanupBeforeExit();
    }

    /// <inheritdoc />
    public override void emergencyShutDown()
    {
        this.Renderer.Dispose();
        base.emergencyShutDown();
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Close the map, going back to the menu it was opened from if any.</summary>
    private void Close()
    {
        this.Renderer.Dispose();
        if (this.ReturnTo != null)
            Game1.activeClickableMenu = this.ReturnTo;
        else
            this.exitThisMenu(playSound: false);
    }

    /// <summary>Fit the menu and the farm to the screen, and redraw the farm.</summary>
    /// <remarks>The menu's bounds are the frame around the farm, so the close button sits on its corner.</remarks>
    private void UpdateLayout()
    {
        const int inset = FarmMapMenu.ScreenMargin + FarmMapMenu.FramePadding;
        int top = inset + FarmMapMenu.TitleHeight;

        xTile.Map map = Game1.getFarm().Map;
        this.Layout = MapLayout.Fit(
            tilesWide: map.DisplayWidth / MapLayout.WorldTileSize,
            tilesHigh: map.DisplayHeight / MapLayout.WorldTileSize,
            areaX: inset,
            areaY: top,
            areaWidth: Game1.uiViewport.Width - inset * 2,
            areaHeight: Game1.uiViewport.Height - top - inset
        );

        this.xPositionOnScreen = this.Layout.X - FarmMapMenu.FramePadding;
        this.yPositionOnScreen = this.Layout.Y - FarmMapMenu.FramePadding;
        this.width = this.Layout.Width + FarmMapMenu.FramePadding * 2;
        this.height = this.Layout.Height + FarmMapMenu.FramePadding * 2;

        this.initializeUpperRightCloseButton();
        this.SecondsUntilRefresh = 0;
    }
}

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Menus;

namespace LiveFarmMap;

/// <summary>A button in the top-left corner of the screen which switches between the world map and the farm map.</summary>
internal sealed class MapSwitchButton
{
    /*********
    ** Fields
    *********/
    /// <summary>The space between the screen corner and the button.</summary>
    private const int ScreenMargin = 24;

    /// <summary>The button height.</summary>
    private const int Height = 64;

    /// <summary>The space between the button edge and its contents.</summary>
    private const int Padding = 16;

    /// <summary>The size of the icon on screen.</summary>
    private const int IconSize = 32;

    /// <summary>The space between the icon and the label.</summary>
    private const int IconGap = 8;

    /// <summary>The text on the button.</summary>
    private readonly string Label;

    /// <summary>The texture with the icon.</summary>
    private readonly Texture2D IconTexture;

    /// <summary>The icon's pixel area in <see cref="IconTexture"/>.</summary>
    private readonly Rectangle IconSource;


    /*********
    ** Accessors
    *********/
    /// <summary>The button's area on screen, in UI pixels.</summary>
    public Rectangle Bounds { get; }


    /*********
    ** Public methods
    *********/
    /// <summary>Get the button shown on the world map, which opens the farm map. Its icon is a parsnip, for the farm.</summary>
    /// <param name="label">The text on the button.</param>
    public static MapSwitchButton ToFarmMap(string label)
    {
        ParsedItemData parsnip = ItemRegistry.GetDataOrErrorItem("(O)24");
        return new MapSwitchButton(label, parsnip.GetTexture(), parsnip.GetSourceRect());
    }

    /// <summary>Get the button shown on the farm map, which goes to the world map. Its icon is the game menu's map tab.</summary>
    /// <param name="label">The text on the button.</param>
    public static MapSwitchButton ToWorldMap(string label)
    {
        return new MapSwitchButton(label, Game1.mouseCursors, new Rectangle(48, 368, 16, 16));
    }

    /// <summary>Get whether a screen position is over the button.</summary>
    /// <param name="x">The screen X position, in UI pixels.</param>
    /// <param name="y">The screen Y position, in UI pixels.</param>
    public bool Contains(int x, int y)
    {
        return this.Bounds.Contains(x, y);
    }

    /// <summary>Draw the button, highlighted while the cursor is over it.</summary>
    /// <param name="b">The sprite batch to draw with, in UI coordinates.</param>
    public void Draw(SpriteBatch b)
    {
        Rectangle bounds = this.Bounds;
        bool hovered = this.Contains(Game1.getMouseX(), Game1.getMouseY());

        IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(432, 439, 9, 9), bounds.X, bounds.Y, bounds.Width, bounds.Height, hovered ? Color.Wheat : Color.White, 4f, drawShadow: true);

        int iconX = bounds.X + MapSwitchButton.Padding;
        int iconY = bounds.Y + (bounds.Height - MapSwitchButton.IconSize) / 2;
        b.Draw(this.IconTexture, new Rectangle(iconX, iconY, MapSwitchButton.IconSize, MapSwitchButton.IconSize), this.IconSource, Color.White);

        Vector2 labelSize = Game1.smallFont.MeasureString(this.Label);
        Vector2 labelPosition = new(iconX + MapSwitchButton.IconSize + MapSwitchButton.IconGap, bounds.Y + (bounds.Height - labelSize.Y) / 2);
        Utility.drawTextWithShadow(b, this.Label, Game1.smallFont, labelPosition, Game1.textColor);
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Construct an instance.</summary>
    /// <param name="label">The text on the button.</param>
    /// <param name="iconTexture">The texture with the icon.</param>
    /// <param name="iconSource">The icon's pixel area in <paramref name="iconTexture"/>.</param>
    private MapSwitchButton(string label, Texture2D iconTexture, Rectangle iconSource)
    {
        this.Label = label;
        this.IconTexture = iconTexture;
        this.IconSource = iconSource;

        int labelWidth = (int)Game1.smallFont.MeasureString(label).X;
        int width = MapSwitchButton.Padding * 2 + MapSwitchButton.IconSize + MapSwitchButton.IconGap + labelWidth;
        this.Bounds = new Rectangle(MapSwitchButton.ScreenMargin, MapSwitchButton.ScreenMargin, width, MapSwitchButton.Height);
    }
}

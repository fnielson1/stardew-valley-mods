using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Menus;

namespace MultipleMiniObelisksMapped;

/// <summary>Draws a marker on Live Farm Map's farm map for each mini-obelisk on the farm, and finds the one under the cursor.</summary>
internal sealed class ObeliskMarkers
{
    /*********
    ** Fields
    *********/
    /// <summary>Live Farm Map's API.</summary>
    private readonly ILiveFarmMapApi Map;

    /// <summary>The mod's translations.</summary>
    private readonly ITranslationHelper Translation;


    /*********
    ** Public methods
    *********/
    /// <summary>Construct an instance.</summary>
    /// <param name="map">Live Farm Map's API.</param>
    /// <param name="translation">The mod's translations.</param>
    public ObeliskMarkers(ILiveFarmMapApi map, ITranslationHelper translation)
    {
        this.Map = map;
        this.Translation = translation;
    }

    /// <summary>Draw the markers, the hovered one's tooltip, and the cursor over them.</summary>
    /// <param name="b">The sprite batch to draw with, in UI coordinates.</param>
    public void Draw(SpriteBatch b)
    {
        if (!this.Map.IsMapOpen())
            return;

        ParsedItemData data = ItemRegistry.GetDataOrErrorItem(ModEntry.MiniObeliskId);
        Texture2D texture = data.GetTexture();
        Rectangle source = data.GetSourceRect();
        bool canTeleport = ModEntry.CanTeleportFromHere();
        Color color = canTeleport ? Color.White : Color.Gray;

        List<(IFarmBuilding Obelisk, Rectangle Tile)> markers = this.GetMarkers();
        int hovered = ObeliskMarkers.FindHovered(markers);

        for (int i = 0; i < markers.Count; i++)
        {
            Rectangle area = ObeliskMarkers.GetArea(markers[i].Tile, hovered: i == hovered);
            b.Draw(texture, new Rectangle(area.X + 2, area.Y + 2, area.Width, area.Height), source, Color.Black * 0.35f);
            b.Draw(texture, area, source, color);
        }

        if (hovered >= 0)
        {
            string hint = this.Translation.Get(canTeleport ? "marker.teleport" : "marker.not-on-farm");
            IClickableMenu.drawHoverText(b, hint, Game1.smallFont, boldTitleText: markers[hovered].Obelisk.DisplayName);
        }

        Game1.activeClickableMenu?.drawMouse(b);
    }

    /// <summary>Get the mini-obelisk whose marker is under the cursor, if the farm map is open.</summary>
    public IFarmBuilding? GetHovered()
    {
        if (!this.Map.IsMapOpen())
            return null;

        List<(IFarmBuilding Obelisk, Rectangle Tile)> markers = this.GetMarkers();
        int hovered = ObeliskMarkers.FindHovered(markers);
        return hovered >= 0 ? markers[hovered].Obelisk : null;
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Get each mini-obelisk on the farm and where its tile is on screen, or none if the farm map isn't open.</summary>
    private List<(IFarmBuilding Obelisk, Rectangle Tile)> GetMarkers()
    {
        List<(IFarmBuilding, Rectangle)> markers = new();

        foreach (IFarmBuilding obelisk in this.Map.GetBuildings())
        {
            if (obelisk.Kind == "mini-obelisk" && this.Map.TryGetTileScreenArea(obelisk.TileX, obelisk.TileY, out Rectangle tile))
                markers.Add((obelisk, tile));
        }

        return markers;
    }

    /// <summary>Get the index of the marker under the cursor, or -1 if there's none.</summary>
    /// <param name="markers">The markers on the farm map.</param>
    private static int FindHovered(List<(IFarmBuilding Obelisk, Rectangle Tile)> markers)
    {
        int mouseX = Game1.getMouseX();
        int mouseY = Game1.getMouseY();

        // the close button sits on the map's corner, so it wins over a marker under it
        if (Game1.activeClickableMenu?.upperRightCloseButton?.containsPoint(mouseX, mouseY) == true)
            return -1;

        // later markers are drawn on top, so check them first
        for (int i = markers.Count - 1; i >= 0; i--)
        {
            if (ObeliskMarkers.GetArea(markers[i].Tile, hovered: false).Contains(mouseX, mouseY))
                return i;
        }
        return -1;
    }

    /// <summary>Get a marker's area on screen.</summary>
    /// <param name="tile">The mini-obelisk's tile on screen.</param>
    /// <param name="hovered">Whether the cursor is over it, so it's drawn bigger.</param>
    private static Rectangle GetArea(Rectangle tile, bool hovered)
    {
        (int x, int y, int width, int height) = MarkerLayout.GetBounds(tile.X, tile.Y, tile.Width, tile.Height, hovered);
        return new Rectangle(x, y, width, height);
    }
}

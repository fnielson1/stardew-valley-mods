using System;

namespace MultipleMiniObelisksMapped;

/// <summary>Where a mini-obelisk's marker is drawn on the farm map.</summary>
internal static class MarkerLayout
{
    /*********
    ** Fields
    *********/
    /// <summary>The narrowest a marker can be on screen, so it's easy to point at when the farm map is small.</summary>
    public const int MinWidth = 20;

    /// <summary>How much bigger a marker is drawn while it's hovered.</summary>
    public const float HoverScale = 1.25f;


    /*********
    ** Public methods
    *********/
    /// <summary>Get a marker's area on screen. A mini-obelisk's sprite is twice as tall as it is wide, so the marker stands on its tile like the mini-obelisk does in the world.</summary>
    /// <param name="tileX">The screen X position of the mini-obelisk's tile.</param>
    /// <param name="tileY">The screen Y position of the mini-obelisk's tile.</param>
    /// <param name="tileWidth">The width of a tile on screen.</param>
    /// <param name="tileHeight">The height of a tile on screen.</param>
    /// <param name="hovered">Whether the cursor is over the marker, so it's drawn bigger.</param>
    public static (int X, int Y, int Width, int Height) GetBounds(int tileX, int tileY, int tileWidth, int tileHeight, bool hovered)
    {
        float width = Math.Max(MarkerLayout.MinWidth, tileWidth);
        if (hovered)
            width *= MarkerLayout.HoverScale;

        int markerWidth = (int)Math.Round(width);
        int markerHeight = markerWidth * 2;
        int centreX = tileX + tileWidth / 2;
        int bottom = tileY + tileHeight;

        return (centreX - markerWidth / 2, bottom - markerHeight, markerWidth, markerHeight);
    }
}

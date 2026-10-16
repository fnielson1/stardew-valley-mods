using System;

namespace LiveFarmMap;

/// <summary>Where the farm is drawn on screen: the farm scaled to fit an area as large as possible, keeping its shape, and centred in it.</summary>
internal sealed class MapLayout
{
    /*********
    ** Accessors
    *********/
    /// <summary>The size of one tile in world pixels, before scaling.</summary>
    public const int WorldTileSize = 64;

    /// <summary>The farm width in tiles.</summary>
    public int TilesWide { get; }

    /// <summary>The farm height in tiles.</summary>
    public int TilesHigh { get; }

    /// <summary>The screen pixels per world pixel.</summary>
    public float Scale { get; }

    /// <summary>The screen X position of the farm's left edge.</summary>
    public int X { get; }

    /// <summary>The screen Y position of the farm's top edge.</summary>
    public int Y { get; }

    /// <summary>The farm's width on screen.</summary>
    public int Width { get; }

    /// <summary>The farm's height on screen.</summary>
    public int Height { get; }

    /// <summary>The size of one tile on screen.</summary>
    public float TileSize => MapLayout.WorldTileSize * this.Scale;


    /*********
    ** Public methods
    *********/
    /// <summary>Fit a farm into a screen area.</summary>
    /// <param name="tilesWide">The farm width in tiles.</param>
    /// <param name="tilesHigh">The farm height in tiles.</param>
    /// <param name="areaX">The screen X position of the area.</param>
    /// <param name="areaY">The screen Y position of the area.</param>
    /// <param name="areaWidth">The area's width.</param>
    /// <param name="areaHeight">The area's height.</param>
    public static MapLayout Fit(int tilesWide, int tilesHigh, int areaX, int areaY, int areaWidth, int areaHeight)
    {
        tilesWide = Math.Max(1, tilesWide);
        tilesHigh = Math.Max(1, tilesHigh);
        areaWidth = Math.Max(1, areaWidth);
        areaHeight = Math.Max(1, areaHeight);

        float worldWidth = tilesWide * MapLayout.WorldTileSize;
        float worldHeight = tilesHigh * MapLayout.WorldTileSize;
        float scale = Math.Min(areaWidth / worldWidth, areaHeight / worldHeight);

        int width = Math.Max(1, Math.Min(areaWidth, (int)Math.Round(worldWidth * scale)));
        int height = Math.Max(1, Math.Min(areaHeight, (int)Math.Round(worldHeight * scale)));
        int x = areaX + (areaWidth - width) / 2;
        int y = areaY + (areaHeight - height) / 2;

        return new MapLayout(tilesWide, tilesHigh, scale, x, y, width, height);
    }

    /// <summary>Get the screen position of a tile's top-left corner.</summary>
    /// <param name="tileX">The tile X position.</param>
    /// <param name="tileY">The tile Y position.</param>
    public (float X, float Y) TileToScreen(int tileX, int tileY)
    {
        return (this.X + tileX * this.TileSize, this.Y + tileY * this.TileSize);
    }

    /// <summary>Get the screen position of a world position.</summary>
    /// <param name="worldX">The X position in world pixels.</param>
    /// <param name="worldY">The Y position in world pixels.</param>
    public (float X, float Y) WorldToScreen(float worldX, float worldY)
    {
        return (this.X + worldX * this.Scale, this.Y + worldY * this.Scale);
    }

    /// <summary>Get the tile under a screen position, if it's on the farm.</summary>
    /// <param name="screenX">The screen X position.</param>
    /// <param name="screenY">The screen Y position.</param>
    /// <param name="tileX">The tile X position.</param>
    /// <param name="tileY">The tile Y position.</param>
    public bool TryScreenToTile(int screenX, int screenY, out int tileX, out int tileY)
    {
        tileX = (int)Math.Floor((screenX - this.X) / this.TileSize);
        tileY = (int)Math.Floor((screenY - this.Y) / this.TileSize);
        return tileX >= 0 && tileY >= 0 && tileX < this.TilesWide && tileY < this.TilesHigh;
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Construct an instance.</summary>
    private MapLayout(int tilesWide, int tilesHigh, float scale, int x, int y, int width, int height)
    {
        this.TilesWide = tilesWide;
        this.TilesHigh = tilesHigh;
        this.Scale = scale;
        this.X = x;
        this.Y = y;
        this.Width = width;
        this.Height = height;
    }
}

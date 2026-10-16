using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using xTile.Dimensions;
using xTile.Layers;

namespace LiveFarmMap;

/// <summary>Draws a whole location into a texture, the way the game draws the world but without lighting, weather or the HUD.</summary>
/// <remarks>This uses the same technique as the game's map screenshot: the world is drawn at normal size one chunk at a time by moving <see cref="Game1.viewport"/>, and each chunk is shrunk into the final texture. It must be called outside the game's draw, e.g. from a menu's update.</remarks>
internal sealed class FarmRenderer : IDisposable
{
    /*********
    ** Fields
    *********/
    /// <summary>The size of each chunk drawn at normal size, in world pixels.</summary>
    private const int ChunkSize = 2048;

    /// <summary>The chunk being drawn at normal size.</summary>
    private RenderTarget2D? Chunk;

    /// <summary>The locations whose tilesheets have been loaded into the game's map display device.</summary>
    private readonly HashSet<GameLocation> LoadedTileSheets = new();


    /*********
    ** Accessors
    *********/
    /// <summary>The last drawing of the location, if any.</summary>
    public RenderTarget2D? Snapshot { get; private set; }


    /*********
    ** Public methods
    *********/
    /// <summary>Draw a location into <see cref="Snapshot"/>.</summary>
    /// <param name="location">The location to draw.</param>
    /// <param name="width">The snapshot width in screen pixels.</param>
    /// <param name="height">The snapshot height in screen pixels.</param>
    public void Render(GameLocation location, int width, int height)
    {
        GraphicsDevice device = Game1.graphics.GraphicsDevice;
        SpriteBatch batch = Game1.spriteBatch;

        if (this.Snapshot == null || this.Snapshot.Width != width || this.Snapshot.Height != height)
        {
            this.Snapshot?.Dispose();
            this.Snapshot = new RenderTarget2D(device, width, height, mipMap: false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        }
        this.Chunk ??= new RenderTarget2D(device, FarmRenderer.ChunkSize, FarmRenderer.ChunkSize, mipMap: false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);

        if (this.LoadedTileSheets.Add(location))
            location.Map.LoadTileSheets(Game1.mapDisplayDevice);

        int worldWidth = location.Map.DisplayWidth;
        int worldHeight = location.Map.DisplayHeight;
        float scale = Math.Min(width / (float)worldWidth, height / (float)worldHeight);

        RenderTargetBinding[] oldTargets = device.GetRenderTargets();
        xTile.Dimensions.Rectangle oldViewport = Game1.viewport;
        GameLocation oldLocation = Game1.game1.instanceGameLocation;
        bool oldTakingScreenshot = Game1.game1.takingMapScreenshot;
        try
        {
            // set the field directly, since the property setter treats it as the player changing location
            Game1.game1.instanceGameLocation = location;
            Game1.game1.takingMapScreenshot = true;

            device.SetRenderTarget(this.Snapshot);
            device.Clear(Color.Black);

            for (int chunkY = 0; chunkY < worldHeight; chunkY += FarmRenderer.ChunkSize)
            {
                for (int chunkX = 0; chunkX < worldWidth; chunkX += FarmRenderer.ChunkSize)
                {
                    Game1.viewport = new xTile.Dimensions.Rectangle(chunkX, chunkY, FarmRenderer.ChunkSize, FarmRenderer.ChunkSize);
                    device.SetRenderTarget(this.Chunk);
                    device.Clear(Game1.bgColor);
                    FarmRenderer.DrawWorld(location, batch);

                    device.SetRenderTarget(this.Snapshot);
                    batch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.LinearClamp);
                    batch.Draw(this.Chunk, new Vector2(chunkX * scale, chunkY * scale), null, Color.White, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
                    batch.End();
                }
            }
        }
        finally
        {
            Game1.viewport = oldViewport;
            Game1.game1.instanceGameLocation = oldLocation;
            Game1.game1.takingMapScreenshot = oldTakingScreenshot;
            if (oldTargets.Length > 0)
                device.SetRenderTargets(oldTargets);
            else
                device.SetRenderTarget(null);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.Snapshot?.Dispose();
        this.Snapshot = null;
        this.Chunk?.Dispose();
        this.Chunk = null;
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Draw the part of a location in <see cref="Game1.viewport"/> into the current render target.</summary>
    /// <param name="location">The location to draw.</param>
    /// <param name="batch">The sprite batch to draw with.</param>
    /// <remarks>Follows <see cref="Game1.DrawWorld"/>, minus shadows, lighting, weather and anything tied to the local player's view.</remarks>
    private static void DrawWorld(GameLocation location, SpriteBatch batch)
    {
        Game1.mapDisplayDevice.BeginScene(batch);

        batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        location.drawBackground(batch);
        batch.End();

        foreach (KeyValuePair<Layer, int> layer in location.backgroundLayers)
        {
            batch.Begin(SpriteSortMode.Texture, BlendState.AlphaBlend, SamplerState.PointClamp);
            layer.Key.Draw(Game1.mapDisplayDevice, Game1.viewport, Location.Origin, wrapAround: false, Game1.pixelZoom, -1f);
            batch.End();
        }

        batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        location.drawWater(batch);
        batch.End();

        batch.Begin(SpriteSortMode.FrontToBack, BlendState.AlphaBlend, SamplerState.PointClamp);
        location.drawFloorDecorations(batch);
        batch.End();

        const float buildingSortOffset = 0.1f;
        batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        for (int i = 0; i < location.buildingLayers.Count; i++)
        {
            float offset = location.buildingLayers.Count > 1 ? i / (float)(location.buildingLayers.Count - 1) : 0f;
            location.buildingLayers[i].Key.Draw(Game1.mapDisplayDevice, Game1.viewport, Location.Origin, wrapAround: false, Game1.pixelZoom, buildingSortOffset * offset);
        }
        batch.End();

        batch.Begin(SpriteSortMode.FrontToBack, BlendState.AlphaBlend, SamplerState.PointClamp);
        location.draw(batch);
        for (int i = 0; i < location.frontLayers.Count; i++)
        {
            float offset = location.frontLayers.Count > 1 ? i / (float)(location.frontLayers.Count - 1) : 0f;
            location.frontLayers[i].Key.Draw(Game1.mapDisplayDevice, Game1.viewport, Location.Origin, wrapAround: false, Game1.pixelZoom, 64f + buildingSortOffset * offset);
        }
        location.drawAboveFrontLayer(batch);
        batch.End();

        foreach (KeyValuePair<Layer, int> layer in location.alwaysFrontLayers)
        {
            batch.Begin(SpriteSortMode.Texture, BlendState.AlphaBlend, SamplerState.PointClamp);
            layer.Key.Draw(Game1.mapDisplayDevice, Game1.viewport, Location.Origin, wrapAround: false, Game1.pixelZoom, -1f);
            batch.End();
        }

        batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        location.drawAboveAlwaysFrontLayer(batch);
        batch.End();

        Game1.mapDisplayDevice.EndScene();
    }
}

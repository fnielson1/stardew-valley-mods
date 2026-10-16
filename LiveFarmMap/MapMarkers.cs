using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Menus;

namespace LiveFarmMap;

/// <summary>Draws small icons over the farm map for the players and characters on the farm, like NPC Map Locations does on the world map. Animals never get one.</summary>
/// <remarks>The icons are drawn every frame from live positions, so they move smoothly between redraws of the farm.</remarks>
internal sealed class MapMarkers
{
    /*********
    ** Fields
    *********/
    /// <summary>The size of a player's icon on screen.</summary>
    private const int PlayerIconSize = 32;

    /// <summary>The size of a character's icon on screen.</summary>
    private const int CharacterIconSize = 28;

    /// <summary>The mod settings.</summary>
    private readonly ModConfig Config;

    /// <summary>The icons drawn this frame, last on top, for finding the one under the cursor.</summary>
    private readonly List<(Rectangle Bounds, string Name)> Drawn = new();


    /*********
    ** Public methods
    *********/
    /// <summary>Construct an instance.</summary>
    /// <param name="config">The mod settings.</param>
    public MapMarkers(ModConfig config)
    {
        this.Config = config;
    }

    /// <summary>Draw the icons for everyone on the farm, and the name of the one under the cursor.</summary>
    /// <param name="b">The sprite batch to draw with.</param>
    /// <param name="farm">The farm being shown.</param>
    /// <param name="layout">Where the farm is drawn on screen.</param>
    public void Draw(SpriteBatch b, Farm farm, MapLayout layout)
    {
        this.Drawn.Clear();
        if (!this.Config.ShowPeople)
            return;

        // only villagers and children get an icon: never animals (farm animals, pets, horses) or monsters like the slimes some farms spawn
        foreach (NPC npc in farm.characters)
        {
            if (!(npc.IsVillager || npc is Child) || npc.IsInvisible || npc.Sprite?.Texture == null)
                continue;

            // villagers show their head, cropped like NPC Map Locations does; children show their whole current frame
            Rectangle source = npc.IsVillager
                ? new Rectangle(0, 0, 16, 15)
                : npc.Sprite.SourceRect;
            this.DrawSprite(b, layout, npc, npc.Sprite, source, MapMarkers.CharacterIconSize, npc.displayName);
        }

        foreach (Farmer farmer in farm.farmers)
            this.DrawFarmer(b, layout, farmer);
    }

    /// <summary>Show the name of the topmost icon under the cursor, if any.</summary>
    /// <param name="b">The sprite batch to draw with.</param>
    /// <remarks>Call this after <see cref="Draw"/> and the rest of the menu, so the name is on top.</remarks>
    public void DrawHoveredName(SpriteBatch b)
    {
        int mouseX = Game1.getMouseX();
        int mouseY = Game1.getMouseY();

        for (int i = this.Drawn.Count - 1; i >= 0; i--)
        {
            (Rectangle bounds, string name) = this.Drawn[i];
            if (bounds.Contains(mouseX, mouseY) && !string.IsNullOrWhiteSpace(name))
            {
                IClickableMenu.drawHoverText(b, name, Game1.smallFont);
                return;
            }
        }
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Draw a character's sprite as an icon centred on where they stand.</summary>
    /// <param name="b">The sprite batch to draw with.</param>
    /// <param name="layout">Where the farm is drawn on screen.</param>
    /// <param name="character">The character.</param>
    /// <param name="sprite">The character's sprite.</param>
    /// <param name="source">The pixel area of the sprite texture to draw.</param>
    /// <param name="size">The largest side of the icon on screen.</param>
    /// <param name="name">The name to show when the icon is hovered.</param>
    private void DrawSprite(SpriteBatch b, MapLayout layout, Character character, AnimatedSprite sprite, Rectangle source, int size, string name)
    {
        if (sprite?.Texture == null || source.Width <= 0 || source.Height <= 0)
            return;

        float scale = size / (float)Math.Max(source.Width, source.Height);
        int width = (int)(source.Width * scale);
        int height = (int)(source.Height * scale);
        Rectangle bounds = this.GetIconBounds(layout, character, width, height);

        SpriteEffects flip = character.flip || (sprite.CurrentAnimation?.Count > 0 && sprite.CurrentAnimation[sprite.currentAnimationIndex].flip)
            ? SpriteEffects.FlipHorizontally
            : SpriteEffects.None;
        b.Draw(sprite.Texture, bounds, source, Color.White, 0f, Vector2.Zero, flip, 1f);
        this.Drawn.Add((bounds, name));
    }

    /// <summary>Draw a player's mini portrait as an icon centred on where they stand.</summary>
    /// <param name="b">The sprite batch to draw with.</param>
    /// <param name="layout">Where the farm is drawn on screen.</param>
    /// <param name="farmer">The player.</param>
    private void DrawFarmer(SpriteBatch b, MapLayout layout, Farmer farmer)
    {
        const int portraitSize = 16;
        Rectangle bounds = this.GetIconBounds(layout, farmer, MapMarkers.PlayerIconSize, MapMarkers.PlayerIconSize);

        farmer.FarmerRenderer.drawMiniPortrat(b, new Vector2(bounds.X, bounds.Y), 0.00011f, MapMarkers.PlayerIconSize / (float)portraitSize, Game1.down, farmer);
        this.Drawn.Add((bounds, farmer.Name));
    }

    /// <summary>Get the screen area for an icon, centred on the tile a character stands on.</summary>
    /// <param name="layout">Where the farm is drawn on screen.</param>
    /// <param name="character">The character.</param>
    /// <param name="width">The icon width.</param>
    /// <param name="height">The icon height.</param>
    private Rectangle GetIconBounds(MapLayout layout, Character character, int width, int height)
    {
        Point centre = character.GetBoundingBox().Center;
        (float x, float y) = layout.WorldToScreen(centre.X, centre.Y);
        return new Rectangle((int)(x - width / 2f), (int)(y - height / 2f), width, height);
    }
}

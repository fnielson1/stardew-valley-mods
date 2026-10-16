using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Extensions;

namespace MultipleMiniObelisksMapped;

/// <summary>Teleports the player to a mini-obelisk on the farm, the same way the base game does between a pair of them.</summary>
internal static class Teleporter
{
    /*********
    ** Public methods
    *********/
    /// <summary>Teleport the player next to a mini-obelisk, or show why they can't.</summary>
    /// <param name="tileX">The mini-obelisk's tile X position on the farm.</param>
    /// <param name="tileY">The mini-obelisk's tile Y position on the farm.</param>
    /// <returns>Whether the player teleported.</returns>
    public static bool TeleportTo(int tileX, int tileY)
    {
        Farmer who = Game1.player;
        GameLocation location = who.currentLocation;

        (int X, int Y)? spot = WarpSpots.Find(tileX, tileY, (x, y) => !location.IsTileBlockedBy(new Vector2(x, y), CollisionMask.All, CollisionMask.All));
        if (spot is not { } target)
        {
            Game1.showRedMessage(Game1.content.LoadString("Strings\\StringsFromCSFiles:MiniObelisk_NeedsSpace"));
            return false;
        }

        // copied from the base game's Object.CheckForActionOnMiniObelisk
        for (int i = 0; i < 12; i++)
        {
            Vector2 position = new(Game1.random.Next((int)who.Position.X - 256, (int)who.Position.X + 192), Game1.random.Next((int)who.Position.Y - 256, (int)who.Position.Y + 192));
            location.temporarySprites.Add(new TemporaryAnimatedSprite(354, Game1.random.Next(25, 75), 6, 1, position, flicker: false, Game1.random.NextBool()));
        }
        location.playSound("wand");
        Game1.displayFarmer = false;
        who.freezePause = 50;
        Game1.flashAlpha = 1f;
        DelayedAction.fadeAfterDelay(() =>
        {
            who.setTileLocation(new Vector2(target.X, target.Y));
            Game1.displayFarmer = true;
            Game1.globalFadeToClear();
        }, 50);

        Point tile = who.TilePoint;
        int delay = 0;
        for (int x = tile.X + 8; x >= tile.X - 8; x--)
        {
            location.temporarySprites.Add(new TemporaryAnimatedSprite(6, new Vector2(x, tile.Y) * 64f, Color.White, 8, flipped: false, 50f)
            {
                layerDepth = 1f,
                delayBeforeAnimationStart = delay * 25,
                motion = new Vector2(-0.25f, 0f)
            });
            delay++;
        }

        return true;
    }
}

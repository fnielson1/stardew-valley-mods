using System;

namespace MultipleMiniObelisksMapped;

/// <summary>Picks where a player lands when they teleport to a mini-obelisk.</summary>
internal static class WarpSpots
{
    /*********
    ** Fields
    *********/
    /// <summary>The tiles next to a mini-obelisk to try, in order: below, left, right, then above, the same as the base game.</summary>
    private static readonly (int X, int Y)[] Offsets = { (0, 1), (-1, 0), (1, 0), (0, -1) };


    /*********
    ** Public methods
    *********/
    /// <summary>Get the first open tile next to a mini-obelisk.</summary>
    /// <param name="obeliskX">The mini-obelisk's tile X position.</param>
    /// <param name="obeliskY">The mini-obelisk's tile Y position.</param>
    /// <param name="isOpen">Get whether the player can stand on a tile.</param>
    /// <returns>The tile to land on, or null if every tile next to it is blocked.</returns>
    public static (int X, int Y)? Find(int obeliskX, int obeliskY, Func<int, int, bool> isOpen)
    {
        foreach ((int offsetX, int offsetY) in WarpSpots.Offsets)
        {
            int x = obeliskX + offsetX;
            int y = obeliskY + offsetY;
            if (isOpen(x, y))
                return (x, y);
        }
        return null;
    }
}

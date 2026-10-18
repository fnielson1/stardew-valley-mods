using System;

namespace SmallerJunimoHut;

/// <summary>Works out how to draw the Junimo Hut's sprites to fit a 2×2 hut.</summary>
/// <remarks>Offsets are in screen pixels at zoom 1 from the hut's bottom-left corner, where the base game anchors the hut's sprite. A source pixel is 4 screen pixels.</remarks>
internal static class HutLayout
{
    /*********
    ** Fields
    *********/
    /// <summary>The width of one season's hut sprite in the texture, in source pixels.</summary>
    private const int SeasonWidth = 48;

    /// <summary>The width of a 2×2 hut, in source pixels.</summary>
    private const int HutWidth = 32;

    /// <summary>The number of seasons in the texture, side by side.</summary>
    private const int Seasons = 4;

    /// <summary>The width of the base game's coin bag sprite, in source pixels.</summary>
    private const int BagWidth = 15;

    /// <summary>Where the base game draws the coin bag for a 3×2 hut.</summary>
    private static readonly (float X, float Y) BaseBagOffset = (128 + 12, -32);


    /*********
    ** Accessors
    *********/
    /// <summary>How much the base game's hut sprite is shrunk to fit 2 tiles wide.</summary>
    public const float ShrunkScale = 2f / 3f;

    /// <summary>Where the base game draws the raisin decoration for a 3×2 hut.</summary>
    public static readonly (float X, float Y) BaseRaisinOffset = (12, 20);

    /// <summary>Where the base game draws the lit window for a 3×2 hut.</summary>
    public static readonly (float X, float Y) BaseLightOffset = (64, -64);


    /*********
    ** Public methods
    *********/
    /// <summary>Get whether the hut texture already has art 2 tiles wide, like a retexture made for a 2×2 hut, so it doesn't need shrinking.</summary>
    /// <param name="width">The texture's width in pixels.</param>
    /// <param name="height">The texture's height in pixels.</param>
    /// <param name="isOpaque">Get whether a pixel in the texture isn't fully transparent.</param>
    /// <returns>True if the right third of every season's sprite is empty.</returns>
    public static bool IsNarrowArt(int width, int height, Func<int, int, bool> isOpaque)
    {
        int bottom = Math.Min(height, 64);
        for (int season = 0; season < HutLayout.Seasons; season++)
        {
            int left = season * HutLayout.SeasonWidth + HutLayout.HutWidth;
            int right = Math.Min(width, (season + 1) * HutLayout.SeasonWidth);
            for (int x = left; x < right; x++)
            {
                for (int y = 0; y < bottom; y++)
                {
                    if (isOpaque(x, y))
                        return false;
                }
            }
        }
        return true;
    }

    /// <summary>Get how much to scale the hut's sprites.</summary>
    /// <param name="narrowArt">Whether the texture already has art 2 tiles wide (see <see cref="IsNarrowArt"/>).</param>
    public static float GetScale(bool narrowArt)
    {
        return narrowArt ? 1f : HutLayout.ShrunkScale;
    }

    /// <summary>Move an offset from the base game's 3×2 hut onto a hut drawn at a scale.</summary>
    /// <param name="baseOffset">The offset the base game uses.</param>
    /// <param name="scale">How much the hut is scaled (see <see cref="GetScale"/>).</param>
    public static (float X, float Y) Scale((float X, float Y) baseOffset, float scale)
    {
        return (baseOffset.X * scale, baseOffset.Y * scale);
    }

    /// <summary>Get where to draw the coin bag, which the base game draws on the hut's third tile. It's scaled with the hut, but kept within the hut's 2 tiles.</summary>
    /// <param name="scale">How much the hut is scaled (see <see cref="GetScale"/>).</param>
    public static (float X, float Y) GetBagOffset(float scale)
    {
        (float x, float y) = HutLayout.Scale(HutLayout.BaseBagOffset, scale);
        float maxX = (HutLayout.HutWidth - HutLayout.BagWidth * scale) * 4;
        return (Math.Min(x, maxX), y);
    }

    /// <summary>Get how far down to move the hut's sprite in a menu, where the base game anchors it at the top-left, so it sits on the same ground as at full size.</summary>
    /// <param name="spriteHeight">The sprite's height in source pixels.</param>
    /// <param name="scale">How much the hut is scaled (see <see cref="GetScale"/>).</param>
    public static float GetMenuOffsetY(int spriteHeight, float scale)
    {
        return spriteHeight * 4 * (1 - scale);
    }
}

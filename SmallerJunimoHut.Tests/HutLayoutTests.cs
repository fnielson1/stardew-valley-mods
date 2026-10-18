using System.Collections.Generic;
using Xunit;

namespace SmallerJunimoHut.Tests;

public class HutLayoutTests
{
    [Fact]
    public void IsNarrowArt_VanillaArtFillsEachSeason_ReturnsFalse()
    {
        Assert.False(HutLayout.IsNarrowArt(256, 64, (x, y) => x < 192));
    }

    [Fact]
    public void IsNarrowArt_EachSeasonOnlyTwoTilesWide_ReturnsTrue()
    {
        Assert.True(HutLayout.IsNarrowArt(256, 64, (x, _) => x % 48 < 32 && x < 192));
    }

    [Fact]
    public void IsNarrowArt_OneSeasonWide_ReturnsFalse()
    {
        Assert.False(HutLayout.IsNarrowArt(256, 64, (x, _) => x % 48 < 32 || x == 180));
    }

    [Fact]
    public void IsNarrowArt_ExtraSpritesPastTheSeasons_AreIgnored()
    {
        Assert.True(HutLayout.IsNarrowArt(256, 64, (x, _) => x >= 192));
    }

    [Fact]
    public void IsNarrowArt_SmallTexture_StaysInBounds()
    {
        List<(int, int)> checkedPixels = new();
        HutLayout.IsNarrowArt(100, 20, (x, y) =>
        {
            checkedPixels.Add((x, y));
            return false;
        });

        Assert.All(checkedPixels, pixel => Assert.True(pixel.Item1 < 100 && pixel.Item2 < 20));
    }

    [Fact]
    public void GetScale_WideArt_ShrinksToTwoThirds()
    {
        Assert.Equal(2f / 3f, HutLayout.GetScale(narrowArt: false));
        Assert.Equal(1f, HutLayout.GetScale(narrowArt: true));
    }

    [Fact]
    public void Scale_MultipliesBothAxes()
    {
        Assert.Equal((32f, -42f), HutLayout.Scale((48, -63), 2f / 3f));
    }

    [Fact]
    public void GetBagOffset_Shrunk_StaysWithinTwoTiles()
    {
        float scale = HutLayout.ShrunkScale;
        (float x, float y) = HutLayout.GetBagOffset(scale);

        Assert.True(x + 15 * 4 * scale <= 128 + 0.001f);
        Assert.Equal(-32f * scale, y, 3);
    }

    [Fact]
    public void GetBagOffset_FullSize_MovedOffTheThirdTile()
    {
        Assert.Equal((68f, -32f), HutLayout.GetBagOffset(1f));
    }

    [Fact]
    public void GetMenuOffsetY_KeepsTheBottomInPlace()
    {
        Assert.Equal(0f, HutLayout.GetMenuOffsetY(64, 1f));
        Assert.Equal(256f / 3f, HutLayout.GetMenuOffsetY(64, 2f / 3f), 3);
    }
}

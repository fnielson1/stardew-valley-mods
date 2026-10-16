using Xunit;

namespace MultipleMiniObelisksMapped.Tests;

public class MarkerLayoutTests
{
    [Fact]
    public void GetBounds_BigTile_IsTileWideAndStandsOnIt()
    {
        Assert.Equal((100, 140, 40, 80), MarkerLayout.GetBounds(100, 180, 40, 40, hovered: false));
    }

    [Fact]
    public void GetBounds_SmallTile_UsesTheMinimumWidthCentredOnTheTile()
    {
        // 10px tile, centre at x=105, bottom at y=210
        Assert.Equal((95, 170, 20, 40), MarkerLayout.GetBounds(100, 200, 10, 10, hovered: false));
    }

    [Fact]
    public void GetBounds_Hovered_IsBiggerAndStillStandsOnTheTile()
    {
        (int x, int y, int width, int height) = MarkerLayout.GetBounds(100, 180, 40, 40, hovered: true);

        Assert.Equal(50, width);
        Assert.Equal(100, height);
        Assert.Equal(120, x + width / 2);
        Assert.Equal(220, y + height);
    }
}

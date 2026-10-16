using System.Collections.Generic;
using Xunit;

namespace MultipleMiniObelisksMapped.Tests;

public class WarpSpotsTests
{
    [Fact]
    public void Find_AllOpen_LandsBelow()
    {
        Assert.Equal((10, 21), WarpSpots.Find(10, 20, (_, _) => true));
    }

    [Fact]
    public void Find_SomeBlocked_TriesBelowLeftRightAbove()
    {
        List<(int, int)> tried = new();
        WarpSpots.Find(10, 20, (x, y) =>
        {
            tried.Add((x, y));
            return false;
        });

        Assert.Equal(new[] { (10, 21), (9, 20), (11, 20), (10, 19) }, tried);
    }

    [Fact]
    public void Find_OnlyAboveOpen_LandsAbove()
    {
        Assert.Equal((10, 19), WarpSpots.Find(10, 20, (x, y) => (x, y) == (10, 19)));
    }

    [Fact]
    public void Find_AllBlocked_ReturnsNull()
    {
        Assert.Null(WarpSpots.Find(10, 20, (_, _) => false));
    }
}

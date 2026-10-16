using Xunit;

namespace LiveFarmMap.Tests;

public class MapLayoutTests
{
    [Fact]
    public void Fit_WideArea_FillsTheHeightAndCentresHorizontally()
    {
        // 80x65 tiles is the standard farm
        MapLayout layout = MapLayout.Fit(80, 65, areaX: 0, areaY: 0, areaWidth: 2000, areaHeight: 650);

        Assert.Equal(650, layout.Height);
        Assert.Equal(800, layout.Width);
        Assert.Equal(600, layout.X);
        Assert.Equal(0, layout.Y);
        Assert.Equal(10f, layout.TileSize, precision: 3);
    }

    [Fact]
    public void Fit_TallArea_FillsTheWidthAndCentresVertically()
    {
        MapLayout layout = MapLayout.Fit(80, 65, areaX: 10, areaY: 20, areaWidth: 800, areaHeight: 2000);

        Assert.Equal(800, layout.Width);
        Assert.Equal(650, layout.Height);
        Assert.Equal(10, layout.X);
        Assert.Equal(20 + (2000 - 650) / 2, layout.Y);
    }

    [Theory]
    [InlineData(80, 65, 1280, 720)]
    [InlineData(80, 65, 1920, 1080)]
    [InlineData(150, 60, 1366, 768)]
    [InlineData(1, 1, 3, 1000)]
    public void Fit_NeverLeavesTheArea(int tilesWide, int tilesHigh, int areaWidth, int areaHeight)
    {
        MapLayout layout = MapLayout.Fit(tilesWide, tilesHigh, areaX: 5, areaY: 7, areaWidth, areaHeight);

        Assert.InRange(layout.X, 5, 5 + areaWidth);
        Assert.InRange(layout.Y, 7, 7 + areaHeight);
        Assert.True(layout.X + layout.Width <= 5 + areaWidth);
        Assert.True(layout.Y + layout.Height <= 7 + areaHeight);
    }

    [Fact]
    public void Fit_EmptySizes_StillGivesAUsableLayout()
    {
        MapLayout layout = MapLayout.Fit(0, 0, areaX: 0, areaY: 0, areaWidth: 0, areaHeight: 0);

        Assert.True(layout.Width >= 1);
        Assert.True(layout.Height >= 1);
        Assert.True(layout.Scale > 0);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(79, 64)]
    [InlineData(40, 30)]
    public void TileToScreen_RoundTrips(int tileX, int tileY)
    {
        MapLayout layout = MapLayout.Fit(80, 65, areaX: 100, areaY: 50, areaWidth: 1200, areaHeight: 900);

        (float x, float y) = layout.TileToScreen(tileX, tileY);
        float half = layout.TileSize / 2;

        Assert.True(layout.TryScreenToTile((int)(x + half), (int)(y + half), out int foundX, out int foundY));
        Assert.Equal(tileX, foundX);
        Assert.Equal(tileY, foundY);
    }

    [Fact]
    public void WorldToScreen_MatchesTheTileGrid()
    {
        MapLayout layout = MapLayout.Fit(80, 65, areaX: 100, areaY: 50, areaWidth: 1200, areaHeight: 900);

        (float tileX, float tileY) = layout.TileToScreen(12, 34);
        (float worldX, float worldY) = layout.WorldToScreen(12 * MapLayout.WorldTileSize, 34 * MapLayout.WorldTileSize);

        Assert.Equal(tileX, worldX, precision: 3);
        Assert.Equal(tileY, worldY, precision: 3);
    }

    [Fact]
    public void WorldToScreen_MidTile_IsBetweenTileCorners()
    {
        MapLayout layout = MapLayout.Fit(80, 65, areaX: 0, areaY: 0, areaWidth: 2000, areaHeight: 650);

        (float x, float y) = layout.WorldToScreen(MapLayout.WorldTileSize * 2.5f, MapLayout.WorldTileSize * 0.5f);

        Assert.Equal(layout.X + layout.TileSize * 2.5f, x, precision: 3);
        Assert.Equal(layout.Y + layout.TileSize * 0.5f, y, precision: 3);
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(10, -1)]
    [InlineData(5000, 10)]
    [InlineData(10, 5000)]
    public void TryScreenToTile_OffTheFarm_IsFalse(int offsetX, int offsetY)
    {
        MapLayout layout = MapLayout.Fit(80, 65, areaX: 100, areaY: 50, areaWidth: 1200, areaHeight: 900);

        Assert.False(layout.TryScreenToTile(layout.X + offsetX, layout.Y + offsetY, out _, out _));
    }
}

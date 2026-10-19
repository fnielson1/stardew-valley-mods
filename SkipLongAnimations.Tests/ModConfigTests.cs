using Xunit;

namespace SkipLongAnimations.Tests;

public class ModConfigTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    [InlineData(10, 10)]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(50, 10)]
    public void GetGeodeSpeed_KeepsSpeedInRange(int speed, int expected)
    {
        Assert.Equal(expected, new ModConfig { GeodeSpeed = speed }.GetGeodeSpeed());
    }

    [Fact]
    public void Reset_RestoresDefaults()
    {
        ModConfig config = new() { SkipBusRide = false, GeodeSpeed = 1 };

        config.Reset();

        Assert.True(config.SkipBusRide);
        Assert.Equal(new ModConfig().GeodeSpeed, config.GeodeSpeed);
    }
}

using Xunit;

namespace CustomCaskAging.Tests;

public class ModConfigTests
{
    [Fact]
    public void Default_IsTheBaseGamesSpeed()
    {
        Assert.Equal(1f, new ModConfig().AgingMultiplier);
    }

    [Theory]
    [InlineData(2f, 2f)]
    [InlineData(2.25f, 2.25f)]
    [InlineData(0.5f, 0.5f)]
    [InlineData(0f, ModConfig.MinMultiplier)]
    [InlineData(-3f, ModConfig.MinMultiplier)]
    [InlineData(5000f, ModConfig.MaxMultiplier)]
    [InlineData(float.PositiveInfinity, ModConfig.MaxMultiplier)]
    [InlineData(float.NegativeInfinity, ModConfig.MinMultiplier)]
    public void GetEffectiveMultiplier_StaysInTheAcceptedRange(float configured, float expected)
    {
        ModConfig config = new() { AgingMultiplier = configured };

        Assert.Equal(expected, config.GetEffectiveMultiplier());
    }

    [Fact]
    public void GetEffectiveMultiplier_FallsBackToTheDefaultForNotANumber()
    {
        ModConfig config = new() { AgingMultiplier = float.NaN };

        Assert.Equal(new ModConfig().AgingMultiplier, config.GetEffectiveMultiplier());
    }

    [Fact]
    public void Reset_RestoresTheDefault()
    {
        ModConfig config = new() { AgingMultiplier = 14f };

        config.Reset();

        Assert.Equal(new ModConfig().AgingMultiplier, config.AgingMultiplier);
    }
}

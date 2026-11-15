using Xunit;

namespace CustomCaskAging.Tests;

public class ModConfigTests
{
    [Fact]
    public void Default_IsTheBaseGamesSpeedForEveryRule()
    {
        ModConfig config = new();

        foreach (string ruleId in CaskRules.BaseGameRuleIds)
            Assert.Equal(1f, config.GetConfiguredMultiplier(ruleId));
    }

    [Fact]
    public void EveryBaseGameRuleHasItsOwnSetting()
    {
        ModConfig config = new()
        {
            CheeseMultiplier = 2f,
            GoatCheeseMultiplier = 3f,
            WineMultiplier = 4f,
            MeadMultiplier = 5f,
            PaleAleMultiplier = 6f,
            BeerMultiplier = 7f
        };

        Assert.Equal(2f, config.GetConfiguredMultiplier("Cheese"));
        Assert.Equal(3f, config.GetConfiguredMultiplier("GoatCheese"));
        Assert.Equal(4f, config.GetConfiguredMultiplier("Wine"));
        Assert.Equal(5f, config.GetConfiguredMultiplier("Mead"));
        Assert.Equal(6f, config.GetConfiguredMultiplier("PaleAle"));
        Assert.Equal(7f, config.GetConfiguredMultiplier("Beer"));
    }

    [Theory]
    [InlineData("Digus.CustomCaskMod.Pickles")]
    [InlineData("wine")]
    [InlineData("")]
    [InlineData(null)]
    public void GetEffectiveMultiplier_HasNothingForOtherRules(string? ruleId)
    {
        Assert.Null(new ModConfig().GetEffectiveMultiplier(ruleId));
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
        ModConfig config = new() { WineMultiplier = configured };

        Assert.Equal(expected, config.GetEffectiveMultiplier("Wine"));
    }

    [Fact]
    public void GetEffectiveMultiplier_FallsBackToTheDefaultForNotANumber()
    {
        ModConfig config = new() { BeerMultiplier = float.NaN };

        Assert.Equal(ModConfig.DefaultMultiplier, config.GetEffectiveMultiplier("Beer"));
    }

    [Fact]
    public void GetEffectiveMultiplier_LimitsEachRuleOnItsOwn()
    {
        ModConfig config = new() { WineMultiplier = 5000f, CheeseMultiplier = 2f };

        Assert.Equal(ModConfig.MaxMultiplier, config.GetEffectiveMultiplier("Wine"));
        Assert.Equal(2f, config.GetEffectiveMultiplier("Cheese"));
    }

    [Fact]
    public void Reset_RestoresTheDefaultForEveryRule()
    {
        ModConfig config = new()
        {
            CheeseMultiplier = 14f,
            GoatCheeseMultiplier = 14f,
            WineMultiplier = 14f,
            MeadMultiplier = 14f,
            PaleAleMultiplier = 14f,
            BeerMultiplier = 14f
        };

        config.Reset();

        foreach (string ruleId in CaskRules.BaseGameRuleIds)
            Assert.Equal(ModConfig.DefaultMultiplier, config.GetConfiguredMultiplier(ruleId));
    }
}

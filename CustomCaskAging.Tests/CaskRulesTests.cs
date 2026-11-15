using System.Globalization;
using Xunit;

namespace CustomCaskAging.Tests;

public class CaskRulesTests
{
    [Theory]
    [InlineData("Cheese")]
    [InlineData("GoatCheese")]
    [InlineData("Wine")]
    [InlineData("Mead")]
    [InlineData("PaleAle")]
    [InlineData("Beer")]
    public void IsBaseGameRule_AcceptsEveryBaseGameRule(string id)
    {
        Assert.True(CaskRules.IsBaseGameRule(id));
    }

    [Theory]
    [InlineData("Digus.CustomCaskMod.Pickles")]
    [InlineData("Digus.CustomCaskMod.Category-26")]
    [InlineData("Digus.CustomCaskMod.Cask.EnableAgeEveryObject")]
    [InlineData("SomeMod.Whiskey")]
    [InlineData("wine")]
    [InlineData("")]
    [InlineData(null)]
    public void IsBaseGameRule_LeavesOtherModsRulesAlone(string? id)
    {
        Assert.False(CaskRules.IsBaseGameRule(id));
    }

    [Fact]
    public void Scale_TreatsARuleWithoutARateAsOne()
    {
        // wine has no rate in the base game
        Assert.Equal("2.25", CaskRules.Scale(null, 2.25f, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Scale_MultipliesTheRulesOwnRate()
    {
        // cheese ages 4 times as fast as wine in the base game, and still does
        Assert.Equal("8", CaskRules.Scale("4", 2f, CultureInfo.InvariantCulture));
        Assert.Equal("4", CaskRules.Scale("4", 1f, CultureInfo.InvariantCulture));
        Assert.Equal("2", CaskRules.Scale("4", 0.5f, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-2")]
    public void Scale_TreatsAnUnusableRateAsOne(string current)
    {
        Assert.Equal("3", CaskRules.Scale(current, 3f, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("4", 4f)]
    [InlineData("2.5", 2.5f)]
    public void TryParseRate_ReadsAPositiveNumber(string text, float expected)
    {
        Assert.True(CaskRules.TryParseRate(text, CultureInfo.InvariantCulture, out float rate));
        Assert.Equal(expected, rate);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("fast")]
    [InlineData("0")]
    [InlineData("-1")]
    public void TryParseRate_RejectsAnythingTheGameWould(string? text)
    {
        Assert.False(CaskRules.TryParseRate(text, CultureInfo.InvariantCulture, out _));
    }

    [Fact]
    public void Scale_UsesTheGivenNumberFormat()
    {
        // the game parses the value with the player's own format, so a comma-decimal player needs a comma both ways
        Assert.Equal("2,25", CaskRules.Scale(null, 2.25f, new CultureInfo("de-DE")));
        Assert.Equal("9", CaskRules.Scale("4,5", 2f, new CultureInfo("de-DE")));
    }

    [Fact]
    public void Scale_DropsNeedlessDecimals()
    {
        Assert.Equal("14", CaskRules.Scale(null, 14f, CultureInfo.InvariantCulture));
        Assert.Equal("2.6667", CaskRules.Scale(null, 2.66666f, CultureInfo.InvariantCulture));
    }
}

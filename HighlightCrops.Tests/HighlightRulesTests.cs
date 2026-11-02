using Xunit;

namespace HighlightCrops.Tests;

public class HighlightRulesTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void ShouldHighlight_EmptySoil_FollowsHighlightEmpty(bool highlightEmpty, bool highlightReady)
    {
        Assert.Equal(highlightEmpty, HighlightRules.ShouldHighlight(hasObject: false, hasCrop: false, isDead: false, readyForHarvest: false, highlightEmpty, highlightReady));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void ShouldHighlight_ReadyCrop_FollowsHighlightReady(bool highlightEmpty, bool highlightReady)
    {
        Assert.Equal(highlightReady, HighlightRules.ShouldHighlight(hasObject: false, hasCrop: true, isDead: false, readyForHarvest: true, highlightEmpty, highlightReady));
    }

    [Fact]
    public void ShouldHighlight_GrowingCrop_IsNotHighlighted()
    {
        Assert.False(HighlightRules.ShouldHighlight(hasObject: false, hasCrop: true, isDead: false, readyForHarvest: false, highlightEmpty: true, highlightReady: true));
    }

    [Fact]
    public void ShouldHighlight_DeadCrop_IsNotHighlighted()
    {
        Assert.False(HighlightRules.ShouldHighlight(hasObject: false, hasCrop: true, isDead: true, readyForHarvest: true, highlightEmpty: true, highlightReady: true));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ShouldHighlight_SoilWithObject_IsNotHighlighted(bool hasCrop, bool readyForHarvest)
    {
        Assert.False(HighlightRules.ShouldHighlight(hasObject: true, hasCrop, isDead: false, readyForHarvest, highlightEmpty: true, highlightReady: true));
    }
}

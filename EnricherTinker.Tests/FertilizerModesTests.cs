using Xunit;

namespace EnricherTinker.Tests;

public class FertilizerModesTests
{
    [Theory]
    [InlineData("multi-fertilizer-stack")]
    [InlineData(" Multi-Fertilizer-Stack ")]
    public void AllowsMultiple_MultiStack(string mode)
    {
        Assert.True(FertilizerModes.AllowsMultiple(mode));
    }

    [Theory]
    [InlineData("multi-fertilizer-single-level")]
    [InlineData("single-fertilizer-stack")]
    [InlineData("single-fertilizer-replace")]
    [InlineData("Vanilla")]
    [InlineData("")]
    [InlineData(null)]
    public void AllowsMultiple_NotForAnyOtherMode(string? mode)
    {
        Assert.False(FertilizerModes.AllowsMultiple(mode));
    }
}

using Xunit;

namespace EnricherTinker.Tests;

public class EnricherSlotsTests
{
    [Fact]
    public void Capacity_IsA3x3Grid()
    {
        Assert.Equal(9, EnricherSlots.Capacity(EnricherSlots.Count, 0));
        Assert.Equal(9, EnricherSlots.Capacity(EnricherSlots.Count, 9));
    }

    [Fact]
    public void Capacity_ModDisabled_KeepsTheBaseGameSlot()
    {
        Assert.Equal(1, EnricherSlots.Capacity(1, 0));
        Assert.Equal(1, EnricherSlots.Capacity(0, 0));
    }

    [Theory]
    [InlineData(1, 4, 4)]
    [InlineData(1, 9, 9)]
    [InlineData(9, 10, 12)]
    [InlineData(9, 40, 42)]
    [InlineData(9, 500, 69)]
    public void Capacity_NeverHidesStoredStacks(int slots, int stored, int expected)
    {
        Assert.Equal(expected, EnricherSlots.Capacity(slots, stored));
    }
}

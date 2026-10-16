using System.Collections.Generic;
using Xunit;

namespace EnricherTinker.Tests;

public class FertilizerApplierTests
{
    /// <summary>A fertilizer stack.</summary>
    private sealed class Stack
    {
        public string Id { get; }
        public int Count { get; set; }

        public Stack(string id, int count)
        {
            this.Id = id;
            this.Count = count;
        }
    }

    private static Stack? ConsumeOne(Stack stack)
    {
        stack.Count--;
        return stack.Count > 0 ? stack : null;
    }

    [Fact]
    public void Apply_All_AppliesEveryStackWhichFits_AndUsesOneOfEach()
    {
        List<Stack?> slots = new() { new("speed", 5), null, new("quality", 1), new("retaining", 3) };
        List<string> applied = new();

        int count = FertilizerApplier.Apply(slots, s => { if (s.Id == "retaining") return false; applied.Add(s.Id); return true; }, ConsumeOne, applyAll: true, infinite: false);

        Assert.Equal(2, count);
        Assert.Equal(new[] { "speed", "quality" }, applied);
        Assert.Equal(4, slots[0]!.Count);
        Assert.Null(slots[2]); // used up
        Assert.Equal(3, slots[3]!.Count);
    }

    [Fact]
    public void Apply_FirstOnly_SkipsStacksWhichDontFit_AndStopsAtTheFirstWhichDoes()
    {
        List<Stack?> slots = new() { new("speed", 5), new("quality", 5), new("retaining", 5) };
        List<string> tried = new();

        int count = FertilizerApplier.Apply(slots, s => { tried.Add(s.Id); return s.Id != "speed"; }, ConsumeOne, applyAll: false, infinite: false);

        Assert.Equal(1, count);
        Assert.Equal(new[] { "speed", "quality" }, tried);
        Assert.Equal(new[] { 5, 4, 5 }, new[] { slots[0]!.Count, slots[1]!.Count, slots[2]!.Count });
    }

    [Fact]
    public void Apply_Infinite_NeverUsesUpAStack()
    {
        List<Stack?> slots = new() { new("speed", 1), new("quality", 1) };

        int count = FertilizerApplier.Apply(slots, _ => true, ConsumeOne, applyAll: true, infinite: true);

        Assert.Equal(2, count);
        Assert.Equal(1, slots[0]!.Count);
        Assert.Equal(1, slots[1]!.Count);
    }

    [Fact]
    public void Apply_NothingFits_ChangesNothing()
    {
        List<Stack?> slots = new() { new("speed", 2) };

        int count = FertilizerApplier.Apply(slots, _ => false, ConsumeOne, applyAll: true, infinite: false);

        Assert.Equal(0, count);
        Assert.Equal(2, slots[0]!.Count);
    }
}

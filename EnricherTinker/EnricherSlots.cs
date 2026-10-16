using System;

namespace EnricherTinker;

/// <summary>Works out how many slots an Enricher's menu shows.</summary>
internal static class EnricherSlots
{
    /// <summary>The number of slots an Enricher has with this mod, shown as a 3x3 grid.</summary>
    public const int Count = 9;

    /// <summary>The most slots a menu can show in 3 rows; from 70 the game uses 5 rows.</summary>
    private const int MaxThreeRows = 69;

    /// <summary>Get the number of slots to give an Enricher.</summary>
    /// <param name="slots">The number of slots it should have.</param>
    /// <param name="storedStacks">The number of stacks the Enricher already holds, which are never hidden even if there are more than <paramref name="slots"/>.</param>
    /// <remarks>The game shows up to 8 slots in one row, and from 9 in 3 rows of <c>slots / 3</c>, so a count from 9 which isn't a multiple of 3 is rounded up to fill whole rows.</remarks>
    public static int Capacity(int slots, int storedStacks)
    {
        int capacity = Math.Clamp(Math.Max(slots, storedStacks), 1, MaxThreeRows);
        if (capacity >= 9 && capacity % 3 != 0)
            capacity += 3 - capacity % 3;
        return capacity;
    }
}

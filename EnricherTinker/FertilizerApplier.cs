using System;
using System.Collections.Generic;

namespace EnricherTinker;

/// <summary>Applies the fertilizers in an Enricher to a newly planted tile.</summary>
internal static class FertilizerApplier
{
    /// <summary>Apply the stacks in an Enricher's slots, in slot order.</summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="slots">The Enricher's slots. A stack which is used up is replaced with <c>null</c>.</param>
    /// <param name="tryApply">Apply one of a stack to the tile, returning whether it was applied.</param>
    /// <param name="consumeOne">Remove one from a stack, returning what's left or <c>null</c> if it's used up.</param>
    /// <param name="applyAll">Whether to apply every stack which can be applied, rather than stopping at the first.</param>
    /// <param name="infinite">Whether stacks are never used up.</param>
    /// <returns>The number of stacks applied.</returns>
    public static int Apply<T>(IList<T?> slots, Func<T, bool> tryApply, Func<T, T?> consumeOne, bool applyAll, bool infinite)
        where T : class
    {
        int applied = 0;
        for (int i = 0; i < slots.Count; i++)
        {
            T? stack = slots[i];
            if (stack == null || !tryApply(stack))
                continue;

            if (!infinite)
                slots[i] = consumeOne(stack);
            applied++;

            if (!applyAll)
                break;
        }
        return applied;
    }
}

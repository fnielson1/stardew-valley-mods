namespace HighlightCrops;

/// <summary>Decides which patches of tilled soil the overlay highlights.</summary>
internal static class HighlightRules
{
    /// <summary>Get whether to highlight a patch of tilled soil.</summary>
    /// <param name="hasCrop">Whether anything is planted in it.</param>
    /// <param name="isDead">Whether what's planted in it has died.</param>
    /// <param name="readyForHarvest">Whether what's planted in it can be harvested now.</param>
    /// <param name="highlightEmpty">Whether to highlight soil with nothing planted in it.</param>
    /// <param name="highlightReady">Whether to highlight crops which are ready to harvest.</param>
    public static bool ShouldHighlight(bool hasCrop, bool isDead, bool readyForHarvest, bool highlightEmpty, bool highlightReady)
    {
        if (!hasCrop)
            return highlightEmpty;

        return highlightReady && readyForHarvest && !isDead;
    }
}

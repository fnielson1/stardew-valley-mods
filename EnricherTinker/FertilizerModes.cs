using System;

namespace EnricherTinker;

/// <summary>The Ultimate Fertilizer setting which lets an Enricher apply more than one fertilizer per tile.</summary>
internal static class FertilizerModes
{
    /// <summary>The unique ID of the Ultimate Fertilizer mod.</summary>
    public const string UltimateFertilizerId = "fox_white25.ultimate_fertilizer";

    /// <summary>The only Ultimate Fertilizer "Fertilizer Mode" this mod supports for applying more than one fertilizer per tile, which lets a tile take any mix of fertilizers.</summary>
    public const string MultiStack = "multi-fertilizer-stack";

    /// <summary>Get whether an Ultimate Fertilizer "Fertilizer Mode" lets an Enricher apply more than one fertilizer per tile.</summary>
    /// <param name="mode">The mode, or <c>null</c> if it's unknown.</param>
    public static bool AllowsMultiple(string? mode)
    {
        return string.Equals(mode?.Trim(), FertilizerModes.MultiStack, StringComparison.OrdinalIgnoreCase);
    }
}
